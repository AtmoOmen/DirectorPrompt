<#
.SYNOPSIS
    DirectorPrompt Velopack build + R2 upload script.
    Called by CI workflow on tag push.

.DESCRIPTION
    1. Download previous release via Worker (for delta generation)
    2. Pack new release via vpk
    3. Pull the remote channel feed from Worker, merge with local
    4. Trim to latest N versions, delete stale nupkgs
    5. Upload new nupkgs and the channel feed to R2

.ENVIRONMENT
    CLOUDFLARE_API_TOKEN  - Cloudflare API Token (R2 read/write)
    CLOUDFLARE_ACCOUNT_ID - Cloudflare Account ID
    GITHUB_REF            - Git ref that triggered the workflow
#>

param(
    [ValidateSet('win', 'linux')]
    [string]$Channel    = 'win',
    [string]$WorkerUrl  = 'https://dp-distribute.atmoomen.top',
    [string]$BucketName = 'directorprompt-distribute',
    [string]$PackId     = 'DirectorPrompt',
    [string]$PackDir    = './bin/publish',
    [string]$OutputDir  = './Releases',
    [string]$MainExe    = 'DirectorPrompt.exe',
    [string]$PackAuthors     = 'OmenCorp',
    [string]$IconPath        = './Assets/Images/Icon.ico',
    [string]$ReleaseNotesPath = './Assets/CHANGELOG.md',
    [string]$Framework       = 'net10.0-x64-desktop',
    [int]$MaxVersions        = 10
)

$ErrorActionPreference = 'Stop'
$releaseFileName = "releases.$Channel.json"

function Write-Step([string]$Msg) {
    Write-Host ">>> $Msg"
}

# ---- Extract version ----
$version = $env:GITHUB_REF -replace '.*/'
Write-Step "Release version: $version"

# ---- Ensure tools ----
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    dotnet tool install -g vpk
}
if (-not (Get-Command wrangler -ErrorAction SilentlyContinue)) {
    npm install -g wrangler
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ---- 1. Download previous release from Worker (for delta) ----
Write-Step 'Downloading previous release feed...'
vpk download http --url $WorkerUrl --channel $Channel --timeout 30
if ($LASTEXITCODE -ne 0) {
    Write-Host '  No previous release found (first release), skipping delta generation.'
}

# ---- 2. Pack new release ----
Write-Step "Packing release $version..."
$packArgs = @(
    '-u', $PackId,
    '-v', $version,
    '-p', $PackDir,
    '-o', $OutputDir,
    '-e', $MainExe,
    '--channel', $Channel,
    '--packAuthors', $PackAuthors,
    '--releaseNotes', $ReleaseNotesPath,
    '--icon', $IconPath,
    '--framework', $Framework
)

if ($Channel -eq 'win') {
    $packArgs += '--noInst'
}

& vpk pack @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed (exit=$LASTEXITCODE)"
}

# ---- 3. Read local generated entries ----
$localReleasePath = Join-Path $OutputDir $releaseFileName
$localJson   = Get-Content -LiteralPath $localReleasePath -Encoding utf8 | ConvertFrom-Json
$localAssets = @($localJson.Assets)
Write-Step "Local new entries: $($localAssets.Count)"

# ---- 4. Pull remote channel feed from Worker ----
$remoteAssets = @()
try {
    $cacheBust = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $remoteObj = Invoke-RestMethod -Uri "$WorkerUrl/$releaseFileName?t=$cacheBust" -ErrorAction Stop
    $remoteAssets = @($remoteObj.Assets)
    Write-Step "Remote existing entries: $($remoteAssets.Count)"
}
catch {
    Write-Host "  No remote $releaseFileName yet (first release). ($_)"
}

# ---- 5. Merge (dedup by FileName, local wins) ----
$merged = @{}
foreach ($a in ($remoteAssets + $localAssets)) {
    $merged[$a.FileName] = $a
}
$mergedList = @($merged.Values)

# ---- 6. Keep latest N versions ----
$versionMap = @{}
foreach ($a in $mergedList) {
    if (-not $versionMap.ContainsKey($a.Version)) { $versionMap[$a.Version] = @() }
    $versionMap[$a.Version] += $a
}
$sortedVersions = $versionMap.Keys | Sort-Object { [Version]$_ } -Descending
$keepVersions   = $sortedVersions | Select-Object -First $MaxVersions
$keepSet        = @{}
foreach ($v in $keepVersions) { $keepSet[$v] = $true }

Write-Step "Keeping versions ($($keepVersions.Count)): $($keepVersions -join ', ')"

$keepAssets       = @($mergedList | Where-Object { $keepSet.ContainsKey($_.Version) })
$keepSetFileNames = @{}
foreach ($a in $keepAssets) { $keepSetFileNames[$a.FileName] = $true }

# ---- 7. Delete stale nupkgs ----
$deleteAssets = @($mergedList | Where-Object { -not $keepSetFileNames.ContainsKey($_.FileName) })
foreach ($a in $deleteAssets) {
    Write-Host "  Deleting stale nupkg: $($a.FileName)"
    npx wrangler r2 object delete "$BucketName/$($a.FileName)" --remote
    if ($LASTEXITCODE -ne 0) { throw "Delete failed: $($a.FileName)" }
}

# ---- 8. Upload new nupkgs (1yr immutable) ----
Get-ChildItem -Path (Join-Path $OutputDir '*.nupkg') -File | ForEach-Object {
    Write-Host "  Uploading nupkg: $($_.Name)"
    npx wrangler r2 object put "$BucketName/$($_.Name)" `
        --remote --file $_.FullName `
        --content-type 'application/octet-stream'
    if ($LASTEXITCODE -ne 0) { throw "Upload failed: $($_.Name)" }
}

# ---- 9. Build and upload the channel feed ----
Write-Step "Uploading $releaseFileName..."
$sortedKeep = $keepAssets | Sort-Object { [Version]$_.Version } -Descending
$releaseJson = @{ Assets = @($sortedKeep) } | ConvertTo-Json -Depth 3
$releaseJsonPath = $localReleasePath
$releaseJson | Set-Content -LiteralPath $releaseJsonPath -Encoding utf8NoBOM
npx wrangler r2 object put "$BucketName/$releaseFileName" `
    --remote --file $releaseJsonPath `
    --content-type 'application/json; charset=utf-8'
if ($LASTEXITCODE -ne 0) { throw "Upload of $releaseFileName failed" }

Write-Host "Done: $($keepAssets.Count) nupkgs, $($keepVersions.Count) versions."

<#
.SYNOPSIS
    DirectorPrompt Velopack build + R2 upload script.
    Called by CI workflow on tag push.

.DESCRIPTION
    1. Download previous release via Worker (for delta generation)
    2. Pack new release via vpk
    3. Pull remote releases.win.json from Worker, merge with local
    4. Trim to latest N versions, delete stale nupkgs
    5. Upload new nupkgs + releases.win.json to R2

.ENVIRONMENT
    CLOUDFLARE_API_TOKEN  - Cloudflare API Token (R2 read/write)
    CLOUDFLARE_ACCOUNT_ID - Cloudflare Account ID
    GITHUB_REF            - Git ref that triggered the workflow
#>

param(
    [string]$Channel    = 'win',
    [string]$WorkerUrl  = 'https://dp-distribute.atmoomen.top',
    [string]$BucketName = 'directorprompt-distribute',
    [string]$PackId     = 'DirectorPrompt',
    [string]$PackDir    = '.\bin\Release',
    [string]$OutputDir  = '.\Releases',
    [string]$MainExe    = 'DirectorPrompt.exe',
    [string]$PackAuthors     = 'OmenCorp',
    [string]$IconPath        = '.\Assets\Images\Icon.ico',
    [string]$ReleaseNotesPath = '.\Assets\CHANGELOG.md',
    [string]$Framework       = 'net10.0-x64-desktop',
    [int]$MaxVersions        = 10
)

$ErrorActionPreference = 'Stop'

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
    '--framework', $Framework,
    '--noInst'
)
& vpk pack @packArgs

# ---- 3. Read local generated entries ----
$localJson   = Get-Content -LiteralPath "$OutputDir\releases.win.json" -Encoding utf8 | ConvertFrom-Json
$localAssets = @($localJson.Assets)
Write-Step "Local new entries: $($localAssets.Count)"

# ---- 4. Pull remote releases.win.json from Worker ----
$remoteAssets = @()
try {
    $cacheBust = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $remoteObj = Invoke-RestMethod -Uri "$WorkerUrl/releases.win.json?t=$cacheBust" -ErrorAction Stop
    $remoteAssets = @($remoteObj.Assets)
    Write-Step "Remote existing entries: $($remoteAssets.Count)"
}
catch {
    Write-Host "  No remote releases.win.json yet (first release). ($_)"
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
}

# ---- 8. Upload new nupkgs (1yr immutable) ----
Get-ChildItem "$OutputDir\*.nupkg" -File | ForEach-Object {
    Write-Host "  Uploading nupkg: $($_.Name)"
    npx wrangler r2 object put "$BucketName/$($_.Name)" `
        --remote --file $_.FullName `
        --content-type 'application/octet-stream'
    if ($LASTEXITCODE -ne 0) { throw "Upload failed: $($_.Name)" }
}

# ---- 9. Build and upload releases.win.json ----
Write-Step 'Uploading releases.win.json...'
$sortedKeep = $keepAssets | Sort-Object { [Version]$_.Version } -Descending
$releaseJson = @{ Assets = @($sortedKeep) } | ConvertTo-Json -Depth 3
$releaseJsonPath = "$OutputDir\releases.win.merged.json"
$releaseJson | Set-Content -LiteralPath $releaseJsonPath -Encoding utf8NoBOM
npx wrangler r2 object put "$BucketName/releases.win.json" `
    --remote --file $releaseJsonPath `
    --content-type 'application/json; charset=utf-8'
if ($LASTEXITCODE -ne 0) { throw 'Upload of releases.win.json failed' }

Write-Host "Done: $($keepAssets.Count) nupkgs, $($keepVersions.Count) versions."

# ---- 10. Create GitHub Release ----
Write-Step 'Creating GitHub Release...'
$portableZip = Get-ChildItem "$OutputDir\*-Portable.zip" -File | Select-Object -First 1

$releaseNotes = Get-Content -LiteralPath $ReleaseNotesPath -Encoding utf8 -Raw

$ghArgs = @(
    'release', 'create', $version,
    '--title', "Release $version",
    '--notes', $releaseNotes
)
if ($portableZip) {
    $ghArgs += $portableZip.FullName
}
Get-ChildItem "$OutputDir\*$version*.nupkg" -File | ForEach-Object {
    $ghArgs += $_.FullName
}

gh @ghArgs --repo $env:GITHUB_REPOSITORY
if ($LASTEXITCODE -ne 0) {
    Write-Warning "GitHub Release creation failed (exit=$LASTEXITCODE), continuing."
}
else {
    Write-Host "  GitHub Release created: $version"

    Write-Host "  Uploading releases.win.json..."
    gh release upload $version "$OutputDir\releases.win.json" --repo $env:GITHUB_REPOSITORY
    if ($LASTEXITCODE -ne 0) { Write-Warning "Upload of releases.win.json to GitHub Release failed (exit=$LASTEXITCODE)" }
}

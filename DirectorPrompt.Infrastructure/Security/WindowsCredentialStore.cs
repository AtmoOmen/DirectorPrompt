using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DirectorPrompt.Domain.Services;
using Meziantou.Framework.Win32;

namespace DirectorPrompt.Infrastructure.Security;

[SupportedOSPlatform("windows5.1.2600")]
internal sealed class WindowsCredentialStore : ISecretStore
{
    private const int MAX_CHUNK_LENGTH = 1200;
    private const string USER_NAME = "DirectorPrompt";

    public string? Get(string key)
    {
        var metadata = ReadMetadata(key);

        if (metadata is null)
            return null;

        return ReadValue(key, metadata);
    }

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var previous = ReadMetadata(key);

        if (previous is not null && ReadValue(key, previous) == value)
            return;

        RemoveOrphanedChunks(key, previous);

        var version           = Guid.NewGuid().ToString("N");
        var chunks            = Split(value);
        var writtenChunkCount = 0;

        try
        {
            for (var index = 0; index < chunks.Count; index++)
            {
                Write(Target(key, version, index), chunks[index]);
                writtenChunkCount++;
            }

            Write(Target(key, "metadata"), $"{version}:{chunks.Count.ToString(CultureInfo.InvariantCulture)}");
        }
        catch
        {
            for (var index = 0; index < writtenChunkCount; index++)
                Delete(Target(key, version, index));

            throw;
        }

        if (previous is not null)
            RemoveChunks(key, previous);
    }

    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var target in EnumerateTargets(key))
            Delete(target);
    }

    private static string ReadValue(string key, CredentialMetadata metadata)
    {
        var result = new StringBuilder();

        for (var index = 0; index < metadata.ChunkCount; index++)
        {
            var chunk = Read(Target(key, metadata.Version, index));

            if (chunk is null)
                throw new InvalidOperationException("Windows 凭据存储中的密钥数据不完整");

            result.Append(chunk);
        }

        return result.ToString();
    }

    private static CredentialMetadata? ReadMetadata(string key)
    {
        var value = Read(Target(key, "metadata"));

        if (value is null)
            return null;

        var parts = value.Split(':', StringSplitOptions.TrimEntries);

        if (parts.Length != 2 ||
            !Guid.TryParseExact(parts[0], "N", out _) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var chunkCount) ||
            chunkCount <= 0)
            throw new InvalidOperationException("Windows 凭据存储中的密钥元数据无效");

        return new CredentialMetadata(parts[0], chunkCount);
    }

    private static string? Read(string target) =>
        CredentialManager.ReadCredential(target)?.Password;

    private static void Write(string target, string value) =>
        CredentialManager.WriteCredential
        (
            target,
            USER_NAME,
            value,
            CredentialPersistence.LocalMachine
        );

    private static void Delete(string target)
    {
        if (CredentialManager.ReadCredential(target) is null)
            return;

        try
        {
            CredentialManager.DeleteCredential(target);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1168)
        {
            return;
        }
    }

    private static void RemoveChunks(string key, CredentialMetadata metadata)
    {
        for (var index = 0; index < metadata.ChunkCount; index++)
            Delete(Target(key, metadata.Version, index));
    }

    private static void RemoveOrphanedChunks(string key, CredentialMetadata? active)
    {
        var metadataTarget = Target(key, "metadata");
        var activePrefix   = active is null ?
                                 null :
                                 $"{Target(key, active.Version)}:";

        foreach (var target in EnumerateTargets(key))
        {
            if (target == metadataTarget ||
                activePrefix is not null && target.StartsWith(activePrefix, StringComparison.Ordinal))
                continue;

            Delete(target);
        }
    }

    private static IReadOnlyList<string> EnumerateTargets(string key)
    {
        try
        {
            return CredentialManager.EnumerateCredentials($"{Prefix(key)}:*")
                                    .Select(credential => credential.ApplicationName)
                                    .ToList();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1168)
        {
            return [];
        }
    }

    private static List<string> Split(string value)
    {
        var chunks = new List<string>();
        var offset = 0;

        while (offset < value.Length)
        {
            var length = Math.Min(MAX_CHUNK_LENGTH, value.Length - offset);

            if (length > 1 && char.IsHighSurrogate(value[offset + length - 1]))
                length--;

            chunks.Add(value.Substring(offset, length));
            offset += length;
        }

        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        return chunks;
    }

    private static string Target(string key, params object[] parts)
    {
        var suffix = string.Join(':', parts.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture)));

        return $"{Prefix(key)}:{suffix}";
    }

    private static string Prefix(string key) =>
        $"DirectorPrompt:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()}";

    private sealed record CredentialMetadata(string Version, int ChunkCount);
}

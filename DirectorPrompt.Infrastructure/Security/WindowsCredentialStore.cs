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

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var previous = ReadMetadata(key);
        var version  = Guid.NewGuid().ToString("N");
        var chunks   = Split(value);

        for (var index = 0; index < chunks.Count; index++)
            Write(Target(key, version, index), chunks[index]);

        Write(Target(key, "metadata"), $"{version}:{chunks.Count.ToString(CultureInfo.InvariantCulture)}");

        if (previous is not null)
            RemoveChunks(key, previous);
    }

    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var metadata = ReadMetadata(key);

        if (metadata is not null)
            RemoveChunks(key, metadata);

        Delete(Target(key, "metadata"));
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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var suffix = string.Join(':', parts.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture)));

        return $"DirectorPrompt:{hash}:{suffix}";
    }

    private sealed record CredentialMetadata(string Version, int ChunkCount);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DirectorPrompt.Agents;

namespace DirectorPrompt.Infrastructure.AI;

internal static class OpenCodeSessionHeaders
{
    private const string ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private const int LENGTH = 8;

    private const int BITS_PER_CHARACTER = 6;

    public static readonly string[] Names =
    [
        "x-opencode-session",
        "x-session-affinity",
        "x-client-request-id",
        "x-session-id"
    ];

    public static string? Resolve()
    {
        var sessionID = SessionAffinityContext.CurrentSessionID;

        return sessionID is null ?
                   null :
                   Derive(sessionID.Value);
    }

    private static string Derive(long sessionID)
    {
        var input   = sessionID.ToString(CultureInfo.InvariantCulture);
        var digest  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder(LENGTH);
        var bit     = 0;

        while (builder.Length < LENGTH)
        {
            if (bit + BITS_PER_CHARACTER > digest.Length * 8)
            {
                digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{input}#{builder.Length}"));
                bit    = 0;
            }

            var index        = bit >> 3;
            var offsetInByte = bit & 7;

            var value = offsetInByte <= 2 ?
                            (digest[index] >> (2 - offsetInByte)) & 0x3F :
                            ((digest[index] & ((1 << (8 - offsetInByte)) - 1)) << (offsetInByte - 2)) |
                            (digest[index + 1] >> (10 - offsetInByte));

            bit += BITS_PER_CHARACTER;

            if (value < ALPHABET.Length)
                builder.Append(ALPHABET[value]);
        }

        return builder.ToString();
    }
}

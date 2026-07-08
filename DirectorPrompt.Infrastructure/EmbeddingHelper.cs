using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DirectorPrompt.Infrastructure;

internal static class EmbeddingHelper
{
    public static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        MemoryMarshal.AsBytes(floats.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    public static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

using System.Security.Cryptography;
using System.Text;

namespace Pinqponq.Auth.ApiKey;

internal static class ApiKeyComparison
{
    public static bool IsMatch(string providedKey, string expectedKey)
    {
        return CryptographicOperations.FixedTimeEquals(CreateDigest(providedKey), CreateDigest(expectedKey));
    }

    // FixedTimeEquals returns early when the lengths differ, which tells a caller the key length.
    // Comparing SHA-256 digests keeps both inputs 32 bytes long, so every comparison walks all bytes.
    internal static byte[] CreateDigest(string key)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}

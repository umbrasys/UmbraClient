using System.Security.Cryptography;
using System.Text;

namespace UmbraSync.Utils;

public static class Crypto
{
    public static string GetFileHash(this string filePath)
    {
        using SHA1 sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this string stringToHash)
    {
        return GetOrComputeHashSHA256(stringToHash);
    }

    private static string GetOrComputeHashSHA256(string stringToCompute)
    {
        ArgumentNullException.ThrowIfNull(stringToCompute);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stringToCompute));
        return BitConverter.ToString(hash).Replace("-", "", StringComparison.Ordinal);
    }
}

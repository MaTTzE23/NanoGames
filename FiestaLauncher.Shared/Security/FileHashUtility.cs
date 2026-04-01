using System.Security.Cryptography;

namespace FiestaLauncher.Shared.Security;

public static class FileHashUtility
{
    public static string CalculateSha256(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

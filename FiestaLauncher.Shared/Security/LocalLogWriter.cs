using System.Text.RegularExpressions;

namespace FiestaLauncher.Shared.Security;

public static partial class LocalLogWriter
{
    public static void Write(string fileName, string message, string? fallbackRoot = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var targetPath = ResolveLogPath(fileName, fallbackRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.AppendAllText(targetPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Sanitize(message)}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var sanitized = QuerySecretPattern().Replace(message, "$1[redacted]");
        sanitized = InlineSecretPattern().Replace(sanitized, "$1[redacted]");
        sanitized = BearerTokenPattern().Replace(sanitized, "$1[redacted]");
        return sanitized;
    }

    private static string ResolveLogPath(string fileName, string? fallbackRoot)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "NanOnline", "Logs", fileName);
        }

        var root = !string.IsNullOrWhiteSpace(fallbackRoot) && Directory.Exists(fallbackRoot)
            ? fallbackRoot
            : AppContext.BaseDirectory;

        return Path.Combine(root, fileName);
    }

    [GeneratedRegex("([?&](?:token|sig|password|access_token|start_token|session_token)=)[^&\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex("((?:access|start|session|osk|launcher)?token|password)\\s*[:=]\\s*[^\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex InlineSecretPattern();

    [GeneratedRegex("(Bearer\\s+)[^\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();
}

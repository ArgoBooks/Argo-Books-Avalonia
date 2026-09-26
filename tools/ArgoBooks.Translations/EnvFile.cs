namespace ArgoBooks.Translations;

/// <summary>
/// Loads KEY=VALUE lines from a .env file into the process environment, so the API keys can
/// live in a file instead of being set in the shell.
/// </summary>
internal static class EnvFile
{
    // Limited so a .env planted in a distant parent directory is never picked up.
    private const int MaxParentSearchDepth = 1;

    /// <summary>
    /// Loads the first .env found in the tool's directory or its parent. Best effort.
    /// </summary>
    public static void Load()
    {
        var path = Find();
        if (path == null)
            return;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                    continue;

                var separatorIndex = trimmedLine.IndexOf('=');
                if (separatorIndex <= 0) continue;

                var key = trimmedLine[..separatorIndex].Trim();
                var value = trimmedLine[(separatorIndex + 1)..].Trim();

                if (value.Length >= 2 &&
                    ((value.StartsWith('"') && value.EndsWith('"')) ||
                     (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }
        catch
        {
            // The keys can also come from the shell, so a missing or unreadable file is not fatal.
        }
    }

    private static string? Find()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;

        for (var depth = 0; depth <= MaxParentSearchDepth && !string.IsNullOrEmpty(directory); depth++)
        {
            var envPath = Path.Combine(directory, ".env");
            if (File.Exists(envPath))
                return envPath;

            var parent = Directory.GetParent(directory);
            if (parent == null) break;
            directory = parent.FullName;
        }

        return null;
    }
}

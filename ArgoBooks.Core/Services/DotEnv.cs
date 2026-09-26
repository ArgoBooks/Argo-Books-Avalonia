namespace ArgoBooks.Core.Services;

/// <summary>
/// Process-level key/value store for values that live only for the session, such as the open
/// company's portal API key. Nothing is read from or written to disk; values are mirrored into
/// the process environment.
/// </summary>
public static class DotEnv
{
    private static readonly Dictionary<string, string> EnvVars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value, falling back to the process environment.
    /// </summary>
    /// <param name="key">The variable name.</param>
    /// <returns>The value, or empty string if not found.</returns>
    public static string Get(string key)
    {
        if (EnvVars.TryGetValue(key, out var value))
        {
            return value;
        }

        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    /// <summary>
    /// Checks if a key exists and has a non-empty value.
    /// </summary>
    /// <param name="key">The variable name.</param>
    /// <returns>True if the key exists and has a value.</returns>
    public static bool HasValue(string key)
    {
        return !string.IsNullOrEmpty(Get(key));
    }

    /// <summary>
    /// Sets a value for the current process only.
    /// </summary>
    /// <param name="key">The variable name.</param>
    /// <param name="value">The value to set.</param>
    public static void SetInMemory(string key, string value)
    {
        EnvVars[key] = value;
        Environment.SetEnvironmentVariable(key, value);
    }

    /// <summary>
    /// Removes a value and clears the corresponding environment variable for the current process.
    /// </summary>
    /// <param name="key">The variable name to remove.</param>
    public static void Unset(string key)
    {
        EnvVars.Remove(key);
        Environment.SetEnvironmentVariable(key, null);
    }
}

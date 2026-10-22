using System.Collections.Concurrent;

namespace ArgoBooks.Tests;

/// <summary>Temp folders for tests, deleted when the test run ends so each run does not leave its folders behind.</summary>
internal static class TestTempFolder
{
    private static readonly ConcurrentBag<string> Created = [];

    // Removed at the end of the run rather than per test, because many tests make a fake platform inline and never dispose it.
    static TestTempFolder() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();

    /// <summary>A new folder path under the system temp folder, starting with <paramref name="prefix"/>.</summary>
    public static string New(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..8]);
        Created.Add(path);
        return path;
    }

    private static void DeleteAll()
    {
        foreach (var path in Created)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

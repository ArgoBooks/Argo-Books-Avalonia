namespace ArgoBooks.Core.Services;

/// <summary>
/// The folders the app keeps outside a company file. Cleanup and install attribution both depend
/// on these exact locations, so they are built here and nowhere else.
/// </summary>
public static class AppPaths
{
    /// <summary>Per-machine state under local app data, such as the first-run marker.</summary>
    public static string LocalData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArgoBooks");

    /// <summary>The app's folder under the system temp folder; every temporary file the app makes lives inside it.</summary>
    public static string Temp => Path.Combine(Path.GetTempPath(), "ArgoBooks");

    /// <summary>The marker FirstRunReporter writes once the install has been reported.</summary>
    public const string FirstRunMarkerFileName = "first_run_reported.marker";

    /// <summary>The id FirstRunReporter gives this computer.</summary>
    public const string MachineUuidFileName = "machine_uuid.txt";
}

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Writes each queued receipt to "&lt;baseDirectory&gt;/pending-scans/&lt;id&gt;.jpg" (the cropped
/// image, while it still needs reviewing) and "&lt;baseDirectory&gt;/pending-scans/&lt;id&gt;.json"
/// (which company it was taken for, plus the confirmed transaction once it has been reviewed).
/// Plain file IO, so it is not unit-tested directly - PendingScanOutboxTests use an in-memory
/// <see cref="IPendingScanStorage"/> fake instead. On Android, construct with
/// <c>Microsoft.Maui.Storage.FileSystem.Current.AppDataDirectory</c>, the same base directory
/// FileSnapshotCache uses.
/// </summary>
public class FilePendingScanStorage : IPendingScanStorage
{
    private readonly string _directory;

    public FilePendingScanStorage(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));

        _directory = Path.Combine(baseDirectory, "pending-scans");
    }

    private string PathFor(string id) => Path.Combine(_directory, $"{id}.jpg");

    private string MetadataPathFor(string id) => Path.Combine(_directory, $"{id}.json");

    public Task<IReadOnlyList<string>> ListIdsAsync()
    {
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        IReadOnlyList<string> ids = Directory.GetFiles(_directory, "*.jpg")
            .Concat(Directory.GetFiles(_directory, "*.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct()
            .ToList();

        return Task.FromResult(ids);
    }

    public async Task SaveAsync(string id, byte[] imageBytes)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(PathFor(id), imageBytes);
    }

    public async Task<byte[]?> LoadAsync(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveMetadataAsync(string id, string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(MetadataPathFor(id), json);
    }

    public async Task<string?> LoadMetadataAsync(string id)
    {
        var path = MetadataPathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string id)
    {
        foreach (var path in new[] { PathFor(id), MetadataPathFor(id) })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }
}

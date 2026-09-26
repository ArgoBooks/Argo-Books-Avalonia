using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Utilities;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Handles loading and saving custom report templates.
/// </summary>
public class ReportTemplateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string TemplateExtension = ".argotemplate";

    private readonly IErrorLogger? _errorLogger;

    public ReportTemplateStorage(IErrorLogger? errorLogger = null)
    {
        TemplatesDirectory = Path.Combine(
            PlatformServiceFactory.GetPlatformService().GetAppDataPath(), "ReportTemplates");
        _errorLogger = errorLogger;
    }

    public ReportTemplateStorage(string templatesDirectory, IErrorLogger? errorLogger = null)
    {
        TemplatesDirectory = templatesDirectory;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Gets the templates directory path.
    /// </summary>
    public string TemplatesDirectory { get; }

    /// <summary>
    /// Ensures the templates directory exists.
    /// </summary>
    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(TemplatesDirectory))
        {
            Directory.CreateDirectory(TemplatesDirectory);
        }
    }

    /// <summary>
    /// Saves a template to storage.
    /// </summary>
    public async Task<bool> SaveTemplateAsync(ReportConfiguration config, string templateName)
    {
        try
        {
            EnsureDirectoryExists();

            var files = ReadTemplateFiles();
            var filePath = FindTemplateFile(files, templateName) ?? NewTemplateFile(templateName);

            var templateData = new SavedTemplate
            {
                Name = templateName,
                Configuration = config,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(templateData, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            return true;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to save report template");
            return false;
        }
    }

    /// <summary>
    /// Loads a template from storage.
    /// </summary>
    public async Task<ReportConfiguration?> LoadTemplateAsync(string templateName)
    {
        try
        {
            var filePath = FindTemplateFile(ReadTemplateFiles(), templateName);
            if (filePath == null)
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            var templateData = JsonSerializer.Deserialize<SavedTemplate>(json, JsonOptions);

            return templateData?.Configuration;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to load report template");
            return null;
        }
    }

    /// <summary>
    /// Gets all saved template names.
    /// </summary>
    public List<string> GetSavedTemplateNames()
    {
        try
        {
            EnsureDirectoryExists();
            return ReadTemplateFiles().Select(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to enumerate report templates");
            return [];
        }
    }

    /// <summary>
    /// Deletes a template from storage.
    /// </summary>
    public bool DeleteTemplate(string templateName)
    {
        try
        {
            var filePath = FindTemplateFile(ReadTemplateFiles(), templateName);
            if (filePath != null)
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, $"Failed to delete report template '{templateName}'");
        }

        return false;
    }

    /// <summary>
    /// Renames a template asynchronously.
    /// </summary>
    public async Task<bool> RenameTemplateAsync(string oldName, string newName)
    {
        try
        {
            var files = ReadTemplateFiles();
            var oldPath = FindTemplateFile(files, oldName);
            if (oldPath == null || FindTemplateFile(files, newName) != null)
                return false;

            var newPath = NewTemplateFile(newName);

            // Load, update, and save atomically (write to temp file first)
            var json = await File.ReadAllTextAsync(oldPath);
            var templateData = JsonSerializer.Deserialize<SavedTemplate>(json, JsonOptions);

            if (templateData != null)
            {
                templateData.Name = newName;
                templateData.ModifiedAt = DateTime.UtcNow;

                var newJson = JsonSerializer.Serialize(templateData, JsonOptions);
                var tempPath = newPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, newJson);
                try
                {
                    await AtomicFile.ReplaceAsync(tempPath, newPath, overwrite: false);
                }
                catch
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* best effort */ }
                    }
                    throw;
                }
                File.Delete(oldPath);

                return true;
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, $"Failed to rename report template '{oldName}' to '{newName}'");
        }

        return false;
    }

    /// <summary>
    /// Checks if a template exists.
    /// </summary>
    public bool TemplateExists(string templateName) => FindTemplateFile(ReadTemplateFiles(), templateName) != null;

    /// <summary>
    /// Gets the images directory path for storing embedded images.
    /// </summary>
    public string GetImagesDirectory()
    {
        var imagesDir = Path.Combine(TemplatesDirectory, "Images");
        if (!Directory.Exists(imagesDir))
        {
            Directory.CreateDirectory(imagesDir);
        }
        return imagesDir;
    }

    /// <summary>
    /// Resolves an image path (handles both relative and absolute paths).
    /// </summary>
    public string ResolveImagePath(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return imagePath;

        if (Path.IsPathRooted(imagePath) && File.Exists(imagePath))
            return imagePath;

        var imagesDir = GetImagesDirectory();
        var possiblePath = Path.Combine(imagesDir, imagePath);

        if (File.Exists(possiblePath))
            return possiblePath;

        return imagePath;
    }

    /// <summary>
    /// Every template file with the name stored inside it. A template is found by that name, never by
    /// its file name: sanitising can give two names one file name ("A:B" and "A-B"), and templates
    /// saved under an older naming rule have file names that match nothing.
    /// </summary>
    private List<(string File, string Name)> ReadTemplateFiles()
    {
        var templates = new List<(string File, string Name)>();
        if (!Directory.Exists(TemplatesDirectory))
            return templates;

        foreach (var file in Directory.GetFiles(TemplatesDirectory, $"*{TemplateExtension}"))
        {
            try
            {
                var templateData = JsonSerializer.Deserialize<SavedTemplate>(File.ReadAllText(file), JsonOptions);
                if (templateData?.Name != null)
                    templates.Add((file, templateData.Name));
            }
            catch (Exception ex)
            {
                _errorLogger?.LogWarning($"Failed to read template file {Path.GetFileName(file)}: {ex.Message}", "ReportTemplateStorage");
            }
        }

        return templates;
    }

    private static string? FindTemplateFile(List<(string File, string Name)> templates, string templateName) =>
        templates.FirstOrDefault(t => t.Name == templateName).File;

    /// <summary>A file for a new template, named after it, that no existing file uses.</summary>
    private string NewTemplateFile(string templateName)
    {
        var stem = SafeFileName.Create(templateName, "Template");
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(TemplatesDirectory, n == 1 ? stem + TemplateExtension : $"{stem}-{n}{TemplateExtension}");
            // Checks the disk rather than the parsed list, so an unreadable file is never overwritten.
            if (!File.Exists(path))
                return path;
        }
    }
}

/// <summary>
/// Container for saved template data.
/// </summary>
public class SavedTemplate
{
    public string Name { get; set; } = string.Empty;
    public ReportConfiguration? Configuration { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? FilePath { get; set; }
}

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for handling .argo company file operations.
/// Manages tar archive creation, gzip compression, and file I/O.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Creates a new company file at the specified path.
    /// </summary>
    /// <param name="filePath">Full path for the new .argo file</param>
    /// <param name="companyName">Name of the company</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CreateCompanyAsync(string filePath, string companyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens and extracts a company file to a temporary directory.
    /// </summary>
    /// <param name="filePath">Path to the .argo file</param>
    /// <param name="password">Optional password for encrypted files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the temporary directory containing extracted files</returns>
    Task<string> OpenCompanyAsync(string filePath, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current company data from the temporary directory to the .argo file.
    /// </summary>
    /// <param name="filePath">Path to the .argo file</param>
    /// <param name="tempDirectory">Path to the temporary directory with company data</param>
    /// <param name="password">Optional password for encryption</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveCompanyAsync(string filePath, string tempDirectory, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current company and cleans up temporary files.
    /// </summary>
    /// <param name="tempDirectory">Path to the temporary directory to clean up</param>
    Task CloseCompanyAsync(string tempDirectory);

    /// <summary>
    /// Checks if a file is encrypted by reading its footer.
    /// </summary>
    /// <param name="filePath">Path to the .argo file</param>
    /// <returns>True if the file is encrypted</returns>
    Task<bool> IsFileEncryptedAsync(string filePath);

    /// <summary>
    /// Reads a JSON file from the temporary directory.
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="tempDirectory">Path to the temporary directory</param>
    /// <param name="fileName">Name of the JSON file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized object or default</returns>
    Task<T?> ReadJsonAsync<T>(string tempDirectory, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a JSON file to the temporary directory.
    /// </summary>
    /// <typeparam name="T">Type of object to serialize</typeparam>
    /// <param name="tempDirectory">Path to the temporary directory</param>
    /// <param name="fileName">Name of the JSON file</param>
    /// <param name="data">Object to serialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteJsonAsync<T>(string tempDirectory, string fileName, T data, CancellationToken cancellationToken = default);
}

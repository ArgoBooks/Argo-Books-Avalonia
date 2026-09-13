using System.Security.Cryptography;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Central manager for company file operations and state.
/// Coordinates FileService (read/write), GlobalSettingsService (device-wide settings),
/// FooterService (recent-companies list) and CompanyInstanceLock (stops two instances
/// holding the same .argo file). Encryption is applied inside FileService, not here.
/// </summary>
public class CompanyManager : IDisposable
{
    private readonly FileService _fileService;
    private readonly GlobalSettingsService _settingsService;
    private readonly FooterService _footerService;
    private readonly IErrorLogger? _errorLogger;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private string? _currentTempDirectory;
    private string? _currentPassword;
    private FileStream? _fileLock;
    // Cross-instance guard: prevents the same company being opened in a second running instance,
    // which would race auto-saves and corrupt the .argo file.
    private readonly CompanyInstanceLock _instanceLock = new();
    private bool _isDisposed;

    // Receipts are loaded off the critical open path (they carry base64 image data).
    // _receiptsLoadTask reads receipts.json in the background; EnsureReceiptsLoadedAsync
    // merges them into CompanyData.Receipts exactly once, before any save that writes
    // receipts.json and before the receipts UI reads them. _receiptsLock guards the
    // merge-once flag and the task reference.
    private readonly object _receiptsLock = new();
    private Task<List<Models.Tracking.Receipt>>? _receiptsLoadTask;
    // The CompanyData the in-flight load belongs to. The merge only proceeds if this is still the
    // current company, so a company switch during the load can't merge old receipts into the new one.
    private CompanyData? _receiptsLoadTarget;
    private bool _receiptsMerged;

    /// <summary>
    /// Gets whether a company is currently open.
    /// </summary>
    public bool IsCompanyOpen => CompanyData != null && _currentTempDirectory != null;

    /// <summary>
    /// Gets the current company data.
    /// </summary>
    public CompanyData? CompanyData { get; private set; }

    /// <summary>
    /// Gets the current company file path.
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    public string? CurrentCompanyName => CompanyData?.Settings.Company.Name;

    /// <summary>
    /// Gets whether the current company has unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges => CompanyData?.ChangesMade ?? false;

    /// <summary>
    /// Gets whether the current company file is encrypted.
    /// </summary>
    public bool IsEncrypted => !string.IsNullOrEmpty(_currentPassword);

    /// <summary>
    /// Gets whether the currently open company is the sample company.
    /// The sample company should not be modified directly; use Save As instead.
    /// </summary>
    public bool IsSampleCompany => CurrentFilePath != null &&
        string.Equals(CurrentFilePath, SampleCompanyService.GetSampleCompanyPath(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verifies if the provided password matches the current company's password.
    /// </summary>
    /// <param name="password">The password to verify.</param>
    /// <returns>True if the password matches, false otherwise.</returns>
    public bool VerifyCurrentPassword(string? password)
    {
        if (!IsCompanyOpen) return false;
        if (!IsEncrypted) return string.IsNullOrEmpty(password);

        // Use constant-time comparison to prevent timing attacks
        if (_currentPassword == null || password == null)
            return _currentPassword == null && password == null;

        var storedBytes = Encoding.UTF8.GetBytes(_currentPassword);
        var inputBytes = Encoding.UTF8.GetBytes(password);
        return CryptographicOperations.FixedTimeEquals(storedBytes, inputBytes);
    }

    /// <summary>
    /// Gets the current password for the open company file.
    /// Used for storing password securely for biometric unlock.
    /// </summary>
    /// <returns>The current password, or null if no company is open or it's not encrypted.</returns>
    public string? GetCurrentPassword()
    {
        return _currentPassword;
    }

    public CompanySettings? CurrentCompanySettings => CompanyData?.Settings;

    /// <summary>
    /// Gets the current company logo file path, if one exists.
    /// </summary>
    public string? CurrentCompanyLogoPath
    {
        get
        {
            if (CompanyData?.Settings.Company.LogoFileName == null || _currentTempDirectory == null)
                return null;

            var logoPath = Path.Combine(_currentTempDirectory, CompanyData.Settings.Company.LogoFileName);
            return File.Exists(logoPath) ? logoPath : null;
        }
    }

    private const string CustomerAvatarSubdirectory = "customer_avatars";
    private const string SupplierAvatarSubdirectory = "supplier_avatars";
    private const int AvatarMaxDimension = 256;

    /// <summary>
    /// Resolves an avatar's relative path to an absolute path within the company temp
    /// directory, or null if the relative path escapes that directory. Defends against
    /// crafted .argo files that set <c>avatarFileName</c> to a traversal path
    /// (e.g. <c>../../etc/passwd</c>) which would otherwise let a load read, or a
    /// remove/rename operation delete or move, files outside the temp directory.
    /// </summary>
    private string? ResolveAvatarPathSafely(string? relativeAvatarPath)
    {
        if (string.IsNullOrEmpty(relativeAvatarPath) || _currentTempDirectory == null)
            return null;

        if (Path.IsPathRooted(relativeAvatarPath))
            return null;

        var candidate = Path.GetFullPath(Path.Combine(_currentTempDirectory, relativeAvatarPath));
        var tempRoot = Path.GetFullPath(_currentTempDirectory);

        // Use Path.GetRelativePath instead of a string-prefix check: on case-sensitive
        // filesystems (Linux/macOS) a string compare needs Ordinal, on Windows it needs
        // OrdinalIgnoreCase, and a mismatch either rejects valid paths or admits invalid
        // ones. GetRelativePath uses the platform's native rules, and ".."-prefixed
        // results unambiguously mean the candidate is outside tempRoot.
        var relativeFromRoot = Path.GetRelativePath(tempRoot, candidate);
        if (Path.IsPathRooted(relativeFromRoot)
            || relativeFromRoot == ".."
            || relativeFromRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeFromRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return candidate;
    }

    private string? GetEntityAvatarPath(IAvatarOwner? entity)
    {
        if (entity == null) return null;
        var path = ResolveAvatarPathSafely(entity.AvatarFileName);
        return path != null && File.Exists(path) ? path : null;
    }

    private async Task SetEntityAvatarFromPathAsync(IAvatarOwner entity, string sourceImagePath, string subdirectory)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(sourceImagePath);
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");
        if (!File.Exists(sourceImagePath))
            throw new FileNotFoundException("Avatar source file not found.", sourceImagePath);

        var (destPath, relativePath) = PrepareAvatarDestination(entity.Id, subdirectory);
        var ok = await Task.Run(() => ReceiptImageHelper.ResizeAndSaveAsPng(sourceImagePath, destPath, AvatarMaxDimension));
        if (!ok)
            throw new InvalidOperationException("Selected file could not be loaded as an image.");

        FinalizeAvatarUpdate(entity, relativePath);
    }

    private async Task SetEntityAvatarFromBytesAsync(IAvatarOwner entity, byte[] sourceBytes, string subdirectory)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(sourceBytes);
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");

        var (destPath, relativePath) = PrepareAvatarDestination(entity.Id, subdirectory);
        var ok = await Task.Run(() => ReceiptImageHelper.ResizeBytesAndSaveAsPng(sourceBytes, destPath, AvatarMaxDimension));
        if (!ok)
            throw new InvalidOperationException("Provided bytes could not be decoded as an image.");

        FinalizeAvatarUpdate(entity, relativePath);
    }

    private (string DestPath, string RelativePath) PrepareAvatarDestination(string entityId, string subdirectory)
    {
        var avatarsDir = Path.Combine(_currentTempDirectory!, subdirectory);
        Directory.CreateDirectory(avatarsDir);
        var safeId = SanitizeForFileName(entityId);
        var fileName = $"{safeId}.png";
        var destPath = Path.Combine(avatarsDir, fileName);
        var relativePath = Path.Combine(subdirectory, fileName).Replace('\\', '/');
        return (destPath, relativePath);
    }

    private void FinalizeAvatarUpdate(IAvatarOwner entity, string relativePath)
    {
        entity.AvatarFileName = relativePath;
        entity.UpdatedAt = DateTime.UtcNow;
        CompanyData!.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RemoveEntityAvatarAsync(IAvatarOwner entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");

        var existing = entity.AvatarFileName;
        if (string.IsNullOrEmpty(existing))
            return;

        // Only delete files that resolve safely under the temp directory, guard against
        // a crafted AvatarFileName escaping into the rest of the filesystem.
        var fullPath = ResolveAvatarPathSafely(existing);
        if (fullPath != null && File.Exists(fullPath))
        {
            await Task.Run(() => File.Delete(fullPath));
        }

        entity.AvatarFileName = null;
        entity.UpdatedAt = DateTime.UtcNow;
        CompanyData.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the bytes of an entity's avatar file, or null if there's no avatar set or
    /// the file can't be read. Used by the undo system to capture a snapshot before
    /// the avatar is changed so it can be restored later.
    /// </summary>
    private byte[]? ReadEntityAvatarBytes(IAvatarOwner entity)
    {
        var path = GetEntityAvatarPath(entity);
        if (path == null) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    /// <summary>
    /// Restores an avatar to an exact prior state. Pass <paramref name="bytes"/> = null
    /// to restore "no avatar" (deletes the file and clears AvatarFileName); pass bytes
    /// to write them back as the avatar (no resize, the bytes are already a resized
    /// PNG captured by an earlier <see cref="ReadEntityAvatarBytes"/>). Synchronous so
    /// it can be called directly from undo/redo callbacks.
    /// </summary>
    private void RestoreEntityAvatarSync(IAvatarOwner entity, byte[]? bytes, string subdirectory)
    {
        if (CompanyData == null || _currentTempDirectory == null) return;

        if (bytes == null)
        {
            var existing = entity.AvatarFileName;
            if (!string.IsNullOrEmpty(existing))
            {
                var path = ResolveAvatarPathSafely(existing);
                if (path != null && File.Exists(path))
                {
                    try { File.Delete(path); } catch { /* best effort */ }
                }
            }
            entity.AvatarFileName = null;
        }
        else
        {
            var (destPath, relativePath) = PrepareAvatarDestination(entity.Id, subdirectory);
            try
            {
                File.WriteAllBytes(destPath, bytes);
                entity.AvatarFileName = relativePath;
            }
            catch
            {
                // If the write fails, leave the entity without an avatar reference rather
                // than pointing at a partially-written file.
                entity.AvatarFileName = null;
            }
        }

        entity.UpdatedAt = DateTime.UtcNow;
        CompanyData.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the customer's avatar file as bytes, or null if none/unreadable.</summary>
    public byte[]? ReadCustomerAvatarBytes(Customer customer) => ReadEntityAvatarBytes(customer);

    /// <summary>Reads the supplier's avatar file as bytes, or null if none/unreadable.</summary>
    public byte[]? ReadSupplierAvatarBytes(Supplier supplier) => ReadEntityAvatarBytes(supplier);

    /// <summary>
    /// Restores a customer's avatar to a prior state captured via
    /// <see cref="ReadCustomerAvatarBytes"/>. Null restores "no avatar".
    /// </summary>
    public void RestoreCustomerAvatar(Customer customer, byte[]? bytes)
        => RestoreEntityAvatarSync(customer, bytes, CustomerAvatarSubdirectory);

    /// <summary>
    /// Restores a supplier's avatar to a prior state captured via
    /// <see cref="ReadSupplierAvatarBytes"/>. Null restores "no avatar".
    /// </summary>
    public void RestoreSupplierAvatar(Supplier supplier, byte[]? bytes)
        => RestoreEntityAvatarSync(supplier, bytes, SupplierAvatarSubdirectory);

    /// <summary>
    /// Move the avatar file to track a renamed entity Id. Failure is non-fatal: if the
    /// file move can't complete, AvatarFileName is left at its previous value and the
    /// avatar simply won't load until the user re-uploads.
    /// </summary>
    private void TryMoveEntityAvatarOnRename(IAvatarOwner entity, string newId, string subdirectory)
    {
        if (string.IsNullOrEmpty(entity.AvatarFileName) || _currentTempDirectory == null)
            return;

        try
        {
            var oldPath = ResolveAvatarPathSafely(entity.AvatarFileName);
            var ext = Path.GetExtension(entity.AvatarFileName);
            var safeNewId = SanitizeForFileName(newId);
            var newRelative = Path.Combine(subdirectory, safeNewId + ext).Replace('\\', '/');
            var newPath = Path.Combine(_currentTempDirectory, newRelative);

            if (oldPath != null && File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                if (File.Exists(newPath))
                    File.Delete(newPath);
                File.Move(oldPath, newPath);
            }
            // Always update AvatarFileName to the new relative path: even if the old
            // file was missing or unsafe, the entity record should now point inside
            // the temp dir using the new Id.
            entity.AvatarFileName = newRelative;
        }
        catch
        {
            // Leave AvatarFileName as-is.
        }
    }

    /// <summary>
    /// Gets the absolute on-disk path to a customer's avatar image, or null when there is no
    /// avatar set, the file is missing, or the stored path escapes the company temp directory.
    /// </summary>
    public string? GetCustomerAvatarPath(Customer customer) => GetEntityAvatarPath(customer);

    /// <summary>
    /// Gets the absolute on-disk path to a supplier's avatar image, or null when there is no
    /// avatar set, the file is missing, or the stored path escapes the company temp directory.
    /// </summary>
    public string? GetSupplierAvatarPath(Supplier supplier) => GetEntityAvatarPath(supplier);

    /// <summary>
    /// Whether another running instance has <paramref name="filePath"/> open. False for the
    /// company this instance has open.
    /// </summary>
    public bool IsOpenInAnotherInstance(string filePath) => _instanceLock.IsHeldByAnotherInstance(filePath);

    /// <summary>
    /// Whether the open company's file can be renamed to <paramref name="newPath"/>: a name it
    /// doesn't already have, with no other file there. A change of capitalization alone counts as
    /// free, since where file names ignore case the file found there is this one. Where they
    /// don't and another file has that name, the move is refused and the save keeps the old name.
    /// </summary>
    public bool CanRenameTo(string newPath) =>
        CurrentFilePath != null && CurrentFilePath != newPath
        && (string.Equals(CurrentFilePath, newPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(newPath));

    /// <summary>
    /// Schedules a file rename to be applied on the next save.
    /// The rename is deferred so that closing without saving leaves the original file untouched.
    /// </summary>
    public void SetPendingRename(string newPath)
    {
        PendingRenamePath = newPath;
    }

    /// <summary>
    /// Clears any pending rename (e.g., when changes are undone or discarded).
    /// </summary>
    public void ClearPendingRename()
    {
        PendingRenamePath = null;
    }

    /// <summary>
    /// Gets the path the file will be renamed to on next save, or null if no rename is pending.
    /// </summary>
    public string? PendingRenamePath { get; private set; }

    /// <summary>
    /// Event raised when a company is opened.
    /// </summary>
    public event EventHandler<CompanyOpenedEventArgs>? CompanyOpened;

    /// <summary>
    /// Event raised when a company is closed.
    /// </summary>
    public event EventHandler? CompanyClosed;

    /// <summary>
    /// Event raised just before a company is saved, allowing listeners to sync
    /// in-memory state (like the event log) to CompanyData before persistence.
    /// </summary>
    public event EventHandler? CompanySaving;

    /// <summary>
    /// Event raised when a company is saved.
    /// </summary>
    public event EventHandler? CompanySaved;

    /// <summary>
    /// Event raised when the open company's file was renamed during a save.
    /// Listeners should refresh any cached recent-company UI to reflect the new path.
    /// </summary>
    public event EventHandler? CompanyRenamed;

    /// <summary>
    /// Event raised when the company data changes.
    /// </summary>
    public event EventHandler? CompanyDataChanged;

    /// <summary>
    /// Event raised when a password is needed to open an encrypted file.
    /// </summary>
    public event EventHandler<PasswordRequiredEventArgs>? PasswordRequired;

    /// <summary>
    /// Async callback for requesting password from UI. Set this to enable async password prompts.
    /// </summary>
    public Func<string, Task<string?>>? PasswordRequestCallback { get; set; }

    /// <summary>
    /// Creates a new CompanyManager instance.
    /// </summary>
    public CompanyManager(
        FileService fileService,
        GlobalSettingsService settingsService,
        FooterService footerService,
        IErrorLogger? errorLogger = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _footerService = footerService ?? throw new ArgumentNullException(nameof(footerService));
        _errorLogger = errorLogger;
    }

    // Test-only constructor: leaves the file-system services unset. Only the in-memory surface
    // (CompanyData, MarkAsChanged/NotifyDataChanged, HasUnsavedChanges) is usable; any file
    // operation will NRE by design. Paired with CreateForTesting below.
    private CompanyManager()
    {
        _fileService = null!;
        _settingsService = null!;
        _footerService = null!;
        _errorLogger = null;
    }

    /// <summary>
    /// Creates a CompanyManager wrapping an existing in-memory <see cref="CompanyData"/> for unit
    /// tests, with no file-system dependencies. Lets tests drive ViewModels that read
    /// <c>App.CompanyManager.CompanyData</c> without opening a real company file.
    /// </summary>
    internal static CompanyManager CreateForTesting(CompanyData data)
    {
        return new CompanyManager { CompanyData = data };
    }

    /// <summary>
    /// Creates a new company file.
    /// </summary>
    /// <param name="filePath">Path where the file will be saved.</param>
    /// <param name="companyName">Name of the company.</param>
    /// <param name="password">Optional password for encryption.</param>
    /// <param name="companyInfo">Optional company information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CreateCompanyAsync(
        string filePath,
        string companyName,
        string? password = null,
        CompanyInfo? companyInfo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentException.ThrowIfNullOrEmpty(companyName);

        // Refuse to overwrite a file another running instance holds open (the Create/Save-As dialog
        // lets the user pick any existing .argo path). Checked BEFORE closing the current company so a
        // rejected create doesn't dump the user on the welcome screen. Mirrors OpenCompanyAsync.
        if (_instanceLock.IsHeldByAnotherInstance(filePath))
        {
            throw new CompanyAlreadyOpenException(filePath);
        }

        // Close any existing company
        if (IsCompanyOpen)
        {
            await CloseCompanyAsync(cancellationToken);
        }

        // Create temporary directory for the new company
        _currentTempDirectory = SecureTempDirectory.Create();

        try
        {
            // Create company directory inside temp
            var companyDir = Path.Combine(_currentTempDirectory, ToCompanyFileName(companyName));
            Directory.CreateDirectory(companyDir);

            // Create default company data
            CompanyData = new CompanyData();
            CompanyData.Settings.Company.Name = companyName;
            CompanyData.Settings.AppVersion = AppInfo.VersionNumber;

            // Apply company info if provided
            if (companyInfo != null)
            {
                CompanyData.Settings.Company = companyInfo;
                StarterCategories.AddTo(CompanyData, companyInfo.Industry);
            }

            if (string.IsNullOrEmpty(CompanyData.Settings.Company.Name))
                CompanyData.Settings.Company.Name = companyName;

            // Save all data to temp directory first (before creating receipts subdirectory,
            // otherwise GetCompanyDirectory will incorrectly find receipts/ as the company dir)
            await _fileService.SaveCompanyDataAsync(companyDir, CompanyData, cancellationToken);

            // Create receipts subdirectory after saving data files
            Directory.CreateDirectory(Path.Combine(companyDir, "receipts"));

            await _fileService.SaveCompanyAsync(filePath, _currentTempDirectory, password, cancellationToken);

            // The new company is now durably on disk, so it starts with no unsaved changes.
            CompanyData.MarkAsSaved();

            CurrentFilePath = filePath;
            _currentPassword = password;

            // Hold a read lock on the file to prevent deletion while the company is open
            AcquireFileLock(filePath);
            // Claim the cross-instance lock (the held-elsewhere case was already rejected above).
            _instanceLock.TryAcquire(filePath);

            // Add to recent companies
            _settingsService.AddRecentCompany(filePath);
            await _settingsService.SaveGlobalSettingsAsync(cancellationToken);

            // Raise event
            CompanyOpened?.Invoke(this, new CompanyOpenedEventArgs(companyName, filePath, false));
        }
        catch (Exception ex)
        {
            // Clean up on failure
            _instanceLock.Release();
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to create company");
            if (_currentTempDirectory != null && Directory.Exists(_currentTempDirectory))
            {
                Directory.Delete(_currentTempDirectory, recursive: true);
            }
            _currentTempDirectory = null;
            CompanyData = null;
            throw;
        }
    }

    /// <summary>
    /// Opens an existing company file.
    /// </summary>
    /// <param name="filePath">Path to the .argo file.</param>
    /// <param name="password">Password if the file is encrypted (or null to prompt).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> OpenCompanyAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Company file not found.", filePath);
        }

        // Fail fast if the company is already open in another instance, BEFORE closing the current
        // company. Otherwise a blocked open would still close the current company first and dump the
        // user on the welcome screen just for attempting to open a locked file.
        if (_instanceLock.IsHeldByAnotherInstance(filePath))
        {
            throw new CompanyAlreadyOpenException(filePath);
        }

        // Close any existing company (this releases the previous company's instance lock).
        if (IsCompanyOpen)
        {
            await CloseCompanyAsync(cancellationToken);
        }

        // Authoritatively claim the lock now that the current company is closed. Re-checked here (not
        // just via the peek above) to close the small race between the peek and this point. Taken
        // before the password prompt so the user isn't asked to unlock a file they can't open anyway;
        // every early exit below releases it, and on success the open company keeps holding it.
        if (!_instanceLock.TryAcquire(filePath))
        {
            throw new CompanyAlreadyOpenException(filePath);
        }

        // From here on the instance lock is held. Any failure or password cancel must release it,
        // so encryption detection, the password prompt, and the load all run inside one try; the
        // finally releases the lock unless the company opened successfully (in which case the open
        // company keeps holding it until it is closed).
        var opened = false;
        try
        {
            var isEncrypted = await _fileService.IsFileEncryptedAsync(filePath);

            if (isEncrypted && string.IsNullOrEmpty(password))
            {
                // Try async callback first (preferred)
                if (PasswordRequestCallback != null)
                {
                    password = await PasswordRequestCallback(filePath);
                    if (string.IsNullOrEmpty(password))
                    {
                        return false;
                    }
                }
                else
                {
                    // Fall back to synchronous event (for backwards compatibility)
                    var args = new PasswordRequiredEventArgs();
                    PasswordRequired?.Invoke(this, args);

                    if (args.IsCancelled || string.IsNullOrEmpty(args.Password))
                    {
                        return false;
                    }

                    password = args.Password;
                }
            }

            _currentTempDirectory = await _fileService.OpenCompanyAsync(filePath, password, cancellationToken);

            // Load company data, but defer receipts (they carry base64 image data and
            // aren't needed to show the dashboard). They load in the background and are
            // merged in by EnsureReceiptsLoadedAsync before any save or receipts UI read.
            CompanyData = await _fileService.LoadCompanyDataAsync(
                _currentTempDirectory, cancellationToken, loadReceipts: false);
            StartReceiptsBackgroundLoad(_currentTempDirectory, CompanyData);

            // Runs before the heal: it removes Payment rows, and the heal
            // recalculates invoice totals from whatever is left.
            MigrateRevenueLinkedPayments(CompanyData);

            // One-time recalc: heal any historic drift between Invoice
            // totals and the Payment rows that drive them.
            HealInvoiceTotalsIfNeeded(CompanyData);

            CurrentFilePath = filePath;
            _currentPassword = password;

            // Sync the company name from the file name so that external renames
            // (e.g., via the OS file explorer) are reflected in the app. A file named for the
            // company's own name leaves it alone, even where "/" and the like became "-".
            var fileBaseName = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(fileBaseName) && ToCompanyFileName(CompanyData.Settings.Company.Name) != fileBaseName)
            {
                CompanyData.Settings.Company.Name = fileBaseName;
            }

            // Hold a read lock on the file to prevent deletion while the company is open
            AcquireFileLock(filePath);

            // Add to recent companies
            _settingsService.AddRecentCompany(filePath);
            await _settingsService.SaveGlobalSettingsAsync(cancellationToken);

            // Raise event
            CompanyOpened?.Invoke(this, new CompanyOpenedEventArgs(
                CompanyData.Settings.Company.Name,
                filePath,
                isEncrypted));

            opened = true;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Invalid password - let UI handle retry. The finally releases the instance lock so the
            // retry (a fresh OpenCompanyAsync) can re-acquire it instead of colliding with our hold.
            throw;
        }
        catch (Exception ex)
        {
            // Clean up on failure (the finally releases the instance lock).
            ReleaseFileLock();
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to open company");
            if (_currentTempDirectory != null && Directory.Exists(_currentTempDirectory))
            {
                Directory.Delete(_currentTempDirectory, recursive: true);
            }
            _currentTempDirectory = null;
            CompanyData = null;
            throw;
        }
        finally
        {
            if (!opened)
            {
                _instanceLock.Release();
            }
        }
    }

    /// <summary>
    /// Current version of the invoice-totals healing logic. Bump this when the
    /// healing rules change so the pass re-runs once on the next open.
    /// </summary>
    public const string InvoiceTotalsHealVersion = "3";

    /// <summary>
    /// One-time recalc that heals any historic drift between Invoice totals and
    /// the Payment rows that drive them. Only runs for invoices that actually
    /// have Payment rows; spreadsheet imports without payments record AmountPaid
    /// directly on the invoice and would otherwise be wiped to zero here. Uses
    /// Recalculate (not just RecalculateFromPayments) so stored Status is also
    /// healed, e.g. Paid to PartiallyRefunded for historic invoices saved before
    /// the refund-status rules.
    ///
    /// Skipped once <see cref="CompanySettings.InvoiceTotalsHealedVersion"/>
    /// matches <see cref="InvoiceTotalsHealVersion"/>. The marker persists on the
    /// next save; the pass is idempotent, so re-running it before then is harmless.
    /// </summary>
    public static void HealInvoiceTotalsIfNeeded(CompanyData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Settings.InvoiceTotalsHealedVersion == InvoiceTotalsHealVersion)
            return;

        var invoicesWithPayments = data.Payments
            .Select(p => p.InvoiceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        // Track whether the heal actually changed anything. The version marker lives in
        // appSettings.json, but the corrected totals live in invoices.json; a settings-only
        // save would otherwise persist the marker without the healed invoices, permanently
        // skipping the heal on the next open. Flagging ChangesMade ensures a full/auto save
        // writes both. Files with no drift don't set the flag, so they get no spurious asterisk.
        var healed = false;
        foreach (var invoice in data.Invoices)
        {
            if (invoicesWithPayments.Contains(invoice.Id))
            {
                var before = (invoice.AmountPaid, invoice.AmountRefunded, invoice.Balance,
                    invoice.BalanceUSD, invoice.Status);
                InvoiceTotalsService.Recalculate(invoice, data.Payments);
                var after = (invoice.AmountPaid, invoice.AmountRefunded, invoice.Balance,
                    invoice.BalanceUSD, invoice.Status);
                if (!before.Equals(after))
                    healed = true;
            }
        }

        // Version 2: an invoice paid in full counts its revenue as collected. Payments recorded by
        // hand never marked it, and a portal refund took a paid invoice's revenue out. Only ever
        // upgrades, so nothing a user marked collected is taken out of the totals.
        foreach (var revenue in data.Revenues)
        {
            if (string.IsNullOrEmpty(revenue.InvoiceId) || RevenueAggregator.IsCollected(revenue))
                continue;

            if (data.GetInvoice(revenue.InvoiceId) is { } invoice && InvoiceTotalsService.IsPaidInFull(invoice))
            {
                revenue.PaymentStatus = RevenuePaymentStatus.Paid;
                healed = true;
            }
        }

        // Version 3: a security deposit is held, not earned (Calculations.md §4). Revenue created from
        // an invoice counted it, so it comes out, and past refunds record how much of them gave the
        // deposit back, taken from the deposit first as a refund made in the provider's dashboard is.
        foreach (var invoice in data.Invoices.Where(i => i.SecurityDeposit > 0))
            healed |= TakeDepositOutOfRevenue(data, invoice) | SplitDepositOutOfRefunds(data, invoice);

        data.Settings.InvoiceTotalsHealedVersion = InvoiceTotalsHealVersion;
        if (healed)
            data.ChangesMade = true;
    }

    private static bool TakeDepositOutOfRevenue(CompanyData data, Invoice invoice)
    {
        var deposit = invoice.SecurityDeposit;
        var pendingIds = new List<string>();
        var changed = false;

        // Only revenue still carrying the invoice's whole total counted the deposit. Revenue the user
        // entered before making the invoice from it never did.
        foreach (var revenue in data.Revenues.Where(r => r.InvoiceId == invoice.Id && !r.IsKeptDeposit
                     && r.Total > 0 && Math.Abs(r.Total - invoice.Total) < 0.01m))
        {
            var total = revenue.Total - deposit;
            var depositUSD = revenue.TotalUSD * deposit / revenue.Total;
            revenue.TotalUSD = revenue.TotalUSD * total / revenue.Total;
            revenue.FeeUSD = Math.Max(0m, revenue.FeeUSD - depositUSD);
            revenue.Fee = Math.Max(0m, revenue.Fee - deposit);
            revenue.Total = total;

            // A revenue still waiting for its rate converts from its queue entry, not the row.
            foreach (var pending in data.PendingConversions.Where(p => p.TransactionId == revenue.Id))
            {
                pending.Total = total;
                pending.Fee = Math.Max(0m, pending.Fee - deposit);
                pendingIds.Add(revenue.Id);
            }
            changed = true;
        }

        if (pendingIds.Count > 0)
            _ = PendingConversionService.Instance?.MirrorAsync(data, pendingIds);
        return changed;
    }

    private static bool SplitDepositOutOfRefunds(CompanyData data, Invoice invoice)
    {
        var refunds = data.Payments
            .Where(p => p.IsRefund && p.InvoiceId == invoice.Id)
            .OrderBy(p => p.Date)
            .ToList();
        if (refunds.Count == 0 || refunds.Any(p => p.DepositAmount > 0))
            return false;

        var held = invoice.SecurityDeposit;
        foreach (var refund in refunds)
        {
            refund.DepositAmount = SecurityDeposits.RefundPortion(Math.Abs(refund.Amount), null, held);
            held -= refund.DepositAmount;
        }
        return true;
    }

    public const string RevenuePaymentsMigrationVersion = "1";

    /// <summary>
    /// Folds payments that were attached to a Revenue into the Revenue itself, then
    /// removes them. Payments became invoice-only in v2.0.12, so these rows have
    /// nowhere to live. Any file last written by v2.0.11 or earlier can contain them.
    /// </summary>
    /// <remarks>
    /// The Revenue is marked collected before its payments go, otherwise money the user
    /// genuinely received would vanish from cash: a Revenue left Pending is excluded from
    /// the cash figures, and the payments were the only record it had arrived. For a sale
    /// only part-collected this counts the whole amount early, which is the accepted
    /// trade: Revenue has a status but no amount-paid field, so partial collection cannot
    /// be represented once the payments are gone.
    ///
    /// Deleting these also removes a double-count. A collected Revenue and its payments
    /// were both being summed into Balance Sheet cash and Cash Flow.
    ///
    /// Same version-marker approach as <see cref="HealInvoiceTotalsIfNeeded"/>: the marker
    /// lives in appSettings.json while the rows live elsewhere, so ChangesMade is flagged
    /// to force a full save rather than a settings-only one.
    /// </remarks>
    public static void MigrateRevenueLinkedPayments(CompanyData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Settings.RevenuePaymentsMigratedVersion == RevenuePaymentsMigrationVersion)
            return;

        List<Payment> revenueLinked = data.Payments
            .Where(p => !string.IsNullOrEmpty(p.RevenueId) && string.IsNullOrEmpty(p.InvoiceId))
            .ToList();

        if (revenueLinked.Count > 0)
        {
            foreach (string revenueId in revenueLinked.Select(p => p.RevenueId).Distinct())
            {
                Revenue? revenue = data.Revenues.FirstOrDefault(r => r.Id == revenueId);
                if (revenue != null && !RevenueAggregator.IsCollected(revenue))
                {
                    revenue.PaymentStatus = RevenuePaymentStatus.Paid;
                }
            }

            foreach (Payment payment in revenueLinked)
            {
                data.Payments.Remove(payment);
            }

            data.ChangesMade = true;
        }

        data.Settings.RevenuePaymentsMigratedVersion = RevenuePaymentsMigrationVersion;
    }

    /// <summary>
    /// Starts reading receipts.json on a background thread. The read does no shared-state
    /// mutation; the merge into CompanyData.Receipts happens in EnsureReceiptsLoadedAsync.
    /// </summary>
    private void StartReceiptsBackgroundLoad(string tempDirectory, CompanyData target)
    {
        lock (_receiptsLock)
        {
            _receiptsMerged = false;
            _receiptsLoadTarget = target;
            _receiptsLoadTask = Task.Run(() => _fileService.LoadReceiptsAsync(tempDirectory));
        }
    }

    /// <summary>
    /// Awaits the background receipts load started on open and merges the persisted
    /// receipts into <see cref="CompanyData"/> exactly once. Must be awaited before any
    /// save that writes receipts.json and before the receipts UI reads attachments, so
    /// the deferred load can never drop receipt data.
    ///
    /// The continuation (the merge) runs on the caller's context. All real callers are on
    /// the UI thread, so this preserves the app-wide invariant that CompanyData.Receipts
    /// is only mutated on the UI thread. Receipts added by the user during the load window
    /// are already in the list and are preserved (the persisted ones are appended).
    /// </summary>
    public async Task EnsureReceiptsLoadedAsync()
    {
        Task<List<Models.Tracking.Receipt>>? task;
        CompanyData? target;
        lock (_receiptsLock)
        {
            if (_receiptsMerged)
                return;
            task = _receiptsLoadTask;
            target = _receiptsLoadTarget;
        }
        if (task == null)
            return;

        List<Models.Tracking.Receipt> loaded;
        try
        {
            loaded = await task;
        }
        catch
        {
            // A company switch can delete the temp directory mid-read, faulting the load. That's
            // expected and harmless because we'd skip the merge anyway (different company). But if
            // we're still on the same company the failure is real - rethrow so a save doesn't
            // silently proceed without the persisted receipts and overwrite them.
            lock (_receiptsLock)
            {
                if (_receiptsMerged || !ReferenceEquals(CompanyData, target))
                    return;
            }
            throw;
        }

        lock (_receiptsLock)
        {
            if (_receiptsMerged)
                return;
            // Only merge if the company hasn't changed since the load started; otherwise these
            // receipts belong to a company that is no longer open.
            if (!ReferenceEquals(CompanyData, target))
                return;
            CompanyData?.Receipts.AddRange(loaded);
            _receiptsMerged = true;
        }
    }

    /// <summary>
    /// Saves the current company to its file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveCompanyAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCompanyOpen || CurrentFilePath == null || _currentTempDirectory == null)
            {
                throw new InvalidOperationException("No company is currently open.");
            }

            // Make sure the deferred receipts have been merged before we write
            // receipts.json, otherwise the save would drop them.
            await EnsureReceiptsLoadedAsync();

            // Notify listeners to sync in-memory state before saving
            CompanySaving?.Invoke(this, EventArgs.Empty);

            // An edit made during the awaits below may miss the file, so it must stay unsaved.
            var changeCount = CompanyData!.ChangeCount;

            // Save data to temp directory
            var companyDir = GetCompanyDirectory(_currentTempDirectory);
            await _fileService.SaveCompanyDataAsync(companyDir, CompanyData!, cancellationToken);

            // Apply pending rename before saving so the file is saved at the new path.
            // Note the rename so we can fire CompanyRenamed AFTER the file save,
            // when the footer at the new path contains the updated company name.
            var wasRenamed = false;

            if (PendingRenamePath != null && PendingRenamePath != CurrentFilePath)
            {
                var oldPath = CurrentFilePath;
                ReleaseFileLock();
                try
                {
                    // overwrite: false makes File.Move atomic: the OS rejects an existing
                    // destination, so there's no TOCTOU window between the check and the move.
                    File.Move(CurrentFilePath, PendingRenamePath, overwrite: false);
                    CurrentFilePath = PendingRenamePath;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or ArgumentException or NotSupportedException)
                {
                    // The file keeps its name. The data still gets saved, and the rename is dropped
                    // below rather than left to fail every save after this one.
                    _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to rename company file");
                }
                finally
                {
                    AcquireFileLock(CurrentFilePath);
                }

                // Update the recent companies list so the old path is replaced with the new one
                if (CurrentFilePath != oldPath)
                {
                    // Move the cross-instance lock onto the new path so a second instance is
                    // blocked from the renamed file (and freed from the old one).
                    _instanceLock.TryAcquire(CurrentFilePath);
                    _settingsService.RemoveRecentCompany(oldPath);
                    _settingsService.AddRecentCompany(CurrentFilePath);
                    await _settingsService.SaveGlobalSettingsAsync(cancellationToken);
                    wasRenamed = true;
                }

                PendingRenamePath = null;
            }

            // Release file lock before saving (save uses exclusive access), then re-acquire
            ReleaseFileLock();
            try
            {
                await _fileService.SaveCompanyAsync(CurrentFilePath, _currentTempDirectory, _currentPassword, cancellationToken);
            }
            finally
            {
                AcquireFileLock(CurrentFilePath);
            }

            CompanyData!.MarkAsSaved(changeCount);

            // Now that the file at the new path contains the freshly-written footer
            // with the updated company name, listeners can refresh recent-company
            // UI from disk and pick up the new name.
            if (wasRenamed)
            {
                CompanyRenamed?.Invoke(this, EventArgs.Empty);
            }

            // Raise event
            CompanySaved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Saves the current company to a new file location.
    /// </summary>
    /// <param name="newFilePath">New file path.</param>
    /// <param name="newPassword">New password (null to keep existing, empty string to remove).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveCompanyAsAsync(
        string newFilePath,
        string? newPassword = null,
        CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCompanyOpen || _currentTempDirectory == null)
            {
                throw new InvalidOperationException("No company is currently open.");
            }

            ArgumentException.ThrowIfNullOrEmpty(newFilePath);

            // Refuse to Save As over a file another running instance holds open (the native save
            // dialog lets the user pick any existing .argo). A path this instance itself holds returns
            // false here, so saving onto our own current file is still allowed.
            if (_instanceLock.IsHeldByAnotherInstance(newFilePath))
            {
                throw new CompanyAlreadyOpenException(newFilePath);
            }

            // Merge deferred receipts before writing receipts.json (see SaveCompanyAsync).
            await EnsureReceiptsLoadedAsync();

            // Notify listeners to sync in-memory state before saving
            CompanySaving?.Invoke(this, EventArgs.Empty);
            var changeCount = CompanyData!.ChangeCount;

            // Determine password to use
            var passwordToUse = newPassword ?? _currentPassword;
            if (newPassword == string.Empty)
            {
                passwordToUse = null; // Remove encryption
            }

            // Sync company name with the new file name so they stay consistent
            var newName = Path.GetFileNameWithoutExtension(newFilePath);
            if (!string.IsNullOrEmpty(newName) && ToCompanyFileName(CompanyData!.Settings.Company.Name) != newName)
            {
                CompanyData.Settings.Company.Name = newName;
            }

            // Save data to temp directory
            var companyDir = GetCompanyDirectory(_currentTempDirectory);
            await _fileService.SaveCompanyDataAsync(companyDir, CompanyData!, cancellationToken);

            // Release file lock before saving, then re-acquire on new path
            ReleaseFileLock();
            try
            {
                await _fileService.SaveCompanyAsync(newFilePath, _currentTempDirectory, passwordToUse, cancellationToken);

                // Update current file path and password
                CurrentFilePath = newFilePath;
                _currentPassword = passwordToUse;
                // Move the cross-instance lock onto the Save-As target so it guards the new file.
                _instanceLock.TryAcquire(newFilePath);
                // A deferred rename targeted the OLD path; once the working file has moved to a new
                // path via Save As it no longer applies, so a later normal Save must not act on it.
                PendingRenamePath = null;
            }
            finally
            {
                AcquireFileLock(newFilePath);
            }

            CompanyData!.MarkAsSaved(changeCount);

            // Add to recent companies
            _settingsService.AddRecentCompany(newFilePath);
            await _settingsService.SaveGlobalSettingsAsync(cancellationToken);

            // Raise event
            CompanySaved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Closes the current company.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseCompanyAsync(CancellationToken cancellationToken = default)
    {
        ReleaseFileLock();
        _instanceLock.Release();

        if (_currentTempDirectory != null)
        {
            await _fileService.CloseCompanyAsync(_currentTempDirectory);
            _currentTempDirectory = null;
        }

        CompanyData = null;
        CurrentFilePath = null;
        _currentPassword = null;
        PendingRenamePath = null;

        // Drop any in-flight receipts load so it can't merge into the next company.
        lock (_receiptsLock)
        {
            _receiptsLoadTask = null;
            _receiptsLoadTarget = null;
            _receiptsMerged = false;
        }

        // Raise event
        CompanyClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the company logo from a file path.
    /// </summary>
    /// <param name="logoPath">Path to the logo image file.</param>
    public async Task SetCompanyLogoAsync(string logoPath)
    {
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");

        if (!File.Exists(logoPath))
            throw new FileNotFoundException("Logo file not found.", logoPath);

        // Generate a unique filename for the logo
        var extension = Path.GetExtension(logoPath);
        var logoFileName = $"logo{extension}";
        var destPath = Path.Combine(_currentTempDirectory, logoFileName);

        // Copy the logo file to the temp directory
        await Task.Run(() => File.Copy(logoPath, destPath, overwrite: true));

        // Update settings
        CompanyData.Settings.Company.LogoFileName = logoFileName;
        CompanyData.ChangesMade = true;

        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes the current company logo.
    /// </summary>
    public async Task RemoveCompanyLogoAsync()
    {
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");

        var logoFileName = CompanyData.Settings.Company.LogoFileName;
        if (string.IsNullOrEmpty(logoFileName))
            return;

        var logoPath = Path.Combine(_currentTempDirectory, logoFileName);

        // Delete the logo file if it exists
        if (File.Exists(logoPath))
        {
            await Task.Run(() => File.Delete(logoPath));
        }

        // Update settings
        CompanyData.Settings.Company.LogoFileName = null;
        CompanyData.ChangesMade = true;

        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets a customer's avatar from a source image on disk. The image is resized down
    /// to a small PNG inside the company temp directory, so it gets bundled into the
    /// encrypted .argo file on next save.
    /// </summary>
    public Task SetCustomerAvatarAsync(Customer customer, string sourceImagePath)
        => SetEntityAvatarFromPathAsync(customer, sourceImagePath, CustomerAvatarSubdirectory);

    /// <summary>
    /// Removes a customer's avatar image. Safe to call when no avatar is set.
    /// </summary>
    public Task RemoveCustomerAvatarAsync(Customer customer)
        => RemoveEntityAvatarAsync(customer);

    /// <summary>
    /// Sets a supplier's avatar from a source image on disk. Same semantics as the
    /// customer variant: image is resized to a PNG inside the company temp directory.
    /// </summary>
    public Task SetSupplierAvatarAsync(Supplier supplier, string sourceImagePath)
        => SetEntityAvatarFromPathAsync(supplier, sourceImagePath, SupplierAvatarSubdirectory);

    /// <summary>
    /// Sets a supplier's avatar from already-loaded bytes (e.g. a downloaded favicon).
    /// The bytes can be in any Skia-supported format (ICO, PNG, JPG, ...) and are
    /// re-encoded to PNG.
    /// </summary>
    public Task SetSupplierAvatarFromBytesAsync(Supplier supplier, byte[] imageBytes)
        => SetEntityAvatarFromBytesAsync(supplier, imageBytes, SupplierAvatarSubdirectory);

    /// <summary>
    /// Removes a supplier's avatar image. Safe to call when no avatar is set.
    /// </summary>
    public Task RemoveSupplierAvatarAsync(Supplier supplier)
        => RemoveEntityAvatarAsync(supplier);

    // Path.GetInvalidFileNameChars lists only this platform's, and a file made on a Mac is
    // often copied to a PC, so Windows' list is always added.
    private static readonly char[] UnsafeFileNameChars =
        [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// The file name, without ".argo", for a company called <paramref name="companyName"/>.
    /// Characters no file name can hold, such as "/", become "-". The company keeps the name as
    /// typed; only its file is named this.
    /// </summary>
    public static string ToCompanyFileName(string companyName)
    {
        var fileName = new string(companyName
            .Select(c => char.IsControl(c) || UnsafeFileNameChars.Contains(c) ? '-' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(fileName) ? "Company" : fileName;
    }

    private static string SanitizeForFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Guid.NewGuid().ToString("N");

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Where(c => !invalid.Contains(c) && c != '.').ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }

    /// <summary>
    /// Renames a customer's Id, cascading to every reference inside the open company
    /// (invoices, revenues, payments, rentals, recurring invoices, returns) and moving
    /// the avatar file. Throws if newId is empty, equals an existing customer's Id,
    /// or if no company is open. A no-op when newId equals the current Id.
    /// </summary>
    public void ChangeCustomerId(Customer customer, string newId)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (CompanyData == null || _currentTempDirectory == null)
            throw new InvalidOperationException("No company is currently open.");

        var trimmed = newId.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Customer ID cannot be empty.", nameof(newId));

        var oldId = customer.Id;
        if (string.Equals(oldId, trimmed, StringComparison.Ordinal))
            return;

        if (CompanyData.Customers.Any(c => !ReferenceEquals(c, customer) && c.Id == trimmed))
            throw new InvalidOperationException($"Another customer already uses ID '{trimmed}'.");

        // Cascade FK references
        foreach (var inv in CompanyData.Invoices)
            if (inv.CustomerId == oldId) inv.CustomerId = trimmed;
        foreach (var rev in CompanyData.Revenues)
            if (rev.CustomerId == oldId) rev.CustomerId = trimmed;
        foreach (var pay in CompanyData.Payments)
            if (pay.CustomerId == oldId) pay.CustomerId = trimmed;
        foreach (var rent in CompanyData.Rentals)
            if (rent.CustomerId == oldId) rent.CustomerId = trimmed;
        foreach (var ri in CompanyData.RecurringInvoices)
        {
            if (ri.CustomerId == oldId) ri.CustomerId = trimmed;
            // The embedded template is what each generated occurrence inherits its CustomerId from, so
            // it must follow the rename too, otherwise future invoices point at the old, gone Id.
            if (ri.Template != null && ri.Template.CustomerId == oldId) ri.Template.CustomerId = trimmed;
        }
        // Recurring transactions hold the same kind of template, for the same reason.
        foreach (var rt in CompanyData.RecurringTransactions)
            if (rt.RevenueTemplate != null && rt.RevenueTemplate.CustomerId == oldId)
                rt.RevenueTemplate.CustomerId = trimmed;
        foreach (var ret in CompanyData.Returns)
            if (ret.CustomerId == oldId) ret.CustomerId = trimmed;

        TryMoveEntityAvatarOnRename(customer, trimmed, CustomerAvatarSubdirectory);

        customer.Id = trimmed;
        customer.UpdatedAt = DateTime.UtcNow;
        // Cached Id→Customer lookup is keyed on the old Id; invalidate so the next
        // GetCustomer(newId) rebuilds the dictionary.
        CompanyData.InvalidateLookupCaches();
        CompanyData.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renames a supplier's Id, cascading to every reference inside the open company
    /// (products, purchase orders, returns, expenses).
    /// </summary>
    public void ChangeSupplierId(Supplier supplier, string newId)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        if (CompanyData == null)
            throw new InvalidOperationException("No company is currently open.");

        var trimmed = newId.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Supplier ID cannot be empty.", nameof(newId));

        var oldId = supplier.Id;
        if (string.Equals(oldId, trimmed, StringComparison.Ordinal))
            return;

        if (CompanyData.Suppliers.Any(s => !ReferenceEquals(s, supplier) && s.Id == trimmed))
            throw new InvalidOperationException($"Another supplier already uses ID '{trimmed}'.");

        foreach (var prod in CompanyData.Products)
            if (prod.SupplierId == oldId) prod.SupplierId = trimmed;
        foreach (var po in CompanyData.PurchaseOrders)
            if (po.SupplierId == oldId) po.SupplierId = trimmed;
        foreach (var ret in CompanyData.Returns)
            if (ret.SupplierId == oldId) ret.SupplierId = trimmed;
        foreach (var exp in CompanyData.Expenses)
            if (exp.SupplierId == oldId) exp.SupplierId = trimmed;
        // The template each generated occurrence is cloned from, so it has to follow the
        // rename or every future expense points at a supplier that is gone.
        foreach (var rt in CompanyData.RecurringTransactions)
            if (rt.ExpenseTemplate != null && rt.ExpenseTemplate.SupplierId == oldId)
                rt.ExpenseTemplate.SupplierId = trimmed;

        TryMoveEntityAvatarOnRename(supplier, trimmed, SupplierAvatarSubdirectory);

        supplier.Id = trimmed;
        supplier.UpdatedAt = DateTime.UtcNow;
        CompanyData.InvalidateLookupCaches();
        CompanyData.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renames a product's Id, cascading to every reference inside the open company
    /// (inventory items, line items on invoices/revenues/expenses, purchase orders,
    /// lost-damaged records, return items).
    /// </summary>
    public void ChangeProductId(Product product, string newId)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (CompanyData == null)
            throw new InvalidOperationException("No company is currently open.");

        var trimmed = newId.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Product ID cannot be empty.", nameof(newId));

        var oldId = product.Id;
        if (string.Equals(oldId, trimmed, StringComparison.Ordinal))
            return;

        if (CompanyData.Products.Any(p => !ReferenceEquals(p, product) && p.Id == trimmed))
            throw new InvalidOperationException($"Another product already uses ID '{trimmed}'.");

        foreach (var item in CompanyData.Inventory)
            if (item.ProductId == oldId) item.ProductId = trimmed;
        foreach (var ld in CompanyData.LostDamaged)
            if (ld.ProductId == oldId) ld.ProductId = trimmed;

        // Line items live on the parent (Invoice / Revenue / Expense / PurchaseOrder).
        // Revenue and Expense derive from Transaction which exposes LineItems<LineItem>;
        // PurchaseOrder uses its own PurchaseOrderLineItem so the loop is inlined.
        foreach (var inv in CompanyData.Invoices)
            CascadeProductIdInLineItems(inv.LineItems, oldId, trimmed);
        foreach (var rev in CompanyData.Revenues)
            CascadeProductIdInLineItems(rev.LineItems, oldId, trimmed);
        foreach (var exp in CompanyData.Expenses)
            CascadeProductIdInLineItems(exp.LineItems, oldId, trimmed);
        foreach (var po in CompanyData.PurchaseOrders)
            foreach (var line in po.LineItems)
                if (line.ProductId == oldId) line.ProductId = trimmed;

        // Both kinds of recurring schedule keep a template whose line items are cloned into
        // every occurrence, so a product rename has to reach inside them as well.
        foreach (var ri in CompanyData.RecurringInvoices)
            if (ri.Template != null) CascadeProductIdInLineItems(ri.Template.LineItems, oldId, trimmed);
        foreach (var rt in CompanyData.RecurringTransactions)
        {
            if (rt.ExpenseTemplate != null) CascadeProductIdInLineItems(rt.ExpenseTemplate.LineItems, oldId, trimmed);
            if (rt.RevenueTemplate != null) CascadeProductIdInLineItems(rt.RevenueTemplate.LineItems, oldId, trimmed);
        }

        // Return items live nested inside Return.Items
        foreach (var ret in CompanyData.Returns)
        {
            foreach (var ri in ret.Items)
                if (ri.ProductId == oldId) ri.ProductId = trimmed;
        }

        product.Id = trimmed;
        product.UpdatedAt = DateTime.UtcNow;
        CompanyData.InvalidateLookupCaches();
        CompanyData.ChangesMade = true;
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void CascadeProductIdInLineItems(List<LineItem> lineItems, string oldId, string newId)
    {
        foreach (var li in lineItems)
            if (li.ProductId == oldId) li.ProductId = newId;
    }

    /// <summary>
    /// Gets file information from a company file without fully opening it.
    /// </summary>
    /// <param name="filePath">Path to the .argo file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File footer containing metadata.</returns>
    public async Task<FileFooter?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        return await _footerService.ReadFooterAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Extracts the company logo from a .argo file without fully opening it.
    /// Returns null for files without a logo.
    /// </summary>
    public Task<byte[]?> ExtractLogoFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _fileService.ExtractLogoFromFileAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Gets the list of recent companies with their metadata.
    /// </summary>
    /// <returns>List of recent company info.</returns>
    public async Task<List<RecentCompanyInfo>> GetRecentCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var recentPaths = _settingsService.GetValidRecentCompanies();

        // Footer reads are pure I/O on independent files opened with FileShare.Read, so we can
        // run them concurrently. Per-task try/catch preserves the previous skip-on-error behavior;
        // Task.WhenAll returns results in input order, preserving most-recent-first ordering.
        var tasks = recentPaths.Select(async path =>
        {
            try
            {
                var footer = await GetFileInfoAsync(path, cancellationToken);
                if (footer == null)
                    return null;

                return new RecentCompanyInfo
                {
                    FilePath = path,
                    CompanyName = footer.CompanyName,
                    IsEncrypted = footer.IsEncrypted,
                    ModifiedAt = footer.ModifiedAt,
                    LogoThumbnail = footer.LogoThumbnail
                };
            }
            catch
            {
                // File may be corrupted or inaccessible, skip it
                return null;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).Cast<RecentCompanyInfo>().ToList();
    }

    /// <summary>
    /// Changes the password for the current company.
    /// This re-encrypts the file with the new password WITHOUT saving any pending data changes.
    /// </summary>
    /// <param name="newPassword">New password (null or empty to remove encryption).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ChangePasswordAsync(string? newPassword, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCompanyOpen || CurrentFilePath == null || _currentTempDirectory == null)
            {
                throw new InvalidOperationException("No company is currently open.");
            }

            // Determine password to use
            var passwordToUse = string.IsNullOrEmpty(newPassword) ? null : newPassword;

            // Re-encrypt what is saved, not the working folder. That already holds changes the user
            // hasn't saved, such as a deleted logo, which quitting without saving could then not undo.
            var savedCopy = await _fileService.OpenCompanyAsync(CurrentFilePath, _currentPassword, cancellationToken);
            try
            {
                // Release file lock before saving (save uses exclusive access), then re-acquire
                ReleaseFileLock();
                try
                {
                    await _fileService.SaveCompanyAsync(CurrentFilePath, savedCopy, passwordToUse, cancellationToken);
                }
                finally
                {
                    AcquireFileLock(CurrentFilePath);
                }
            }
            finally
            {
                await _fileService.CloseCompanyAsync(savedCopy);
            }

            _currentPassword = passwordToUse;

            // Note: We intentionally do NOT:
            // - Call SaveCompanyDataAsync (preserves unsaved changes in memory)
            // - Call _companyData.MarkAsSaved() (keeps HasUnsavedChanges state)
            // - Raise CompanySaved event (no data was saved, only password changed)
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Exports a backup of the current company to a .argobk file.
    /// This saves the current in-memory state to a separate file without affecting
    /// the working file, file lock, or unsaved changes state.
    /// </summary>
    /// <param name="backupPath">The path for the .argobk backup file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCompanyOpen || _currentTempDirectory == null || CompanyData == null)
            {
                throw new InvalidOperationException("No company is currently open.");
            }

            ArgumentException.ThrowIfNullOrEmpty(backupPath);

            // Merge deferred receipts before writing receipts.json (see SaveCompanyAsync).
            await EnsureReceiptsLoadedAsync();

            // Sync in-memory state before exporting (e.g., event log)
            CompanySaving?.Invoke(this, EventArgs.Empty);

            // Save current data to temp directory
            var companyDir = GetCompanyDirectory(_currentTempDirectory);
            await _fileService.SaveCompanyDataAsync(companyDir, CompanyData, cancellationToken);

            // Export the entire temp directory as-is (includes receipts/). Use the working file's
            // password so a backup of an encrypted company is itself encrypted; passing null wrote
            // the backup in plaintext, letting anyone restore it without the password.
            await _fileService.SaveCompanyAsync(backupPath, _currentTempDirectory, _currentPassword, cancellationToken);

            // Note: We intentionally do NOT:
            // - Change _currentFilePath (backup is a separate file)
            // - Release/acquire file lock (working file stays locked)
            // - Mark as saved (unsaved changes state is unchanged)
            // - Add to recent companies (backups are not working files)
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Saves only the payment-sync-related files (payments, invoices, revenues, id counters, settings)
    /// to the temp directory and repackages the .argo file, without triggering a full company save
    /// workflow (no CompanySaving/CompanySaved events, no MarkAsSaved).
    /// </summary>
    /// <returns>
    /// Whether <paramref name="companyData"/> was written to its file. False when it is no longer the
    /// open company or no company file is open, in which case nothing was saved.
    /// </returns>
    public async Task<bool> SavePaymentSyncAsync(CompanyData companyData, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            // Opening or closing a company doesn't take the save lock, so the company the payments
            // were synced into may have gone while this waited for it.
            if (!IsCompanyOpen || CurrentFilePath == null || _currentTempDirectory == null || CompanyData == null
                || !ReferenceEquals(CompanyData, companyData))
                return false;

            // Merge deferred receipts before writing receipts.json (see SaveCompanyAsync).
            // This save path is auto-triggered by portal sync shortly after open, so the
            // gate here is what prevents an early sync from dropping receipts.
            await EnsureReceiptsLoadedAsync();
            if (!ReferenceEquals(CompanyData, companyData) || CurrentFilePath == null || _currentTempDirectory == null)
                return false;

            var companyDir = GetCompanyDirectory(_currentTempDirectory);
            await _fileService.SaveCompanyDataAsync(companyDir, CompanyData, cancellationToken);

            // Repackage the .argo file so changes persist across restarts
            ReleaseFileLock();
            try
            {
                await _fileService.SaveCompanyAsync(CurrentFilePath, _currentTempDirectory, _currentPassword, cancellationToken);
            }
            finally
            {
                AcquireFileLock(CurrentFilePath);
            }

            return true;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Persists ONLY appSettings.json, used when a single setting (e.g. owner
    /// email) changes and we don't want to flush other in-memory edits to disk.
    /// Writes the latest <see cref="CompanyData.Settings"/> to the temp dir
    /// and re-zips the .argo. The other 29 domain JSON files in the temp dir
    /// are left untouched (they still hold whatever was last saved). Does NOT
    /// call <c>MarkAsSaved</c> so an outstanding ChangesMade flag for OTHER
    /// edits is preserved.
    /// </summary>
    public async Task SaveSettingsOnlyAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCompanyOpen || CurrentFilePath == null || _currentTempDirectory == null || CompanyData == null)
                return;

            var companyDir = GetCompanyDirectory(_currentTempDirectory);

            // These markers record work done on the other files: the open-time repairs and the
            // forecast backtest. This save writes none of those files, so the markers stay as the
            // temp copy has them; writing the new ones would make the next open skip that work.
            var onDisk = await _fileService.ReadJsonAsync<CompanySettings>(companyDir, "appSettings.json", cancellationToken);
            var settings = JsonSerializer.Deserialize<CompanySettings>(
                JsonSerializer.Serialize(CompanyData.Settings, FileService.JsonOptions),
                FileService.JsonOptions)!;
            settings.InvoiceTotalsHealedVersion = onDisk?.InvoiceTotalsHealedVersion;
            settings.RevenuePaymentsMigratedVersion = onDisk?.RevenuePaymentsMigratedVersion;
            settings.BacktestVersion = onDisk?.BacktestVersion;
            settings.LastBacktestedMonth = onDisk?.LastBacktestedMonth;

            await _fileService.WriteJsonAsync(companyDir, "appSettings.json", settings, cancellationToken);

            ReleaseFileLock();
            try
            {
                await _fileService.SaveCompanyAsync(CurrentFilePath, _currentTempDirectory, _currentPassword, cancellationToken);
            }
            finally
            {
                AcquireFileLock(CurrentFilePath);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Marks the company data as changed.
    /// </summary>
    public void MarkAsChanged()
    {
        if (CompanyData != null)
        {
            CompanyData.ChangesMade = true;
            CompanyDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Notifies listeners that company data has been updated without marking as modified.
    /// Used for in-memory transformations like sample data time-shifting.
    /// </summary>
    public void NotifyDataChanged()
    {
        CompanyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the containing folder for the current company file.
    /// </summary>
    public void ShowInFolder()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
            return;

        var directory = Path.GetDirectoryName(CurrentFilePath);
        if (string.IsNullOrEmpty(directory))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{CurrentFilePath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"-R \"{CurrentFilePath}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                // Try common file managers
                System.Diagnostics.Process.Start("xdg-open", directory);
            }
        }
        catch
        {
            // Ignore errors opening folder
        }
    }

    private static string GetCompanyDirectory(string tempDirectory)
    {
        var subdirs = Directory.GetDirectories(tempDirectory);
        if (subdirs.Length == 0) return tempDirectory;
        if (subdirs.Length == 1) return subdirs[0];

        // Multiple subdirectories: pick the one that contains company data files
        // (exclude known non-company directories like "receipts")
        var companyDir = subdirs.FirstOrDefault(d =>
            File.Exists(Path.Combine(d, "appSettings.json")) ||
            File.Exists(Path.Combine(d, "revenues.json")) ||
            File.Exists(Path.Combine(d, "expenses.json")));

        return companyDir ?? subdirs[0];
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        ReleaseFileLock();
        _instanceLock.Dispose();
        _saveLock.Dispose();
        _currentPassword = null;

        // Clean up temp directory
        if (_currentTempDirectory != null && Directory.Exists(_currentTempDirectory))
        {
            try
            {
                Directory.Delete(_currentTempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _isDisposed = true;
    }

    /// <summary>
    /// Acquires a read lock on the company file to prevent deletion while open.
    /// </summary>
    private void AcquireFileLock(string filePath)
    {
        ReleaseFileLock();
        try
        {
            _fileLock = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Could not acquire file lock on {filePath}: {ex.Message}", "FileLock");
        }
    }

    /// <summary>
    /// Releases the file lock on the company file.
    /// </summary>
    private void ReleaseFileLock()
    {
        if (_fileLock != null)
        {
            _fileLock.Dispose();
            _fileLock = null;
        }
    }
}

/// <summary>
/// Event args for company opened event.
/// </summary>
public class CompanyOpenedEventArgs(string companyName, string filePath, bool isEncrypted) : EventArgs
{
    public string CompanyName { get; } = companyName;
    public string FilePath { get; } = filePath;
    public bool IsEncrypted { get; } = isEncrypted;
}

/// <summary>
/// Event args for password required event.
/// </summary>
public class PasswordRequiredEventArgs : EventArgs
{
    public string? Password { get; set; }
    public bool IsCancelled { get; set; }
}

/// <summary>
/// Information about a recent company.
/// </summary>
public class RecentCompanyInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? LogoThumbnail { get; set; }
}

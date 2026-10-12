using System.Security.Cryptography;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Security;
using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for handling .argo company file operations.
/// </summary>
/// <param name="recoveryPublicKeyPem">
/// Recovery public key to wrap each file's data key under. Overridable so tests can verify
/// the recovery path end to end without depending on a configured build.
///
/// Null means use the key embedded in this build, which is the normal case. An empty string
/// means write no recovery path at all, which is how a test reproduces an unconfigured build
/// even when this build does have a key.
/// </param>
public class FileService(
    CompressionService compressionService,
    FooterService footerService,
    IEncryptionService? encryptionService = null,
    string? recoveryPublicKeyPem = null)
    : IFileService
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A converter listed here outranks the [JsonConverter] attribute on the enum, so the lenient
        // ones must come before the general enum converter or they never run.
        Converters =
        {
            new RevenuePaymentStatusJsonConverter(),
            new PaymentSourceJsonConverter(),
            new JsonStringEnumConverter()
        }
    };

    /// <inheritdoc />
    public async Task CreateCompanyAsync(
        string filePath,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        var tempDirectory = SecureTempDirectory.Create();

        try
        {
            var companyDir = Path.Combine(tempDirectory, CompanyManager.ToCompanyFileName(companyName));
            Directory.CreateDirectory(companyDir);

            // Create default company data
            var companyData = new CompanyData();
            companyData.Settings.Company.Name = companyName;
            companyData.Settings.AppVersion = "1.0.0";

            // Write initial settings file
            await WriteJsonAsync(companyDir, "appSettings.json", companyData.Settings, cancellationToken);

            // Write empty data files
            await WriteJsonAsync(companyDir, "customers.json", companyData.Customers, cancellationToken);
            await WriteJsonAsync(companyDir, "products.json", companyData.Products, cancellationToken);
            await WriteJsonAsync(companyDir, "suppliers.json", companyData.Suppliers, cancellationToken);
            await WriteJsonAsync(companyDir, "categories.json", companyData.Categories, cancellationToken);
            await WriteJsonAsync(companyDir, "accountants.json", companyData.Accountants, cancellationToken);
            await WriteJsonAsync(companyDir, "locations.json", companyData.Locations, cancellationToken);
            await WriteJsonAsync(companyDir, "revenues.json", companyData.Revenues, cancellationToken);
            await WriteJsonAsync(companyDir, "expenses.json", companyData.Expenses, cancellationToken);
            await WriteJsonAsync(companyDir, "invoices.json", companyData.Invoices, cancellationToken);
            await WriteJsonAsync(companyDir, "quotes.json", companyData.Quotes, cancellationToken);
            await WriteJsonAsync(companyDir, "payments.json", companyData.Payments, cancellationToken);
            await WriteJsonAsync(companyDir, "recurringInvoices.json", companyData.RecurringInvoices, cancellationToken);
            await WriteJsonAsync(companyDir, "recurringTransactions.json", companyData.RecurringTransactions, cancellationToken);
            await WriteJsonAsync(companyDir, "inventory.json", companyData.Inventory, cancellationToken);
            await WriteJsonAsync(companyDir, "stockAdjustments.json", companyData.StockAdjustments, cancellationToken);
            await WriteJsonAsync(companyDir, "stockTransfers.json", companyData.StockTransfers, cancellationToken);
            await WriteJsonAsync(companyDir, "purchaseOrders.json", companyData.PurchaseOrders, cancellationToken);
            await WriteJsonAsync(companyDir, "rentalInventory.json", companyData.RentalInventory, cancellationToken);
            await WriteJsonAsync(companyDir, "rentals.json", companyData.Rentals, cancellationToken);
            await WriteJsonAsync(companyDir, "returns.json", companyData.Returns, cancellationToken);
            await WriteJsonAsync(companyDir, "lostDamaged.json", companyData.LostDamaged, cancellationToken);
            await WriteJsonAsync(companyDir, "receipts.json", companyData.Receipts, cancellationToken);
            await WriteJsonAsync(companyDir, "idCounters.json", companyData.IdCounters, cancellationToken);
            await WriteJsonAsync(companyDir, "eventLog.json", companyData.EventLog, cancellationToken);
            await WriteJsonAsync(companyDir, "pendingConversions.json", companyData.PendingConversions, cancellationToken);
            await WriteJsonAsync(companyDir, "bankImportSessions.json", companyData.BankImportSessions, cancellationToken);
            await WriteJsonAsync(companyDir, "employees.json", companyData.Employees, cancellationToken);
            await WriteJsonAsync(companyDir, "payRuns.json", companyData.PayRuns, cancellationToken);

            Directory.CreateDirectory(Path.Combine(companyDir, "receipts"));

            await SaveCompanyAsync(filePath, companyDir, null, cancellationToken);
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <inheritdoc />
    public async Task<string> OpenCompanyAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        // Read footer first
        var footer = await footerService.ReadFooterAsync(filePath, cancellationToken)
            ?? throw new InvalidDataException("Invalid file format or corrupted file.");

        // Guard: a file written by a newer build may use an envelope layout this build doesn't understand.
        if (footer.FormatVersion > FileFormatConstants.FormatVersion)
            throw new CompanyFileTooNewException(footer.Version, AppInfo.VersionNumber);

        // Guard: encrypted files need a password and the encryption service.
        // This is a cheap check, no key derivation yet.
        if (footer.IsEncrypted)
        {
            if (string.IsNullOrEmpty(password))
                throw new UnauthorizedAccessException("Password is required for this file.");

            if (encryptionService == null)
                throw new InvalidOperationException("Encryption service not available.");
        }

        // Read content (excluding footer)
        await using var contentStream = await footerService.ReadContentAsync(filePath, cancellationToken);

        Stream dataStream = contentStream;
        if (footer.IsEncrypted && encryptionService != null)
        {
            dataStream = footer.FormatVersion >= 2
                ? DecryptEnvelope(contentStream, password!, footer)
                // Format version 1: the archive was encrypted directly with the
                // password-derived key. Verify and decrypt in a single PBKDF2 pass.
                : await encryptionService.DecryptWithVerificationAsync(
                    contentStream, password!, footer.Salt!, footer.Iv!, footer.PasswordHash!);
        }

        // Decompressed straight into the extractor rather than into memory first. A failure part-way
        // leaves decrypted files behind, so the directory goes with it.
        var tempDirectory = SecureTempDirectory.Create();
        try
        {
            await compressionService.ExtractGZipTarArchiveAsync(dataStream, tempDirectory, cancellationToken);
        }
        catch
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { /* best effort */ }
            throw;
        }

        return tempDirectory;
    }

    /// <summary>
    /// Opens a format version 2 envelope: verify the password, use it to unwrap the file's
    /// data key, then decrypt the archive with that data key.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The password is wrong.</exception>
    /// <exception cref="InvalidDataException">Envelope fields are missing or malformed.</exception>
    private Stream DecryptEnvelope(Stream contentStream, string password, FileFooter footer)
    {
        if (string.IsNullOrEmpty(footer.Salt) ||
            string.IsNullOrEmpty(footer.Iv) ||
            string.IsNullOrEmpty(footer.PasswordHash) ||
            string.IsNullOrEmpty(footer.WrappedKey) ||
            string.IsNullOrEmpty(footer.KeyWrapNonce))
        {
            throw new InvalidDataException("This encrypted file is missing its key envelope data.");
        }

        var saltBytes = Convert.FromBase64String(footer.Salt);
        KeyDerivation.DeriveKeyAndHash(password, saltBytes, out var kek, out var verifyHash);

        byte[]? dataKey = null;
        try
        {
            // Constant-time check first, so a wrong password reports itself as a wrong password rather than as a GCM tag mismatch, which the caller could not tell apart from a corrupt file.
            var expected = Convert.FromBase64String(footer.PasswordHash);
            if (!CryptographicOperations.FixedTimeEquals(verifyHash, expected))
                throw new UnauthorizedAccessException("Invalid password.");

            dataKey = KeyEnvelope.Unwrap(
                Convert.FromBase64String(footer.WrappedKey),
                kek,
                Convert.FromBase64String(footer.KeyWrapNonce));

            var plaintext = encryptionService!.DecryptWithKey(
                ReadAllBytes(contentStream), dataKey, Convert.FromBase64String(footer.Iv));

            return new MemoryStream(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(verifyHash);
            if (dataKey != null)
                CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Reads a stream fully into a byte array.
    /// </summary>
    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream memoryStream)
            return memoryStream.ToArray();

        using var buffer = new MemoryStream();
        if (stream.CanSeek)
            stream.Position = 0;
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    /// <remarks>Virtual so tests can hold a save part-way through.</remarks>
    public virtual async Task SaveCompanyAsync(
        string filePath,
        string tempDirectory,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        // Create TAR archive - use includeBaseDirectory: false to avoid nesting under temp dir GUID
        await using var tarStream = await compressionService.CreateTarArchiveAsync(
            tempDirectory, includeBaseDirectory: false, cancellationToken);

        await using var compressedStream = await compressionService.CompressGZipAsync(
            tarStream, cancellationToken: cancellationToken);

        // Encrypt if password provided. From format version 2 the archive is encrypted with a randomly generated data key instead of directly with the password-derived key.
        Stream contentStream = compressedStream;
        string? salt = null;
        string? iv = null;
        string? passwordHash = null;
        string? wrappedKey = null;
        string? keyWrapNonce = null;
        string? recoveryBlob = null;
        string? recoveryKeyId = null;

        // The footer records IsEncrypted from whether a password was given, so a password without an encryption service would write plaintext claiming to be encrypted.
        if (!string.IsNullOrEmpty(password) && encryptionService == null)
            throw new InvalidOperationException("Encryption service not available.");

        if (!string.IsNullOrEmpty(password))
        {
            var saltBytes = KeyDerivation.GenerateSalt();
            var dataNonce = KeyDerivation.GenerateIv();
            var wrapNonce = KeyEnvelope.GenerateWrapNonce();

            // One PBKDF2 pass yields both the key that wraps the data key and the hash
            // used to tell a wrong password apart from a corrupt file.
            KeyDerivation.DeriveKeyAndHash(password, saltBytes, out var kek, out var verifyHash);
            var dataKey = KeyEnvelope.GenerateDataKey();
            try
            {
                salt = Convert.ToBase64String(saltBytes);
                iv = Convert.ToBase64String(dataNonce);
                passwordHash = Convert.ToBase64String(verifyHash);
                keyWrapNonce = Convert.ToBase64String(wrapNonce);
                wrappedKey = Convert.ToBase64String(KeyEnvelope.Wrap(dataKey, kek, wrapNonce));

                // Null when no recovery key is configured for this build. The file is
                // still valid, it just has no recovery path.
                recoveryBlob = RecoveryKeyProvider.TryWrapDataKey(dataKey, recoveryPublicKeyPem);
                recoveryKeyId = recoveryBlob is null ? null : RecoveryKeyProvider.CurrentKeyId;

                var plaintext = ReadAllBytes(compressedStream);
                contentStream = new MemoryStream(
                    encryptionService!.EncryptWithKey(plaintext, dataKey, dataNonce));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
                CryptographicOperations.ZeroMemory(verifyHash);
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }

        // Create footer, read settings once and share across footer fields
        var cachedSettings = ReadSettingsFromDirectory(tempDirectory);
        var footer = new FileFooter
        {
            Version = GetAppVersionFromDirectory(cachedSettings),
            FormatVersion = FileFormatConstants.FormatVersion,
            IsEncrypted = !string.IsNullOrEmpty(password),
            Salt = salt,
            Iv = iv,
            PasswordHash = passwordHash,
            WrappedKey = wrappedKey,
            KeyWrapNonce = keyWrapNonce,
            RecoveryBlob = recoveryBlob,
            RecoveryKeyId = recoveryKeyId,
            CompanyName = GetCompanyNameFromDirectory(tempDirectory, cachedSettings),
            Accountants = await GetAccountantNamesAsync(tempDirectory, cancellationToken),
            ModifiedAt = DateTime.UtcNow,
            // Biometric unlock substitutes for typing the password, so it is meaningless without one.
            BiometricEnabled = !string.IsNullOrEmpty(password) && GetBiometricEnabledFromDirectory(cachedSettings),
            LogoThumbnail = GenerateLogoThumbnail(tempDirectory)
        };

        // Check if file exists to preserve created date
        if (File.Exists(filePath))
        {
            var existingFooter = await footerService.ReadFooterAsync(filePath, cancellationToken);
            if (existingFooter != null)
                footer.CreatedAt = existingFooter.CreatedAt;
        }

        // Write to file (atomic: write to temp, then move)
        var tempPath = filePath + ".tmp";
        try
        {
            await using (var fileStream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                contentStream.Position = 0;
                await contentStream.CopyToAsync(fileStream, cancellationToken);
                await footerService.WriteFooterAsync(fileStream, footer, cancellationToken);
            }
            await AtomicFile.ReplaceAsync(tempPath, filePath, overwrite: true, cancellationToken);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Task CloseCompanyAsync(string tempDirectory)
    {
        if (Directory.Exists(tempDirectory))
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> IsFileEncryptedAsync(string filePath)
    {
        var footer = await footerService.ReadFooterAsync(filePath);
        return footer?.IsEncrypted ?? false;
    }

    /// <inheritdoc />
    public async Task<T?> ReadJsonAsync<T>(
        string tempDirectory,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // Find the file (may be in a subdirectory with company name)
        var filePath = FindFileInDirectory(tempDirectory, fileName);
        if (filePath == null || !File.Exists(filePath))
            return default;

        // Streamed, because receipts.json holds every receipt's bytes as base64 and the intermediate string is twice that again in UTF-16.
        await using var stream = await AtomicFile.OpenReadAsync(
            filePath,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan,
            cancellationToken: cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteJsonAsync<T>(
        string tempDirectory,
        string fileName,
        T data,
        CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(tempDirectory, fileName);

        // Serialized in full before the first await. SerializeAsync resumes on the thread pool
        // after its first flush, where it would read lists the UI thread may be editing.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    /// <summary>
    /// Attaches a continuation to each task that reads its <see cref="Task.Exception"/> if it
    /// faults, so the fault doesn't surface later as <c>UnobservedTaskException</c>. Used when
    /// we're about to throw on the load path and want to discard the in-flight reads cleanly.
    /// </summary>
    private static void ObserveFaults(IEnumerable<Task> tasks)
    {
        foreach (var t in tasks)
        {
            _ = t.ContinueWith(
                static x => { _ = x.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>
    /// Throws <see cref="CompanyFileTooNewException"/> if the file's stamped version is greater
    /// than the running app's version. Legacy files (stamped "1.0.0" before the version-check
    /// feature shipped) and files with unparseable versions are allowed to load.
    /// </summary>
    private static void ValidateAppVersion(string? fileVersion)
    {
        if (string.IsNullOrEmpty(fileVersion) || !Version.TryParse(fileVersion, out var fileVer))
        {
            return;
        }
        if (!Version.TryParse(AppInfo.VersionNumber, out var appVer))
        {
            return;
        }
        // Compare on Major.Minor.Build only to keep things in lockstep with the public version string.
        var normalizedFile = new Version(fileVer.Major, fileVer.Minor, Math.Max(0, fileVer.Build));
        var normalizedApp = new Version(appVer.Major, appVer.Minor, Math.Max(0, appVer.Build));
        if (normalizedFile > normalizedApp)
        {
            throw new CompanyFileTooNewException(fileVersion, AppInfo.VersionNumber);
        }
    }

    /// <summary>
    /// Loads all company data from a temporary directory.
    /// </summary>
    /// <remarks>
    /// Reads are issued concurrently. The files are already extracted to disk by the caller,
    /// the collections have no cross-deserialization dependencies, and ReadJsonAsync uses a
    /// shared immutable <see cref="JsonOptions"/> instance, so concurrent deserialization is safe.
    /// </remarks>
    public async Task<CompanyData> LoadCompanyDataAsync(
        string tempDirectory,
        CancellationToken cancellationToken = default,
        bool loadReceipts = true)
    {
        var settingsTask          = ReadJsonAsync<CompanySettings>(tempDirectory, "appSettings.json", cancellationToken);
        var idCountersTask        = ReadJsonAsync<IdCounters>(tempDirectory, "idCounters.json", cancellationToken);
        var customersTask         = ReadJsonAsync<List<Models.Entities.Customer>>(tempDirectory, "customers.json", cancellationToken);
        var productsTask          = ReadJsonAsync<List<Models.Entities.Product>>(tempDirectory, "products.json", cancellationToken);
        var suppliersTask         = ReadJsonAsync<List<Models.Entities.Supplier>>(tempDirectory, "suppliers.json", cancellationToken);
        var categoriesTask        = ReadJsonAsync<List<Models.Entities.Category>>(tempDirectory, "categories.json", cancellationToken);
        var accountantsTask       = ReadJsonAsync<List<Models.Entities.Accountant>>(tempDirectory, "accountants.json", cancellationToken);
        var locationsTask         = ReadJsonAsync<List<Models.Entities.Location>>(tempDirectory, "locations.json", cancellationToken);
        var revenuesTask          = ReadJsonAsync<List<Models.Transactions.Revenue>>(tempDirectory, "revenues.json", cancellationToken);
        var expensesTask          = ReadJsonAsync<List<Models.Transactions.Expense>>(tempDirectory, "expenses.json", cancellationToken);
        var invoicesTask          = ReadJsonAsync<List<Models.Transactions.Invoice>>(tempDirectory, "invoices.json", cancellationToken);
        // Absent from every file written before quotes shipped, which ReadJsonAsync handles by
        // returning null, so those open with no quotes rather than failing.
        var quotesTask            = ReadJsonAsync<List<Models.Transactions.Quote>>(tempDirectory, "quotes.json", cancellationToken);
        var paymentsTask          = ReadJsonAsync<List<Models.Transactions.Payment>>(tempDirectory, "payments.json", cancellationToken);
        var recurringInvoicesTask = ReadJsonAsync<List<Models.Transactions.RecurringInvoice>>(tempDirectory, "recurringInvoices.json", cancellationToken);
        var recurringTransactionsTask = ReadJsonAsync<List<Models.Transactions.RecurringTransaction>>(tempDirectory, "recurringTransactions.json", cancellationToken);
        var inventoryTask         = ReadJsonAsync<List<Models.Inventory.InventoryItem>>(tempDirectory, "inventory.json", cancellationToken);
        var stockAdjustmentsTask  = ReadJsonAsync<List<Models.Inventory.StockAdjustment>>(tempDirectory, "stockAdjustments.json", cancellationToken);
        var stockTransfersTask    = ReadJsonAsync<List<Models.Inventory.StockTransfer>>(tempDirectory, "stockTransfers.json", cancellationToken);
        var purchaseOrdersTask    = ReadJsonAsync<List<Models.Inventory.PurchaseOrder>>(tempDirectory, "purchaseOrders.json", cancellationToken);
        var rentalInventoryTask   = ReadJsonAsync<List<Models.Rentals.RentalItem>>(tempDirectory, "rentalInventory.json", cancellationToken);
        var rentalsTask           = ReadJsonAsync<List<Models.Rentals.RentalRecord>>(tempDirectory, "rentals.json", cancellationToken);
        var returnsTask           = ReadJsonAsync<List<Models.Tracking.Return>>(tempDirectory, "returns.json", cancellationToken);
        var lostDamagedTask       = ReadJsonAsync<List<Models.Tracking.LostDamaged>>(tempDirectory, "lostDamaged.json", cancellationToken);
        // Receipts carry base64 image data and can dominate load time, but nothing on the dashboard needs them.
        var receiptsTask          = loadReceipts
            ? ReadJsonAsync<List<Models.Tracking.Receipt>>(tempDirectory, "receipts.json", cancellationToken)
            : Task.FromResult<List<Models.Tracking.Receipt>?>([]);
        var invoiceTemplatesTask  = ReadJsonAsync<List<Models.Invoices.InvoiceTemplate>>(tempDirectory, "invoiceTemplates.json", cancellationToken);
        var eventLogTask          = ReadJsonAsync<List<AuditEvent>>(tempDirectory, "eventLog.json", cancellationToken);
        var pendingConversionsTask = ReadJsonAsync<List<PendingConversion>>(tempDirectory, "pendingConversions.json", cancellationToken);
        var forecastRecordsTask   = ReadJsonAsync<List<Models.Insights.ForecastAccuracyRecord>>(tempDirectory, "forecastRecords.json", cancellationToken);
        var bankImportSessionsTask = ReadJsonAsync<List<Models.BankMatching.BankImportSession>>(tempDirectory, "bankImportSessions.json", cancellationToken);

        // Absent from every file written before payroll shipped, which ReadJsonAsync handles by
        // returning null, so those open with no employees rather than failing.
        var employeesTask         = ReadJsonAsync<List<Models.Payroll.Employee>>(tempDirectory, "employees.json", cancellationToken);
        var payRunsTask           = ReadJsonAsync<List<Models.Payroll.PayRun>>(tempDirectory, "payRuns.json", cancellationToken);

        // Mobile sync. Absent from every file written before they were persisted, which
        // ReadJsonAsync handles by returning null.
        var pairedDevicesTask     = ReadJsonAsync<List<Models.Tracking.PairedDevice>>(tempDirectory, "pairedDevices.json", cancellationToken);
        var ingestedScanUidsTask  = ReadJsonAsync<List<string>>(tempDirectory, "ingestedScanUids.json", cancellationToken);

        // The version is checked before the rest is awaited, because a file from a newer build may hold enum values this one cannot deserialise.
        Task[] otherReads =
        [
            idCountersTask, customersTask, productsTask, suppliersTask,
            categoriesTask, accountantsTask, locationsTask,
            revenuesTask, expensesTask, invoicesTask, quotesTask, paymentsTask, recurringInvoicesTask,
            recurringTransactionsTask,
            inventoryTask, stockAdjustmentsTask, stockTransfersTask, purchaseOrdersTask,
            rentalInventoryTask, rentalsTask, returnsTask, lostDamagedTask, receiptsTask,
            invoiceTemplatesTask, eventLogTask, pendingConversionsTask,
            forecastRecordsTask, bankImportSessionsTask,
            employeesTask, payRunsTask,
            pairedDevicesTask, ingestedScanUidsTask
        ];

        var settings = await settingsTask;
        try
        {
            ValidateAppVersion(settings?.AppVersion);
        }
        catch (CompanyFileTooNewException)
        {
            // Don't let the in-flight reads fault into UnobservedTaskException. Their results
            // would likely be JsonExceptions from newer enum values; we discard them.
            ObserveFaults(otherReads);
            throw;
        }

        await Task.WhenAll(otherReads);

        return new CompanyData
        {
            Settings = settings ?? new CompanySettings(),
            IdCounters = idCountersTask.Result ?? new IdCounters(),
            Customers = customersTask.Result ?? [],
            Products = productsTask.Result ?? [],
            Suppliers = suppliersTask.Result ?? [],
            Categories = categoriesTask.Result ?? [],
            Accountants = accountantsTask.Result ?? [],
            Locations = locationsTask.Result ?? [],
            Revenues = revenuesTask.Result ?? [],
            Expenses = expensesTask.Result ?? [],
            Invoices = invoicesTask.Result ?? [],
            Quotes = quotesTask.Result ?? [],
            Payments = paymentsTask.Result ?? [],
            RecurringInvoices = recurringInvoicesTask.Result ?? [],
            RecurringTransactions = recurringTransactionsTask.Result ?? [],
            Inventory = inventoryTask.Result ?? [],
            StockAdjustments = stockAdjustmentsTask.Result ?? [],
            StockTransfers = stockTransfersTask.Result ?? [],
            PurchaseOrders = purchaseOrdersTask.Result ?? [],
            RentalInventory = rentalInventoryTask.Result ?? [],
            Rentals = rentalsTask.Result ?? [],
            Returns = returnsTask.Result ?? [],
            LostDamaged = lostDamagedTask.Result ?? [],
            Receipts = receiptsTask.Result ?? [],
            InvoiceTemplates = invoiceTemplatesTask.Result ?? [],
            EventLog = eventLogTask.Result ?? [],
            PendingConversions = pendingConversionsTask.Result ?? [],
            ForecastRecords = forecastRecordsTask.Result ?? [],
            BankImportSessions = bankImportSessionsTask.Result ?? [],
            Employees = employeesTask.Result ?? [],
            PayRuns = payRunsTask.Result ?? [],
            PairedDevices = pairedDevicesTask.Result ?? [],
            IngestedScanUids = ingestedScanUidsTask.Result ?? []
        };
    }

    /// <summary>
    /// Reads just the receipts (receipts.json) from an extracted temp directory.
    /// Used to load receipt attachments off the critical open path; pair with
    /// <see cref="LoadCompanyDataAsync"/> called with <c>loadReceipts: false</c>.
    /// </summary>
    public async Task<List<Models.Tracking.Receipt>> LoadReceiptsAsync(
        string tempDirectory,
        CancellationToken cancellationToken = default)
    {
        return await ReadJsonAsync<List<Models.Tracking.Receipt>>(tempDirectory, "receipts.json", cancellationToken) ?? [];
    }

    /// <summary>
    /// Saves all company data to a temporary directory.
    /// </summary>
    /// <param name="companyDirectory">The company subdirectory (not the temp root) where data files should be saved.</param>
    /// <param name="data">The company data to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveCompanyDataAsync(
        string companyDirectory,
        CompanyData data,
        CancellationToken cancellationToken = default)
    {
        // Stamp the running app's version into the file so a future older app can detect that the file is too new for it to safely open.
        data.Settings.AppVersion = AppInfo.VersionNumber;

        // Every file is serialized before the first await, so the snapshot is taken in one go on
        // the calling thread and no edit made while the files are written can land in some of them.
        var files = new List<(string FileName, byte[] Bytes)>();
        void Add<T>(string fileName, T value) =>
            files.Add((fileName, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

        Add("appSettings.json", data.Settings);
        Add("idCounters.json", data.IdCounters);
        Add("customers.json", data.Customers);
        Add("products.json", data.Products);
        Add("suppliers.json", data.Suppliers);
        Add("categories.json", data.Categories);
        Add("accountants.json", data.Accountants);
        Add("locations.json", data.Locations);
        Add("revenues.json", data.Revenues);
        Add("expenses.json", data.Expenses);
        Add("invoices.json", data.Invoices);
        Add("quotes.json", data.Quotes);
        Add("payments.json", data.Payments);
        Add("recurringInvoices.json", data.RecurringInvoices);
        Add("recurringTransactions.json", data.RecurringTransactions);
        Add("inventory.json", data.Inventory);
        Add("stockAdjustments.json", data.StockAdjustments);
        Add("stockTransfers.json", data.StockTransfers);
        Add("purchaseOrders.json", data.PurchaseOrders);
        Add("rentalInventory.json", data.RentalInventory);
        Add("rentals.json", data.Rentals);
        Add("returns.json", data.Returns);
        Add("lostDamaged.json", data.LostDamaged);
        Add("receipts.json", data.Receipts);
        Add("invoiceTemplates.json", data.InvoiceTemplates);
        Add("eventLog.json", data.EventLog);
        Add("pendingConversions.json", data.PendingConversions);
        Add("forecastRecords.json", data.ForecastRecords);
        Add("bankImportSessions.json", data.BankImportSessions);
        Add("employees.json", data.Employees);
        Add("payRuns.json", data.PayRuns);
        Add("pairedDevices.json", data.PairedDevices);
        Add("ingestedScanUids.json", data.IngestedScanUids);

        // Write directly to the provided company directory - caller is responsible for providing the correct path
        await Task.Run(() =>
        {
            foreach (var (fileName, bytes) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(companyDirectory, fileName), bytes);
            }
        }, cancellationToken);

        // Deliberately does not mark the data saved, because this only stages JSON into the temp directory and nothing is durable until the caller commits.
    }

    /// <inheritdoc />
    public async Task<byte[]?> ExtractLogoFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var footer = await footerService.ReadFooterAsync(filePath, cancellationToken);
        if (footer?.LogoThumbnail == null)
            return null;

        try
        {
            return Convert.FromBase64String(footer.LogoThumbnail);
        }
        catch
        {
            return null;
        }
    }

    #region Helper Methods

    /// <summary>
    /// Reads and deserializes appSettings.json once from the given directory.
    /// Returns null if the file is missing or cannot be parsed.
    /// </summary>
    private CompanySettings? ReadSettingsFromDirectory(string tempDirectory)
    {
        try
        {
            var settingsPath = FindFileInDirectory(tempDirectory, "appSettings.json");
            if (settingsPath != null && File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return JsonSerializer.Deserialize<CompanySettings>(json, JsonOptions);
            }
        }
        catch
        {
            // Unreadable or malformed settings
        }

        return null;
    }

    private string GetCompanyNameFromDirectory(string tempDirectory, CompanySettings? settings = null)
    {
        settings ??= ReadSettingsFromDirectory(tempDirectory);
        if (!string.IsNullOrEmpty(settings?.Company.Name))
            return settings.Company.Name;

        // Look for company subdirectory
        var subdirs = Directory.GetDirectories(tempDirectory);
        if (subdirs.Length > 0)
            return Path.GetFileName(subdirs[0]);

        return Path.GetFileName(tempDirectory);
    }

    private static bool GetBiometricEnabledFromDirectory(CompanySettings? settings)
    {
        return settings?.Security.BiometricEnabled ?? false;
    }

    private static string GetAppVersionFromDirectory(CompanySettings? settings = null)
    {
        if (!string.IsNullOrEmpty(settings?.AppVersion))
            return settings.AppVersion;

        return "1.0.0";
    }

    private static string? FindFileInDirectory(string directory, string fileName, int maxDepth = 3)
    {
        // First check directly in directory
        var directPath = Path.Combine(directory, fileName);
        if (File.Exists(directPath))
            return directPath;

        if (maxDepth <= 0)
            return null;

        // Check in subdirectories recursively (for backward compatibility with nested archives)
        foreach (var subDir in Directory.GetDirectories(directory))
        {
            var result = FindFileInDirectory(subDir, fileName, maxDepth - 1);
            if (result != null)
                return result;
        }

        return null;
    }

    private const int ThumbnailMaxSize = 64;

    private static string? GenerateLogoThumbnail(string tempDirectory)
    {
        var logoPath = FindLogoFileInDirectory(tempDirectory);
        if (logoPath == null)
            return null;

        try
        {
            using var bitmap = SKBitmap.Decode(logoPath);
            if (bitmap == null)
                return null;

            // Calculate scaled dimensions preserving aspect ratio
            var scale = Math.Min(
                (float)ThumbnailMaxSize / bitmap.Width,
                (float)ThumbnailMaxSize / bitmap.Height);

            SKBitmap target;
            if (scale >= 1f)
            {
                // Image is already small enough, use as-is
                target = bitmap;
            }
            else
            {
                var newWidth = Math.Max(1, (int)(bitmap.Width * scale));
                var newHeight = Math.Max(1, (int)(bitmap.Height * scale));

                target = new SKBitmap(newWidth, newHeight);
                using var canvas = new SKCanvas(target);
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, newWidth, newHeight));
            }

            string result;
            using (var image = SKImage.FromBitmap(target))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                result = Convert.ToBase64String(encoded.ToArray());
            }

            if (!ReferenceEquals(target, bitmap))
                target.Dispose();

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLogoFileInDirectory(string directory, int maxDepth = 3)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("logo.", StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            if (maxDepth <= 0)
                return null;

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                var result = FindLogoFileInDirectory(subDir, maxDepth - 1);
                if (result != null)
                    return result;
            }
        }
        catch
        {
            // Directory may be inaccessible
        }

        return null;
    }

    private async Task<List<string>> GetAccountantNamesAsync(
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var accountants = await ReadJsonAsync<List<Models.Entities.Accountant>>(
            tempDirectory, "accountants.json", cancellationToken);

        return accountants?.Select(a => a.Name).ToList() ?? [];
    }

    #endregion
}

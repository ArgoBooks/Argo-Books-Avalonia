using System.Security.Cryptography;
using System.Text;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for managing license storage with machine-specific encryption.
/// </summary>
public class LicenseService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string LicenseValidateUrl = $"{ApiConfig.BaseUrl}/api/license/validate.php";

    private readonly IEncryptionService _encryptionService;
    private readonly IGlobalSettingsService _settingsService;
    private readonly IPlatformService _platformService;
    private readonly IConnectivityService _connectivityService;
    private readonly IErrorLogger? _errorLogger;

    /// <summary>
    /// Internal license data structure.
    /// </summary>
    private class LicenseData
    {
        public bool HasPremium { get; init; }
        public string? LicenseKey { get; init; }
        public DateTime ActivationDate { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the LicenseService.
    /// Uses the default platform service from the factory.
    /// </summary>
    public LicenseService(IEncryptionService encryptionService, IGlobalSettingsService settingsService, IErrorLogger? errorLogger = null)
        : this(encryptionService, settingsService, PlatformServiceFactory.GetPlatformService(), new ConnectivityService(), errorLogger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the LicenseService with a specific platform service.
    /// </summary>
    public LicenseService(IEncryptionService encryptionService, IGlobalSettingsService settingsService, IPlatformService platformService, IErrorLogger? errorLogger = null)
        : this(encryptionService, settingsService, platformService, new ConnectivityService(), errorLogger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the LicenseService with all dependencies.
    /// </summary>
    public LicenseService(IEncryptionService encryptionService, IGlobalSettingsService settingsService, IPlatformService platformService, IConnectivityService connectivityService, IErrorLogger? errorLogger = null)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _connectivityService = connectivityService ?? throw new ArgumentNullException(nameof(connectivityService));
        _errorLogger = errorLogger;
        Instance ??= this;
    }

    /// <summary>
    /// Singleton instance, set by the constructor.
    /// </summary>
    public static LicenseService? Instance { get; private set; }

    /// <summary>
    /// Saves the license status securely.
    /// </summary>
    /// <param name="hasPremium">Whether user has Premium plan.</param>
    /// <param name="licenseKey">The license key that was verified.</param>
    public async Task SaveLicenseAsync(bool hasPremium, string? licenseKey)
    {
        var settings = _settingsService.GetSettings();
        if (settings == null)
            return;

        var licenseData = new LicenseData
        {
            HasPremium = hasPremium,
            LicenseKey = licenseKey,
            ActivationDate = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(licenseData);
        var dataBytes = Encoding.UTF8.GetBytes(json);

        // Generate salt and IV
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        // Use machine-specific password for encryption
        var machineKey = GetMachineKey();

        // Encrypt the license data
        var encryptedData = _encryptionService.Encrypt(dataBytes, machineKey, salt, iv);

        // Store in settings
        settings.License.LicenseData = Convert.ToBase64String(encryptedData);
        settings.License.Salt = salt;
        settings.License.Iv = iv;

        await _settingsService.SaveAsync(settings);
    }

    /// <summary>
    /// Loads the saved license status.
    /// </summary>
    /// <returns>True if the user has Premium access, false otherwise.</returns>
    public bool LoadLicense()
    {
        try
        {
            var settings = _settingsService.GetSettings();
            if (settings?.License.LicenseData == null ||
                settings.License.Salt == null ||
                settings.License.Iv == null)
            {
                return false;
            }

            var encryptedData = Convert.FromBase64String(settings.License.LicenseData);

            var machineKey = GetMachineKey();
            var licenseData = TryDecryptLicense(encryptedData, machineKey, settings.License.Salt, settings.License.Iv);

            if (licenseData == null)
                return false;

            return licenseData.HasPremium;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.License, "Failed to load license status");
            return false;
        }
    }

    /// <summary>
    /// Attempts to decrypt license data with the given key.
    /// </summary>
    private LicenseData? TryDecryptLicense(byte[] encryptedData, string machineKey, string salt, string iv)
    {
        try
        {
            var decryptedData = _encryptionService.Decrypt(encryptedData, machineKey, salt, iv);
            var json = Encoding.UTF8.GetString(decryptedData);
            return JsonSerializer.Deserialize<LicenseData>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the stored license key (if available).
    /// </summary>
    /// <returns>The license key, or null if not available.</returns>
    public string? GetLicenseKey()
    {
        try
        {
            var settings = _settingsService.GetSettings();
            if (settings?.License.LicenseData == null ||
                settings.License.Salt == null ||
                settings.License.Iv == null)
            {
                return null;
            }

            var encryptedData = Convert.FromBase64String(settings.License.LicenseData);

            var machineKey = GetMachineKey();
            var licenseData = TryDecryptLicense(encryptedData, machineKey, settings.License.Salt, settings.License.Iv);

            return licenseData?.LicenseKey;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.License, "Failed to retrieve license key");
            return null;
        }
    }

    /// <summary>
    /// Clears the saved license (for logout or plan cancellation).
    /// </summary>
    public async Task ClearLicenseAsync()
    {
        var settings = _settingsService.GetSettings();
        if (settings == null)
            return;

        settings.License = new LicenseSettings();
        await _settingsService.SaveAsync(settings);
    }

    /// <summary>
    /// Gets a hashed device identifier for server-side device tracking.
    /// </summary>
    public string GetDeviceId() => GetMachineKey();

    /// <summary>
    /// Validates the stored license key online, checking subscription status and device ownership.
    /// </summary>
    public async Task<LicenseValidationResult> ValidateLicenseOnlineAsync(CancellationToken cancellationToken = default)
    {
        var licenseKey = GetLicenseKey();
        if (string.IsNullOrEmpty(licenseKey))
        {
            return new LicenseValidationResult
            {
                Status = LicenseValidationStatus.InvalidKey,
                Message = "No license key found."
            };
        }

        try
        {
            var deviceId = GetDeviceId();
            var requestBody = new { license_key = licenseKey, device_id = deviceId };
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await HttpClient.PostAsync(LicenseValidateUrl, content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<LicenseValidateResponse>(responseJson);

            if (result == null)
            {
                return new LicenseValidationResult
                {
                    Status = LicenseValidationStatus.NetworkError,
                    Message = "Invalid server response."
                };
            }

            if (result.Success)
            {
                return new LicenseValidationResult
                {
                    Status = LicenseValidationStatus.Valid,
                    Message = result.Message
                };
            }

            var status = result.Status?.ToLowerInvariant() switch
            {
                "invalid_key" => LicenseValidationStatus.InvalidKey,
                "expired" => LicenseValidationStatus.ExpiredSubscription,
                "wrong_device" => LicenseValidationStatus.WrongDevice,
                "rate_limited" => LicenseValidationStatus.RateLimited,
                _ => LicenseValidationStatus.NetworkError
            };

            return new LicenseValidationResult
            {
                Status = status,
                Message = result.Message
            };
        }
        catch (HttpRequestException ex)
        {
            // The probe runs either way to pick the user's message, so classifying the log
            // entry from its answer is free: a customer whose connection dropped is a
            // warning, our licence endpoint being unreachable is an error worth chasing.
            return new LicenseValidationResult
            {
                Status = LicenseValidationStatus.NetworkError,
                Message = await NetworkFailure.ResolveAndReportAsync(
                    _errorLogger, ex, "License validation network error", _connectivityService, cancellationToken)
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            return new LicenseValidationResult
            {
                Status = LicenseValidationStatus.NetworkError,
                Message = await NetworkFailure.ResolveAndReportAsync(
                    _errorLogger, ex, "License validation timeout", _connectivityService, cancellationToken)
            };
        }
        catch (TaskCanceledException)
        {
            return new LicenseValidationResult
            {
                Status = LicenseValidationStatus.NetworkError,
                Message = "Request was cancelled."
            };
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.License, "License validation failed");
            return new LicenseValidationResult
            {
                Status = LicenseValidationStatus.NetworkError,
                Message = "An unexpected error occurred during license validation. Please try again."
            };
        }
    }

    /// <summary>
    /// Gets a machine-specific key for encryption using stable platform identifiers.
    /// </summary>
    private string GetMachineKey()
    {
        var machineInfo = new StringBuilder();

        // Use the platform-specific stable machine ID
        machineInfo.Append(_platformService.GetMachineId());

        // Add static application key (v2 to differentiate from legacy key)
        machineInfo.Append("ArgoBooks_License_v2");

        // Hash the combined data to create a fixed-length key
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo.ToString()));
        return Convert.ToBase64String(hashBytes);
    }

    private class LicenseValidateResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}

/// <summary>
/// Status of an online license validation check.
/// </summary>
public enum LicenseValidationStatus
{
    Valid,
    InvalidKey,
    ExpiredSubscription,
    WrongDevice,
    NetworkError,
    RateLimited
}

/// <summary>
/// Result of an online license validation check.
/// </summary>
public class LicenseValidationResult
{
    public LicenseValidationStatus Status { get; init; }
    public string? Message { get; init; }
}

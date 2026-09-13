using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Implementation of ISecureStore using Microsoft.Maui.Storage.SecureStorage.
/// On Android, this stores data in the Android Keystore (biometric-bound if configured).
/// This class is not unit-tested; the ISecureStore interface is tested with an in-memory fake.
/// Device verification is done on actual hardware.
/// </summary>
public class MauiSecureStore : ISecureStore
{
    /// <summary>
    /// Stores a key-value pair in MAUI SecureStorage.
    /// </summary>
    public Task SetAsync(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        return SecureStorage.Default.SetAsync(key, value ?? string.Empty);
    }

    /// <summary>
    /// Retrieves a value from MAUI SecureStorage.
    /// </summary>
    public Task<string?> GetAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        return SecureStorage.Default.GetAsync(key);
    }

    /// <summary>
    /// Removes a key-value pair from MAUI SecureStorage.
    /// </summary>
    public Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

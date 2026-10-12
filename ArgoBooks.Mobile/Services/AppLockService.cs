using System.Threading.Tasks;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Microsoft.Maui.ApplicationModel;

namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Shows the AndroidX BiometricPrompt system UI, requesting either a class-3 ("strong")
/// biometric (fingerprint/face) OR the device's own PIN/pattern/password as a fallback, so the
/// app lock still works on phones with no biometric enrolled. Real device auth - not stubbed.
///
/// Not unit-tested: BiometricPrompt requires a live FragmentActivity and real hardware/OS prompt
/// UI. Device-verified separately (see task-7-report.md). The lock screen and grace-period logic
/// that call into this are tested independently (AppLockPolicy) or are simple UI wiring.
/// </summary>
public class AppLockService
{
    /// <summary>
    /// Shows the biometric/device-credential prompt and returns true once the user authenticates.
    /// Returns false if the user cancels, fails to authenticate, or biometrics/device-credential
    /// authentication isn't available at all on this device (nothing further can be required of
    /// the user in that case, so the caller may choose to treat "unavailable" as unlocked instead
    /// of trapping the user out of their own app - see the CanAuthenticate check below).
    /// </summary>
    public Task<bool> AuthenticateAsync(string title)
    {
        var tcs = new TaskCompletionSource<bool>();

        var activity = Platform.CurrentActivity as FragmentActivity;
        if (activity == null)
        {
            // No current FragmentActivity to host the prompt (shouldn't happen in practice on
            // Android). Fail open rather than leaving the user stuck on the lock screen forever.
            tcs.SetResult(true);
            return tcs.Task;
        }

        const int allowedAuthenticators = BiometricManager.Authenticators.BiometricStrong | BiometricManager.Authenticators.DeviceCredential;

        var manager = BiometricManager.From(activity);
        var canAuthenticate = manager.CanAuthenticate(allowedAuthenticators);
        if (canAuthenticate != BiometricManager.BiometricSuccess)
        {
            // No biometric hardware, nothing enrolled, and no device PIN/pattern/password set either.
            tcs.SetResult(true);
            return tcs.Task;
        }

        var executor = ContextCompat.GetMainExecutor(activity)!;
        var callback = new AuthenticationCallback(tcs);
        var prompt = new BiometricPrompt(activity, executor, callback);

        var promptInfo = new BiometricPrompt.PromptInfo.Builder()
            .SetTitle(title)
            .SetAllowedAuthenticators(allowedAuthenticators)
            // A negative button and DeviceCredential are mutually exclusive in SetAllowedAuthenticators, and the system prompt has its own way back.
            .Build();

        prompt.Authenticate(promptInfo);

        return tcs.Task;
    }

    private sealed class AuthenticationCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public AuthenticationCallback(TaskCompletionSource<bool> tcs)
        {
            _tcs = tcs;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
        {
            base.OnAuthenticationSucceeded(result);
            _tcs.TrySetResult(true);
        }

        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            // Covers cancellation, lockout and any other terminal error alike: the prompt is closed either way, so it resolves as not authenticated.
            _tcs.TrySetResult(false);
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
            // A single non-matching attempt (e.g. wrong fingerprint). The system prompt stays
            // open and lets the user retry, so don't resolve the task yet.
        }
    }
}

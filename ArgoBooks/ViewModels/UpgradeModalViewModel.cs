using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Validation;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the upgrade modal.
/// </summary>
public partial class UpgradeModalViewModel : ViewModelBase
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string LicenseRedeemUrl = $"{ApiConfig.BaseUrl}/api/license/redeem.php";
    private static readonly string LicenseCaptureEmailUrl = $"{ApiConfig.BaseUrl}/api/license/capture-email.php";
    private readonly IConnectivityService _connectivityService = new ConnectivityService();
    private static readonly string PricingApiUrl = $"{ApiConfig.BaseUrl}/api/pricing/plans.php";

    private static readonly string PremiumUpgradeUrl = $"{ApiConfig.BaseUrl}/pricing/";
    private static readonly string CancelSubscriptionUrl = $"{ApiConfig.BaseUrl}/community/users/subscription.php";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isEnterKeyModalOpen;

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private string? _verificationError;

    [ObservableProperty]
    private bool _isVerificationSuccess;

    [ObservableProperty]
    private bool _showContinueButton;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    private bool _isOffline;

    partial void OnIsVerificationSuccessChanged(bool value)
    {
        // The key entry form is hidden by either this or the email step, so it has to be told
        // when either moves.
        OnPropertyChanged(nameof(ShowKeyEntryForm));

        if (value)
        {
            // Show continue button after 2 second delay
            _ = ShowContinueButtonAfterDelayAsync();
        }
        else
        {
            ShowContinueButton = false;
        }
    }

    private async Task ShowContinueButtonAfterDelayAsync()
    {
        await Task.Delay(1500);
        if (IsVerificationSuccess)
        {
            ShowContinueButton = true;
        }
    }

    #region Pricing

    // The headline figure only, without a currency symbol: the "$" is a separate,
    // smaller run in the card so the layout matches the website's price block.
    // Holds the monthly price or the yearly-billed per-month equivalent depending
    // on the selected cycle, so a single price block serves both.
    [ObservableProperty]
    private string _premiumAmountDisplay = "";

    [ObservableProperty]
    private string _premiumBillingPeriod = "";

    // Struck-out monthly price (e.g. "$15/month") shown above the amount on yearly.
    [ObservableProperty]
    private string _premiumStrikeText = "";

    // "Billed monthly" or "Billed annually at $150 CAD", below the amount.
    [ObservableProperty]
    private string _premiumBilledText = "";

    // "Save $30" pill on the annual toggle option. The dollar figure, not a
    // percentage, so it matches the website's toggle.
    [ObservableProperty]
    private string _yearlySavingsDisplay = "";

    // One-line plan pitches above the price, matching the website cards. These
    // live here rather than the plans API because the website hardcodes them too.
    [ObservableProperty]
    private string _freePlanPitch = "";

    [ObservableProperty]
    private string _premiumPlanPitch = "";

    // Uppercase plan chips ("FREE" / "PREMIUM") beside the word "Plan".
    [ObservableProperty]
    private string _freeChip = "";

    [ObservableProperty]
    private string _premiumChip = "";

    // Billing-cycle toggle state. Monthly is the default, as on the website.
    [ObservableProperty]
    private bool _isYearlyBilling;

    public bool IsMonthlyBilling => !IsYearlyBilling;

    partial void OnIsYearlyBillingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMonthlyBilling));
        RefreshCycleDisplay();
    }

    [ObservableProperty]
    private bool _isLoadingPlans;

    [ObservableProperty]
    private bool _hasLoadError;

    /// <summary>
    /// True when plans have been fetched successfully and should be displayed.
    /// </summary>
    public bool ShowPlans => !IsOffline && !IsLoadingPlans && !HasLoadError && _hasFetchedPlans;

    partial void OnIsOfflineChanged(bool value) => OnPropertyChanged(nameof(ShowPlans));
    partial void OnIsLoadingPlansChanged(bool value) => OnPropertyChanged(nameof(ShowPlans));
    partial void OnHasLoadErrorChanged(bool value) => OnPropertyChanged(nameof(ShowPlans));

    private bool _hasFetchedPlans;

    public ObservableCollection<string> FreePlanFeatures { get; } = [];

    public ObservableCollection<string> PremiumPlanFeatures { get; } = [];

    // Raw label/detail pairs from the API, kept so we can re-translate when the language changes
    private List<PlanFeature> _rawFreeFeatures = [];
    private List<PlanFeature> _rawPremiumFeatures = [];

    // Raw pricing strings from the API, kept so we can re-translate when the language changes
    private string? _rawPremiumYearlyPriceDisplay;
    private string? _rawPremiumYearlySavingsDisplay;

    // Raw numeric pricing from the API, used to derive the strike/per-month/savings-percent
    // strings for the yearly toggle state.
    private double _rawMonthlyPrice;
    private double _rawYearlyPrice;
    private string _rawCurrency = "CAD";

    #endregion

    #region Plan Status

    [ObservableProperty]
    private bool _hasPremium;

    /// <summary>
    /// Gets whether to show "Active" badge on Premium card.
    /// </summary>
    public bool ShowPremiumActive => HasPremium;

    /// <summary>
    /// Gets whether to show Select Premium button (doesn't have Premium).
    /// </summary>
    public bool ShowSelectPremium => !HasPremium;

    /// <summary>
    /// Gets whether to show the cancel subscription button for Premium.
    /// </summary>
    public bool ShowCancelPremium => HasPremium;

    partial void OnHasPremiumChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPremiumActive));
        OnPropertyChanged(nameof(ShowSelectPremium));
        OnPropertyChanged(nameof(ShowCancelPremium));
    }

    #endregion

    /// <summary>
    /// Gets the formatted license key for API calls (keeps dashes).
    /// </summary>
    private string GetFormattedLicenseKey()
    {
        return LicenseKey.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public UpgradeModalViewModel()
    {
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        RefreshFeatureDisplay();
    }

    private void RefreshFeatureDisplay()
    {
        FreePlanFeatures.Clear();
        foreach (var feature in _rawFreeFeatures)
            FreePlanFeatures.Add(feature.DisplayText);

        PremiumPlanFeatures.Clear();
        foreach (var feature in _rawPremiumFeatures)
            PremiumPlanFeatures.Add(feature.DisplayText);

        RefreshPricingDisplay();
    }

    private void RefreshPricingDisplay()
    {
        // Period carries the currency code so it renders as e.g. "CAD/month"
        // at the same size and color as the period text. We construct it manually so
        // the slash is preserved and the word stays lowercase regardless of how the
        // translation pipeline handles "/month" or "month".
        var monthWord = "month".Translate();
        if (!string.IsNullOrEmpty(monthWord) && char.IsUpper(monthWord[0]))
        {
            monthWord = char.ToLowerInvariant(monthWord[0]) + monthWord.Substring(1);
        }
        PremiumBillingPeriod = string.IsNullOrEmpty(_rawCurrency)
            ? "/" + monthWord
            : _rawCurrency + "/" + monthWord;

        FreePlanPitch = "Just starting out, or you only need the basics? This is the place.".Translate();
        PremiumPlanPitch = "Want unlimited invoicing, bigger monthly limits, and forecasts you can act on? Go Premium.".Translate();
        FreeChip = "Free".Translate().ToUpperInvariant();
        PremiumChip = "Premium".Translate().ToUpperInvariant();

        YearlySavingsDisplay = _rawPremiumYearlySavingsDisplay is not null
            ? "Save {0}".TranslateFormat(_rawPremiumYearlySavingsDisplay)
            // Clear stale text from a prior fetch, otherwise an API response that omits
            // the yearly fields would leave the previous savings figure visible.
            : string.Empty;

        // Struck-out reference price, from the raw numeric so the currency stays with
        // the period text rather than sitting next to the dollar amount.
        PremiumStrikeText = _rawMonthlyPrice > 0
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "${0:0}/month",
                _rawMonthlyPrice)
            : string.Empty;

        RefreshCycleDisplay();
    }

    /// <summary>
    /// Rebuilds the parts of the price block that depend on the selected billing
    /// cycle. One block serves both cycles, so switching only changes text.
    /// </summary>
    private void RefreshCycleDisplay()
    {
        if (_rawMonthlyPrice <= 0 || _rawYearlyPrice <= 0)
        {
            PremiumAmountDisplay = string.Empty;
            PremiumBilledText = string.Empty;
            return;
        }

        if (IsYearlyBilling)
        {
            // The headline figure is the per-month equivalent so it compares
            // like-for-like against the monthly plan, but the amount actually
            // charged today has to be stated or checkout comes as a surprise.
            PremiumAmountDisplay = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.00}",
                _rawYearlyPrice / 12.0);
            PremiumBilledText = _rawPremiumYearlyPriceDisplay is not null
                ? "Billed annually at {0}".TranslateFormat(_rawPremiumYearlyPriceDisplay)
                : "Billed annually".Translate();
        }
        else
        {
            PremiumAmountDisplay = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:0}",
                _rawMonthlyPrice);
            PremiumBilledText = "Billed monthly".Translate();
        }
    }

    #region Commands

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;

        // Fetch plans on first open, or retry if a previous attempt failed
        if (!_hasFetchedPlans || IsOffline || HasLoadError)
        {
            _ = FetchPlansAsync();
        }
        else
        {
            // Rebuild display in case language changed while modal was closed
            RefreshFeatureDisplay();
        }
    }

    #region Email capture

    /// <summary>
    /// Shown between redeeming the key and the success panel, and only for a key we hold no
    /// contact address for.
    ///
    /// Keys sold through a reseller arrive as a pre-generated batch with no buyer details, so
    /// this is the only moment their address can be asked for. Anyone who bought through the
    /// website is already on record and never sees this: they get the success panel directly.
    ///
    /// Premium is ALREADY active by the time this appears, both on the server and locally.
    /// Nothing here can withhold it, and closing the modal at this step costs us an address
    /// rather than costing the customer what they paid for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyEntryForm))]
    private bool _isEmailCaptureStep;

    [ObservableProperty]
    private string _customerEmail = string.Empty;

    [ObservableProperty]
    private string? _emailError;

    [ObservableProperty]
    private bool _isSubmittingEmail;

    /// <summary>The key just redeemed, kept so the capture call can identify it.</summary>
    private string _redeemedKey = string.Empty;

    /// <summary>The entry form is now one of three states rather than the inverse of success.</summary>
    public bool ShowKeyEntryForm => !IsVerificationSuccess && !IsEmailCaptureStep;

    /// <summary>
    /// Records the address, then closes the modal.
    ///
    /// A server failure still closes. The licence is active either way, and stopping someone
    /// on a dead-end screen over a contact detail is precisely the support ticket this whole
    /// flow is meant to avoid.
    /// </summary>
    [RelayCommand]
    private async Task SubmitEmailAsync()
    {
        string email = CustomerEmail.Trim();

        if (email.Length == 0)
        {
            EmailError = "Please enter your email address".Translate();
            return;
        }

        if (!DataValidator.IsValidEmail(email))
        {
            EmailError = "That does not look like an email address".Translate();
            return;
        }

        IsSubmittingEmail = true;
        EmailError = null;

        try
        {
            await CaptureEmailAsync(_redeemedKey, email);
        }
        catch (Exception ex)
        {
            // Logged, not shown. There is nothing the customer can usefully do about it.
            App.ErrorLogger?.LogError(ex, ErrorCategory.Network, "License email capture failed");
        }
        finally
        {
            IsSubmittingEmail = false;
        }

        IsEmailCaptureStep = false;

        // Same exit as declining: this step already told them premium is active, so the
        // success panel would only repeat it.
        ContinueAfterSuccess();
    }

    /// <summary>
    /// Dismisses the email request without sending one. The licence is already active by
    /// the time this step appears, so declining costs the user nothing and must not look
    /// like it might. Closes the modal rather than going on to the success panel: this step
    /// already leads with "Premium is active", so that panel would be the same news twice.
    /// </summary>
    [RelayCommand]
    private void SkipEmailCapture()
    {
        CustomerEmail = string.Empty;
        EmailError = null;
        IsEmailCaptureStep = false;

        // Out through the success panel's own exit, because that is what raises KeyVerified
        // and turns premium on in the running app. Closing any other way leaves the customer
        // on the free tier until they restart.
        ContinueAfterSuccess();
    }

    private async Task CaptureEmailAsync(string premiumKey, string email)
    {
        var deviceId = App.LicenseService?.GetDeviceId() ?? "";

        var requestBody = new { premium_key = premiumKey, device_id = deviceId, email };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await HttpClient.PostAsync(LicenseCaptureEmailUrl, content);
    }

    #endregion

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsEnterKeyModalOpen = false;
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;
        CustomerEmail = string.Empty;
        EmailError = null;
        LicenseKey = string.Empty;
        VerificationError = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    private void SelectPremium()
    {
        OpenUrl(PremiumUpgradeUrl);
        Close();
    }

    [RelayCommand]
    private void SelectMonthlyBilling() => IsYearlyBilling = false;

    [RelayCommand]
    private void SelectYearlyBilling() => IsYearlyBilling = true;

    [RelayCommand]
    private void CancelSubscription()
    {
        OpenUrl(CancelSubscriptionUrl);
        Close();
    }

    private static void OpenUrl(string url)
    {
        UrlHelper.SafeOpenUrl(url);
    }

    [RelayCommand]
    private async Task RequestCloseEnterKey()
    {
        // Don't allow closing during success animation - user must click Continue
        if (IsVerificationSuccess)
            return;

        // The email step comes after the licence is already active, so a click on the
        // backdrop must not be treated as abandoning the key entry. LicenseKey is still
        // populated at this point, so without this guard the click fell through to the
        // discard-changes prompt and asked the user to confirm throwing away a purchase
        // they had already completed. Dismissing here is the X button's job.
        if (IsEmailCaptureStep)
            return;

        // If no data was entered, just close
        if (string.IsNullOrWhiteSpace(LicenseKey))
        {
            CloseEnterKey();
            return;
        }

        // Data was entered - ask for confirmation
        if (!await ConfirmDiscardNewAsync())
            return;

        CloseEnterKey();
    }

    [RelayCommand]
    private void OpenEnterKey()
    {
        IsOpen = false;
        IsEnterKeyModalOpen = true;
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;
        LicenseKey = string.Empty;
        VerificationError = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    private void CloseEnterKey()
    {
        IsEnterKeyModalOpen = false;
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;
        LicenseKey = string.Empty;
        VerificationError = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    private void GoBackToUpgrade()
    {
        IsEnterKeyModalOpen = false;
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;
        LicenseKey = string.Empty;
        VerificationError = null;
        SuccessMessage = null;
        IsOpen = true;
    }

    [RelayCommand]
    private void ContinueAfterSuccess()
    {
        IsEnterKeyModalOpen = false;
        KeyVerified?.Invoke(this, LicenseKey);
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;
        LicenseKey = string.Empty;
        SuccessMessage = null;
    }

    [RelayCommand]
    private async Task VerifyKey()
    {
        var key = GetFormattedLicenseKey();

        if (string.IsNullOrWhiteSpace(key))
        {
            VerificationError = "Please enter a license key";
            return;
        }

        // Format: XXXX-XXXX-XXXX-XXXX-XXXX (24 chars with dashes)
        if (key.Length != 24)
        {
            VerificationError = "License key must be in format XXXX-XXXX-XXXX-XXXX-XXXX";
            return;
        }

        IsVerifying = true;
        VerificationError = null;
        IsVerificationSuccess = false;
        IsEmailCaptureStep = false;

        try
        {
            var response = await RedeemLicenseAsync(key);

            if (response?.Success == true)
            {
                _redeemedKey = key;
                SuccessMessage = response.Message ?? "License activated successfully!";

                // Ask for an address only when the server has none. Anyone who bought through
                // the website goes straight to the success panel, unchanged.
                if (response.NeedsEmail)
                {
                    IsEmailCaptureStep = true;
                }
                else
                {
                    IsVerificationSuccess = true;
                }

                // Save license securely
                var licenseType = response.Type?.ToLowerInvariant() ?? "";
                var hasPremium = licenseType.Contains("premium");

                if (App.LicenseService != null)
                {
                    try
                    {
                        await App.LicenseService.SaveLicenseAsync(hasPremium, key);
                    }
                    catch (Exception ex)
                    {
                        App.ErrorLogger?.LogError(ex, ErrorCategory.License, "Failed to save license after verification");
                        var dialog = App.ConfirmationDialog;
                        if (dialog != null)
                        {
                            await dialog.ShowAsync(new ConfirmationDialogOptions
                            {
                                Title = "Warning".Translate(),
                                Message = "Your license was activated but could not be saved locally. You may need to re-enter your license key next time.".Translate(),
                                PrimaryButtonText = "OK".Translate(),
                                SecondaryButtonText = null,
                                CancelButtonText = null
                            });
                        }
                    }
                }

                // User will click Continue button to close
            }
            else
            {
                VerificationError = response?.Message ?? "Invalid license key";
            }
        }
        catch (HttpRequestException ex)
        {
            // Someone trying to redeem a licence and failing is the most expensive network
            // failure in the app, so it is worth knowing whether it was their connection or
            // ours. The probe was already running to choose the message.
            VerificationError = (await NetworkFailure.ResolveAndReportAsync(
                App.ErrorLogger, ex, "License redemption network error", _connectivityService)).Translate();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken != default)
        {
            VerificationError = (await NetworkFailure.ResolveAndReportAsync(
                App.ErrorLogger, ex, "License redemption timeout", _connectivityService)).Translate();
        }
        catch (TaskCanceledException)
        {
            VerificationError = "Request was cancelled.".Translate();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Network, "License redemption request failed");
            VerificationError = "Verification failed: {0}".TranslateFormat(ex.Message);
        }
        finally
        {
            IsVerifying = false;
        }
    }

    /// <summary>
    /// Redeems a license key on the server, marking it as used and binding to this device.
    /// </summary>
    private async Task<LicenseResponse?> RedeemLicenseAsync(string premiumKey)
    {
        var deviceId = App.LicenseService?.GetDeviceId() ?? "";

        var requestBody = new { premium_key = premiumKey, device_id = deviceId };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync(LicenseRedeemUrl, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LicenseResponse>(responseJson);
    }

    #endregion

    #region Events

    public event EventHandler<string>? KeyVerified;

    #endregion

    #region Plans API

    /// <summary>
    /// Fetches plan details and pricing from the website API.
    /// Updates features lists and pricing from the server so the app stays in sync.
    /// Sets IsOffline if the API is unreachable.
    /// Called once on app startup for free-tier users.
    /// </summary>
    public async Task FetchPlansAsync()
    {
        IsLoadingPlans = true;
        HasLoadError = false;
        // Reset IsOffline so this attempt's outcome isn't blended with a previous one.
        // The catch path re-sets it only on connectivity errors; a non-connectivity
        // failure (e.g. 5xx) must not leave the offline panel from a prior call visible.
        IsOffline = false;

        try
        {
            var response = await HttpClient.GetStringAsync(PricingApiUrl);
            var apiResponse = JsonSerializer.Deserialize<PlansApiResponse>(response);

            if (apiResponse?.Pricing != null)
            {
                _rawPremiumYearlyPriceDisplay = apiResponse.Pricing.PremiumYearlyPriceDisplay;
                _rawPremiumYearlySavingsDisplay = apiResponse.Pricing.PremiumYearlySavingsDisplay;
                _rawMonthlyPrice = apiResponse.Pricing.PremiumMonthlyPriceNumeric;
                _rawYearlyPrice = apiResponse.Pricing.PremiumYearlyPriceNumeric;
                _rawCurrency = apiResponse.Pricing.Currency ?? "CAD";
                RefreshPricingDisplay();
            }

            if (apiResponse?.Plans != null)
            {
                _rawFreeFeatures = apiResponse.Plans.Free?.Features ?? [];
                _rawPremiumFeatures = apiResponse.Plans.Premium?.Features ?? [];
                RefreshFeatureDisplay();
            }

            if (apiResponse?.Limits != null)
            {
                FreePlanLimits.Apply(
                    apiResponse.Limits.FreeInvoiceMonthlyLimit,
                    apiResponse.Limits.FreeReceiptScanMonthlyLimit);
            }

            _hasFetchedPlans = true;
            OnPropertyChanged(nameof(ShowPlans));
        }
        catch (Exception ex)
        {
            var isConnectivityError = ex is HttpRequestException { StatusCode: null }
                || (ex is TaskCanceledException tce && (tce.InnerException is TimeoutException || tce.CancellationToken != default));

            if (isConnectivityError)
            {
                IsOffline = true;
            }
            else
            {
                HasLoadError = true;
                // isConnectivityError already ruled out the offline case above, so anything
                // reaching here failed with a working connection and is ours.
                NetworkFailure.Report(App.ErrorLogger, ex, "Failed to fetch plans from API");
            }
        }
        finally
        {
            IsLoadingPlans = false;
        }
    }

    #region API Response Models

    private class PlansApiResponse
    {
        [JsonPropertyName("plans")]
        public PlansData? Plans { get; init; }

        [JsonPropertyName("pricing")]
        public PricingData? Pricing { get; init; }

        [JsonPropertyName("limits")]
        public LimitsData? Limits { get; init; }
    }

    private class LimitsData
    {
        [JsonPropertyName("free_invoice_monthly_limit")]
        public int? FreeInvoiceMonthlyLimit { get; init; }

        [JsonPropertyName("free_receipt_scan_monthly_limit")]
        public int? FreeReceiptScanMonthlyLimit { get; init; }
    }

    private class PlansData
    {
        [JsonPropertyName("free")]
        public PlanInfo? Free { get; init; }

        [JsonPropertyName("premium")]
        public PlanInfo? Premium { get; init; }
    }

    private class PlanInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("features")]
        public List<PlanFeature>? Features { get; init; }
    }

    private class PlanFeature
    {
        [JsonPropertyName("label")]
        public string Label { get; init; } = "";

        [JsonPropertyName("detail")]
        public string? Detail { get; init; }

        public string DisplayText => Detail != null ? $"{Label.Translate()} ({Detail.Translate()})" : Label.Translate();
    }

    private class PricingData
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; init; }

        [JsonPropertyName("premium_price_display")]
        public string PremiumPriceDisplay { get; init; } = "$10 CAD";

        [JsonPropertyName("premium_yearly_price_display")]
        public string? PremiumYearlyPriceDisplay { get; init; }

        [JsonPropertyName("premium_yearly_savings_display")]
        public string? PremiumYearlySavingsDisplay { get; init; }

        [JsonPropertyName("premium_monthly_price")]
        public double PremiumMonthlyPriceNumeric { get; init; }

        [JsonPropertyName("premium_yearly_price")]
        public double PremiumYearlyPriceNumeric { get; init; }
    }

    #endregion

    #endregion

    #region Response Models

    private class LicenseResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; init; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; init; }

        [JsonPropertyName("duration_months")]
        public int DurationMonths { get; init; }

        /// <summary>
        /// True when the server holds no contact address for this key, which is the case for a
        /// batch sold through a reseller. False for anything bought through the website, so an
        /// existing customer is never asked twice.
        /// </summary>
        [JsonPropertyName("needs_email")]
        public bool NeedsEmail { get; init; }
    }

    #endregion
}

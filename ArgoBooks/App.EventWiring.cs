using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.ViewModels;

namespace ArgoBooks;

/// <summary>
/// Event-wiring for the application, split out of App.axaml.cs to keep that file focused.
/// These are members of the same partial App class; behavior is unchanged.
/// </summary>
public partial class App
{
    // The undo state the running save writes, taken as it starts. Edits made while it is
    // writing come after this point, so they stay unsaved.
    private static IUndoableAction? _saveUndoPoint;

    /// <summary>
    /// Wires up CompanyManager events to update UI.
    /// </summary>
    private static void WireCompanyManagerEvents()
    {
        if (CompanyManager == null || _mainWindowViewModel == null || _appShellViewModel == null)
            return;

        // Sync event log to CompanyData before every save (centralized handler)
        CompanyManager.CompanySaving += (_, _) =>
        {
            _saveUndoPoint = UndoRedoManager.SavePoint;
            SyncEventLogBeforeSave();
        };

        CompanyManager.CompanyOpened += async (_, args) =>
        {
            // Don't hide the welcome screen yet, keep it visible behind the loading
            // overlay so the user doesn't see the half-initialized dashboard.
            _mainWindowViewModel.CurrentCompanyName = args.CompanyName;
            var logo = LoadBitmapFromPath(CompanyManager.CurrentCompanyLogoPath);
            _appShellViewModel.SetCompanyInfo(args.CompanyName, logo);
            _appShellViewModel.CompanySwitcherPanelViewModel.SetCurrentCompany(args.CompanyName, args.FilePath, logo);
            _appShellViewModel.FileMenuPanelViewModel.SetCurrentCompany(args.FilePath);

            // Clear undo/redo history for fresh start with new company
            UndoRedoManager.Clear();

            // Initialize the event log service with persisted events from the company file
            if (EventLogService == null)
            {
                EventLogService = new EventLogService();

                // Wire bidirectional sync between UndoRedoManager and EventLogService
                EventLogService.SetUndoRedoManager(UndoRedoManager);

                // Wire UndoRedoManager to automatically record audit events
                // when any CRUD operation records an undoable action
                UndoRedoManager.ActionRecorded += (_, e) =>
                {
                    EventLogService.RecordFromAction(e.Action);
                };
            }
            if (CompanyManager.CompanyData != null)
            {
                EventLogService.Initialize(CompanyManager.CompanyData.EventLog);

                _appShellViewModel.VersionHistoryModalViewModel.SetEventLogService(EventLogService);
            }

            // Reset unsaved changes state - opening a company starts with no unsaved changes
            _mainWindowViewModel.HasUnsavedChanges = false;
            _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;

            // Load and apply language setting from company settings
            var companySettings = CompanyManager.CompanyData?.Settings;
            _appShellViewModel.ApplyFeatureVisibility(companySettings);
            if (companySettings != null)
            {
                // Hooked here rather than at the create-company screen so it also covers
                // existing companies, and companies opened on a second
                // machine. TrackCompanyProfileAsync ignores repeats within a session.
                //
                // The sample company is skipped outright: its name, industry, country and
                // currency ship with the demo file and are identical on every install, so
                // reporting them says nothing about this user, and every reader downstream
                // would otherwise have to know to throw them away.
                if (!CompanyManager.IsSampleCompany)
                {
                    _ = TelemetryManager?.TrackCompanyProfileAsync(
                        companySettings.Company.Name,
                        companySettings.Company.BusinessType,
                        companySettings.Company.Industry,
                        companySettings.Company.Country,
                        companySettings.Localization.Currency,
                        companySettings.Localization.Language);

                    var data = CompanyManager.CompanyData;
                    if (data != null)
                    {
                        _ = TelemetryManager?.TrackCompanyScaleAsync(new CompanyScaleCounts(
                            data.Expenses.Count,
                            data.Revenues.Count,
                            data.Invoices.Count,
                            data.Payments.Count,
                            data.Customers.Count,
                            data.Suppliers.Count,
                            data.Products.Count,
                            data.Categories.Count,
                            data.Receipts.Count,
                            data.Employees.Count,
                            data.BankImportSessions.Sum(session => session.Lines.Count)));
                    }
                }

                var language = companySettings.Localization.Language;
                if (!string.IsNullOrEmpty(language))
                {
                    await LanguageService.Instance.SetLanguageAsync(language);

                    // Also update global setting so WelcomeScreen uses same language
                    if (SettingsService != null)
                    {
                        SettingsService.GlobalSettings.Ui.Language = language;
                        await SettingsService.SaveGlobalSettingsAsync();
                    }
                }
            }

            // Load company-specific chart settings (date range, chart type, etc.)
            // before navigating so the dashboard renders with the right range.
            ChartSettingsService.Instance.LoadForCompany(args.FilePath);

            // Migrate: if a legacy .env API key exists but the company has no persisted key,
            // adopt the .env key, but only if this company actually has portal activity
            // (connected providers or a portal URL), so we don't assign the key to the wrong company.
            // Best-effort: persists to .argo on next save; re-runs harmlessly if the save doesn't happen.
            var portalSettings = CompanyManager.CompanyData?.Settings.PaymentPortal;
            if (portalSettings != null
                && string.IsNullOrEmpty(portalSettings.PersistedApiKey)
                && DotEnv.HasValue(PortalSettings.ApiKeyEnvVar)
                && (portalSettings.ConnectedAccounts.StripeConnected
                    || portalSettings.ConnectedAccounts.PaypalConnected
                    || portalSettings.ConnectedAccounts.SquareConnected
                    || !string.IsNullOrEmpty(portalSettings.PortalUrl)))
            {
                portalSettings.PersistedApiKey = DotEnv.Get(PortalSettings.ApiKeyEnvVar);
            }

            // Load this company's portal API key into the process-level cache (cheap, so any
            // portal-dependent UI has it on first paint; the actual sync is deferred below).
            PortalSettings.ActivateApiKey(portalSettings);

            // Navigate to Dashboard when company is opened
            NavigationService?.NavigateTo("Dashboard");

            // Now that the dashboard is ready, hide the welcome screen and loading
            // overlay together so the user never sees a half-initialized dashboard.
            _mainWindowViewModel.OpenCompany(args.CompanyName);
            _mainWindowViewModel.HideLoading();

            // Defer non-visual and network work until after the dashboard has painted,
            // so the company opens as fast as possible. Posted on the UI thread at
            // Background priority to keep thread affinity but yield to rendering first.
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    // Merge the deferred receipts on the UI thread now that the dashboard
                    // has painted, so the rest of the app sees them and any auto-save below
                    // writes them back. Save paths also gate on this, so it's safe either way.
                    await CompanyManager.EnsureReceiptsLoadedAsync();

                    // Generate any recurring invoices that came due while the app was closed.
                    // Draft-first and idempotent: each schedule is keyed on its next date, so this
                    // only produces the occurrences actually missed since the last open.
                    // Not for the sample company. Its recurring schedules are time-shifted to look
                    // current on every open, so this generated fresh drafts and announced them
                    // every single time, and worse, called SaveCompanyAsync on a file the app
                    // treats as read-only everywhere else. This is the second notification path;
                    // CheckAndSendNotifications below is gated separately.
                    if (CompanyManager.CompanyData != null && !CompanyManager.IsSampleCompany)
                    {
                        var generatedRecurring = RecurringInvoiceService
                            .GenerateDueInvoices(CompanyManager.CompanyData, DateTime.Today);
                        if (generatedRecurring.Count > 0)
                        {
                            await CompanyManager.SaveCompanyAsync();
                            RecurringInvoiceService.RaiseGenerated(generatedRecurring.Count);
                            var recurringCount = generatedRecurring.Count;
                            AddNotification(
                                "Recurring invoices",
                                recurringCount == 1
                                    ? "1 recurring invoice was generated and needs to be sent (Recurring tab)."
                                    : $"{recurringCount} recurring invoices were generated and need to be sent (Recurring tab).",
                                NotificationType.Info,
                                OpenRecurringInvoicesTab);
                        }

                        var generatedTxns = RecurringTransactionService
                            .GenerateDue(CompanyManager.CompanyData, DateTime.Today);
                        if (generatedTxns.Count > 0)
                        {
                            await CompanyManager.SaveCompanyAsync();
                            var txnExpenses = generatedTxns.Count(t => t is Core.Models.Transactions.Expense);
                            var txnRevenues = generatedTxns.Count - txnExpenses;
                            RecurringTransactionService.RaiseGenerated(txnExpenses, txnRevenues);
                            _ = TelemetryManager?.TrackFeatureAsync(
                                FeatureName.RecurringTransactionsGenerated, $"{generatedTxns.Count}");
                            AddNotification(
                                "Recurring transactions",
                                generatedTxns.Count == 1
                                    ? "1 recurring transaction was generated and needs review."
                                    : $"{generatedTxns.Count} recurring transactions were generated and need review.",
                                NotificationType.Info);
                        }

                        // Remind about recurring drafts still waiting to be sent: ones generated on an
                        // earlier open that the user hasn't sent yet. Exclude the ones just generated above
                        // (they already got the "added to drafts" notice) so the same drafts aren't announced twice.
                        var pendingRecurringDrafts = CompanyManager.CompanyData.Invoices.Count(
                            i => i.Status == InvoiceStatus.Draft && !string.IsNullOrEmpty(i.RecurringInvoiceId));
                        var awaitingSend = pendingRecurringDrafts - generatedRecurring.Count;
                        if (awaitingSend > 0)
                        {
                            AddNotification(
                                "Recurring invoices",
                                awaitingSend == 1
                                    ? "1 recurring invoice is waiting to be sent."
                                    : $"{awaitingSend} recurring invoices are waiting to be sent.",
                                NotificationType.Warning,
                                OpenRecurringInvoicesTab);
                        }
                    }

                    // Reconcile and process any pending currency conversions
                    if (PendingConversionService != null && CompanyManager.CompanyData != null)
                    {
                        await PendingConversionService.ReconcileWithCompanyDataAsync(CompanyManager.CompanyData);
                        await PendingConversionService.ProcessPendingConversionsAsync(CompanyManager.CompanyData);
                    }

                    // Start periodic timer to process pending conversions when connectivity returns
                    StartPendingConversionTimer();

                    // Check for low stock and overdue invoice notifications
                    CheckAndSendNotifications();

                    // Auto-sync online payments from the portal on company open
                    await AutoSyncPortalPaymentsAsync();

                    ApplyPortalSyncInterval();
                }
                catch (Exception ex)
                {
                    ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Deferred post-open initialization failed");
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        CompanyManager.CompanyClosed += async (_, _) =>
        {
            // Clear the portal API key so a new company starts fresh
            PortalSettings.DeactivateApiKey();

            StopPortalSyncTimer();

            _mainWindowViewModel.CloseCompany();
            _appShellViewModel.SetCompanyInfo(null);
            _appShellViewModel.CompanySwitcherPanelViewModel.SetCurrentCompany("");
            _appShellViewModel.FileMenuPanelViewModel.SetCurrentCompany(null);
            if (!_isOpeningCompany && !_isCreatingCompany)
                _mainWindowViewModel.HideLoading();
            _mainWindowViewModel.HasUnsavedChanges = false;
            _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;

            UndoRedoManager.Clear();
            EventLogService?.Clear();
            ChangeTrackingService?.ClearAllChanges();
            _appShellViewModel.HeaderViewModel.ClearNotifications();

            // Stop pending conversion timer when company is closed
            StopPendingConversionTimer();

            // Clear cached page ViewModels to ensure fresh state when opening a new company
            ClearPageCaches();
            ViewModels.InsightsPageViewModel.ClearSampleSnapshot();

            NavigationService?.NavigateTo("Welcome");
            _welcomeScreenViewModel?.InitializeTutorialMode();

            // Reset to the global language LAST. This handler is async void, and creating a new
            // company closes the old one then immediately opens the new one, so this close handler
            // and the open handler interleave. Keeping the only await at the very end guarantees the
            // synchronous UI changes above (including the Welcome navigation) all run during the
            // close, before the open navigates to the Dashboard. If the await sat earlier, this
            // handler could resume after the open and re-navigate to "Welcome", stranding the
            // welcome screen inside the freshly opened company's shell.
            var globalLanguage = SettingsService?.GlobalSettings.Ui.Language ?? "English";
            await LanguageService.Instance.SetLanguageAsync(globalLanguage);
        };

        CompanyManager.CompanySaved += (_, _) =>
        {
            _mainWindowViewModel.HideLoading();

            if (_suppressSavedFeedback)
                _suppressSavedFeedback = false;
            else
                _appShellViewModel.HeaderViewModel.ShowSavedFeedback();

            _mainWindowViewModel.HasUnsavedChanges = false;

            // Mark undo/redo state as saved so IsAtSavedState returns true
            UndoRedoManager.MarkSaved(_saveUndoPoint);

            // Clear tracked changes after saving
            ChangeTrackingService?.ClearAllChanges();
        };

        CompanyManager.CompanyDataChanged += (_, _) =>
        {
            _mainWindowViewModel.HasUnsavedChanges = true;
            _appShellViewModel.HeaderViewModel.HasUnsavedChanges = true;
        };

        // When the open company's file is renamed during a save, the watcher's
        // Renamed handler strips the old path from the recent-companies UI caches,
        // but the new path was only added to settings.json, never to those caches.
        // Refresh from disk so the welcome screen, file menu, and switcher all see
        // the new entry.
        CompanyManager.CompanyRenamed += async (_, _) =>
        {
            await LoadRecentCompaniesAsync();
        };

        // Use async callback for password requests (allows proper awaiting)
        CompanyManager.PasswordRequestCallback = async (filePath) =>
        {
            // Hide the loading modal before showing password prompt
            _mainWindowViewModel.HideLoading();

            // Get company name and settings from footer if possible
            var footer = await CompanyManager.GetFileInfoAsync(filePath);
            var companyName = footer?.CompanyName ?? Path.GetFileNameWithoutExtension(filePath);

            // Check if biometric login is enabled for this file and available on the system
            var biometricEnabled = footer?.BiometricEnabled ?? false;
            var biometricAvailable = false;
            var platformService = PlatformServiceFactory.GetPlatformService();

            if (biometricEnabled)
            {
                biometricAvailable = await platformService.IsBiometricAvailableAsync();
            }

            var password = await _appShellViewModel.PasswordPromptModalViewModel.ShowAsync(
                companyName, filePath, biometricAvailable);

            // Handle biometric login success - retrieve stored password
            if (password == "__BIOMETRIC__")
            {
                var fileId = GetBiometricFileId(filePath);
                password = platformService.GetPasswordForBiometric(fileId);

                if (string.IsNullOrEmpty(password))
                {
                    // Password not found in secure storage - fall back to manual entry
                    _appShellViewModel.PasswordPromptModalViewModel.ShowError(
                        "Stored password not found. Please enter the password manually.".Translate());
                    password = await _appShellViewModel.PasswordPromptModalViewModel.WaitForPasswordAsync();
                }
            }

            // Close the password modal and show loading before returning
            if (!string.IsNullOrEmpty(password))
            {
                _appShellViewModel.PasswordPromptModalViewModel.Close();
                _mainWindowViewModel.ShowLoading("Opening company...".Translate());
            }

            return password;
        };

        // Wire up biometric login authentication request from password modal
        _appShellViewModel.PasswordPromptModalViewModel.BiometricAuthRequested += async (_, _) =>
        {
            var passwordModal = _appShellViewModel.PasswordPromptModalViewModel;
            var platformService = PlatformServiceFactory.GetPlatformService();

            try
            {
                var success = await platformService.AuthenticateWithBiometricAsync(
                    "Verify your identity to open {0}".TranslateFormat(passwordModal.CompanyName));

                if (success)
                {
                    passwordModal.OnBiometricSuccess();
                }
                else
                {
                    passwordModal.OnBiometricFailed();
                }
            }
            catch
            {
                passwordModal.OnBiometricFailed();
            }
        };
    }

    /// <summary>
    /// Wires up modal save/delete events to update HasUnsavedChanges.
    /// This is separate from WireCompanyManagerEvents because it doesn't depend on CompanyManager.
    /// </summary>
    private static void WireModalChangeEvents()
    {
        if (_mainWindowViewModel == null || _appShellViewModel == null)
            return;

        var mainWindowVm = _mainWindowViewModel;
        var appShellVm = _appShellViewModel;

        void MarkUnsavedChanges(object? sender, EventArgs e)
        {
            mainWindowVm.HasUnsavedChanges = true;
            appShellVm.HeaderViewModel.HasUnsavedChanges = true;
        }

        // Central handler for all modal save/delete events (wired lazily in each getter)
        _appShellViewModel.UnsavedChangesMade += MarkUnsavedChanges;

        // Invoice template designer: BrowseLogoRequested needs desktop file picker access
        // Note: TemplateSaved is already wired to RaiseUnsavedChanges in the lazy getter
        _appShellViewModel.InvoiceTemplateDesignerViewModel.BrowseLogoRequested += async (_, _) =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Logo".Translate(),
                AllowMultiple = false,
                FileTypeFilter = [Utilities.FilePickerTypes.ImageFileType]
            });

            if (files.Count > 0)
            {
                var picked = files[0].Path.LocalPath;
                var prepared = await Task.Run(() => ImageFileLoader.TryPrepare(picked));
                if (prepared == null)
                {
                    await ShowErrorMessageBoxAsync(
                        "Logo Not Supported".Translate(),
                        "That image could not be read. Try a PNG or JPEG.".Translate());
                    return;
                }

                _appShellViewModel.InvoiceTemplateDesignerViewModel.SetLogoFromFile(prepared.Path);
            }
        };

        // Customer modals: let the user pick an avatar image. The bitmap is loaded for
        // an immediate preview; the file is staged and copied/resized into the company
        // temp directory only when the modal is saved.
        _appShellViewModel.CustomerModalsViewModel.BrowseAvatarRequested += async (_, _) =>
        {
            await PickAvatarAsync(
                "Select Customer Avatar".Translate(),
                (path, bmp) => _appShellViewModel.CustomerModalsViewModel.SetPendingAvatar(path, bmp),
                "CustomerAvatar");
        };

        // Supplier modals: same pattern as customer.
        _appShellViewModel.SupplierModalsViewModel.BrowseAvatarRequested += async (_, _) =>
        {
            await PickAvatarAsync(
                "Select Supplier Avatar".Translate(),
                (path, bmp) => _appShellViewModel.SupplierModalsViewModel.SetPendingAvatar(path, bmp),
                "SupplierAvatar");
        };
    }

    /// <summary>
    /// Wires up file menu events.
    /// </summary>
    private static void WireFileMenuEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var fileMenu = _appShellViewModel.FileMenuPanelViewModel;

        fileMenu.OpenCompanyRequested += async (_, _) =>
        {
            await OpenCompanyFileDialogAsync(desktop);
        };

        // Save
        fileMenu.SaveRequested += async (_, _) =>
        {
            if (CompanyManager?.IsCompanyOpen == true)
            {
                // Sample company cannot be saved directly - redirect to Save As
                if (CompanyManager.IsSampleCompany)
                {
                    await SaveCompanyAsDialogAsync(desktop);
                    return;
                }

                _appShellViewModel.HeaderViewModel.ShowSavingIndicator = true;
                try
                {
                    await CompanyManager.SaveCompanyAsync();
                }
                catch (Exception ex)
                {
                    _appShellViewModel.HeaderViewModel.ShowSavingIndicator = false;
                    ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to save company");
                    await ShowErrorMessageBoxAsync("Error".Translate(), GetFriendlySaveErrorMessage(ex));
                }
            }
        };

        // Save As
        fileMenu.SaveAsRequested += async (_, _) =>
        {
            if (CompanyManager?.IsCompanyOpen == true)
            {
                await SaveCompanyAsDialogAsync(desktop);
            }
        };

        fileMenu.CloseCompanyRequested += async (_, _) =>
        {
            if (CompanyManager?.IsCompanyOpen == true && await ConfirmLeavingCompanyAsync())
            {
                await CompanyManager.CloseCompanyAsync();
            }
        };

        fileMenu.ShowInFolderRequested += (_, _) =>
        {
            CompanyManager?.ShowInFolder();
        };

        fileMenu.OpenRecentCompanyRequested += async (_, company) =>
        {
            if (string.IsNullOrEmpty(company.FilePath)) return;
            await OpenCompanyWithRetryAsync(company.FilePath);
        };
    }

    /// <summary>
    /// Wires up create company wizard events.
    /// </summary>
    private static void WireCreateCompanyEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var createCompany = _appShellViewModel.CreateCompanyViewModel;

        createCompany.CompanyCreated += async (_, args) =>
        {
            var file = await ShowSaveFileDialogAsync(desktop, args.CompanyName);
            if (file == null) return;

            var filePath = file.Path.LocalPath;

            var companyInfo = new CompanyInfo
            {
                Name = args.CompanyName,
                Industry = args.Industry,
                Country = args.Country
            };

            // Keep the loading overlay up across the close-then-open transition inside
            // CreateCompanyAsync so the welcome screen doesn't flash; reset on every exit path.
            _isCreatingCompany = true;
            try
            {
            // Retry loop so a save blocked by security software (antivirus / ransomware
            // protection) can offer Retry / Save to a different folder instead of failing.
            while (true)
            {
                _mainWindowViewModel?.ShowLoading("Creating company...".Translate());
                try
                {
                    await CompanyManager!.CreateCompanyAsync(
                        filePath,
                        args.CompanyName,
                        args.Password,
                        companyInfo);

                    // Apply default currency if specified
                    if (!string.IsNullOrEmpty(args.DefaultCurrency))
                    {
                        CompanyManager.CompanyData!.Settings.Localization.Currency = args.DefaultCurrency;
                    }

                    // Apply logo if one was selected
                    if (!string.IsNullOrEmpty(args.LogoPath))
                    {
                        await CompanyManager.SetCompanyLogoAsync(args.LogoPath);

                        // Refresh sidebar/UI with the newly set logo
                        var logo = LoadBitmapFromPath(CompanyManager.CurrentCompanyLogoPath);
                        _appShellViewModel.SetCompanyInfo(args.CompanyName, logo);
                        _appShellViewModel.CompanySwitcherPanelViewModel.SetCurrentCompany(
                            args.CompanyName, filePath, logo);
                    }

                    _suppressSavedFeedback = true;
                    await CompanyManager.SaveCompanyAsync();

                    await LoadRecentCompaniesAsync();
                    return;
                }
                catch (Exception ex) when (FileAccessHelper.IsLikelySecurityBlock(ex))
                {
                    _mainWindowViewModel?.HideLoading();
                    ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Company create blocked by security software");
                    switch (await ShowSaveBlockedDialogAsync(filePath))
                    {
                        case SaveBlockedChoice.Retry:
                            continue;
                        case SaveBlockedChoice.SaveElsewhere:
                            var newFile = await ShowSaveFileDialogAsync(desktop, args.CompanyName);
                            if (newFile == null) return;
                            filePath = newFile.Path.LocalPath;
                            continue;
                        default:
                            return;
                    }
                }
                catch (Exception ex)
                {
                    _mainWindowViewModel?.HideLoading();
                    ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to create company");
                    await ShowErrorMessageBoxAsync("Error".Translate(), "Failed to create company: {0}".TranslateFormat(ex.Message));
                    return;
                }
            }
            }
            finally
            {
                _isCreatingCompany = false;
            }
        };

        createCompany.BrowseLogoRequested += async (_, _) =>
        {
            var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Company Logo".Translate(),
                AllowMultiple = false,
                FileTypeFilter = [Utilities.FilePickerTypes.ImageFileType]
            });

            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                var prepared = await Task.Run(() => ImageFileLoader.TryPrepare(path));
                if (prepared == null)
                {
                    await ShowErrorMessageBoxAsync(
                        "Logo Not Supported".Translate(),
                        "That image could not be read. Try a PNG or JPEG.".Translate());
                    return;
                }

                createCompany.SetLogo(prepared.Path, prepared.Bitmap);
            }
        };
    }

    /// <summary>
    /// Wires up welcome screen events.
    /// </summary>
    private static void WireWelcomeScreenEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_welcomeScreenViewModel == null)
            return;

        // Create new company - show create company wizard
        _welcomeScreenViewModel.CreateNewCompanyRequested += (_, _) =>
        {
            _appShellViewModel?.CreateCompanyViewModel.OpenCommand.Execute(null);
        };

        // Open company - show file picker
        _welcomeScreenViewModel.OpenCompanyRequested += async (_, _) =>
        {
            await OpenCompanyFileDialogAsync(desktop);
        };

        _welcomeScreenViewModel.OpenRecentCompanyRequested += async (_, company) =>
        {
            if (string.IsNullOrEmpty(company.FilePath)) return;
            await OpenCompanyWithRetryAsync(company.FilePath);
        };

        // Remove from recent companies
        _welcomeScreenViewModel.RemoveFromRecentRequested += async (_, company) =>
        {
            if (string.IsNullOrEmpty(company.FilePath)) return;
            SettingsService?.RemoveRecentCompany(company.FilePath);
            if (SettingsService != null)
            {
                await SettingsService.SaveGlobalSettingsAsync();
            }
        };

        // Clear all recent companies
        _welcomeScreenViewModel.ClearRecentRequested += async (_, _) =>
        {
            if (SettingsService != null)
            {
                SettingsService.GlobalSettings.RecentCompanies.Clear();
                await SettingsService.SaveGlobalSettingsAsync();
            }
        };

        _welcomeScreenViewModel.OpenSampleCompanyRequested += async (_, _) =>
        {
            await OpenSampleCompanyAsync();
        };
    }

    /// <summary>
    /// Wires up company switcher panel events.
    /// </summary>
    private static void WireCompanySwitcherEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var companySwitcher = _appShellViewModel.CompanySwitcherPanelViewModel;

        // Switch to a recent company
        companySwitcher.SwitchCompanyRequested += async (_, company) =>
        {
            if (string.IsNullOrEmpty(company.FilePath)) return;
            await OpenCompanyWithRetryAsync(company.FilePath);
        };

        // Open company from file dialog
        companySwitcher.OpenCompanyRequested += async (_, _) =>
        {
            await OpenCompanyFileDialogAsync(desktop);
        };

        // Edit current company
        companySwitcher.EditCompanyRequested += (_, _) =>
        {
            OpenEditCompanyModal();
        };

        // Edit company from quick action
        _appShellViewModel.EditCompanyRequested += (_, _) =>
        {
            OpenEditCompanyModal();
        };

        // Restart tutorial from help panel
        _appShellViewModel.RestartTutorialRequested += async (_, _) =>
        {
            if (_welcomeScreenViewModel != null)
                _welcomeScreenViewModel.IsTutorialMode = true;

            if (CompanyManager?.IsCompanyOpen == true)
            {
                if (!CompanyManager.IsSampleCompany)
                {
                    try
                    {
                        await CompanyManager.SaveCompanyAsync();
                    }
                    catch (Exception ex)
                    {
                        // Don't close on a failed save: CloseCompanyAsync discards unsaved edits.
                        // Abort the restart and keep the company open to protect the user's data.
                        ErrorLogger?.LogWarning($"Save before tutorial restart failed: {ex.Message}", "AutoSave");
                        if (_welcomeScreenViewModel != null)
                            _welcomeScreenViewModel.IsTutorialMode = false;
                        _appShellViewModel.AddNotification(
                            "Could not restart tutorial",
                            "Your company could not be saved, so it was left open to protect unsaved changes. Please save manually and try again.",
                            NotificationType.Warning);
                        return;
                    }
                }
                await CompanyManager.CloseCompanyAsync();
            }
        };

        // Wire up edit company modal events
        WireEditCompanyEvents(desktop);
    }

    /// <summary>
    /// Wires up edit company modal events.
    /// </summary>
    private static void WireEditCompanyEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var editCompany = _appShellViewModel.EditCompanyModalViewModel;

        // Save company changes
        editCompany.CompanySaved += async (_, args) =>
        {
            if (CompanyManager?.IsCompanyOpen != true) return;

            try
            {
                var settings = CompanyManager.CurrentCompanySettings;
                if (settings != null)
                {
                    // Capture old values for undo
                    var oldName = settings.Company.Name;
                    var oldBusinessType = settings.Company.BusinessType;
                    var oldIndustry = settings.Company.Industry;
                    var oldPhone = settings.Company.Phone;
                    var oldEmail = settings.Company.Email;
                    var oldCountry = settings.Company.Country;
                    var oldCity = settings.Company.City;
                    var oldAddress = settings.Company.Address;
                    var oldProvinceState = settings.Company.ProvinceState;
                    var oldLogoFileName = settings.Company.LogoFileName;

                    // Save old logo bytes for potential undo restore
                    byte[]? oldLogoBytes = null;
                    var oldLogoFilePath = CompanyManager.CurrentCompanyLogoPath;
                    if (!string.IsNullOrEmpty(oldLogoFilePath) && File.Exists(oldLogoFilePath))
                    {
                        oldLogoBytes = await Task.Run(() => SharedFileReader.ReadAllBytes(oldLogoFilePath));
                    }

                    var nameChanged = oldName != args.CompanyName;

                    // Apply new values
                    settings.Company.Name = args.CompanyName;
                    settings.Company.BusinessType = args.BusinessType;
                    settings.Company.Industry = args.Industry;
                    settings.Company.Phone = args.Phone;
                    settings.Company.Email = args.Email;
                    settings.Company.Country = args.Country;
                    settings.Company.City = args.City;
                    settings.Company.Address = args.Address;
                    settings.Company.ProvinceState = args.ProvinceState;

                    // Handle logo update if a new one was uploaded
                    if (!string.IsNullOrEmpty(args.LogoPath))
                    {
                        await CompanyManager.SetCompanyLogoAsync(args.LogoPath);
                    }
                    else if (args.LogoSource == null && CompanyManager.CurrentCompanyLogoPath != null)
                    {
                        // Logo was removed
                        await CompanyManager.RemoveCompanyLogoAsync();
                    }

                    // Capture new logo state after changes
                    var newLogoFileName = settings.Company.LogoFileName;
                    byte[]? newLogoBytes = null;
                    var newLogoFilePath = CompanyManager.CurrentCompanyLogoPath;
                    if (!string.IsNullOrEmpty(newLogoFilePath) && File.Exists(newLogoFilePath))
                    {
                        newLogoBytes = await Task.Run(() => SharedFileReader.ReadAllBytes(newLogoFilePath));
                    }

                    // Derive temp directory for logo file operations during undo/redo
                    var logoTempDir = !string.IsNullOrEmpty(oldLogoFilePath)
                        ? Path.GetDirectoryName(oldLogoFilePath)
                        : (!string.IsNullOrEmpty(newLogoFilePath)
                            ? Path.GetDirectoryName(newLogoFilePath)
                            : null);

                    // Mark settings as changed
                    settings.ChangesMade = true;

                    // If company name changed, schedule a file rename (skip for sample company).
                    // The rename is deferred to save time so that closing without saving
                    // leaves the original file untouched.
                    var oldFilePath = CompanyManager.CurrentFilePath;
                    if (nameChanged && CompanyManager.CurrentFilePath != null && !CompanyManager.IsSampleCompany)
                    {
                        var currentPath = CompanyManager.CurrentFilePath;
                        var directory = Path.GetDirectoryName(currentPath);
                        var newFileName = CompanyManager.ToCompanyFileName(args.CompanyName) + ".argo";
                        var newPath = Path.Combine(directory!, newFileName);

                        if (CompanyManager.CanRenameTo(newPath))
                        {
                            CompanyManager.SetPendingRename(newPath);
                        }
                    }
                    var newFilePath = CompanyManager.PendingRenamePath ?? CompanyManager.CurrentFilePath;

                    // Update UI
                    _mainWindowViewModel?.OpenCompany(args.CompanyName);
                    var logo = LoadBitmapFromPath(CompanyManager.CurrentCompanyLogoPath);
                    _appShellViewModel.SetCompanyInfo(args.CompanyName, logo);
                    // The industry may have just changed, and anyone who has not set the
                    // toggles themselves should follow the new industry's starting point.
                    _appShellViewModel.ApplyFeatureVisibility(settings);
                    _appShellViewModel.InvoiceModalsViewModel.RefreshCompanyDetailsPrompt();
                    _appShellViewModel.CompanySwitcherPanelViewModel.SetCurrentCompany(
                        args.CompanyName,
                        CompanyManager.PendingRenamePath ?? CompanyManager.CurrentFilePath,
                        logo);
                    _reportsPageViewModel?.RefreshCanvas();

                    // Record undo/redo action only if non-currency fields changed
                    var newName = args.CompanyName;
                    var newBusinessType = args.BusinessType;
                    var newIndustry = args.Industry;
                    var newPhone = args.Phone;
                    var newEmail = args.Email;
                    var newCountry = args.Country;
                    var newCity = args.City;
                    var newAddress = args.Address;
                    var newProvinceState = args.ProvinceState;

                    var hasNonCurrencyChanges = oldName != newName
                        || oldBusinessType != newBusinessType
                        || oldIndustry != newIndustry
                        || oldPhone != newPhone
                        || oldEmail != args.Email
                        || oldCountry != newCountry
                        || oldCity != newCity
                        || oldAddress != newAddress
                        || oldProvinceState != newProvinceState
                        || oldLogoFileName != newLogoFileName;

                    if (!hasNonCurrencyChanges) return;

                    UndoRedoManager.RecordAction(new DelegateAction(
                        $"Edit company '{newName}'",
                        () =>
                        {
                            // Restore old values
                            settings.Company.Name = oldName;
                            settings.Company.BusinessType = oldBusinessType;
                            settings.Company.Industry = oldIndustry;
                            settings.Company.Phone = oldPhone;
                            settings.Company.Email = oldEmail;
                            settings.Company.Country = oldCountry;
                            settings.Company.City = oldCity;
                            settings.Company.Address = oldAddress;
                            settings.Company.ProvinceState = oldProvinceState;

                            RestoreCompanyLogo(settings, oldLogoFileName, oldLogoBytes, logoTempDir);

                            // Clear pending rename (revert to original file name)
                            if (oldFilePath != newFilePath)
                            {
                                CompanyManager.ClearPendingRename();
                            }

                            settings.ChangesMade = true;
                            RefreshCompanyUi(oldName);
                        },
                        () =>
                        {
                            // Re-apply new values
                            settings.Company.Name = newName;
                            settings.Company.BusinessType = newBusinessType;
                            settings.Company.Industry = newIndustry;
                            settings.Company.Phone = newPhone;
                            settings.Company.Email = newEmail;
                            settings.Company.Country = newCountry;
                            settings.Company.City = newCity;
                            settings.Company.Address = newAddress;
                            settings.Company.ProvinceState = newProvinceState;

                            RestoreCompanyLogo(settings, newLogoFileName, newLogoBytes, logoTempDir);

                            // Re-schedule the file rename
                            if (oldFilePath != newFilePath && newFilePath != null)
                            {
                                CompanyManager.SetPendingRename(newFilePath);
                            }

                            settings.ChangesMade = true;
                            RefreshCompanyUi(newName);
                        }));
                }
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to update company");
                await ShowErrorMessageBoxAsync("Error".Translate(), "Failed to update company: {0}".TranslateFormat(ex.Message));
            }
        };

        editCompany.BrowseLogoRequested += async (_, _) =>
        {
            var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Company Logo".Translate(),
                AllowMultiple = false,
                FileTypeFilter = [Utilities.FilePickerTypes.ImageFileType]
            });

            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                var prepared = await Task.Run(() => ImageFileLoader.TryPrepare(path));
                if (prepared == null)
                {
                    await ShowErrorMessageBoxAsync(
                        "Logo Not Supported".Translate(),
                        "That image could not be read. Try a PNG or JPEG.".Translate());
                    return;
                }

                editCompany.SetLogo(prepared.Path, prepared.Bitmap);
            }
        };
    }

    /// <summary>
    /// Wires up settings modal events for password management.
    /// </summary>
    private static void WireSettingsModalEvents()
    {
        if (_appShellViewModel == null)
            return;

        var settings = _appShellViewModel.SettingsModalViewModel;

        // Initialize HasPassword based on current company
        if (CompanyManager != null)
        {
            CompanyManager.CompanyOpened += (_, args) =>
            {
                settings.CloseForCompanyChange();
                settings.HasPassword = args.IsEncrypted;
                settings.IsSampleCompany = CompanyManager.IsSampleCompany;
            };

            CompanyManager.CompanyClosed += (_, _) =>
            {
                settings.CloseForCompanyChange();
                settings.HasPassword = false;
                settings.IsSampleCompany = false;
            };
        }

        // Biometric unlock works by storing the password itself in the operating system's
        // protected store, so any change to the password leaves that copy stale. Without this,
        // changing a password silently broke fingerprint unlock, removing one left the old
        // password stored, and a file returned by support recovery still advertised a
        // biometric option that could never succeed.
        void SyncBiometricEnrolment(string? newPassword, bool keepEnrolment)
        {
            if (CompanyManager?.CurrentFilePath == null)
                return;

            var fileId = GetBiometricFileId(CompanyManager.CurrentFilePath);
            var platformService = PlatformServiceFactory.GetPlatformService();
            var security = CompanyManager.CurrentCompanySettings?.Security;

            // Keep an existing enrolment working across a password change by re-storing the
            // new password. Anything else clears it, so the user opts in again deliberately
            // rather than inheriting an enrolment they never made for this password.
            if (keepEnrolment && !string.IsNullOrEmpty(newPassword) && security?.BiometricEnabled == true)
            {
                platformService.StorePasswordForBiometric(fileId, newPassword);
                return;
            }

            platformService.ClearPasswordForBiometric(fileId);
            if (security != null)
                security.BiometricEnabled = false;
            settings.SetBiometricLoginWithoutAuth(false);
        }

        settings.AddPasswordRequested += async (_, args) =>
        {
            if (CompanyManager?.IsCompanyOpen != true || args.NewPassword == null) return;

            try
            {
                await CompanyManager.ChangePasswordAsync(args.NewPassword);

                // The file had no password until now, so any enrolment still on disk belongs
                // to an older password. Start clean and let the user re-enable biometrics.
                SyncBiometricEnrolment(args.NewPassword, keepEnrolment: false);
                ConfigureAutoLock();

                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.CompanyPasswordSet, "set");
                _appShellViewModel.AddNotification("Success".Translate(), "Password has been set.".Translate(), NotificationType.Success);
            }
            catch (Exception ex)
            {
                settings.HasPassword = false;
                ErrorLogger?.LogError(ex, ErrorCategory.Authentication, "Failed to set password");
                await ShowErrorMessageBoxAsync("Error".Translate(), "Failed to set password: {0}".TranslateFormat(ex.Message));
            }
        };

        settings.ChangePasswordRequested += async (_, args) =>
        {
            if (CompanyManager?.IsCompanyOpen != true || args.NewPassword == null) return;

            // Verify the current password before changing
            if (!CompanyManager.VerifyCurrentPassword(args.CurrentPassword))
            {
                settings.OnPasswordVerificationFailed();
                return;
            }

            try
            {
                await CompanyManager.ChangePasswordAsync(args.NewPassword);

                // Re-store so fingerprint or face unlock keeps working with the new password
                // instead of quietly failing on the next open.
                SyncBiometricEnrolment(args.NewPassword, keepEnrolment: true);

                settings.OnPasswordChanged();
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.CompanyPasswordSet, "changed");
                _appShellViewModel.AddNotification("Success".Translate(), "Password has been changed.".Translate(), NotificationType.Success);
            }
            catch (Exception ex)
            {
                settings.OnPasswordVerificationFailed();
                ErrorLogger?.LogError(ex, ErrorCategory.Authentication, "Failed to change password");
                await ShowErrorMessageBoxAsync("Error".Translate(), "Failed to change password: {0}".TranslateFormat(ex.Message));
            }
        };

        settings.RemovePasswordRequested += async (_, args) =>
        {
            if (CompanyManager?.IsCompanyOpen != true) return;

            // Verify the current password before removing
            if (!CompanyManager.VerifyCurrentPassword(args.CurrentPassword))
            {
                settings.OnPasswordVerificationFailed();
                return;
            }

            try
            {
                await CompanyManager.ChangePasswordAsync(null);

                // With no password there is nothing for biometrics to unlock, so drop the
                // stored credential rather than leaving the old password on disk.
                SyncBiometricEnrolment(null, keepEnrolment: false);
                ConfigureAutoLock();

                settings.OnPasswordRemoved();
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.CompanyPasswordSet, "removed");
                _appShellViewModel.AddNotification("Success".Translate(), "Password has been removed.".Translate(), NotificationType.Success);
            }
            catch (Exception ex)
            {
                settings.OnPasswordVerificationFailed();
                ErrorLogger?.LogError(ex, ErrorCategory.Authentication, "Failed to remove password");
                await ShowErrorMessageBoxAsync("Error".Translate(), "Failed to remove password: {0}".TranslateFormat(ex.Message));
            }
        };

        settings.AutoLockSettingsChanged += (_, args) =>
        {
            if (_idleDetectionService != null)
            {
                var enabled = args.TimeoutMinutes > 0;
                _idleDetectionService.Configure(enabled, args.TimeoutMinutes);
            }

            if (CompanyManager?.CurrentCompanySettings != null)
            {
                CompanyManager.CurrentCompanySettings.Security.AutoLockEnabled = args.TimeoutMinutes > 0;
                CompanyManager.CurrentCompanySettings.Security.AutoLockMinutes = args.TimeoutMinutes;
                CompanyManager.MarkAsChanged();
            }
        };

        // biometric login authentication requested (before enabling)
        settings.BiometricAuthRequested += async (_, _) =>
        {
            var platformService = PlatformServiceFactory.GetPlatformService();

            try
            {
                // Check if biometric login is available
                var available = await platformService.IsBiometricAvailableAsync();
                if (!available)
                {
                    // Get detailed reason why biometric login is not available
                    var details = await platformService.GetBiometricAvailabilityDetailsAsync();

                    var dialog = ConfirmationDialog;
                    if (dialog != null)
                    {
                        await dialog.ShowAsync(new ConfirmationDialogOptions
                        {
                            Title = "Biometric Login Not Available".Translate(),
                            Message = "Biometric login cannot be enabled on this device.\n\nReason: {0}".TranslateFormat(details),
                            PrimaryButtonText = "OK".Translate(),
                            CancelButtonText = ""
                        });
                    }
                    settings.OnBiometricAuthResult(false);
                    return;
                }

                // Request authentication
                var success = await platformService.AuthenticateWithBiometricAsync("Verify your identity to enable biometric login".Translate());
                settings.OnBiometricAuthResult(success);

                if (!success)
                {
                    var dialog = ConfirmationDialog;
                    if (dialog != null)
                    {
                        await dialog.ShowAsync(new ConfirmationDialogOptions
                        {
                            Title = "Biometric Login".Translate(),
                            Message = "Authentication was cancelled or failed. Biometric login has not been enabled.".Translate(),
                            PrimaryButtonText = "OK".Translate(),
                            CancelButtonText = ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogError(ex, ErrorCategory.Authentication, "Biometric authentication failed");
                var dialog = ConfirmationDialog;
                if (dialog != null)
                {
                    await dialog.ShowAsync(new ConfirmationDialogOptions
                    {
                        Title = "Biometric Login Error".Translate(),
                        Message = "Failed to authenticate:\n\n{0}".TranslateFormat(ex.Message),
                        PrimaryButtonText = "OK".Translate(),
                        CancelButtonText = ""
                    });
                }
                settings.OnBiometricAuthResult(false);
            }
        };

        // biometric login setting changed (after successful authentication)
        settings.BiometricLoginChanged += (_, args) =>
        {
            if (CompanyManager?.CurrentCompanySettings != null)
            {
                CompanyManager.CurrentCompanySettings.Security.BiometricEnabled = args.Enabled;
                CompanyManager.MarkAsChanged();

                // Store or clear password for biometric unlock
                if (CompanyManager.CurrentFilePath != null)
                {
                    var fileId = GetBiometricFileId(CompanyManager.CurrentFilePath);
                    var platformService = PlatformServiceFactory.GetPlatformService();

                    if (args.Enabled && CompanyManager.IsEncrypted)
                    {
                        // Store the current password for biometric unlock
                        // Note: We need to get the password from CompanyManager
                        var password = CompanyManager.GetCurrentPassword();
                        if (!string.IsNullOrEmpty(password))
                        {
                            platformService.StorePasswordForBiometric(fileId, password);
                        }
                    }
                    else
                    {
                        // Clear stored password
                        platformService.ClearPasswordForBiometric(fileId);
                    }
                }
            }
        };

        // Load biometric login setting when company opens
        if (CompanyManager != null)
        {
            CompanyManager.CompanyOpened += (_, _) =>
            {
                var securitySettings = CompanyManager.CurrentCompanySettings?.Security;
                if (securitySettings != null)
                {
                    // Use SetBiometricLoginWithoutAuth to avoid triggering authentication on load
                    settings.SetBiometricLoginWithoutAuth(securitySettings.BiometricEnabled);
                }
            };
        }

        // Portal logo file picker
        settings.BrowsePortalLogoRequested += async (_, _) =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Portal Logo".Translate(),
                AllowMultiple = false,
                FileTypeFilter = [Utilities.FilePickerTypes.ImageFileType]
            });

            if (files.Count > 0)
            {
                await settings.UploadPortalLogoFromFileAsync(files[0].Path.LocalPath);
            }
        };

        // Portal authentication: prompt for password/biometric before allowing portal changes
        settings.PortalAuthenticationRequested += async () =>
        {
            if (CompanyManager?.IsCompanyOpen != true || !CompanyManager.IsEncrypted)
                return true; // No password set, allow access

            var passwordModal = _appShellViewModel.PasswordPromptModalViewModel;
            var companyName = CompanyManager.CompanyData!.Settings.Company.Name;
            var filePath = CompanyManager.CurrentFilePath ?? "";

            // Check if biometric login is available for this file
            var biometricAvailable = false;
            var platformService = PlatformServiceFactory.GetPlatformService();
            var securitySettings = CompanyManager.CurrentCompanySettings?.Security;
            if (securitySettings?.BiometricEnabled == true)
            {
                biometricAvailable = await platformService.IsBiometricAvailableAsync();
            }

            var password = await passwordModal.ShowAsync(companyName, filePath, biometricAvailable,
                "Password is required to make changes to the payment portal.".Translate());

            if (password == null)
            {
                // User cancelled
                return false;
            }

            if (password == "__BIOMETRIC__")
            {
                // biometric login succeeded, retrieve stored password and verify
                var fileId = GetBiometricFileId(filePath);
                var storedPassword = platformService.GetPasswordForBiometric(fileId);
                if (!string.IsNullOrEmpty(storedPassword) && CompanyManager.VerifyCurrentPassword(storedPassword))
                {
                    passwordModal.Close();
                    return true;
                }

                // Stored password didn't match, fall back to manual entry
                passwordModal.ShowError("Stored password not found. Please enter the password manually.".Translate());
                password = await passwordModal.WaitForPasswordAsync();
                if (password == null)
                    return false;
            }

            // Verify the entered password
            while (true)
            {
                if (CompanyManager.VerifyCurrentPassword(password))
                {
                    passwordModal.Close();
                    return true;
                }

                passwordModal.ShowError("Incorrect password. Please try again.".Translate());
                password = await passwordModal.WaitForPasswordAsync();
                if (password == null)
                    return false;

                // Handle biometric login retry
                if (password == "__BIOMETRIC__")
                {
                    var fileId = GetBiometricFileId(filePath);
                    var storedPassword = platformService.GetPasswordForBiometric(fileId);
                    if (!string.IsNullOrEmpty(storedPassword) && CompanyManager.VerifyCurrentPassword(storedPassword))
                    {
                        passwordModal.Close();
                        return true;
                    }
                    passwordModal.ShowError("Stored password not found. Please enter the password manually.".Translate());
                    password = await passwordModal.WaitForPasswordAsync();
                    if (password == null)
                        return false;
                }
            }
        };

    }

    /// <summary>
    /// Wires up export as modal events for spreadsheet export.
    /// </summary>
    private static void WireExportEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var exportModal = _appShellViewModel.ExportAsModalViewModel;

        exportModal.RefreshRecordCountsRequested += (_, _) =>
        {
            exportModal.RefreshRecordCounts(CompanyManager?.CompanyData);
        };

        // Handle export request
        exportModal.ExportRequested += async (_, args) =>
        {
            if (args.Format == "backup")
            {
                if (CompanyManager?.IsCompanyOpen != true)
                {
                    await ShowErrorMessageBoxAsync("Error".Translate(), "No company is currently open.".Translate());
                    return;
                }

                // Show save file dialog for backup
                var backupFile = await desktop.MainWindow!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Backup".Translate(),
                    SuggestedFileName = $"{CompanyManager.ToCompanyFileName(CompanyManager.CurrentCompanyName ?? "Backup")}-{DateTime.Now:yyyy-MM-dd}.argobk",
                    DefaultExtension = "argobk",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Argo Books Backup")
                        {
                            Patterns = ["*.argobk"]
                        }
                    ]
                });

                if (backupFile == null) return;

                var backupPath = backupFile.Path.LocalPath;
                _mainWindowViewModel?.ShowLoading("Exporting backup...".Translate());

                var backupStopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await CompanyManager.ExportBackupAsync(backupPath);

                    backupStopwatch.Stop();
                    _mainWindowViewModel?.HideLoading();

                    // Track telemetry
                    var fileSize = new FileInfo(backupPath).Length;
                    if (TelemetryManager != null)
                    {
                        await TelemetryManager.TrackExportAsync(ExportType.Backup, backupStopwatch.ElapsedMilliseconds, fileSize);
                        _ = TelemetryManager.TrackFeatureAsync(FeatureName.BackupCreated);
                    }

                    // Open the containing folder
                    try
                    {
                        var directory = Path.GetDirectoryName(backupPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            if (OperatingSystem.IsWindows())
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{backupPath}\"");
                            else if (OperatingSystem.IsMacOS())
                                System.Diagnostics.Process.Start("open", $"-R \"{backupPath}\"");
                            else if (OperatingSystem.IsLinux())
                                System.Diagnostics.Process.Start("xdg-open", directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger?.LogWarning($"Failed to open folder after backup export: {ex.Message}", "BackupExportFolder");
                    }

                    _appShellViewModel.AddNotification(
                        "Backup Complete".Translate(),
                        "Company backup exported successfully.".Translate(),
                        NotificationType.Success);
                }
                catch (Exception ex)
                {
                    backupStopwatch.Stop();
                    _mainWindowViewModel?.HideLoading();
                    ErrorLogger?.LogError(ex, ErrorCategory.Export, "Failed to export backup");
                    await ShowErrorMessageBoxAsync("Export Failed".Translate(), "Failed to export backup: {0}".TranslateFormat(ex.Message));
                }

                return;
            }

            // Spreadsheet export
            if (args.SelectedDataItems.Count == 0)
            {
                _appShellViewModel.AddNotification("Warning".Translate(), "Please select at least one data type to export.".Translate(), NotificationType.Warning);
                return;
            }

            if (CompanyManager?.CompanyData == null)
            {
                await ShowErrorMessageBoxAsync("Error".Translate(), "No company is currently open.".Translate());
                return;
            }

            // Show save file dialog
            var extension = args.Format;
            var filterName = args.Format.ToUpperInvariant() switch
            {
                "XLSX" => "Excel Workbook",
                "CSV" => "CSV File",
                "PDF" => "PDF Document",
                _ => "File"
            };

            var file = await desktop.MainWindow!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Data".Translate(),
                SuggestedFileName = $"{CompanyManager.CurrentCompanyName ?? "Export"}-{DateTime.Now:yyyy-MM-dd}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices =
                [
                    new FilePickerFileType(filterName)
                    {
                        Patterns = [$"*.{extension}"]
                    }
                ]
            });

            if (file == null) return;

            var filePath = file.Path.LocalPath;
            _mainWindowViewModel?.ShowLoading("Exporting data...".Translate());

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var exportService = new SpreadsheetExportService();

                switch (args.Format.ToLowerInvariant())
                {
                    case "xlsx":
                        await exportService.ExportToExcelAsync(
                            filePath,
                            CompanyManager.CompanyData,
                            args.SelectedDataItems,
                            args.StartDate,
                            args.EndDate);
                        break;

                    case "csv":
                        await exportService.ExportToCsvAsync(
                            filePath,
                            CompanyManager.CompanyData,
                            args.SelectedDataItems,
                            args.StartDate,
                            args.EndDate);
                        break;

                    case "pdf":
                        await exportService.ExportToPdfAsync(
                            filePath,
                            CompanyManager.CompanyData,
                            args.SelectedDataItems,
                            args.StartDate,
                            args.EndDate);
                        break;
                }

                stopwatch.Stop();
                _mainWindowViewModel?.HideLoading();

                // Track export telemetry
                var fileSize = new FileInfo(filePath).Length;
                var exportType = args.Format.ToLowerInvariant() switch
                {
                    "xlsx" => ExportType.Excel,
                    "csv" => ExportType.Csv,
                    "pdf" => ExportType.Pdf,
                    _ => ExportType.Excel
                };
                if (TelemetryManager != null)
                    await TelemetryManager.TrackExportAsync(exportType, stopwatch.ElapsedMilliseconds, fileSize);

                // Open the containing folder
                try
                {
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                        }
                        else if (OperatingSystem.IsMacOS())
                        {
                            System.Diagnostics.Process.Start("open", $"-R \"{filePath}\"");
                        }
                        else if (OperatingSystem.IsLinux())
                        {
                            System.Diagnostics.Process.Start("xdg-open", directory);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLogger?.LogWarning($"Failed to open folder after export: {ex.Message}", "ExportFolder");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _mainWindowViewModel?.HideLoading();
                ErrorLogger?.LogError(ex, ErrorCategory.Export, $"Failed to export {args.Format}");
                await ShowErrorMessageBoxAsync("Export Failed".Translate(), "Failed to export data: {0}".TranslateFormat(ex.Message));
            }
        };
    }

    /// <summary>
    /// Wires up import modal events for spreadsheet import.
    /// </summary>
    private static void WireImportEvents(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_appShellViewModel == null)
            return;

        var importModal = _appShellViewModel.ImportModalViewModel;

        // Handle format selection
        importModal.FormatSelected += async (_, format) =>
        {
            if (CompanyManager?.CompanyData == null)
            {
                await ShowErrorMessageBoxAsync("Error".Translate(), "No company is currently open.".Translate());
                return;
            }

            if (format.ToUpperInvariant() == "BACKUP")
            {
                await RestoreFromBackupAsync(desktop);
                return;
            }

            if (format.ToUpperInvariant() == "BANKSTATEMENT")
            {
                await OpenBankStatementImportAsync();
                return;
            }

            // Excel and CSV import supported
            if (format.ToUpperInvariant() != "EXCEL")
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, $"format-unavailable:{format}");
                await ShowInfoMessageBoxAsync("Info".Translate(), "{0} import will be available in a future update.".TranslateFormat(format));
                return;
            }

            // Show open file dialog: support both .xlsx and .csv
            var file = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Spreadsheet".Translate(),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Spreadsheets")
                    {
                        Patterns = ["*.xlsx", "*.xls", "*.csv"]
                    },
                    new FilePickerFileType("Excel Workbook")
                    {
                        Patterns = ["*.xlsx", "*.xls"]
                    },
                    new FilePickerFileType("CSV File")
                    {
                        Patterns = ["*.csv"]
                    }
                ]
            });

            if (file.Count == 0)
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "file-picker");
                return;
            }

            var filePath = file[0].Path.LocalPath;
            var companyData = CompanyManager.CompanyData;
            var isCsv = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            // Use AI import flow
            await PerformAiImportAsync(filePath, companyData, isCsv);
        };
    }

    /// <summary>
    /// Wires up idle detection for auto-logout functionality.
    /// </summary>
    private static void WireIdleDetection(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_idleDetectionService == null || CompanyManager == null || _appShellViewModel == null)
            return;

        // Handle idle timeout - close the company
        _idleDetectionService.IdleTimeoutReached += async (_, _) =>
        {
            // Must run on UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (CompanyManager.IsCompanyOpen != true) return;

                // Check for unsaved changes (skip auto-save for sample company)
                if (CompanyManager.HasUnsavedChanges && !CompanyManager.IsSampleCompany)
                {
                    // Auto-save before locking
                    try
                    {
                        _mainWindowViewModel?.ShowLoading("Auto-saving before lock...".Translate());
                        await CompanyManager.SaveCompanyAsync();
                        _mainWindowViewModel?.HideLoading();
                    }
                    catch (Exception ex)
                    {
                        _mainWindowViewModel?.HideLoading();
                        ErrorLogger?.LogWarning($"Auto-save before lock failed: {ex.Message}", "AutoSave");
                        // Do NOT close on a failed save: CloseCompanyAsync discards the working copy and
                        // every unsaved edit. Abort the lock and keep the session open so the user can
                        // save manually, matching the window-close path which also refuses to discard.
                        _appShellViewModel.AddNotification(
                            "Auto-lock postponed",
                            "Could not auto-save before locking, so your session stays open to protect unsaved changes. Please save manually.",
                            NotificationType.Warning);
                        _idleDetectionService.ResetIdleTimer();
                        return;
                    }
                }

                // Close the company - this will trigger navigation back to welcome screen
                await CompanyManager.CloseCompanyAsync();

                // Re-enable idle detection for next session
                _idleDetectionService.ResetIdleTimer();
            });
        };

        // Configure based on current company settings when company opens
        CompanyManager.CompanyOpened += (_, _) =>
        {
            var companySettings = CompanyManager.CurrentCompanySettings;
            if (companySettings != null)
            {
                var security = companySettings.Security;
                ConfigureAutoLock();

                // Sync the UI with company settings
                var timeoutString = security.AutoLockMinutes switch
                {
                    0 => "Never",
                    60 => "1 hour",
                    _ => $"{security.AutoLockMinutes} minutes"
                };
                
                // Use SetAutoLockWithoutNotify to avoid triggering MarkAsChanged during load
                _appShellViewModel.SettingsModalViewModel.SetAutoLockWithoutNotify(timeoutString);
            }
        };

        // Disable idle detection when company closes
        CompanyManager.CompanyClosed += (_, _) =>
        {
            _idleDetectionService.Configure(false, 0);
        };

        // Record activity on main window pointer/key events
        if (desktop.MainWindow != null)
        {
            desktop.MainWindow.PointerMoved += (_, _) => _idleDetectionService.RecordActivity();
            desktop.MainWindow.KeyDown += (_, _) => _idleDetectionService.RecordActivity();
            desktop.MainWindow.PointerPressed += (_, _) => _idleDetectionService.RecordActivity();
        }
    }

    /// <summary>
    /// Arms or disarms auto-lock for the open company. Called on open and whenever a password is
    /// added or removed, since whether the company has one decides it.
    /// </summary>
    private static void ConfigureAutoLock()
    {
        var security = CompanyManager?.CurrentCompanySettings?.Security;
        if (_idleDetectionService == null || security == null) return;

        // The timeout is authoritative: "Never" (0) is off, any positive value is on, and it
        // requires a password. The separate AutoLockEnabled flag defaults to false while the
        // default AutoLockMinutes is 5, so trusting it left auto-lock disabled even though the
        // settings dropdown showed "5 minutes". Derive enablement from the timeout instead.
        var autoLockOn = security.AutoLockMinutes > 0 && CompanyManager!.IsEncrypted;
        _idleDetectionService.Configure(autoLockOn, security.AutoLockMinutes);
    }

    /// <summary>
    /// Set when the "Where did you hear about Argo Books?" survey is waiting on a completion
    /// guidance card, which the step that triggered it raises in the same call-stack, to be
    /// dismissed before the survey opens on top of it.
    /// </summary>
    private static bool _surveyPendingAfterGuidance;

    private static void WireSourceSurveyEvents()
    {
        // Asked once the second step lands rather than on a finished checklist. The third
        // step is now a real import, which not everyone will complete, and this survey is
        // one of the few ways an unattributed install ever reports where it came from.
        // Both triggers go through ShouldShowSourceSurvey(), which returns false once an
        // answer is stored, so keeping the completion one as a fallback cannot double-ask.
        TutorialService.Instance.ChecklistItemCompleted += (_, itemId) =>
        {
            if (itemId == TutorialService.ChecklistItems.RecordExpense)
                ShowSourceSurveyWhenGuidanceClears();
        };

        TutorialService.Instance.AllChecklistItemsCompleted += (_, _) => ShowSourceSurveyWhenGuidanceClears();

        void ShowSourceSurveyWhenGuidanceClears()
        {
            if (!TutorialService.Instance.ShouldShowSourceSurvey())
                return;

            // The step that triggered this raised its own guidance card in the same
            // call-stack. Wait for it to be dismissed rather than stacking the survey on top.
            if (TutorialService.Instance.ShowCompletionGuidance)
            {
                _surveyPendingAfterGuidance = true;
            }
            else
            {
                TutorialService.Instance.RequestShowSourceSurvey();
            }
        }

        TutorialService.Instance.CompletionGuidanceChanged += (_, show) =>
        {
            if (show || !_surveyPendingAfterGuidance) return;
            _surveyPendingAfterGuidance = false;
            TutorialService.Instance.RequestShowSourceSurvey();
        };
    }

}

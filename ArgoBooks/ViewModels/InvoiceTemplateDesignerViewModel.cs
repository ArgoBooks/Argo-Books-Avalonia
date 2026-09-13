using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Invoice Template Designer modal.
/// Allows users to customize invoice templates.
/// </summary>
public partial class InvoiceTemplateDesignerViewModel : ViewModelBase
{
    #region Events

    public event EventHandler? TemplateSaved;
    public event EventHandler? ModalClosed;
    public event EventHandler? BrowseLogoRequested;

    /// <summary>
    /// Function to capture a screenshot of the current preview. Set by the view.
    /// </summary>
    public Func<Task<string?>>? CapturePreviewFunc { get; set; }

    #endregion

    #region Modal State

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isTemplateListMode;

    [ObservableProperty]
    private string _templateToDelete = string.Empty;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private string _modalTitle = "Create Invoice Template";

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    private int _propertiesTabIndex;

    private readonly UndoRedoManager _undoRedoManager = new();
    private bool _suppressUndoRecording;

    // Cached services
    private readonly InvoiceEmailService _emailService = new();

    public UndoRedoButtonGroupViewModel UndoRedoViewModel { get; }

    public bool HasUnsavedChanges => !_undoRedoManager.IsAtSavedState;

    public InvoiceTemplateDesignerViewModel()
    {
        UndoRedoViewModel = new UndoRedoButtonGroupViewModel(_undoRedoManager);
    }

    #endregion

    #region Template Properties

    private string _editingTemplateId = string.Empty;

    [ObservableProperty]
    private string _templateName = string.Empty;

    [ObservableProperty]
    private InvoiceTemplateType _selectedBaseTemplate = InvoiceTemplateType.Professional;

    [ObservableProperty]
    private bool _isDefault;

    // Colors
    [ObservableProperty]
    private string _primaryColor = AppColors.PrimaryHover;

    [ObservableProperty]
    private string _secondaryColor = AppColors.ChartGrid;

    [ObservableProperty]
    private string _accentColor = AppColors.EmeraldHover;

    [ObservableProperty]
    private string _headerColor = AppColors.PrimaryHover;

    [ObservableProperty]
    private string _textColor = AppColors.TextLight;

    [ObservableProperty]
    private string _backgroundColor = AppColors.White;

    // Dynamic color labels per template type
    public string PrimaryColorLabel => SelectedBaseTemplate switch
    {
        InvoiceTemplateType.Ribbon => "Ribbon 1 (Blue)",
        InvoiceTemplateType.Professional => "Banner Color",
        InvoiceTemplateType.Modern => "Sidebar Color",
        InvoiceTemplateType.Classic => "Header & Border",
        InvoiceTemplateType.Elegant => "Accent Border",
        _ => "Primary Color"
    };

    public string SecondaryColorLabel => SelectedBaseTemplate switch
    {
        InvoiceTemplateType.Ribbon => "Ribbon 2 (Yellow)",
        InvoiceTemplateType.Modern => "Background Tint",
        InvoiceTemplateType.Classic => "Border Color",
        InvoiceTemplateType.Elegant => "Subtle Background",
        _ => "Secondary Color"
    };

    public string AccentColorLabel => SelectedBaseTemplate switch
    {
        InvoiceTemplateType.Ribbon => "Ribbon 3 (Green)",
        InvoiceTemplateType.Modern => "Payment Accent",
        InvoiceTemplateType.Elegant => "Highlight Color",
        _ => "Accent Color"
    };

    // The Classic template doesn't use an accent color anywhere, so hide the option
    public bool ShowAccentColorOption => SelectedBaseTemplate != InvoiceTemplateType.Classic;

    public string HeaderColorLabel => SelectedBaseTemplate switch
    {
        InvoiceTemplateType.Ribbon => "Title & Heading",
        InvoiceTemplateType.Professional => "Heading & Totals",
        InvoiceTemplateType.Modern => "Title & Labels",
        InvoiceTemplateType.Classic => "Title & Labels",
        InvoiceTemplateType.Elegant => "Title & Totals",
        _ => "Header Color"
    };

    // Font
    [ObservableProperty]
    private string _selectedFontFamily = "Arial, Helvetica, sans-serif";

    // Logo
    [ObservableProperty]
    private bool _showLogo = true;

    [ObservableProperty]
    private string? _logoBase64;

    // The actual logo width value used for rendering (always valid)
    private int _logoWidth = 150;
    public int LogoWidth
    {
        get => _logoWidth;
        set => SetLogoWidth(value, updateText: true);
    }

    // String property for TextBox binding with validation
    private string _logoWidthText = "150";
    public string LogoWidthText
    {
        get => _logoWidthText;
        set
        {
            if (SetProperty(ref _logoWidthText, value))
            {
                ValidateLogoWidth(value);
            }
        }
    }

    [ObservableProperty]
    private string _logoWidthWarning = string.Empty;

    [ObservableProperty]
    private bool _hasLogoWidthWarning;

    private void SetLogoWidth(int value, bool updateText)
    {
        var oldValue = _logoWidth;
        if (SetProperty(ref _logoWidth, value, nameof(LogoWidth)))
        {
            if (updateText)
            {
                _logoWidthText = value.ToString();
                OnPropertyChanged(nameof(LogoWidthText));
            }
            LogoWidthWarning = string.Empty;
            HasLogoWidthWarning = false;
            RecordChange("Change logo width", v => LogoWidth = v, oldValue, value);
            UpdatePreview();
        }
    }

    private void ValidateLogoWidth(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            LogoWidthWarning = "Please enter a value";
            HasLogoWidthWarning = true;
            return;
        }

        if (!int.TryParse(value, out var width))
        {
            LogoWidthWarning = "Please enter a valid number";
            HasLogoWidthWarning = true;
            return;
        }

        if (width < 20 || width > 300)
        {
            LogoWidthWarning = "Value must be between 20 and 300";
            HasLogoWidthWarning = true;
            return;
        }

        // Valid value - clear warning first, then update width
        LogoWidthWarning = string.Empty;
        HasLogoWidthWarning = false;
        SetLogoWidth(width, updateText: false);
    }

    [ObservableProperty]
    private bool _hasLogo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogoFileName))]
    private string _logoPath = string.Empty;

    public string LogoFileName => string.IsNullOrEmpty(LogoPath)
        ? string.Empty
        : Path.GetFileName(LogoPath);

    [ObservableProperty]
    private bool _lockAspectRatio = true;

    // Text content
    [ObservableProperty]
    private string _headerText = "INVOICE";

    [ObservableProperty]
    private string _footerText = "Thank you for your business!";

    [ObservableProperty]
    private string _paymentInstructions = string.Empty;

    [ObservableProperty]
    private string _defaultNotes = string.Empty;

    // Display options
    [ObservableProperty]
    private bool _showCompanyAddress = true;

    [ObservableProperty]
    private bool _showCompanyPhone = true;

    [ObservableProperty]
    private bool _showCompanyCity = true;

    [ObservableProperty]
    private bool _showCompanyProvinceState = true;

    [ObservableProperty]
    private bool _showCompanyCountry = true;

    [ObservableProperty]
    private bool _showTaxBreakdown = true;

    [ObservableProperty]
    private bool _showItemDescriptions = true;

    [ObservableProperty]
    private bool _showPaymentInstructions = true;

    [ObservableProperty]
    private bool _showDueDateProminent = true;

    // Preview HTML
    [ObservableProperty]
    private string _previewHtml = string.Empty;

    // Controls WebView visibility - hidden when confirmation dialog is shown (airspace issue)
    [ObservableProperty]
    private bool _isPreviewVisible;

    #endregion

    #region Collections

    public ObservableCollection<InvoiceTemplate> CustomTemplates { get; } = [];

    [ObservableProperty]
    private bool _hasNoCustomTemplates;

    public ObservableCollection<InvoiceTemplateType> BaseTemplateOptions { get; } =
    [
        InvoiceTemplateType.Professional,
        InvoiceTemplateType.Modern,
        InvoiceTemplateType.Classic,
        InvoiceTemplateType.Elegant,
        InvoiceTemplateType.Ribbon
    ];

    public ObservableCollection<string> FontFamilyOptions { get; } =
    [
        "Arial, Helvetica, sans-serif",
        "'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
        "'Open Sans', 'Segoe UI', Arial, sans-serif",
        "Georgia, 'Times New Roman', Times, serif",
        "system-ui, -apple-system, sans-serif",
        "'Courier New', Courier, monospace"
    ];

    #endregion

    #region Template List

    public void OpenTemplateList()
    {
        LoadSavedTemplates();
        IsTemplateListMode = true;
        IsDeleteConfirmOpen = false;
        ModalTitle = "Invoice Templates".Translate();
        IsOpen = true;
        IsPreviewVisible = false;
        _undoRedoManager.Clear();
    }

    private void LoadSavedTemplates()
    {
        CustomTemplates.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            HasNoCustomTemplates = true;
            return;
        }

        foreach (var template in companyData.InvoiceTemplates
                     .Where(t => !t.Id.StartsWith("default-"))
                     .OrderBy(t => t.Name))
        {
            CustomTemplates.Add(template);
        }

        HasNoCustomTemplates = CustomTemplates.Count == 0;
    }

    [RelayCommand]
    private void EditTemplate(InvoiceTemplate? template)
    {
        if (template == null) return;
        IsTemplateListMode = false;
        OpenEditModal(template);
    }

    [RelayCommand]
    private void OpenDeleteTemplate(InvoiceTemplate? template)
    {
        if (template == null) return;
        TemplateToDelete = template.Id;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        TemplateToDelete = string.Empty;
    }

    [RelayCommand]
    private void ConfirmDeleteTemplate()
    {
        if (string.IsNullOrEmpty(TemplateToDelete)) return;

        var companyData = App.CompanyManager?.CompanyData;
        var template = companyData?.InvoiceTemplates.FirstOrDefault(t => t.Id == TemplateToDelete);
        if (template != null)
        {
            var deletedTemplate = template;
            companyData!.InvoiceTemplates.Remove(template);
            App.CompanyManager?.MarkAsChanged();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete invoice template '{deletedTemplate.Name}'",
                () =>
                {
                    companyData.InvoiceTemplates.Add(deletedTemplate);
                    App.CompanyManager?.MarkAsChanged();
                    LoadSavedTemplates();
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    companyData.InvoiceTemplates.Remove(deletedTemplate);
                    App.CompanyManager?.MarkAsChanged();
                    LoadSavedTemplates();
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                }));

            TemplateSaved?.Invoke(this, EventArgs.Empty);
            LoadSavedTemplates();
        }

        IsDeleteConfirmOpen = false;
        TemplateToDelete = string.Empty;
    }

    [RelayCommand]
    private void CreateNewFromList()
    {
        IsTemplateListMode = false;
        _suppressUndoRecording = true;
        ResetForm();
        _suppressUndoRecording = false;
        _undoRedoManager.Clear();
        IsEditMode = false;
        ModalTitle = "Create Invoice Template".Translate();
        UpdatePreview();
        IsPreviewVisible = true;
    }

    #endregion

    #region Open Modal

    public void OpenCreateModal()
    {
        _suppressUndoRecording = true;
        ResetForm();
        _suppressUndoRecording = false;
        _undoRedoManager.Clear();
        IsEditMode = false;
        ModalTitle = "Create Invoice Template".Translate();
        UpdatePreview();
        IsOpen = true;
        IsPreviewVisible = true;
    }

    public void OpenEditModal(InvoiceTemplate template)
    {
        _suppressUndoRecording = true;
        LoadTemplate(template);
        _suppressUndoRecording = false;
        _undoRedoManager.Clear();
        IsEditMode = true;
        IsTemplateListMode = false;
        ModalTitle = "Edit Template: {0}".TranslateFormat(template.Name);
        UpdatePreview();
        IsOpen = true;
        IsPreviewVisible = true;
    }

    public void OpenEditModal(string templateId)
    {
        var template = App.CompanyManager?.CompanyData?.GetInvoiceTemplate(templateId);
        if (template != null)
        {
            OpenEditModal(template);
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task RequestClose()
    {
        if (HasUnsavedChanges)
        {
            // Hide the WebView so the confirmation dialog renders above it (airspace issue)
            IsPreviewVisible = false;

            if (!await ConfirmDiscardEditsAsync())
            {
                IsPreviewVisible = true;
                return;
            }
        }

        Close();
    }

    private void Close()
    {
        IsPreviewVisible = false;
        IsTemplateListMode = false;
        IsDeleteConfirmOpen = false;
        IsOpen = false;
        IsFullscreen = false;
        ModalClosed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    [RelayCommand]
    private async Task Save()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            ValidationMessage = "Please enter a template name".Translate();
            HasValidationMessage = true;
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        // Check for duplicate name (exclude the template being edited)
        var duplicateName = companyData.InvoiceTemplates
            .Where(t => !t.Id.StartsWith("default-"))
            .Any(t => t.Id != _editingTemplateId
                       && string.Equals(t.Name, TemplateName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
        {
            ValidationMessage = "A template with this name already exists. Please choose a different name.".Translate();
            HasValidationMessage = true;
            return;
        }

        // Capture thumbnail from the live preview before saving
        string? thumbnail = null;
        if (CapturePreviewFunc != null)
        {
            try { thumbnail = await CapturePreviewFunc(); }
            catch { /* Ignore capture failures */ }
        }

        if (IsEditMode)
        {
            // Update existing template
            var template = companyData.GetInvoiceTemplate(_editingTemplateId);
            if (template != null)
            {
                // Capture snapshot before editing for undo
                var oldSnapshot = template.Clone();
                var oldDefaults = companyData.InvoiceTemplates
                    .Where(t => t.Id != template.Id && t.IsDefault)
                    .Select(t => t.Id).ToList();

                UpdateTemplateFromForm(template);
                if (thumbnail != null) template.ThumbnailBase64 = thumbnail;

                // If this is being set as default, unset others
                if (template.IsDefault)
                {
                    foreach (var t in companyData.InvoiceTemplates.Where(t => t.Id != template.Id))
                    {
                        t.IsDefault = false;
                    }
                }

                var newSnapshot = template.Clone();
                var templateToUndo = template;

                App.UndoRedoManager.RecordAction(new DelegateAction(
                    $"Edit invoice template '{newSnapshot.Name}'",
                    () =>
                    {
                        RestoreTemplateFromSnapshot(templateToUndo, oldSnapshot);
                        // Restore previous default flags
                        foreach (var t in companyData.InvoiceTemplates.Where(t => t.Id != templateToUndo.Id))
                            t.IsDefault = oldDefaults.Contains(t.Id);
                        companyData.MarkAsModified();
                        TemplateSaved?.Invoke(this, EventArgs.Empty);
                    },
                    () =>
                    {
                        RestoreTemplateFromSnapshot(templateToUndo, newSnapshot);
                        if (newSnapshot.IsDefault)
                        {
                            foreach (var t in companyData.InvoiceTemplates.Where(t => t.Id != templateToUndo.Id))
                                t.IsDefault = false;
                        }
                        companyData.MarkAsModified();
                        TemplateSaved?.Invoke(this, EventArgs.Empty);
                    }));
            }
        }
        else
        {
            // Create new template
            var id = $"template-{++companyData.IdCounters.InvoiceTemplate}";
            var template = new InvoiceTemplate { Id = id };
            UpdateTemplateFromForm(template);
            template.CreatedAt = DateTime.UtcNow;
            if (thumbnail != null) template.ThumbnailBase64 = thumbnail;

            // If this is being set as default or is the first template, handle defaults
            if (template.IsDefault || companyData.InvoiceTemplates.Count == 0)
            {
                template.IsDefault = true;
                foreach (var t in companyData.InvoiceTemplates)
                {
                    t.IsDefault = false;
                }
            }

            companyData.InvoiceTemplates.Add(template);

            var templateToUndo = template;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Add invoice template '{template.Name}'",
                () =>
                {
                    companyData.InvoiceTemplates.Remove(templateToUndo);
                    companyData.MarkAsModified();
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    companyData.InvoiceTemplates.Add(templateToUndo);
                    companyData.MarkAsModified();
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                }));
        }

        App.CompanyManager?.MarkAsChanged();
        _undoRedoManager.MarkSaved();
        TemplateSaved?.Invoke(this, EventArgs.Empty);
        OpenTemplateList();
    }

    [RelayCommand]
    private async Task BackToTemplateList()
    {
        if (HasUnsavedChanges)
        {
            IsPreviewVisible = false;
            if (!await ConfirmDiscardEditsAsync())
            {
                IsPreviewVisible = true;
                return;
            }
        }

        OpenTemplateList();
    }

    [RelayCommand]
    private void SelectLogo()
    {
        // Raise event to open file picker in App.axaml.cs
        BrowseLogoRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the logo from a file path.
    /// </summary>
    /// <param name="filePath">Path to the image file.</param>
    public void SetLogoFromFile(string filePath)
    {
        try
        {
            var bytes = SharedFileReader.ReadAllBytes(filePath);
            var newBase64 = Convert.ToBase64String(bytes);

            var oldBase64 = LogoBase64;
            var oldPath = LogoPath;
            var oldHasLogo = HasLogo;
            var oldLockAspect = LockAspectRatio;

            _suppressUndoRecording = true;
            LogoBase64 = newBase64;
            LogoPath = filePath;
            HasLogo = true;
            LockAspectRatio = true;
            _suppressUndoRecording = false;

            _undoRedoManager.RecordAction(new DelegateAction(
                "Set logo",
                () => { _suppressUndoRecording = true; LogoBase64 = oldBase64; LogoPath = oldPath; HasLogo = oldHasLogo; LockAspectRatio = oldLockAspect; _suppressUndoRecording = false; UpdatePreview(); },
                () => { _suppressUndoRecording = true; LogoBase64 = newBase64; LogoPath = filePath; HasLogo = true; LockAspectRatio = true; _suppressUndoRecording = false; UpdatePreview(); }
            ));

            UpdatePreview();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "Failed to load logo");
        }
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        var oldBase64 = LogoBase64;
        var oldPath = LogoPath;

        _suppressUndoRecording = true;
        LogoBase64 = null;
        LogoPath = string.Empty;
        HasLogo = false;
        _suppressUndoRecording = false;

        _undoRedoManager.RecordAction(new DelegateAction(
            "Remove logo",
            () => { _suppressUndoRecording = true; LogoBase64 = oldBase64; LogoPath = oldPath; HasLogo = !string.IsNullOrEmpty(oldBase64); _suppressUndoRecording = false; UpdatePreview(); },
            () => { _suppressUndoRecording = true; LogoBase64 = null; LogoPath = string.Empty; HasLogo = false; _suppressUndoRecording = false; UpdatePreview(); }
        ));

        UpdatePreview();
    }

    [RelayCommand]
    private async Task PreviewInBrowser()
    {
        UpdatePreview();
        await InvoicePreviewService.PreviewInBrowserAsync(PreviewHtml, "template-preview");
    }

    [RelayCommand]
    private void SetPropertiesTab(string index)
    {
        if (int.TryParse(index, out var tabIndex))
        {
            PropertiesTabIndex = tabIndex;
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnSelectedBaseTemplateChanged(InvoiceTemplateType oldValue, InvoiceTemplateType newValue)
    {
        // Load default colors for the selected base template
        var defaults = newValue switch
        {
            InvoiceTemplateType.Professional => InvoiceTemplateFactory.CreateProfessionalTemplate(),
            InvoiceTemplateType.Modern => InvoiceTemplateFactory.CreateModernTemplate(),
            InvoiceTemplateType.Classic => InvoiceTemplateFactory.CreateClassicTemplate(),
            InvoiceTemplateType.Elegant => InvoiceTemplateFactory.CreateElegantTemplate(),
            InvoiceTemplateType.Ribbon => InvoiceTemplateFactory.CreateRibbonTemplate(),
            _ => InvoiceTemplateFactory.CreateProfessionalTemplate()
        };

        // Only update colors if not in edit mode and not suppressed (e.g. during undo/load)
        if (!IsEditMode && !_suppressUndoRecording)
        {
            // Capture old state
            var prevPrimary = PrimaryColor;
            var prevSecondary = SecondaryColor;
            var prevAccent = AccentColor;
            var prevHeader = HeaderColor;
            var prevText = TextColor;
            var prevBg = BackgroundColor;
            var prevFont = SelectedFontFamily;

            _suppressUndoRecording = true;
            PrimaryColor = defaults.PrimaryColor;
            SecondaryColor = defaults.SecondaryColor;
            AccentColor = defaults.AccentColor;
            HeaderColor = !string.IsNullOrEmpty(defaults.HeaderColor) ? defaults.HeaderColor : defaults.PrimaryColor;
            TextColor = defaults.TextColor;
            BackgroundColor = defaults.BackgroundColor;
            SelectedFontFamily = defaults.FontFamily;
            _suppressUndoRecording = false;

            // Capture new state
            var newPrimary = PrimaryColor;
            var newSecondary = SecondaryColor;
            var newAccent = AccentColor;
            var newHeader = HeaderColor;
            var newText = TextColor;
            var newBg = BackgroundColor;
            var newFont = SelectedFontFamily;

            _undoRedoManager.RecordAction(new DelegateAction(
                $"Switch to {newValue}",
                () =>
                {
                    _suppressUndoRecording = true;
                    PrimaryColor = prevPrimary;
                    SecondaryColor = prevSecondary;
                    AccentColor = prevAccent;
                    HeaderColor = prevHeader;
                    TextColor = prevText;
                    BackgroundColor = prevBg;
                    SelectedFontFamily = prevFont;
                    SelectedBaseTemplate = oldValue;
                    _suppressUndoRecording = false;
                },
                () =>
                {
                    _suppressUndoRecording = true;
                    PrimaryColor = newPrimary;
                    SecondaryColor = newSecondary;
                    AccentColor = newAccent;
                    HeaderColor = newHeader;
                    TextColor = newText;
                    BackgroundColor = newBg;
                    SelectedFontFamily = newFont;
                    SelectedBaseTemplate = newValue;
                    _suppressUndoRecording = false;
                }
            ));
        }

        // Notify label changes (always)
        OnPropertyChanged(nameof(PrimaryColorLabel));
        OnPropertyChanged(nameof(SecondaryColorLabel));
        OnPropertyChanged(nameof(AccentColorLabel));
        OnPropertyChanged(nameof(HeaderColorLabel));
        OnPropertyChanged(nameof(ShowAccentColorOption));

        UpdatePreview();
    }

    partial void OnTemplateNameChanged(string? oldValue, string newValue)
    {
        RecordChange("Change template name", v => TemplateName = v, oldValue!, newValue);
        // Clear validation message when user starts typing
        if (HasValidationMessage)
        {
            ValidationMessage = string.Empty;
            HasValidationMessage = false;
        }
    }

    partial void OnIsDefaultChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle default template", v => IsDefault = v, oldValue, newValue);
    }

    partial void OnPrimaryColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change primary color", v => PrimaryColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnSecondaryColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change secondary color", v => SecondaryColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnAccentColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change accent color", v => AccentColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnHeaderColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change header color", v => HeaderColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnTextColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change text color", v => TextColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnBackgroundColorChanged(string? oldValue, string newValue)
    {
        RecordChange("Change background color", v => BackgroundColor = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnSelectedFontFamilyChanged(string? oldValue, string newValue)
    {
        RecordChange("Change font family", v => SelectedFontFamily = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnHeaderTextChanged(string? oldValue, string newValue)
    {
        RecordChange("Change header text", v => HeaderText = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnFooterTextChanged(string? oldValue, string newValue)
    {
        RecordChange("Change footer text", v => FooterText = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnPaymentInstructionsChanged(string? oldValue, string newValue)
    {
        RecordChange("Change payment instructions", v => PaymentInstructions = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnDefaultNotesChanged(string? oldValue, string newValue)
    {
        RecordChange("Change default notes", v => DefaultNotes = v, oldValue!, newValue);
        UpdatePreview();
    }

    partial void OnShowLogoChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle show logo", v => ShowLogo = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowCompanyAddressChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle company address", v => ShowCompanyAddress = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowCompanyPhoneChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle company phone", v => ShowCompanyPhone = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowCompanyCityChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle company city", v => ShowCompanyCity = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowCompanyProvinceStateChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle company province/state", v => ShowCompanyProvinceState = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowCompanyCountryChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle company country", v => ShowCompanyCountry = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowTaxBreakdownChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle tax breakdown", v => ShowTaxBreakdown = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowItemDescriptionsChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle item descriptions", v => ShowItemDescriptions = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowPaymentInstructionsChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle payment instructions", v => ShowPaymentInstructions = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnShowDueDateProminentChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle due date prominent", v => ShowDueDateProminent = v, oldValue, newValue);
        UpdatePreview();
    }

    partial void OnLogoBase64Changed(string? oldValue, string? newValue)
    {
        HasLogo = !string.IsNullOrEmpty(newValue);
        UpdatePreview();
    }

    partial void OnLockAspectRatioChanged(bool oldValue, bool newValue)
    {
        RecordChange("Toggle aspect ratio lock", v => LockAspectRatio = v, oldValue, newValue);
        UpdatePreview();
    }

    #endregion

    #region Helper Methods

    private void RecordChange<T>(string description, Action<T> setter, T oldValue, T newValue, [System.Runtime.CompilerServices.CallerMemberName] string? callerName = null)
    {
        if (!_suppressUndoRecording)
            _undoRedoManager.RecordAction(new CoalescingPropertyChangeAction<T>(
                description, $"template:{callerName}", setter, oldValue, newValue));
    }

    private DispatcherTimer? _previewDebounceTimer;
    private static readonly TimeSpan PreviewDebounceInterval = TimeSpan.FromMilliseconds(300);

    private void UpdatePreview()
    {
        // Debounce re-renders so rapid edits (e.g. fast typing) don't trigger
        // a NavigateToString flash on every keystroke.
        if (_previewDebounceTimer == null)
        {
            _previewDebounceTimer = new DispatcherTimer { Interval = PreviewDebounceInterval };
            _previewDebounceTimer.Tick += OnPreviewDebounceTick;
        }
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private void OnPreviewDebounceTick(object? sender, EventArgs e)
    {
        _previewDebounceTimer?.Stop();
        var template = BuildTemplateFromForm();
        var companySettings = App.CompanyManager?.CompanyData?.Settings ?? new();
        PreviewHtml = _emailService.RenderTemplatePreview(template, companySettings, LockAspectRatio);
    }

    private InvoiceTemplate BuildTemplateFromForm()
    {
        return new InvoiceTemplate
        {
            Id = _editingTemplateId,
            Name = TemplateName,
            BaseTemplate = SelectedBaseTemplate,
            IsDefault = IsDefault,
            PrimaryColor = PrimaryColor,
            SecondaryColor = SecondaryColor,
            AccentColor = AccentColor,
            HeaderColor = HeaderColor,
            TextColor = TextColor,
            BackgroundColor = BackgroundColor,
            FontFamily = SelectedFontFamily,
            LogoBase64 = LogoBase64,
            LogoWidth = LogoWidth,
            HeaderText = HeaderText,
            FooterText = FooterText,
            PaymentInstructions = PaymentInstructions,
            DefaultNotes = DefaultNotes,
            ShowLogo = ShowLogo,
            ShowCompanyAddress = ShowCompanyAddress,
            ShowCompanyPhone = ShowCompanyPhone,
            ShowCompanyCity = ShowCompanyCity,
            ShowCompanyProvinceState = ShowCompanyProvinceState,
            ShowCompanyCountry = ShowCompanyCountry,
            ShowTaxBreakdown = ShowTaxBreakdown,
            ShowItemDescriptions = ShowItemDescriptions,
            ShowPaymentInstructions = ShowPaymentInstructions,
            ShowDueDateProminent = ShowDueDateProminent
        };
    }

    private void UpdateTemplateFromForm(InvoiceTemplate template)
    {
        template.Name = TemplateName;
        template.BaseTemplate = SelectedBaseTemplate;
        template.IsDefault = IsDefault;
        template.PrimaryColor = PrimaryColor;
        template.SecondaryColor = SecondaryColor;
        template.AccentColor = AccentColor;
        template.HeaderColor = HeaderColor;
        template.TextColor = TextColor;
        template.BackgroundColor = BackgroundColor;
        template.FontFamily = SelectedFontFamily;
        template.LogoBase64 = LogoBase64;
        template.LogoWidth = LogoWidth;
        template.HeaderText = HeaderText;
        template.FooterText = FooterText;
        template.PaymentInstructions = PaymentInstructions;
        template.DefaultNotes = DefaultNotes;
        template.ShowLogo = ShowLogo;
        template.ShowCompanyAddress = ShowCompanyAddress;
        template.ShowCompanyPhone = ShowCompanyPhone;
        template.ShowCompanyCity = ShowCompanyCity;
        template.ShowCompanyProvinceState = ShowCompanyProvinceState;
        template.ShowCompanyCountry = ShowCompanyCountry;
        template.ShowTaxBreakdown = ShowTaxBreakdown;
        template.ShowItemDescriptions = ShowItemDescriptions;
        template.ShowPaymentInstructions = ShowPaymentInstructions;
        template.ShowDueDateProminent = ShowDueDateProminent;
        template.UpdatedAt = DateTime.UtcNow;
    }

    private static void RestoreTemplateFromSnapshot(InvoiceTemplate target, InvoiceTemplate snapshot)
    {
        target.Name = snapshot.Name;
        target.BaseTemplate = snapshot.BaseTemplate;
        target.IsDefault = snapshot.IsDefault;
        target.PrimaryColor = snapshot.PrimaryColor;
        target.SecondaryColor = snapshot.SecondaryColor;
        target.AccentColor = snapshot.AccentColor;
        target.HeaderColor = snapshot.HeaderColor;
        target.TextColor = snapshot.TextColor;
        target.BackgroundColor = snapshot.BackgroundColor;
        target.FontFamily = snapshot.FontFamily;
        target.LogoBase64 = snapshot.LogoBase64;
        target.LogoWidth = snapshot.LogoWidth;
        target.HeaderText = snapshot.HeaderText;
        target.FooterText = snapshot.FooterText;
        target.PaymentInstructions = snapshot.PaymentInstructions;
        target.DefaultNotes = snapshot.DefaultNotes;
        target.ShowLogo = snapshot.ShowLogo;
        target.ShowCompanyAddress = snapshot.ShowCompanyAddress;
        target.ShowCompanyPhone = snapshot.ShowCompanyPhone;
        target.ShowCompanyCity = snapshot.ShowCompanyCity;
        target.ShowCompanyProvinceState = snapshot.ShowCompanyProvinceState;
        target.ShowCompanyCountry = snapshot.ShowCompanyCountry;
        target.ShowTaxBreakdown = snapshot.ShowTaxBreakdown;
        target.ShowItemDescriptions = snapshot.ShowItemDescriptions;
        target.ShowPaymentInstructions = snapshot.ShowPaymentInstructions;
        target.ShowDueDateProminent = snapshot.ShowDueDateProminent;
        target.ThumbnailBase64 = snapshot.ThumbnailBase64;
        target.UpdatedAt = snapshot.UpdatedAt;
    }

    private void LoadTemplate(InvoiceTemplate template)
    {
        _editingTemplateId = template.Id;
        TemplateName = template.Name;
        SelectedBaseTemplate = template.BaseTemplate;
        IsDefault = template.IsDefault;
        PrimaryColor = template.PrimaryColor;
        SecondaryColor = template.SecondaryColor;
        AccentColor = template.AccentColor;
        HeaderColor = !string.IsNullOrEmpty(template.HeaderColor) ? template.HeaderColor : template.PrimaryColor;
        TextColor = template.TextColor;
        BackgroundColor = template.BackgroundColor;
        SelectedFontFamily = template.FontFamily;
        LogoBase64 = template.LogoBase64;
        LogoWidth = template.LogoWidth;
        LockAspectRatio = true;
        HeaderText = template.HeaderText;
        FooterText = template.FooterText;
        PaymentInstructions = template.PaymentInstructions;
        DefaultNotes = template.DefaultNotes;
        ShowLogo = template.ShowLogo;
        ShowCompanyAddress = template.ShowCompanyAddress;
        ShowCompanyPhone = template.ShowCompanyPhone;
        ShowCompanyCity = template.ShowCompanyCity;
        ShowCompanyProvinceState = template.ShowCompanyProvinceState;
        ShowCompanyCountry = template.ShowCompanyCountry;
        ShowTaxBreakdown = template.ShowTaxBreakdown;
        ShowItemDescriptions = template.ShowItemDescriptions;
        ShowPaymentInstructions = template.ShowPaymentInstructions;
        ShowDueDateProminent = template.ShowDueDateProminent;
        HasLogo = !string.IsNullOrEmpty(template.LogoBase64);
    }

    private void ResetForm()
    {
        _editingTemplateId = string.Empty;
        TemplateName = string.Empty;
        SelectedBaseTemplate = InvoiceTemplateType.Professional;
        IsDefault = false;
        PropertiesTabIndex = 0;

        var defaults = InvoiceTemplateFactory.CreateProfessionalTemplate();
        PrimaryColor = defaults.PrimaryColor;
        SecondaryColor = defaults.SecondaryColor;
        AccentColor = defaults.AccentColor;
        HeaderColor = !string.IsNullOrEmpty(defaults.HeaderColor) ? defaults.HeaderColor : defaults.PrimaryColor;
        TextColor = defaults.TextColor;
        BackgroundColor = defaults.BackgroundColor;
        SelectedFontFamily = defaults.FontFamily;
        LogoBase64 = null;
        LogoPath = string.Empty;
        LogoWidth = 150;
        LockAspectRatio = true;
        HeaderText = defaults.HeaderText;
        FooterText = defaults.FooterText;
        PaymentInstructions = string.Empty;
        DefaultNotes = string.Empty;
        ShowLogo = true;
        ShowCompanyAddress = true;
        ShowCompanyPhone = true;
        ShowCompanyCity = true;
        ShowCompanyProvinceState = true;
        ShowCompanyCountry = true;
        ShowTaxBreakdown = true;
        ShowItemDescriptions = true;
        ShowPaymentInstructions = true;
        ShowDueDateProminent = true;
        HasLogo = false;
        ValidationMessage = string.Empty;
        HasValidationMessage = false;
    }

    #endregion
}

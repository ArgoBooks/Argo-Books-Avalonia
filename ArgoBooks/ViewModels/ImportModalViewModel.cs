using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Import modal.
/// </summary>
public partial class ImportModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string? _selectedFormat;

    /// <summary>
    /// Whether to offer "Backup File". Restoring a backup opens a different company, so anywhere
    /// the point is to bring records into the company already open should leave it out.
    /// </summary>
    [ObservableProperty]
    private bool _showBackupOption = true;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ImportModalViewModel()
    {
    }

    /// <summary>
    /// Opens the modal.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ImportOpened);

        SelectedFormat = null;
        ShowBackupOption = true;
        IsOpen = true;
    }

    /// <summary>
    /// Opens with only the formats that add records to the open company.
    /// </summary>
    public void OpenForCurrentCompany()
    {
        Open();
        ShowBackupOption = false;
    }

    /// <summary>
    /// Closes the modal.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        // SelectFormat closes the modal too, so the format guard is what makes this an abandon.
        if (IsOpen && string.IsNullOrEmpty(SelectedFormat))
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "format-picker");

        IsOpen = false;
    }

    /// <summary>
    /// Selects an import format and proceeds.
    /// </summary>
    [RelayCommand]
    private void SelectFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return;

        SelectedFormat = format;
        Close();

        // Raise event to open file picker
        FormatSelected?.Invoke(this, format);
    }

    #region Events

    public event EventHandler<string>? FormatSelected;

    #endregion
}

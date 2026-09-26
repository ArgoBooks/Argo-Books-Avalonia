using System.Collections.ObjectModel;
using System.ComponentModel;
using ArgoBooks.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents an exportable data item.
/// </summary>
public partial class ExportDataItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _recordCount;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLast;

    /// <summary>
    /// Internal key used for data lookup (may differ from display name).
    /// </summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for the Export As modal.
/// </summary>
public partial class ExportAsModalViewModel : ViewModelBase
{
    private bool _isUpdatingSelectAll;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isExporting;

    public bool IsBackupSelected => SelectedTabIndex == 0;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBackupSelected));
    }

    [ObservableProperty]
    private string _selectedFileFormat = "xlsx";

    [ObservableProperty]
    private DateTimeOffset? _startDate = DateTimeOffset.Now.AddMonths(-1);

    [ObservableProperty]
    private DateTimeOffset? _endDate = DateTimeOffset.Now;

    [ObservableProperty]
    private bool _selectAllData;

    /// <summary>
    /// Available file formats for spreadsheet export.
    /// </summary>
    public ObservableCollection<string> FileFormats { get; } = ["xlsx"];

    /// <summary>
    /// Data items available for export.
    /// </summary>
    public ObservableCollection<ExportDataItem> DataItems { get; } = [];

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ExportAsModalViewModel()
    {
        InitializeDataItems();
    }

    /// <summary>
    /// Initializes the data items list with all available export types.
    /// </summary>
    private void InitializeDataItems()
    {
        // Unsubscribe from existing items before clearing
        foreach (var item in DataItems)
        {
            item.PropertyChanged -= OnDataItemPropertyChanged;
        }

        DataItems.Clear();

        // Entities
        AddDataItem(new ExportDataItem { Name = "Customers", Key = "Customers", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Suppliers", Key = "Suppliers", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Products", Key = "Products", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Categories", Key = "Categories", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Locations", Key = "Locations", RecordCount = 0, IsSelected = true });

        // Transactions
        AddDataItem(new ExportDataItem { Name = "Revenue", Key = "Revenue", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Expenses", Key = "Expenses", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Invoices", Key = "Invoices", RecordCount = 0, IsSelected = true });

        // Straight after Invoices, because the importer assigns lines to whichever invoice it has
        // already seen and sheets are processed in the order they are written.
        AddDataItem(new ExportDataItem { Name = "Invoice Line Items", Key = "Invoice Line Items", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Payments", Key = "Payments", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Recurring Invoices", Key = "Recurring Invoices", RecordCount = 0, IsSelected = true });

        // Inventory
        AddDataItem(new ExportDataItem { Name = "Inventory", Key = "Inventory", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Stock Adjustments", Key = "Stock Adjustments", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Purchase Orders", Key = "Purchase Orders", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Purchase Order Line Items", Key = "Purchase Order Line Items", RecordCount = 0, IsSelected = true });

        // Rentals
        AddDataItem(new ExportDataItem { Name = "Rental Inventory", Key = "Rental Inventory", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Rental Records", Key = "Rental Records", RecordCount = 0, IsSelected = true });

        // Payroll. Employees import like any other entity list; Pay Runs is a register to read,
        // and is not imported, because an approved run's figures are frozen so the stub the
        // employee was handed keeps matching.
        AddDataItem(new ExportDataItem { Name = "Employees", Key = "Employees", RecordCount = 0, IsSelected = true });
        AddDataItem(new ExportDataItem { Name = "Pay Runs", Key = "Pay Runs", RecordCount = 0, IsSelected = true, IsLast = true });

        // Initialize SelectAllData based on initial state
        UpdateSelectAllState();
    }

    private void AddDataItem(ExportDataItem item)
    {
        item.PropertyChanged += OnDataItemPropertyChanged;
        DataItems.Add(item);
    }

    private void OnDataItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportDataItem.IsSelected))
        {
            UpdateSelectAllState();
        }
    }

    /// <summary>
    /// Refreshes the record counts and start date from the current company data.
    /// </summary>
    public void RefreshRecordCounts(CompanyData? companyData)
    {
        if (companyData == null)
        {
            foreach (var item in DataItems)
            {
                item.RecordCount = 0;
            }
            return;
        }

        StartDate = new DateTimeOffset(companyData.GetEarliestDate());

        foreach (var item in DataItems)
        {
            item.RecordCount = item.Key switch
            {
                "Customers" => companyData.Customers.Count,
                "Suppliers" => companyData.Suppliers.Count,
                "Products" => companyData.Products.Count,
                "Categories" => companyData.Categories.Count,
                "Locations" => companyData.Locations.Count,
                "Revenue" => companyData.Revenues.Count,
                "Expenses" => companyData.Expenses.Count,
                "Invoices" => companyData.Invoices.Count,
                "Invoice Line Items" => companyData.Invoices.Sum(i => i.LineItems.Count),
                "Payments" => companyData.Payments.Count,
                "Recurring Invoices" => companyData.RecurringInvoices.Count,
                "Inventory" => companyData.Inventory.Count,
                "Stock Adjustments" => companyData.StockAdjustments.Count,
                "Purchase Orders" => companyData.PurchaseOrders.Count,
                "Purchase Order Line Items" => companyData.PurchaseOrders.Sum(p => p.LineItems.Count),
                "Rental Inventory" => companyData.RentalInventory.Count,
                "Rental Records" => companyData.Rentals.Count,
                "Employees" => companyData.Employees.Count,
                "Pay Runs" => companyData.PayRuns.Sum(r => r.Lines.Count),
                _ => 0
            };
        }
    }

    partial void OnSelectAllDataChanged(bool value)
    {
        if (_isUpdatingSelectAll)
            return;

        foreach (var item in DataItems)
        {
            item.IsSelected = value;
        }
    }

    private void UpdateSelectAllState()
    {
        if (_isUpdatingSelectAll)
            return;

        _isUpdatingSelectAll = true;
        try
        {
            SelectAllData = DataItems.Count > 0 && DataItems.All(item => item.IsSelected);
        }
        finally
        {
            _isUpdatingSelectAll = false;
        }
    }

    /// <summary>
    /// Opens the modal.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        SelectedTabIndex = 0;
        EndDate = DateTimeOffset.Now;
        // Request record count refresh from the hosting code
        RefreshRecordCountsRequested?.Invoke(this, EventArgs.Empty);
        IsOpen = true;
    }

    /// <summary>
    /// Event raised when record counts should be refreshed.
    /// </summary>
    public event EventHandler? RefreshRecordCountsRequested;

    /// <summary>
    /// Closes the modal.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Performs the export.
    /// </summary>
    [RelayCommand]
    private void Export()
    {
        if (IsBackupSelected)
        {
            ExportRequested?.Invoke(this, new ExportEventArgs("backup"));
        }
        else
        {
            // Use Key for data lookup (internal identifier)
            var selectedItems = DataItems.Where(x => x.IsSelected).Select(x => x.Key).ToList();
            ExportRequested?.Invoke(this, new ExportEventArgs(SelectedFileFormat, selectedItems, StartDate?.DateTime, EndDate?.DateTime));
        }
        Close();
    }

    #region Events

    public event EventHandler<ExportEventArgs>? ExportRequested;

    #endregion
}

/// <summary>
/// Event arguments for export requests.
/// </summary>
public class ExportEventArgs(string format, List<string> selectedDataItems, DateTime? startDate, DateTime? endDate)
    : EventArgs
{
    public string Format { get; } = format;
    public List<string> SelectedDataItems { get; } = selectedDataItems;
    public DateTime? StartDate { get; } = startDate;
    public DateTime? EndDate { get; } = endDate;

    public ExportEventArgs(string format) : this(format, [], null, null)
    {
    }
}

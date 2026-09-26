using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Employees page, the list of people on the payroll.
///
/// Archived employees are hidden by default but never deleted, because a T4 must still be
/// produceable for someone who left part way through the year.
/// </summary>
public partial class EmployeesPageViewModel : SortablePageViewModelBase
{
    private readonly List<Employee> _all = [];

    public EmployeesTableColumnWidths ColumnWidths => App.EmployeesColumnWidths;

    public ObservableCollection<EmployeeDisplayItem> Employees { get; } = [];

    #region Statistics

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private string _annualPayroll = "$0";

    [ObservableProperty]
    private int _archivedCount;

    #endregion

    #region Filters

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// Mirrors of the filter modal's state. The page keeps its own copy so cancelling the
    /// modal cannot change what the list shows.
    /// </summary>
    [ObservableProperty]
    /// <summary>
    /// Everyone by default, archived included. Archiving is a status change, not a deletion,
    /// so hiding the row the moment it is archived reads as though the record went away.
    /// </summary>
    private string _filterStatus = "All";

    [ObservableProperty]
    private string _filterProvince = "All";

    [ObservableProperty]
    private string _filterPayType = "All";

    [ObservableProperty]
    private string _filterFrequency = "All";

    partial void OnSearchQueryChanged(string value) => Filter();

    #endregion

    public EmployeesPageViewModel()
    {
        Load();

        // The modal lives on the shell rather than on this page, so a save has to tell the
        // list to refresh. Without this a newly added employee only appears after navigating
        // away and back.
        if (App.PayrollModalsViewModel is { } modals)
        {
            modals.EmployeeSaved += OnEmployeeSaved;
            modals.FiltersApplied += OnFiltersApplied;
            modals.FiltersCleared += OnFiltersCleared;
        }
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (App.PayrollModalsViewModel is { } modals)
        {
            modals.EmployeeSaved -= OnEmployeeSaved;
            modals.FiltersApplied -= OnFiltersApplied;
            modals.FiltersCleared -= OnFiltersCleared;
        }
    }

    private void OnEmployeeSaved(object? sender, EventArgs e) => Load();

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        if (App.PayrollModalsViewModel is { } modals)
        {
            FilterStatus = modals.FilterStatus;
            FilterProvince = modals.FilterProvince;
            FilterPayType = modals.FilterPayType;
            FilterFrequency = modals.FilterFrequency;
            ActiveFilterCount = modals.ActiveFilterCount;
        }

        CurrentPage = 1;
        Filter();
    }

    protected override void ClearTableFilters() => App.PayrollModalsViewModel?.ClearFiltersCommand.Execute(null);

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        FilterStatus = "All";
        FilterProvince = "All";
        FilterPayType = "All";
        FilterFrequency = "All";
        CurrentPage = 1;
        Filter();
    }

    [RelayCommand]
    private void OpenFilterModal() => App.PayrollModalsViewModel?.OpenFilterModal();

    #region Column visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Employees", new Dictionary<string, bool>
    {
        ["Employee"] = true,
        ["Province"] = true,
        ["PayType"] = true,
        ["PayRate"] = true,
        ["Frequency"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showEmployeeColumn = ColumnDefaults.Load("Employee");

    [ObservableProperty]
    private bool _showProvinceColumn = ColumnDefaults.Load("Province");

    [ObservableProperty]
    private bool _showPayTypeColumn = ColumnDefaults.Load("PayType");

    [ObservableProperty]
    private bool _showPayRateColumn = ColumnDefaults.Load("PayRate");

    [ObservableProperty]
    private bool _showFrequencyColumn = ColumnDefaults.Load("Frequency");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Commands

    [RelayCommand]
    private void AddEmployee() => App.PayrollModalsViewModel?.OpenAddEmployeeModal();

    [RelayCommand]
    private void EditEmployee(EmployeeDisplayItem? item)
    {
        if (item == null)
        {
            return;
        }

        Employee? employee = _all.FirstOrDefault(e => e.Id == item.Id);
        if (employee != null)
        {
            App.PayrollModalsViewModel?.OpenEditEmployeeModal(employee);
        }
    }

    /// <summary>
    /// Opens the Record of Employment form: the worksheet to key into ROE Web, or the payroll
    /// extract XML to upload instead of keying anything.
    ///
    /// Offered per employee rather than at year end because an ROE is due five calendar days
    /// after the pay period in which someone stops being paid, which has nothing to do with
    /// December.
    /// </summary>
    [RelayCommand]
    private async Task RecordOfEmploymentAsync(EmployeeDisplayItem? item)
    {
        if (item == null || App.CompanyManager?.CompanyData is not { } data)
        {
            return;
        }

        try
        {
            RoeWorksheet sheet = new RoeService().Build(data, item.Id);
            App.PayrollModalsViewModel?.Roe.Show(sheet);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.Roe");
        }
    }

    /// <summary>
    /// Archives rather than deletes. Pay history references the employee, and a T4 has to
    /// remain produceable, so removing the record outright would break both.
    /// </summary>
    [RelayCommand]
    private void ArchiveEmployee(EmployeeDisplayItem? item)
    {
        if (item == null)
        {
            return;
        }

        Employee? employee = _all.FirstOrDefault(e => e.Id == item.Id);
        if (employee == null)
        {
            return;
        }

        bool wasArchived = employee.IsArchived;
        DateTime previousUpdate = employee.UpdatedAt;

        Apply(!wasArchived, DateTime.UtcNow);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            wasArchived ? $"Restore employee '{employee.Name}'" : $"Archive employee '{employee.Name}'",
            () => Apply(wasArchived, previousUpdate),
            () => Apply(!wasArchived, DateTime.UtcNow)));

        void Apply(bool archived, DateTime updatedAt)
        {
            employee.IsArchived = archived;
            employee.UpdatedAt = updatedAt;
            App.CompanyManager?.MarkAsChanged();
            Load();
        }
    }

    #endregion

    /// <summary>
    /// Re-applies the filter when the sort column or page changes. The list is small enough
    /// that rebuilding it is cheaper than tracking incremental changes.
    /// </summary>
    protected override void OnSortOrPageChanged() => Filter();

    /// <summary>Reloads from company data. Called on navigation and after any edit.</summary>
    public void Load()
    {
        _all.Clear();

        List<Employee>? employees = App.CompanyManager?.CompanyData?.Employees;
        if (employees != null)
        {
            _all.AddRange(employees);
        }

        UpdateStatistics();
        Filter();
    }

    private void UpdateStatistics()
    {
        List<Employee> active = _all.Where(e => !e.IsArchived).ToList();

        ActiveCount = active.Count;
        ArchivedCount = _all.Count - active.Count;

        // Salaried people have a known annual cost. Hourly people do not until their hours are
        // entered on a pay run, so they are left out rather than guessed at.
        decimal annual = active
            .Where(e => e.PayType == PayType.Salary)
            .Sum(e => e.PayRate);

        AnnualPayroll = $"${annual:N0}";
    }

    private void Filter()
    {
        Employees.Clear();

        IEnumerable<Employee> filtered = FilterStatus switch
        {
            "Archived" => _all.Where(e => e.IsArchived),
            "All" => _all,
            _ => _all.Where(e => !e.IsArchived),
        };

        if (FilterProvince != "All")
        {
            filtered = filtered.Where(e => e.Province == FilterProvince);
        }

        if (FilterPayType != "All")
        {
            PayType wanted = FilterPayType == "Hourly" ? PayType.Hourly : PayType.Salary;
            filtered = filtered.Where(e => e.PayType == wanted);
        }

        if (FilterFrequency != "All")
        {
            filtered = filtered.Where(e => e.PayFrequency.DisplayName() == FilterFrequency);
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            filtered = filtered.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.EmployeeNumber.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Name unless a header says otherwise.
        IEnumerable<Employee> sorted = SortDirection == Controls.SortDirection.None
            ? filtered.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            : filtered.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<Employee, object?>>
                {
                    ["Employee"] = e => e.Name,
                    ["Province"] = e => e.Province,
                    ["PayType"] = e => e.PayType,
                    ["PayRate"] = e => e.PayRate,
                    ["Frequency"] = e => e.PayFrequency,
                    ["Status"] = e => e.IsArchived,
                });

        List<Employee> ordered = sorted.ToList();

        foreach (Employee e in Paginate(ordered, "employee", "employees"))
        {
            Employees.Add(EmployeeDisplayItem.From(e));
        }
    }
}

/// <summary>One row of the employees table, already formatted for display.</summary>
public partial class EmployeeDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _employeeNumber = string.Empty;

    /// <summary>Chosen beside ArchiveTooltip so the arrow and the words cannot disagree.</summary>
    [ObservableProperty]
    private string _archiveIcon = Icons.Archive;

    [ObservableProperty]
    private string _province = string.Empty;

    [ObservableProperty]
    private string _payType = string.Empty;

    [ObservableProperty]
    private string _payRate = string.Empty;

    [ObservableProperty]
    private string _frequency = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isArchived;

    /// <summary>
    /// The action the button will actually perform, not both possibilities at once. A tooltip
    /// that says "archive or restore" makes the reader work out which one applies.
    /// </summary>
    [ObservableProperty]
    private string _archiveTooltip = "Archive";

    public static EmployeeDisplayItem From(Employee e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        EmployeeNumber = e.EmployeeNumber,
        Province = e.Province,
        PayType = e.PayType == Core.Models.Payroll.PayType.Salary ? "Salary" : "Hourly",
        PayRate = e.PayType == Core.Models.Payroll.PayType.Salary
            ? $"{CurrencyService.Format(e.PayRate)} / year"
            : $"{CurrencyService.Format(e.PayRate)} / hour",
        Frequency = e.PayFrequency.DisplayName(),
        Status = e.IsArchived ? "Archived" : "Active",
        IsArchived = e.IsArchived,
        ArchiveTooltip = e.IsArchived ? "Restore" : "Archive",
        ArchiveIcon = e.IsArchived ? Icons.Unarchive : Icons.Archive,
    };
}

using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public abstract partial class WidgetViewModelBase : ObservableObject
{
    /// <summary>The customer's name for a widget row, or Unknown when it is missing or deleted.</summary>
    protected static string GetCustomerName(Core.Data.CompanyData data, string? customerId) =>
        (string.IsNullOrEmpty(customerId) ? null : data.GetCustomer(customerId)?.Name) ?? "Unknown".Translate();

    public abstract WidgetType WidgetType { get; }

    /// <summary>
    /// Indicates whether this widget has configuration options to display.
    /// </summary>
    public virtual bool HasConfig => false;

    /// <summary>
    /// Whether the widget host card wrapper should be visible.
    /// Widgets can set this to false to hide themselves (e.g., setup checklist when dismissed).
    /// </summary>
    [ObservableProperty] private bool _isWidgetVisible = true;

    protected CompanyManager? CompanyManager { get; private set; }

    public void SetCompanyManager(CompanyManager? companyManager) => CompanyManager = companyManager;

    public virtual void Initialize(Dictionary<string, string> config) { }
    public virtual void LoadData() { }
    public virtual void Cleanup() { }
    public virtual Dictionary<string, string> GetConfig() => new();
    public virtual void ApplyConfig(Dictionary<string, string> config) { }
}

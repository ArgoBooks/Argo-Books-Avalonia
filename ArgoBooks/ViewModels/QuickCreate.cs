using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Turns a name typed into a picker into a real record, so a form creates what the user typed
/// instead of refusing to save. An existing record of the same name is reused whatever its
/// capitalisation. Each create is its own undo step, recorded before the form's own.
/// </summary>
public static class QuickCreate
{
    public static Category? EnsureCategory(CompanyData? data, CategoryType type, string? typedName)
    {
        var name = typedName?.Trim();
        if (data == null || string.IsNullOrEmpty(name))
            return null;

        var category = ImportLookup.FindOrCreateCategory(data, type, name, out var created);
        if (created)
            Record(data, $"Add category '{category.Name}'", FeatureName.CategoryCreated,
                () => data.Categories.RemoveRecord(category), () => data.Categories.RestoreRecord(category));
        return category;
    }

    public static Customer? EnsureCustomer(CompanyData? data, string? typedName)
    {
        var name = typedName?.Trim();
        if (data == null || string.IsNullOrEmpty(name))
            return null;

        var customer = ImportLookup.FindOrCreateCustomer(data, name, out var created);
        if (created)
            Record(data, $"Add customer '{customer.Name}'", FeatureName.CustomerCreated,
                () => data.Customers.RemoveRecord(customer), () => data.Customers.RestoreRecord(customer));
        return customer;
    }

    public static Supplier? EnsureSupplier(CompanyData? data, string? typedName)
    {
        var name = typedName?.Trim();
        if (data == null || string.IsNullOrEmpty(name))
            return null;

        var supplier = ImportLookup.FindOrCreateSupplier(data, name, out var created);
        if (created)
            Record(data, $"Add supplier '{supplier.Name}'", FeatureName.SupplierCreated,
                () => data.Suppliers.RemoveRecord(supplier), () => data.Suppliers.RestoreRecord(supplier));
        return supplier;
    }

    private static void Record(CompanyData data, string label, FeatureName feature, Action undo, Action redo)
    {
        _ = App.TelemetryManager?.TrackFeatureAsync(feature);
        data.MarkAsModified();
        App.UndoRedoManager.RecordAction(new DelegateAction(label,
            () => { undo(); data.MarkAsModified(); },
            () => { redo(); data.MarkAsModified(); }));
    }
}

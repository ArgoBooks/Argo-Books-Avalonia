using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The lists the forms' dropdowns offer, sorted by name. An "All" entry, where a list has one,
/// carries a null Id.
/// </summary>
public static class OptionLoader
{
    public static IEnumerable<Customer> Customers(CompanyData? data, bool activeOnly = false) =>
        data == null
            ? Enumerable.Empty<Customer>()
            : data.Customers.Where(c => !activeOnly || c.Status == EntityStatus.Active).OrderBy(c => c.Name);

    public static IEnumerable<Supplier> Suppliers(CompanyData? data) =>
        data == null ? Enumerable.Empty<Supplier>() : data.Suppliers.OrderBy(s => s.Name);

    public static IEnumerable<Category> Categories(CompanyData? data, CategoryType type) =>
        data == null ? Enumerable.Empty<Category>() : data.Categories.Where(c => c.Type == type).OrderBy(c => c.Name);

    public static IEnumerable<Accountant> Accountants(CompanyData? data) =>
        data == null ? Enumerable.Empty<Accountant>() : data.Accountants.OrderBy(a => a.Name);

    public static IEnumerable<string> Countries(IEnumerable<Address> addresses) =>
        addresses
            .Select(a => a.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c);

    public static IEnumerable<T> AsOptions<T>(this IEnumerable<Customer> source) where T : NamedOption, new() =>
        source.Select(c => new T { Id = c.Id, Name = c.Name });

    public static IEnumerable<T> AsOptions<T>(this IEnumerable<Supplier> source) where T : NamedOption, new() =>
        source.Select(s => new T { Id = s.Id, Name = s.Name });

    public static IEnumerable<T> AsOptions<T>(this IEnumerable<Category> source) where T : NamedOption, new() =>
        source.Select(c => new T { Id = c.Id, Name = c.Name });

    public static IEnumerable<T> AsOptions<T>(this IEnumerable<Accountant> source) where T : NamedOption, new() =>
        source.Select(a => new T { Id = a.Id, Name = a.Name });

    /// <summary>Replaces the target's contents, with the "All" entry first when one is given.</summary>
    public static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items, T? allOption = null) where T : class
    {
        target.Clear();
        if (allOption != null)
            target.Add(allOption);
        foreach (var item in items)
            target.Add(item);
    }
}

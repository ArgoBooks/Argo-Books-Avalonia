using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Entities;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The categories a new company starts with, chosen from the industry picked in the create
/// wizard. A company with none leaves every category box empty, which is where new users were
/// stopping; a short list gives the box something to offer, and any of it can be renamed.
/// </summary>
public static class StarterCategories
{
    private static readonly (string Name, string Icon)[] CommonExpenses =
    [
        ("Advertising", "⭐"),
        ("Bank & Card Fees", "💵"),
        ("Insurance", "🏷️"),
        ("Office Supplies", "📁"),
        ("Phone & Internet", "📱"),
        ("Rent", "🏠"),
        ("Software & Subscriptions", "💻"),
        ("Travel & Meals", "🏷️")
    ];

    public static void AddTo(CompanyData data, string? industry)
    {
        var (revenue, expenses) = ForIndustry(industry);

        foreach (var (name, icon) in revenue)
            Add(data, name, icon, CategoryType.Revenue);

        foreach (var (name, icon) in CommonExpenses.Concat(expenses))
            Add(data, name, icon, CategoryType.Expense);
    }

    private static ((string Name, string Icon)[] Revenue, (string Name, string Icon)[] Expenses) ForIndustry(string? industry) =>
        industry switch
        {
            IndustryNames.Retail => (
                [("Sales", "🛒")],
                [("Inventory Purchases", "📦"), ("Shipping", "🚚")]),
            IndustryNames.Services => (
                [("Services", "💵")],
                [("Vehicle & Fuel", "🚚"), ("Contractors", "🔧")]),
            IndustryNames.Manufacturing => (
                [("Product Sales", "🛒")],
                [("Raw Materials", "📦"), ("Equipment", "⚙️"), ("Shipping", "🚚")]),
            IndustryNames.Technology => (
                [("Services", "💵"), ("Software Sales", "💻")],
                [("Hosting & Cloud", "💻"), ("Equipment", "⚙️"), ("Contractors", "🔧")]),
            IndustryNames.Healthcare => (
                [("Patient Services", "❤️")],
                [("Medical Supplies", "📦"), ("Equipment", "⚙️"), ("Licenses & Dues", "🏷️")]),
            IndustryNames.FoodAndBeverage => (
                [("Food & Drink Sales", "🛒")],
                [("Ingredients", "🛒"), ("Kitchen Supplies", "📦"), ("Equipment", "⚙️")]),
            IndustryNames.Construction => (
                [("Contract Work", "🔧")],
                [("Materials", "📦"), ("Equipment Rental", "⚙️"), ("Subcontractors", "🔧"), ("Vehicle & Fuel", "🚚")]),
            IndustryNames.Transportation => (
                [("Delivery & Freight", "🚚")],
                [("Fuel", "🚚"), ("Vehicle Maintenance", "🔧"), ("Tolls & Parking", "🏷️")]),
            IndustryNames.RealEstate => (
                [("Rent Income", "🏠"), ("Commissions", "💵")],
                [("Property Maintenance", "🔧"), ("Property Tax", "🏷️"), ("Utilities", "💡")]),
            _ => (
                [("Sales", "🛒"), ("Services", "💵")],
                [])
        };

    private static void Add(CompanyData data, string name, string icon, CategoryType type)
    {
        data.IdCounters.Category++;
        var typePrefix = type == CategoryType.Expense ? "PUR" : "SAL";
        data.Categories.Add(new Category
        {
            Id = $"CAT-{typePrefix}-{data.IdCounters.Category:D3}",
            Name = name,
            Type = type,
            Icon = icon
        });
    }
}

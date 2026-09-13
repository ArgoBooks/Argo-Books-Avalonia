using ArgoBooks.Core.Models.Entities;
using Avalonia.Media.Imaging;

namespace ArgoBooks.Helpers;

/// <summary>
/// Loads a customer's or supplier's avatar bitmap from the company temp directory,
/// returning null if there's no avatar or the file fails to decode.
/// </summary>
public static class AvatarBitmapLoader
{
    public static Bitmap? LoadCustomer(Customer? customer)
    {
        if (customer == null) return null;
        return LoadFromPath(App.CompanyManager?.GetCustomerAvatarPath(customer));
    }

    public static Bitmap? LoadSupplier(Supplier? supplier)
    {
        if (supplier == null) return null;
        return LoadFromPath(App.CompanyManager?.GetSupplierAvatarPath(supplier));
    }

    private static Bitmap? LoadFromPath(string? path)
    {
        if (path == null) return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}

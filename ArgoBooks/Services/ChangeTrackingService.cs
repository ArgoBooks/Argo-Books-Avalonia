using ArgoBooks.ViewModels;

namespace ArgoBooks.Services;

/// <summary>
/// Service that aggregates changes from multiple sources and provides
/// a unified view of all unsaved changes in the application.
/// </summary>
public class ChangeTrackingService
{
    /// <summary>
    /// Gets all change categories with their changes.
    /// </summary>
    public IEnumerable<ChangeCategory> GetAllChangeCategories()
    {
        return [];
    }
}

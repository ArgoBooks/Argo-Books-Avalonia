using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ChangeTrackingService class.
/// </summary>
public class ChangeTrackingServiceTests
{
    private readonly ChangeTrackingService _service = new();

    [Fact]
    public void GetAllChangeCategories_WhenEmpty_ReturnsEmpty()
    {
        var categories = _service.GetAllChangeCategories();

        Assert.Empty(categories);
    }
}

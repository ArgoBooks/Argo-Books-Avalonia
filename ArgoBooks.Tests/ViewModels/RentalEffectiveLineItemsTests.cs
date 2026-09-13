using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Rentals created via the "Rent Out" quick action populate only the legacy top-level fields
/// (RentalItemId/Quantity/RateType/RateAmount/SecurityDeposit) and leave LineItems empty. Every
/// return-cost / inventory-restore / display path funnels through GetEffectiveLineItems, so that
/// helper must fall back to the top-level fields when LineItems is empty (mirroring
/// RentalAvailabilityModalViewModel.QuantitiesForItem).
/// </summary>
public class RentalEffectiveLineItemsTests
{
    [Fact]
    public void GetEffectiveLineItems_RecordWithoutLineItems_FallsBackToTopLevelFields()
    {
        var record = new RentalRecord
        {
            Id = "RNT-001",
            RentalItemId = "RNT-ITM-001",
            Quantity = 3,
            RateType = RateType.Daily,
            RateAmount = 12.5m,
            SecurityDeposit = 50m,
            // LineItems intentionally left empty (the "Rent Out" creation path)
        };

        var items = RentalRecordsModalsViewModel.GetEffectiveLineItems(record);

        Assert.Single(items);
        Assert.Equal("RNT-ITM-001", items[0].RentalItemId);
        Assert.Equal(3, items[0].Quantity);
        Assert.Equal(RateType.Daily, items[0].RateType);
        Assert.Equal(12.5m, items[0].RateAmount);
        Assert.Equal(50m, items[0].SecurityDeposit);
    }

    [Fact]
    public void GetEffectiveLineItems_RecordWithLineItems_ReturnsThemVerbatim()
    {
        var record = new RentalRecord
        {
            Id = "RNT-002",
            // Legacy fields present but should be ignored in favour of LineItems.
            RentalItemId = "RNT-ITM-LEGACY",
            Quantity = 1,
            LineItems =
            [
                new RentalLineItem { RentalItemId = "RNT-ITM-010", Quantity = 2 },
                new RentalLineItem { RentalItemId = "RNT-ITM-011", Quantity = 5 },
            ]
        };

        var items = RentalRecordsModalsViewModel.GetEffectiveLineItems(record);

        Assert.Equal(2, items.Count);
        Assert.Equal("RNT-ITM-010", items[0].RentalItemId);
        Assert.Equal("RNT-ITM-011", items[1].RentalItemId);
        Assert.DoesNotContain(items, li => li.RentalItemId == "RNT-ITM-LEGACY");
    }
}

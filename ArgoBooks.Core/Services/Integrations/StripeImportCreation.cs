using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Integrations;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Records everything one Stripe sync created (revenues, fee/refund expenses, auto-created
/// customers/products/categories, returns, remembered payouts) plus the cursor/time/counter
/// state before and after, mirroring the bank-statement import. Import only appends to the
/// collections, so the created items are captured as the tail of each one.
/// </summary>
public class StripeImportCreation : IntegrationImportCreation
{
    public List<StripePayoutRecord> Payouts { get; } = [];

    public string? PreviousCursor { get; set; }
    public string? NewCursor { get; set; }

    /// <summary>True when the sync actually created or remembered anything (so an undo is worth recording).</summary>
    public override bool AnyCreated => base.AnyCreated || Payouts.Count > 0;

    protected override void UndoIntegrationState(CompanyData data)
    {
        var stripe = data.Settings.Integrations.Stripe;
        foreach (var po in Payouts) stripe.ImportedPayouts.Remove(po);
        stripe.LastSyncCursor = PreviousCursor;
        stripe.LastSyncTime = PreviousSyncTime;
    }

    protected override void RedoIntegrationState(CompanyData data)
    {
        var stripe = data.Settings.Integrations.Stripe;
        foreach (var po in Payouts) if (!stripe.ImportedPayouts.Contains(po)) stripe.ImportedPayouts.Add(po);
        stripe.LastSyncCursor = NewCursor;
        stripe.LastSyncTime = NewSyncTime;
    }
}

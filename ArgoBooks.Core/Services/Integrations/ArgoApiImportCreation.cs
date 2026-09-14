using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Records everything one Argo Books API import created, plus the id-counter
/// state before and after, so the UI can offer a single undo/redo for the whole
/// import. The part shared with Stripe lives in <see cref="IntegrationImportCreation"/>.
///
/// One difference from the Stripe version matters: this import also claimed
/// objects on the server. <see cref="BatchId"/> carries that claim so the caller
/// can release it when the merchant undoes, otherwise the developer's queue would
/// keep reporting data as imported that is no longer in anyone's books.
/// </summary>
public class ArgoApiImportCreation : IntegrationImportCreation
{
    /// <summary>The server-side batch this import claimed, once the claim succeeded.</summary>
    public string? BatchId { get; set; }

    /// <summary>Every API object id this import took, in claim order.</summary>
    public List<string> ClaimedObjectIds { get; } = [];

    /// <summary>API id to the local id it became, sent to the server so developers can trace it.</summary>
    public Dictionary<string, string> LocalRefs { get; } = new(StringComparer.Ordinal);

    protected override void UndoIntegrationState(CompanyData data)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (BatchId != null) api.ImportedBatches.Remove(BatchId);
        api.LastSyncTime = PreviousSyncTime;
    }

    protected override void RedoIntegrationState(CompanyData data)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (BatchId != null && !api.ImportedBatches.Contains(BatchId)) api.ImportedBatches.Add(BatchId);
        api.LastSyncTime = NewSyncTime;
    }
}

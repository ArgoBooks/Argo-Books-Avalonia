using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// An object referenced by something in this preview but imported on an earlier
/// occasion, so no longer pending and therefore absent from the preview itself.
/// <see cref="LocalRef"/> is the id the desktop gave it at that time.
/// </summary>
public record ArgoExternalRef(string Id, string? LocalRef, string? Name, string? Email);

/// <summary>
/// Everything currently waiting in the merchant's Argo Books API queue.
/// Read-only: building a preview never changes the books or the server.
/// </summary>
public record ArgoApiSyncPreview(
    IReadOnlyList<ArgoCustomer> Customers,
    IReadOnlyList<ArgoSupplier> Suppliers,
    IReadOnlyList<ArgoCategory> Categories,
    IReadOnlyList<ArgoProduct> Products,
    IReadOnlyList<ArgoExpense> Expenses,
    IReadOnlyList<ArgoRevenue> Revenue,
    IReadOnlyList<ArgoRefund> Refunds,
    IReadOnlyDictionary<string, ArgoExternalRef> ExternalRefs)
{
    public int TotalObjects =>
        Customers.Count + Suppliers.Count + Categories.Count +
        Products.Count + Expenses.Count + Revenue.Count + Refunds.Count;

    public bool HasActivity => TotalObjects > 0;

    /// <summary>Gross revenue waiting, for the "you are about to import" summary.</summary>
    public decimal TotalRevenue => Revenue.Sum(r => ArgoMoney.ToDecimal(r.Amount, r.Currency));

    /// <summary>Total expenses waiting, likewise.</summary>
    public decimal TotalExpenses => Expenses.Sum(e => ArgoMoney.ToDecimal(e.Amount, e.Currency));

    public static ArgoApiSyncPreview Empty() =>
        new([], [], [], [], [], [], [], new Dictionary<string, ArgoExternalRef>());
}

/// <summary>
/// Orchestrates an Argo Books API sync: pull what developers have pushed, let the
/// merchant look at it, then import the approved objects into the books and tell
/// the server they were taken.
///
/// The claim happens after the local write, not before. Both orders can fail
/// somewhere, and this is the order whose failure is recoverable: a local write
/// with no claim can be undone precisely from memory, whereas a claim with no
/// local write would have told the developer their data landed in books that
/// never received it.
/// </summary>
public class ArgoApiSyncService
{
    private readonly ArgoApiClient _client;

    public ArgoApiSyncService(ArgoApiClient client) => _client = client;

    public async Task<ArgoApiSyncPreview> PreviewAsync(CompanyData data, CancellationToken ct = default)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (!api.Enabled || string.IsNullOrWhiteSpace(api.DesktopKey))
            return ArgoApiSyncPreview.Empty();

        var key = api.DesktopKey!;

        // Sequential rather than concurrent: the server rate-limits per key, and
        // seven parallel paginated drains is the one client most likely to trip it.
        var categories = await _client.ListPendingAsync<ArgoCategory>(key, "categories", ct: ct);
        var customers = await _client.ListPendingAsync<ArgoCustomer>(key, "customers", ct: ct);
        var suppliers = await _client.ListPendingAsync<ArgoSupplier>(key, "suppliers", ct: ct);
        var products = await _client.ListPendingAsync<ArgoProduct>(key, "products", ct: ct);
        var expenses = await _client.ListPendingAsync<ArgoExpense>(key, "expenses", expandLineItems: true, ct: ct);
        var revenue = await _client.ListPendingAsync<ArgoRevenue>(key, "revenue", expandLineItems: true, ct: ct);
        var refunds = await _client.ListPendingAsync<ArgoRefund>(key, "refunds", ct: ct);

        var preview = new ArgoApiSyncPreview(
            customers, suppliers, categories, products, expenses, revenue, refunds,
            new Dictionary<string, ArgoExternalRef>());

        var external = await ResolveExternalRefsAsync(key, preview, ct);

        // Appended, not prepended: Import() already walks categories, customers,
        // suppliers and products before anything that points at them, so adding to the
        // end of each list keeps that ordering intact. They also count toward
        // TotalObjects, so the merchant is told these are coming rather than finding
        // them afterwards.
        return preview with
        {
            ExternalRefs = external.Refs,
            Products   = [.. preview.Products,   .. external.Products],
            Categories = [.. preview.Categories, .. external.Categories],
            Customers  = [.. preview.Customers,  .. external.Customers],
            Suppliers  = [.. preview.Suppliers,  .. external.Suppliers],
        };
    }

    /// <summary>
    /// Look up every referenced object that is not itself in this preview.
    ///
    /// A developer creates a customer once and then points a year of orders at
    /// it. That customer is imported with the first batch and is no longer
    /// pending, so nothing after the first batch would find it, and every later
    /// order would import with no customer attached and no error to show for it.
    ///
    /// Only distinct missing ids are fetched, so this is a handful of requests
    /// per import rather than one per row.
    /// </summary>
    /// <summary>
    /// Everything a preview references but does not itself contain.
    ///
    /// <see cref="Refs"/> is the lookup used to match against records the merchant
    /// already has. The typed lists are the supporting objects that exist server-side,
    /// have never been imported, and match nothing locally: without them a revenue line
    /// would import with an empty product link and the product would never appear at all.
    /// </summary>
    private sealed record ExternalResolution(
        Dictionary<string, ArgoExternalRef> Refs,
        List<ArgoProduct> Products,
        List<ArgoCategory> Categories,
        List<ArgoCustomer> Customers,
        List<ArgoSupplier> Suppliers);

    private async Task<ExternalResolution> ResolveExternalRefsAsync(
        string key, ArgoApiSyncPreview preview, CancellationToken ct)
    {
        var extraProducts = new List<ArgoProduct>();
        var extraCategories = new List<ArgoCategory>();
        var extraCustomers = new List<ArgoCustomer>();
        var extraSuppliers = new List<ArgoSupplier>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in preview.Customers) known.Add(c.Id);
        foreach (var x in preview.Suppliers) known.Add(x.Id);
        foreach (var x in preview.Categories) known.Add(x.Id);
        foreach (var x in preview.Products) known.Add(x.Id);
        foreach (var x in preview.Revenue) known.Add(x.Id);

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        void Want(string? id)
        {
            if (!string.IsNullOrEmpty(id) && !known.Contains(id)) wanted.Add(id);
        }

        foreach (var p in preview.Products) Want(p.Category);
        foreach (var e in preview.Expenses)
        {
            Want(e.Supplier);
            Want(e.Category);
            foreach (var li in e.LineItems ?? []) Want(li.Product);
        }
        foreach (var r in preview.Revenue)
        {
            Want(r.Customer);
            Want(r.Category);
            foreach (var li in r.LineItems ?? []) Want(li.Product);
        }
        foreach (var r in preview.Refunds) Want(r.Revenue);

        var resolved = new Dictionary<string, ArgoExternalRef>(StringComparer.Ordinal);
        foreach (var id in wanted)
        {
            var resource = ArgoApiClient.ResourceForId(id);
            if (resource == null) continue;

            var obj = await _client.GetRawObjectAsync(key, resource, id, ct);
            if (obj == null) continue;

            string? Read(string prop) =>
                obj.Value.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            string? localRef = null;
            if (obj.Value.TryGetProperty("import", out var imp)
                && imp.TryGetProperty("local_ref", out var lr)
                && lr.ValueKind == JsonValueKind.String)
            {
                localRef = lr.GetString();
            }

            resolved[id] = new ArgoExternalRef(id, localRef, Read("name"), Read("email"));

            // An object that has never been imported and carries no local record is one
            // the merchant has no way of getting: it is not pending, so it is absent from
            // the preview, and there is nothing local to match it against. Pull it in so
            // it is created alongside the row that needs it.
            //
            // Only 'pending' qualifies. 'imported' already has a local_ref to resolve
            // against, and 'rejected' was refused on purpose: recreating that as a side
            // effect of accepting something else would undo the refusal silently.
            var status = obj.Value.TryGetProperty("import", out var st)
                         && st.TryGetProperty("status", out var sv)
                         && sv.ValueKind == JsonValueKind.String
                ? sv.GetString()
                : null;

            if (localRef != null || status != "pending")
            {
                continue;
            }

            switch (resource)
            {
                case "products":
                    Add(extraProducts, obj.Value);
                    break;
                case "categories":
                    Add(extraCategories, obj.Value);
                    break;
                case "customers":
                    Add(extraCustomers, obj.Value);
                    break;
                case "suppliers":
                    Add(extraSuppliers, obj.Value);
                    break;
            }
        }

        static void Add<T>(List<T> into, JsonElement raw)
        {
            // A shape this build cannot read is skipped rather than thrown: the reference
            // still resolves to nothing, which is exactly where it stood before.
            try
            {
                var parsed = raw.Deserialize<T>();
                if (parsed != null) into.Add(parsed);
            }
            catch (JsonException)
            {
            }
        }

        return new ExternalResolution(
            resolved, extraProducts, extraCategories, extraCustomers, extraSuppliers);
    }

    /// <summary>
    /// Import the preview and claim it server-side. Returns a record of everything
    /// created so the caller can register one undo/redo for the whole import.
    ///
    /// If the claim fails, the local changes are rolled back and the exception is
    /// rethrown, so the merchant is never left with books the server disagrees with.
    /// </summary>
    public async Task<ArgoApiImportCreation> ImportPreviewAsync(
        CompanyData data, ArgoApiSyncPreview preview,
        IProgress<int>? rateProgress = null, CancellationToken ct = default)
    {
        var api = data.Settings.Integrations.ArgoApi;
        var creation = new ArgoApiImportCreation
        {
            PreviousSyncTime = api.LastSyncTime,
            Pre = ArgoApiImportCreation.CounterSnapshot.From(data.IdCounters)
        };

        if (!preview.HasActivity || string.IsNullOrWhiteSpace(api.DesktopKey))
        {
            creation.Post = creation.Pre;
            return creation;
        }

        // Cache the rates for the dates about to land, or every row on a day the
        // cache does not already hold shows "Pending" in place of its amount and
        // never recovers, because nothing refetches rates for rows already saved.
        await IntegrationRates.EnsureAsync(
            preview.Expenses.Select(e => (ArgoApiImporter.ParseDate(e.OccurredOn), e.Currency))
                .Concat(preview.Revenue.Select(r => (ArgoApiImporter.ParseDate(r.OccurredOn), r.Currency)))
                .Concat(preview.Refunds.Select(r => (ArgoApiImporter.ParseDate(r.OccurredOn), r.Currency))),
            data.Settings.Localization.Currency,
            rateProgress,
            ct: ct);

        try
        {
            new ArgoApiImporter().Import(data, preview, creation);
        }
        catch
        {
            // Nothing is claimed yet, so any rows left behind would be offered again on the next
            // sync and imported a second time.
            creation.Post = ArgoApiImportCreation.CounterSnapshot.From(data.IdCounters);
            creation.Undo(data);
            throw;
        }

        // Taken before the claim is awaited, so a record something else creates meanwhile is not
        // counted as the import's and its id is not handed back by an undo.
        creation.Post = ArgoApiImportCreation.CounterSnapshot.From(data.IdCounters);

        try
        {
            var batch = await _client.CreateImportBatchAsync(
                api.DesktopKey!, creation.ClaimedObjectIds, creation.LocalRefs,
                ClaimKey("claim", creation.ClaimedObjectIds), ct);

            creation.BatchId = batch?.Id;
        }
        catch
        {
            // The claim may well have committed and only the response been lost.
            // Rolling back on that would leave the objects imported on the server
            // with nothing in the books, and the next sync would not even list
            // them, because they are no longer pending. Silent loss. So ask the
            // server what actually happened before throwing the work away.
            if (await TryAdoptCommittedClaimAsync(data, creation, ct))
                return await FinishImportAsync(data, creation);

            // Undo restores the id counters too, so a retry produces the same ids
            // rather than leaving a gap in the sequence.
            creation.Undo(data);
            throw;
        }

        return await FinishImportAsync(data, creation);
    }

    /// <summary>Record the claim and stamp the sync. Shared by the normal path and the recovery one.</summary>
    private static Task<ArgoApiImportCreation> FinishImportAsync(CompanyData data, ArgoApiImportCreation creation)
    {
        var api = data.Settings.Integrations.ArgoApi;

        if (creation.BatchId != null && !api.ImportedBatches.Contains(creation.BatchId))
            api.ImportedBatches.Add(creation.BatchId);

        api.LastSyncTime = DateTime.Now;
        creation.NewSyncTime = api.LastSyncTime;
        data.MarkAsModified();

        return Task.FromResult(creation);
    }

    /// <summary>
    /// An idempotency key that is the same for every retry of one logical claim and
    /// different for a deliberately new one.
    ///
    /// It used to be a fresh Guid per call, which meant the server's replay cache
    /// could never fire: a retry looked like a brand new request, found the objects
    /// no longer pending, and failed with object_not_claimable.
    /// </summary>
    private static string ClaimKey(string purpose, IReadOnlyList<string> objectIds)
    {
        // Sorted, so the same set of objects in a different order is the same claim.
        var ordered = objectIds.OrderBy(id => id, StringComparer.Ordinal);
        var material = purpose + "|" + string.Join(",", ordered);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Did the claim actually land before the response went missing?
    ///
    /// Asked only after the claim threw. If the objects are imported and carry a
    /// batch, the server did the work and only the answer was lost, so the local
    /// rows are correct and keeping them is right. Undoing instead would strand the
    /// developer's data: imported on the server, absent from the books, and invisible
    /// to the next sync because it is no longer pending.
    ///
    /// Any doubt at all reports false, so the caller rolls back as before. A wrong
    /// "yes" would leave books the server disagrees with, which is the worse error.
    /// </summary>
    private async Task<bool> TryAdoptCommittedClaimAsync(
        CompanyData data, ArgoApiImportCreation creation, CancellationToken ct)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (string.IsNullOrWhiteSpace(api.DesktopKey) || creation.ClaimedObjectIds.Count == 0)
            return false;

        try
        {
            var probe = creation.ClaimedObjectIds[0];
            var resource = ArgoApiClient.ResourceForId(probe);
            if (resource == null) return false;

            var obj = await _client.GetRawObjectAsync(api.DesktopKey!, resource, probe, ct);
            if (obj == null || !obj.Value.TryGetProperty("import", out var import))
                return false;

            var status = import.TryGetProperty("status", out var st) ? st.GetString() : null;
            var batch = import.TryGetProperty("batch", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString()
                : null;

            if (status != "imported" || string.IsNullOrEmpty(batch))
                return false;

            creation.BatchId = batch;
            return true;
        }
        catch
        {
            // Still unreachable. Roll back, which is the safe direction.
            return false;
        }
    }

    /// <summary>
    /// Release a batch the merchant has undone, so the objects return to the queue.
    ///
    /// Deliberately swallows failures: the local undo has already happened and must
    /// not be blocked by the network. The batch id stays in the company file, so a
    /// later sync can notice the disagreement and retry.
    /// </summary>
    /// <summary>
    /// Claim the import's objects again after a redo.
    ///
    /// Undo hands them back to the queue, so without this a redo leaves the books
    /// holding rows the server still reports as pending, and the next sync imports
    /// every one of them a second time.
    ///
    /// There is no "unrevert" on the server, so this is a fresh batch under a new
    /// id, and the old id is dropped from everywhere it was recorded.
    /// </summary>
    public async Task<bool> TryReclaimBatchAsync(
        CompanyData data, ArgoApiImportCreation creation, CancellationToken ct = default)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (string.IsNullOrWhiteSpace(api.DesktopKey) || creation.ClaimedObjectIds.Count == 0)
            return false;

        try
        {
            var batch = await _client.CreateImportBatchAsync(
                api.DesktopKey!, creation.ClaimedObjectIds, creation.LocalRefs,
                ClaimKey("reclaim-" + (creation.BatchId ?? "none"), creation.ClaimedObjectIds), ct);

            if (batch?.Id == null) return false;

            if (creation.BatchId != null) api.ImportedBatches.Remove(creation.BatchId);
            creation.BatchId = batch.Id;
            if (!api.ImportedBatches.Contains(batch.Id)) api.ImportedBatches.Add(batch.Id);

            data.MarkAsModified();
            return true;
        }
        catch (ArgoApiException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decline everything in a preview, so the queue empties and the apps that sent it are
    /// told they were refused.
    ///
    /// Declining is the missing third answer. Without it the only options are "take it" and
    /// "not now", so an object nobody wants is re-offered on every sync forever and the
    /// developer who pushed it can never tell refusal from inattention.
    ///
    /// Each object is rejected on its own because that is the shape of the endpoint, and a
    /// failure on one is swallowed: leaving the rest queued because a single id had already
    /// been actioned elsewhere would be a worse outcome than a partial clear.
    /// </summary>
    /// <returns>How many objects the server accepted a rejection for.</returns>
    public async Task<int> RejectPreviewAsync(
        CompanyData data,
        ArgoApiSyncPreview preview,
        CancellationToken ct = default)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (!api.Enabled || string.IsNullOrWhiteSpace(api.DesktopKey))
            return 0;

        var key = api.DesktopKey!;

        var targets = new List<(string Resource, string Id)>();
        foreach (var c in preview.Categories) targets.Add(("categories", c.Id));
        foreach (var c in preview.Customers) targets.Add(("customers", c.Id));
        foreach (var x in preview.Suppliers) targets.Add(("suppliers", x.Id));
        foreach (var x in preview.Products) targets.Add(("products", x.Id));
        foreach (var x in preview.Expenses) targets.Add(("expenses", x.Id));
        foreach (var x in preview.Revenue) targets.Add(("revenue", x.Id));
        foreach (var x in preview.Refunds) targets.Add(("refunds", x.Id));

        var rejected = 0;
        foreach (var (resource, id) in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(id))
                continue;

            try
            {
                await _client.RejectAsync(key, resource, id, ct);
                rejected++;
            }
            catch (ArgoApiException)
            {
                // Already imported or already rejected by another client. Nothing to undo
                // and nothing to report: the object is out of the queue either way.
            }
        }

        return rejected;
    }

    public async Task<bool> TryReleaseBatchAsync(CompanyData data, string batchId, CancellationToken ct = default)
    {
        var api = data.Settings.Integrations.ArgoApi;
        if (string.IsNullOrWhiteSpace(api.DesktopKey))
            return false;

        try
        {
            await _client.RevertImportBatchAsync(api.DesktopKey!, batchId, ct);
            return true;
        }
        catch (ArgoApiException)
        {
            return false;
        }
    }
}

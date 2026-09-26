using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Data;

/// <summary>
/// Takes a record out of, or puts it back into, one of the company's lists by id, for undo and
/// redo. An undo step keeps the objects it moved, but undoing or redoing an import restores the
/// company from a snapshot, which swaps every record for a copy with the same id. Matched by
/// reference, an undo after that left the record in the books and a redo added a second one with
/// the same id.
/// </summary>
public static class RecordLists
{
    /// <summary>Returns true when the record was in the list.</summary>
    public static bool RemoveRecord<T>(this IList<T> list, T record) where T : IRecord
    {
        var removed = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (Same(list[i], record))
            {
                list.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    public static void RestoreRecord<T>(this IList<T> list, T record) where T : IRecord
    {
        if (!list.Any(r => Same(r, record)))
            list.Add(record);
    }

    private static bool Same<T>(T a, T b) where T : IRecord =>
        ReferenceEquals(a, b) || (!string.IsNullOrEmpty(b.Id) && a.Id == b.Id);
}

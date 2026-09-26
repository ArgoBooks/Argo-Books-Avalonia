using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Data;

/// <summary>
/// Keeps each record the same object for as long as it is in the books. Undo steps hold the records
/// they changed and change them again later. Anything that swapped a record for a copy with the same
/// id, such as an import updating it or an import's undo restoring the company from a snapshot, left
/// those steps changing an object no longer in the books, while what they did by id, like renaming a
/// customer's references, reached the live records.
/// </summary>
public static class RecordLists
{
    private static readonly ConditionalWeakTable<object, StrongBox<int>> SameIdLeftWhenRemoved = new();
    private static readonly ConcurrentDictionary<Type, Action<object, object>?> Copiers = new();
    private static readonly ConcurrentDictionary<Type, (string Name, Func<object, object?> Get, Action<object, object?> Set)[]> SavedProperties = new();

    private static readonly MethodInfo RestoreNestedMethod =
        typeof(RecordLists).GetMethod(nameof(RestoreNested), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Takes the record out: the record itself when it is in the list, otherwise the first record
    /// with its id. Returns true when something was removed.
    /// </summary>
    public static bool RemoveRecord<T>(this IList<T> list, T record) where T : class, IRecord
    {
        var index = IndexOf(list, record);
        if (index < 0)
            return false;

        list.RemoveAt(index);
        SameIdLeftWhenRemoved.AddOrUpdate(record, new StrongBox<int>(CountWithId(list, record.Id)));
        return true;
    }

    /// <summary>
    /// Puts a record back unless it is already there. Another record with its id counts as it being
    /// there, unless as many were left when this one was taken out: files from older versions can
    /// hold two records with one id, and undoing the delete of one of them has to bring it back.
    /// </summary>
    public static void RestoreRecord<T>(this IList<T> list, T record) where T : class, IRecord
    {
        if (list.Any(r => ReferenceEquals(r, record)))
            return;

        var leftWhenRemoved = SameIdLeftWhenRemoved.TryGetValue(record, out var left) ? left.Value : 0;
        if (CountWithId(list, record.Id) <= leftWhenRemoved)
            list.Add(record);
    }

    /// <summary>
    /// Adds <paramref name="incoming"/>, or when <paramref name="existing"/> is given, writes its saved
    /// values over that record so it stays the same object. Returns the record now in the list.
    /// </summary>
    public static T AddOrUpdate<T>(this IList<T> list, T? existing, T incoming) where T : class, IRecord
    {
        if (existing == null)
        {
            list.Add(incoming);
            return incoming;
        }

        if (CopyValues(incoming, existing))
            return existing;

        list.Remove(existing);
        list.Add(incoming);
        return incoming;
    }

    /// <summary>
    /// For an import updating a record with only what its row gives: every saved property whose name
    /// (as the company file spells it, any case) isn't in <paramref name="given"/> is taken from
    /// <paramref name="existing"/>. Writing the result over <paramref name="existing"/> with
    /// <see cref="AddOrUpdate{T}"/> then changes only the given fields. Returns
    /// <paramref name="incoming"/>.
    /// </summary>
    public static T FillAbsent<T>(this T incoming, T existing, IReadOnlySet<string> given) where T : class
    {
        foreach (var (name, get, set) in SavedProperties.GetOrAdd(incoming.GetType(), ReadSavedProperties))
        {
            if (!given.Contains(name))
                set(incoming, get(existing));
        }
        return incoming;
    }

    private static (string, Func<object, object?>, Action<object, object?>)[] ReadSavedProperties(Type type)
    {
        var info = JsonSerializerOptions.Default.GetTypeInfo(type);
        return info.Kind != JsonTypeInfoKind.Object
            ? []
            : info.Properties
                .Where(p => p.Get != null && p.Set != null)
                .Select(p => (p.Name, p.Get!, p.Set!))
                .ToArray();
    }

    /// <summary>
    /// Makes the list hold <paramref name="restored"/> in its order, reusing the record already in the
    /// list for each id: its saved values are overwritten from the restored copy, and lists of records
    /// inside it are restored the same way. Records missing from <paramref name="restored"/> are
    /// dropped, and restored ones with no match are added as they are.
    /// </summary>
    public static void RestoreInPlace<T>(this List<T> list, IEnumerable<T> restored) where T : class, IRecord
    {
        var current = new Dictionary<string, Queue<T>>(StringComparer.Ordinal);
        foreach (var record in list)
        {
            var key = record.Id ?? string.Empty;
            if (!current.TryGetValue(key, out var queue))
                current[key] = queue = new Queue<T>();
            queue.Enqueue(record);
        }

        var result = new List<T>();
        foreach (var copy in restored)
        {
            if (current.TryGetValue(copy.Id ?? string.Empty, out var queue)
                && queue.TryPeek(out var live)
                && live.GetType() == copy.GetType()
                && CopyValues(copy, live))
            {
                queue.Dequeue();
                result.Add(live);
            }
            else
            {
                result.Add(copy);
            }
        }

        list.Clear();
        list.AddRange(result);
    }

    private static int IndexOf<T>(IList<T> list, T record) where T : class, IRecord
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], record))
                return i;
        }

        if (string.IsNullOrEmpty(record.Id))
            return -1;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == record.Id)
                return i;
        }

        return -1;
    }

    private static int CountWithId<T>(IList<T> list, string id) where T : IRecord =>
        string.IsNullOrEmpty(id) ? 0 : list.Count(r => r.Id == id);

    /// <summary>
    /// Copies every property the company file saves. False for a type saved by a custom converter,
    /// whose properties can't be read off its contract.
    /// </summary>
    private static bool CopyValues(object from, object to)
    {
        var copier = Copiers.GetOrAdd(from.GetType(), BuildCopier);
        if (copier == null)
            return false;

        copier(from, to);
        return true;
    }

    private static Action<object, object>? BuildCopier(Type type)
    {
        var info = JsonSerializerOptions.Default.GetTypeInfo(type);
        if (info.Kind != JsonTypeInfoKind.Object)
            return null;

        var steps = new List<Action<object, object>>();
        foreach (var property in info.Properties)
        {
            if (property.Get is not { } get || property.Set is not { } set)
                continue;

            if (RecordListElement(property.PropertyType) is { } element)
            {
                var restore = RestoreNestedMethod.MakeGenericMethod(element).CreateDelegate<Action<object, object>>();
                steps.Add((from, to) =>
                {
                    if (get(to) is { } live && get(from) is { } copy)
                        restore(live, copy);
                    else
                        set(to, get(from));
                });
            }
            else
            {
                steps.Add((from, to) => set(to, get(from)));
            }
        }

        return (from, to) =>
        {
            foreach (var step in steps)
                step(from, to);
        };
    }

    private static Type? RecordListElement(Type type)
    {
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>))
            return null;

        var element = type.GetGenericArguments()[0];
        return element.IsClass && typeof(IRecord).IsAssignableFrom(element) ? element : null;
    }

    private static void RestoreNested<T>(object live, object copy) where T : class, IRecord =>
        ((List<T>)live).RestoreInPlace((List<T>)copy);
}

namespace ArgoBooks.Helpers;

/// <summary>
/// A read-only list that compares by its items, so a record holding one still compares by value.
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToArray();

    public int Count => _items?.Length ?? 0;

    public bool Equals(EquatableArray<T> other) => (_items ?? []).SequenceEqual(other._items ?? []);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items ?? [])
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

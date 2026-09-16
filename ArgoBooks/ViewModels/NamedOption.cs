namespace ArgoBooks.ViewModels;

/// <summary>
/// A dropdown option shown by its name. A class rather than a record because selectors match
/// the chosen item by reference, and a value-equal record would match a stale copy.
/// </summary>
public class NamedOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

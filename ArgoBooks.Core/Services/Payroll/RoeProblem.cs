namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Which box on the Record of Employment form a validation problem belongs to. The rest come
/// from the employee's record and the pay runs, so there is nothing on that form to put them
/// under and they stay in the list above it. Mirrors <see cref="T4ProblemField"/>.
/// </summary>
public enum RoeProblemField
{
    /// <summary>Fixed on the employee or in the pay runs, not on the ROE form.</summary>
    None,
    Reason,
    RecallDate,
    ContactName,
    ContactPhone,
}

/// <summary>Something that stops a Record of Employment being written.</summary>
public sealed record RoeProblem(string Message, RoeProblemField Field = RoeProblemField.None)
{
    public override string ToString() => Message;

    /// <summary>A problem with no box to sit under is just its message.</summary>
    public static implicit operator RoeProblem(string message) => new(message);
}

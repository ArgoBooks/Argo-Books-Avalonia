namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Which box on the year end screen a validation problem belongs to, so the screen can put the
/// message under the field instead of listing it in a panel above and leaving the reader to
/// work out which box it meant.
/// </summary>
public enum T4ProblemField
{
    /// <summary>Nothing on the year end screen fixes it: a draft pay run, or a company detail in Settings.</summary>
    None,
    PayrollAccountNumber,
    ContactName,
    ContactPhone,
    ContactEmail,
}

/// <summary>Something that stops a T4 return being filed.</summary>
public sealed record T4Problem(string Message, T4ProblemField Field = T4ProblemField.None)
{
    public override string ToString() => Message;

    /// <summary>A problem with no box to sit under is just its message.</summary>
    public static implicit operator T4Problem(string message) => new(message);
}

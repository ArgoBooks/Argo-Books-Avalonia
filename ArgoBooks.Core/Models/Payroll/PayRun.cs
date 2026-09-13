namespace ArgoBooks.Core.Models.Payroll;

/// <summary>
/// One payroll for one period.
///
/// Once approved, every computed figure on its lines is frozen. Rates change twice a year, so
/// recalculating on demand would make a historical run disagree with the pay stub the employee
/// is holding, and the T4 would not reconcile. This is the difference between a payroll record
/// and a calculator.
/// </summary>
public class PayRun
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The date the employees are paid. This, not the period, decides which CRA edition
    /// applies, because a July edition can carry figures that only apply to the second half
    /// of the year.
    /// </summary>
    [JsonPropertyName("payDate")]
    public DateTime PayDate { get; set; }

    [JsonPropertyName("periodStart")]
    public DateTime PeriodStart { get; set; }

    [JsonPropertyName("periodEnd")]
    public DateTime PeriodEnd { get; set; }

    [JsonPropertyName("status")]
    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;

    /// <summary>
    /// The CRA edition this run was calculated with, so it can always say which rules produced
    /// its numbers.
    /// </summary>
    [JsonPropertyName("rateEditionId")]
    public string RateEditionId { get; set; } = string.Empty;

    /// <summary>
    /// Set on the reversing run created by a void, pointing at the run it cancels. Voiding
    /// writes a reversal rather than deleting, because a pay stub may already be in someone's
    /// hands.
    /// </summary>
    [JsonPropertyName("voidsPayRunId")]
    public string? VoidsPayRunId { get; set; }

    [JsonPropertyName("lines")]
    public List<PayRunLine> Lines { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>What leaves the bank: the sum of every employee's net pay.</summary>
    public decimal TotalNetPay => Lines.Sum(l => l.NetPay);

    /// <summary>
    /// What is owed for the run: everything withheld, plus the employer's share. For a Quebec
    /// employee that is two payments, split by <see cref="CraRemittance"/> and
    /// <see cref="QuebecRemittance"/>.
    /// </summary>
    public decimal TotalRemittance => Lines.Sum(l => l.TotalRemittance);

    [JsonIgnore]
    public decimal CraRemittance => Lines.Sum(l => l.CraRemittance);

    [JsonIgnore]
    public decimal QuebecRemittance => Lines.Sum(l => l.QuebecRemittance);

    /// <summary>What the payroll actually costs: gross plus employer contributions.</summary>
    public decimal TotalCost => Lines.Sum(l => l.TotalCost);

    public decimal TotalGross => Lines.Sum(l => l.GrossPay);
}

public enum PayRunStatus
{
    /// <summary>Still editable. Nothing has been recorded in the books.</summary>
    Draft,

    /// <summary>Locked. Stubs issued, expenses recorded, figures frozen.</summary>
    Approved,

    /// <summary>Cancelled by a reversing run. Kept for the audit trail.</summary>
    Void,
}

/// <summary>
/// One employee's pay for one run. Everything below the inputs is stored rather than derived,
/// so an approved run never changes.
/// </summary>
public class PayRunLine
{
    [JsonPropertyName("employeeId")]
    public string EmployeeId { get; set; } = string.Empty;

    /// <summary>Copied at approval so a stub still reads correctly if the employee is renamed.</summary>
    [JsonPropertyName("employeeName")]
    public string EmployeeName { get; set; } = string.Empty;

    [JsonPropertyName("province")]
    public string Province { get; set; } = string.Empty;

    [JsonPropertyName("payPeriodsPerYear")]
    public int PayPeriodsPerYear { get; set; }

    #region Inputs

    [JsonPropertyName("hoursWorked")]
    public decimal HoursWorked { get; set; }

    [JsonPropertyName("basePay")]
    public decimal BasePay { get; set; }

    [JsonPropertyName("bonus")]
    public decimal Bonus { get; set; }

    [JsonPropertyName("vacationPay")]
    public decimal VacationPay { get; set; }

    #endregion

    #region Stored results

    [JsonPropertyName("grossPay")]
    public decimal GrossPay { get; set; }

    [JsonPropertyName("cppEmployee")]
    public decimal CppEmployee { get; set; }

    [JsonPropertyName("cppEmployer")]
    public decimal CppEmployer { get; set; }

    [JsonPropertyName("cpp2Employee")]
    public decimal Cpp2Employee { get; set; }

    [JsonPropertyName("cpp2Employer")]
    public decimal Cpp2Employer { get; set; }

    [JsonPropertyName("eiEmployee")]
    public decimal EiEmployee { get; set; }

    [JsonPropertyName("eiEmployer")]
    public decimal EiEmployer { get; set; }

    /// <summary>Quebec parental insurance plan. Zero for every employee outside Quebec.</summary>
    [JsonPropertyName("qpipEmployee")]
    public decimal QpipEmployee { get; set; }

    [JsonPropertyName("qpipEmployer")]
    public decimal QpipEmployer { get; set; }

    [JsonPropertyName("federalTax")]
    public decimal FederalTax { get; set; }

    [JsonPropertyName("provincialTax")]
    public decimal ProvincialTax { get; set; }

    [JsonPropertyName("netPay")]
    public decimal NetPay { get; set; }

    #endregion

    /// <summary>Set when approval records the wage expense, so a void can reverse it.</summary>
    [JsonPropertyName("expenseId")]
    public string? ExpenseId { get; set; }

    public decimal TotalRemittance =>
        CppEmployee + CppEmployer + Cpp2Employee + Cpp2Employer
        + EiEmployee + EiEmployer + QpipEmployee + QpipEmployer + FederalTax + ProvincialTax;

    /// <summary>
    /// The part of <see cref="TotalRemittance"/> paid to Revenu Quebec rather than CRA: Quebec
    /// income tax and QPP from a Quebec line, and QPIP from any line. The same split the Payroll
    /// Remittance report makes, read off the line's own province so it survives a transfer.
    /// </summary>
    [JsonIgnore]
    public decimal QuebecRemittance =>
        (string.Equals(Province, "QC", StringComparison.OrdinalIgnoreCase)
            ? ProvincialTax + CppEmployee + CppEmployer + Cpp2Employee + Cpp2Employer
            : 0m)
        + QpipEmployee + QpipEmployer;

    /// <summary>Everything else: federal tax and EI, and provincial tax and CPP outside Quebec.</summary>
    [JsonIgnore]
    public decimal CraRemittance => TotalRemittance - QuebecRemittance;

    public decimal TotalCost => GrossPay + CppEmployer + Cpp2Employer + EiEmployer + QpipEmployer;
}

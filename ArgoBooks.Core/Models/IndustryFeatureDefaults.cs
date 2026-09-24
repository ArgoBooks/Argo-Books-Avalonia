namespace ArgoBooks.Core.Models;

/// <summary>
/// Which optional sidebar sections a company starts with. Only a starting point: a toggle set in
/// Settings wins, see <see cref="FeatureVisibility"/>.
///
/// Deliberately generous, because a missing section reads as a feature the app does not have
/// while a spare one is only clutter. Keyed on Industry rather than BusinessType, which is the
/// legal structure and says nothing about whether a business holds stock or rents things out.
/// </summary>
public static class IndustryFeatureDefaults
{
    public static (bool Inventory, bool Rentals, bool Payroll) For(string? industry)
    {
        // A blank industry is not a statement about the business, so show everything.
        if (string.IsNullOrWhiteSpace(industry) || industry == IndustryNames.Other)
            return (true, true, true);

        var inventory = industry is not (IndustryNames.Services
            or IndustryNames.Technology
            or IndustryNames.RealEstate);

        // Rentals covers renting items out, which outside property management is rare enough
        // that showing it by default costs more than it helps.
        var rentals = industry == IndustryNames.RealEstate;

        // Payroll stays on for everyone: headcount is not something the industry tells us,
        // and an employer who cannot find payroll concludes the app has none.
        return (inventory, rentals, true);
    }
}

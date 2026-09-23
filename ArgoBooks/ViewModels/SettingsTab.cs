namespace ArgoBooks.ViewModels;

/// <summary>
/// The tabs of the settings modal, in the SAME order as the &lt;TabItem&gt;s in SettingsModal.axaml.
/// The TabControl selects by position, so <see cref="SettingsModalViewModel.SelectedTabIndex"/> is
/// the integer value of one of these. Use this enum instead of bare numbers at call sites
/// (<c>OpenWithTab(SettingsTab.PaymentPortal)</c>) so the intent is clear and the positions live in
/// one place.
///
/// IMPORTANT: this enum's member order MUST mirror the tab order in SettingsModal.axaml. Reordering
/// the TabItems means reordering these members too - there is no automatic link between them.
/// </summary>
internal enum SettingsTab
{
    General = 0,
    Features,
    Notifications,
    Appearance,
    Security,
    PaymentPortal,
    BankImportRules,
    MobileApp,
    Integrations,
}

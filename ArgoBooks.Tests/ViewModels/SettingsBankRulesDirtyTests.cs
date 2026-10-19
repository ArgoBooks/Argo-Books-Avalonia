using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Uses App.CompanyManager (process-wide static), so run in the serialized modal-VM collection.
[Collection("ModalViewModels")]
public class SettingsBankRulesDirtyTests : IDisposable
{
    public SettingsBankRulesDirtyTests()
    {
        var company = new CompanyData();
        company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        company.Settings.BankCategoryRules.Add(new BankCategoryRule { Id = "R1", Pattern = "staples", CategoryId = "CAT-PUR-001" });
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(company));
    }

    // Editing a bank import rule must mark the settings modal dirty, so closing it prompts to discard.
    [Fact]
    public void EditingABankRulePattern_MakesSettingsDirty()
    {
        var vm = new SettingsModalViewModel();
        vm.LoadBankRules();
        Assert.False(vm.HasUnsavedChanges);

        vm.BankCategoryRules[0].Pattern = "office depot";

        Assert.True(vm.HasUnsavedChanges);
    }

    public void Dispose()
    {
        App.SetCompanyManagerForTesting(null);
        GC.SuppressFinalize(this);
    }
}

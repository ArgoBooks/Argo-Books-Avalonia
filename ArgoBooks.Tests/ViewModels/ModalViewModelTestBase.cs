using System.Reflection;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// xUnit collection that forces all modal-ViewModel test classes to run sequentially. They mutate
/// PROCESS-WIDE static state (<c>App.CompanyManager</c> and the shared <c>App.UndoRedoManager</c>), so
/// running two of them in parallel lets one test's setup clobber another's company/undo stack
/// mid-run. DisableParallelization also keeps this collection from overlapping any other collection.
/// </summary>
[CollectionDefinition("ModalViewModels", DisableParallelization = true)]
public class ModalViewModelsCollection;

/// <summary>
/// Test harness for the transaction modal ViewModels (expenses, revenues, invoices, payments,
/// purchase orders, rentals). These ViewModels read <c>App.CompanyManager.CompanyData</c> and record
/// undo/redo actions on the shared <c>App.UndoRedoManager</c>; without a harness there is no way to
/// exercise their save / edit / undo / redo flows, which is exactly where multi-currency and
/// undo-asymmetry bugs have repeatedly hidden. Each test gets a fresh in-memory company and a cleared
/// undo stack; the shared statics are reset on dispose so tests don't leak into each other.
///
/// xUnit constructs a new instance of the test class per test method, so the ctor/Dispose here act
/// as per-test setup/teardown.
/// </summary>
[Collection("ModalViewModels")]
public abstract class ModalViewModelTestBase : IDisposable
{
    protected CompanyData Company { get; }

    protected ModalViewModelTestBase()
    {
        Company = new CompanyData();
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(Company));
        App.UndoRedoManager.Clear();
    }

    /// <summary>Undo the most recent recorded action.</summary>
    protected static void Undo() => App.UndoRedoManager.Undo();

    /// <summary>Redo the most recently undone action.</summary>
    protected static void Redo() => App.UndoRedoManager.Redo();

    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    private object? _priorRates;
    private bool _ratesRemoved;

    /// <summary>
    /// Runs the test with no exchange rate service, so no date has a rate. Picking a currency no
    /// stub serves isn't enough: the shared service is whichever one the run built first, and that
    /// can be one with a real HTTP client that fetches the rate.
    /// </summary>
    protected void UseNoExchangeRates()
    {
        if (_ratesRemoved) return;
        _priorRates = RatesInstance.GetValue(null);
        RatesInstance.SetValue(null, null);
        _ratesRemoved = true;
    }

    public void Dispose()
    {
        App.UndoRedoManager.Clear();
        App.SetCompanyManagerForTesting(null);
        if (_ratesRemoved)
            RatesInstance.SetValue(null, _priorRates);
        GC.SuppressFinalize(this);
    }
}

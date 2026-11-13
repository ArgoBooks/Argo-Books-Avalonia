using System.Reflection;
using System.Text.Json.Nodes;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArgoBooks.Video;

/// <summary>
/// The scripted takes. Each one is a single continuous recording in real time; the edit picks the
/// parts it uses by the marks a take leaves.
/// </summary>
internal static class Takes
{
    /// <summary>The window's size in the app's own units. It is drawn at twice this, so the edit can zoom in and stay sharp.</summary>
    // A Mac recording leaves room for the 28-point title bar macOS draws above the window, which the edit adds.
    private static readonly Size WindowSize = new(1600, Environment.GetEnvironmentVariable("ARGO_VIDEO_MAC") == "1" ? 872 : 900);

    private const string BusinessName = "Cedar Ridge IT";

    public static async Task RunAsync(Window window, TakeOptions options)
    {
        await Task.Delay(300);
        window.WindowState = WindowState.Normal;
        window.Width = WindowSize.Width;
        window.Height = WindowSize.Height;
        window.SetRenderScaling(Recorder.Scale);
        await Task.Delay(300);
        try
        {
            if (options.Take == "welcome")
            {
                await WelcomeAsync(window, options);
                return;
            }
            if (options.Take == "quickbooks")
            {
                await QuickBooksAsync(window, options);
                return;
            }
            if (options.Take == "qbcheck")
            {
                await QuickBooksCheckAsync(window, options);
                return;
            }
            if (options.Take is "payment" or "matching" or "reports")
            {
                await OpenCompanyCopyAsync(window, options);
                await (options.Take switch
                {
                    "payment" => PaymentAsync(window, options),
                    "matching" => MatchingAsync(window, options),
                    _ => ReportsAsync(window, options),
                });
                return;
            }
            await OpenSampleCompanyAsync(window);
            switch (options.Take)
            {
                case "receipt":
                    await ReceiptAsync(window, options);
                    break;
                case "tour":
                    await TourAsync(window, options);
                    break;
                case "bank":
                    await BankAsync(window, options);
                    break;
                case "bill":
                    await BillAsync(window, options);
                    break;
                case "salestax":
                    await SalesTaxAsync(window, options);
                    break;
                case "forecast":
                    await ForecastAsync(window);
                    break;
                default:
                    throw new ArgumentException($"There is no take called {options.Take}");
            }
        }
        catch
        {
            window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, $"{options.Take}_failed.png"));
            throw;
        }
    }

    /// <summary>The welcome screen, the hand settling on Create New Company: a new company is a new file, with no account.</summary>
    private static async Task WelcomeAsync(Window window, TakeOptions options)
    {
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "welcome.mkv"), new Point(820, 780));
        await rec.WarmUpAsync(2);
        rec.Mark("start");
        await rec.HoldAsync(1.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Create New Company")) + new Point(0, 12), 1.3);
        rec.Mark("hover_create");
        await rec.HoldAsync(3.5);
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// The main pages one after another from the sidebar. Each is held for about eleven seconds, so the edit can stay on
    /// any one of them for a whole sentence.
    /// </summary>
    private static async Task TourAsync(Window window, TakeOptions options)
    {
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "tour.mkv"), new Point(1100, 260));
        await rec.WarmUpAsync(3);
        rec.Mark("dashboard");
        await rec.HoldAsync(2.0);
        await rec.MoveToAsync(new Point(1180, 330), 2.5);
        await rec.HoldAsync(2.5);
        await rec.MoveToAsync(new Point(960, 420), 2.5);
        await rec.HoldAsync(1.5);
        foreach (var (label, page) in new[]
                 {
                     ("Expenses", PageNames.Expenses), ("Revenue", PageNames.Revenue), ("Invoices", PageNames.Invoices),
                     ("Reports", "Reports"), ("Insights", "Insights"), ("Analytics", PageNames.Analytics),
                 })
        {
            var item = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => Ui.TextOf(t) == label && t.IsEffectivelyVisible && Ui.CentreOf(window, t).X < 300);
            await rec.MoveToAsync(Ui.LeftOf(window, item), 0.9);
            await rec.HoldAsync(0.15);
            rec.Mark("click_" + label.ToLowerInvariant());
            await rec.ClickAsync();
            await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == page, 10, $"the {label} page");
            rec.Mark(label.ToLowerInvariant());
            // The hand drifts out over the page and wanders slowly, as someone reading it would.
            await rec.MoveToAsync(new Point(900, 470), 1.2);
            await rec.HoldAsync(2.5);
            await rec.MoveToAsync(new Point(1150, 420), 2.5);
            await rec.HoldAsync(2.0);
            await rec.MoveToAsync(new Point(980, 520), 2.0);
            await rec.HoldAsync(0.8);
        }
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// Import Bank Statement from the search box, the prompt that says nothing is connected to the bank, then the
    /// statement's lines read and sorted, ready to import.
    /// </summary>
    private static async Task BankAsync(Window window, TakeOptions options)
    {
        var statement = options.Statement ?? throw new ArgumentException("The bank take needs a statement file");
        var import = App.BankStatementImportModalViewModel!;
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "bank.mkv"), new Point(1250, 320));
        await rec.WarmUpAsync(3);
        rec.Mark("start");
        await rec.HoldAsync(1.0);

        var search = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Search or jump to...");
        await rec.MoveToAsync(Ui.LeftOf(window, search, 0.35), 1.0);
        await rec.ClickAsync();
        await rec.HoldAsync(0.3);
        rec.Mark("typing");
        await rec.TypeAsync("bank statement");
        await rec.HoldAsync(0.8);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Import bank statement")), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_result");
        await rec.ClickAsync();

        var prompt = AppHooks.ImportPrompt();
        await rec.WaitForAsync(() => (bool)prompt.GetType().GetProperty("IsOpen")!.GetValue(prompt)!, 10, "the import prompt");
        rec.Mark("prompt");
        await rec.HoldAsync(0.8);
        var noLogin = Ui.Text(window, "There is no bank login, and nothing is connected to your account.");
        await rec.MoveToAsync(Ui.LeftOf(window, noLogin, 0.85) + new Point(0, 14), 1.0);
        rec.Mark("no_login");
        await rec.HoldAsync(2.2);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Choose statement")), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_choose");
        // A window with no desktop has no file dialog, so the click is shown and the statement is handed over as if picked.
        await rec.PressAsync();
        AppHooks.CancelImportPrompt(prompt);
        _ = import.OpenAsync(statement);
        await rec.WaitForAsync(() => import.IsOpen && !import.IsLoading && !Ui.Shows(window, "Scanning bank statement..."), 180, "the statement's lines");
        rec.Mark("rows");
        await rec.HoldAsync(1.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Amount")) + new Point(0, 120), 1.2);
        await rec.HoldAsync(1.5);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Amount")) + new Point(0, 330), 1.4);
        await rec.HoldAsync(2.0);
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// A new, empty company, then the real QuickBooks import of the exports from make_qb_exports.py, dropped in together.
    /// Every report is analysed, reviewed and imported, and the books are then read back from the app's own pages.
    /// </summary>
    private static async Task QuickBooksAsync(Window window, TakeOptions options)
    {
        var exports = options.Statement ?? throw new ArgumentException("The quickbooks take needs the exports folder");
        var company = Path.Combine(options.OutFolder, "Cedar Ridge IT.argo");
        File.Delete(company);
        AppHooks.AnswerFilePickers(window, company);

        var rec = new Recorder(window, Path.Combine(options.OutFolder, "quickbooks.mkv"), new Point(1100, 560));
        await rec.WarmUpAsync(2);
        rec.Mark("welcome");
        await rec.HoldAsync(1.0);

        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Create New Company")), 0.9);
        rec.Mark("click_create");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Company Information"), 10, "the new company form");
        await rec.HoldAsync(0.5);
        rec.Mark("wizard");

        var wizard = AppHooks.Shell("CreateCompanyViewModel");
        var name = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Enter company name");
        await rec.MoveToAsync(Ui.LeftOf(window, name), 0.8);
        await rec.ClickAsync();
        await rec.TypeAsync("Cedar Ridge IT");
        await rec.HoldAsync(0.3);
        AppHooks.SetProperty(wizard, "Country", "Canada");
        await rec.HoldAsync(0.3);
        AppHooks.SetProperty(wizard, "ProvinceState", "Ontario");
        await rec.HoldAsync(0.3);

        var balance = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Optional");
        await rec.MoveToAsync(Ui.LeftOf(window, balance), 0.8);
        rec.Mark("balance");
        await rec.ClickAsync();
        await rec.TypeAsync("18250.00");
        await rec.HoldAsync(1.6);

        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.8);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Create Company"), 10, "the second step");
        await rec.HoldAsync(0.8);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Create Company")), 0.8);
        rec.Mark("click_finish");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == PageNames.Dashboard && !Ui.Shows(window, "Create Company"), 30, "the new company");
        await rec.HoldAsync(1.5);
        Ui.Dump(window, "empty dashboard");
        rec.Mark("empty");
        await rec.HoldAsync(2.5);

        await rec.MoveToAsync(new Point(286, 59), 0.9);
        rec.Mark("click_file");
        await rec.ClickAsync();
        await rec.HoldAsync(0.7);
        Ui.Dump(window, "file menu");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Import")), 0.7);
        await rec.HoldAsync(0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Bring your lists and transactions across"), 10, "the import choices");
        await rec.HoldAsync(0.9);
        rec.Mark("choices");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "QuickBooks")), 0.9);
        await rec.HoldAsync(0.5);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Import from QuickBooks"), 10, "the QuickBooks steps");
        rec.Mark("online_steps");
        await rec.HoldAsync(2.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "QuickBooks Desktop")), 0.8);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Save them somewhere together"), 10, "the Desktop steps");
        rec.Mark("desktop_steps");
        await rec.HoldAsync(5.0);

        var drop = Ui.Text(window, "Drop your QuickBooks exports here");
        var from = Ui.CentreOf(window, drop) + new Point(620, 300);
        var into = Ui.CentreOf(window, drop);
        rec.Mark("drag");
        await rec.MoveToAsync(from, 0.8);
        await rec.DragFilesAsync(ExportOrder.Select(f => Path.Combine(exports, f)).ToArray(), from, into, 1.5);
        await rec.WaitForAsync(() => Ui.Shows(window, "5 files ready"), 10, "the dropped files");
        rec.Mark("dropped");
        await rec.HoldAsync(3.5);
        Ui.Dump(window, "dropped");

        var mapping = AppHooks.Shell("ImportMappingDialogViewModel");
        var result = AppHooks.Shell("ImportResultDialogViewModel");
        var validation = AppHooks.Shell("ImportValidationDialogViewModel");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Continue")), 0.8);
        rec.Mark("click_continue");
        await rec.ClickAsync();

        foreach (var (file, n) in ExportOrder.Select((f, i) => (f, i + 1)))
        {
            rec.Mark($"analysing_{n}");
            // A report that is not a plain table goes straight to its result, with no review of columns to show.
            await rec.WaitForAsync(() => AppHooks.IsOpen(mapping) || AppHooks.IsOpen(result), 400, $"the review of {file}");
            if (AppHooks.IsOpen(mapping))
            {
                rec.Mark($"review_{n}");
                await rec.HoldAsync(1.5);
                var accept = (string)mapping.GetType().GetProperty("AcceptButtonText")!.GetValue(mapping)!;
                await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, accept)), 0.8);
                await rec.HoldAsync(1.0);
                await rec.ClickAsync();
                await rec.WaitForAsync(() => AppHooks.IsOpen(result) || AppHooks.IsOpen(validation), 400, $"the result of {file}");
            }
            if (AppHooks.IsOpen(validation))
            {
                rec.Mark($"validation_{n}");
                await rec.HoldAsync(1.5);
                Ui.Dump(window, $"validation of {file}");
                throw new InvalidOperationException("The importer asked about missing records; script this dialog from what the dump shows");
            }
            rec.Mark($"result_{n}");
            await rec.HoldAsync(1.5);
            Ui.Dump(window, $"result of {file}");
            AppHooks.Audit(file);
            await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "OK")), 0.7);
            await rec.ClickAsync();
            await rec.WaitForAsync(() => !AppHooks.IsOpen(result), 10, "the result to close");
            await rec.HoldAsync(0.3);
        }
        rec.Mark("imported");
        await AppHooks.FinishConversionsAsync();
        await rec.WaitForAsync(() => !App.PendingConversionService!.HasPendingConversions, 120, "the currency conversions");
        await rec.HoldAsync(0.5);
        AppHooks.Audit("after conversions");
        await rec.MoveToAsync(new Point(488, 59), 0.9);
        rec.Mark("click_save");
        await rec.ClickAsync();
        await rec.HoldAsync(1.0);

        foreach (var (label, page) in new[]
                 {
                     ("Dashboard", PageNames.Dashboard), ("Revenue", PageNames.Revenue), ("Expenses", PageNames.Expenses),
                     ("Invoices", PageNames.Invoices), ("Customers", "Customers"), ("Suppliers", "Suppliers"), ("Reports", "Reports"),
                 })
        {
            // The sidebar's lower items are below the window, so those pages are opened directly and the edit cuts to them.
            var item = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => Ui.TextOf(t) == label && t.IsEffectivelyVisible && Ui.CentreOf(window, t) is { X: < 300, Y: < 860 });
            if (item != null)
            {
                await rec.MoveToAsync(Ui.LeftOf(window, item), 0.9);
                await rec.ClickAsync();
            }
            else
                App.NavigationService!.NavigateTo(page);
            await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == page, 10, $"the {label} page");
            await rec.HoldAsync(1.2);
            rec.Mark("page_" + label.ToLowerInvariant());
            Ui.Dump(window, label);
            await rec.HoldAsync(4.0);
        }
        var reports = window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ReportsPageViewModel>().First();
        reports.SelectTemplateCommand.Execute(ReportTemplateFactory.TemplateNames.IncomeStatement);
        await rec.HoldAsync(0.8);
        reports.SelectedDatePreset = DatePresetNames.YearToDate;
        await rec.HoldAsync(1.0);
        rec.Mark("statement_choice");
        Ui.Dump(window, "statement choice");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
        await rec.ClickAsync();
        await rec.HoldAsync(2.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
        await rec.ClickAsync();
        await rec.HoldAsync(5.0);
        rec.Mark("statement");
        Ui.Dump(window, "statement");
        await rec.HoldAsync(4.0);
        window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, "statement.png"));
        rec.Mark("end");
        await rec.FinishAsync();
    }

    // Alphabetical, as choosing a whole folder gives them.
    private static readonly string[] ExportOrder =
        ["Customer Contact List.xlsx", "Item Listing.xlsx", "Open Invoices.xlsx", "Profit and Loss Detail.xlsx", "Vendor Contact List.xlsx"];

    /// <summary>
    /// Imports every file in the folder through the QuickBooks screen, however many there are and whatever each one asks,
    /// printing what the company holds after each, for checking real exports against their own totals.
    /// </summary>
    private static async Task QuickBooksCheckAsync(Window window, TakeOptions options)
    {
        var folder = options.Statement ?? throw new ArgumentException("The check needs the exports folder");
        var company = Path.Combine(options.OutFolder, "Check.argo");
        File.Delete(company);
        AppHooks.AnswerFilePickers(window, company);
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "qbcheck.mkv"), new Point(1100, 560));
        await rec.WarmUpAsync(1);

        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Create New Company")), 0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Company Information"), 10, "the new company form");
        var wizard = AppHooks.Shell("CreateCompanyViewModel");
        AppHooks.SetProperty(wizard, "CompanyName", "Check");
        AppHooks.SetProperty(wizard, "Country", "Canada");
        await rec.HoldAsync(0.5);
        AppHooks.SetProperty(wizard, "ProvinceState", "Ontario");
        await rec.HoldAsync(0.5);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Create Company"), 10, "the second step");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Create Company")), 0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == PageNames.Dashboard && !Ui.Shows(window, "Create Company"), 30, "the new company");
        await rec.HoldAsync(1.0);

        await rec.MoveToAsync(new Point(286, 59), 0.2);
        await rec.ClickAsync();
        await rec.HoldAsync(0.5);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Import")), 0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Bring your lists and transactions across"), 10, "the import choices");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "QuickBooks")), 0.2);
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Import from QuickBooks"), 10, "the QuickBooks steps");

        var drop = Ui.Text(window, "Drop your QuickBooks exports here");
        var into = Ui.CentreOf(window, drop);
        await rec.DragFilesAsync(Directory.GetFiles(folder).OrderBy(f => f).ToArray(), into + new Point(300, 100), into, 0.3);
        await rec.HoldAsync(1.5);
        Ui.Dump(window, "dropped");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Continue")), 0.2);
        await rec.ClickAsync();

        var mapping = AppHooks.Shell("ImportMappingDialogViewModel");
        var result = AppHooks.Shell("ImportResultDialogViewModel");
        var validation = AppHooks.Shell("ImportValidationDialogViewModel");
        var quiet = 0.0;
        string[] busy = ["Analyzing spreadsheet structure...", "AI processing...", "Importing data...", "Processing..."];
        while (quiet < 45)
        {
            await rec.HoldAsync(0.5);
            quiet += 0.5;
            if (busy.Any(b => Ui.Shows(window, b)) || (App.CompanyManager?.CompanyData != null && window.GetVisualDescendants().OfType<ProgressBar>().Any(b => b.IsEffectivelyVisible)))
                quiet = 0;
            if (AppHooks.IsOpen(mapping))
            {
                quiet = 0;
                Ui.Dump(window, "review");
                var accept = (string)mapping.GetType().GetProperty("AcceptButtonText")!.GetValue(mapping)!;
                await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, accept)), 0.1);
                await rec.ClickAsync();
                await rec.HoldAsync(1.0);
            }
            else if (AppHooks.IsOpen(validation))
            {
                quiet = 0;
                Ui.Dump(window, "validation");
                throw new InvalidOperationException("The importer asked about something; script this from the dump");
            }
            else if (Ui.Shows(window, "OK"))
            {
                quiet = 0;
                Ui.Dump(window, AppHooks.IsOpen(result) ? "result" : "message");
                AppHooks.Audit("after a file");
                await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "OK")), 0.1);
                await rec.ClickAsync();
                await rec.HoldAsync(1.0);
            }
        }

        await AppHooks.FinishConversionsAsync();
        await rec.WaitForAsync(() => !App.PendingConversionService!.HasPendingConversions, 120, "the currency conversions");
        await rec.HoldAsync(0.5);
        AppHooks.Audit("finished");
        App.NavigationService!.NavigateTo("Reports");
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == "Reports", 10, "the Reports page");
        await rec.HoldAsync(1.5);
        var reports = window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ReportsPageViewModel>().First();
        reports.SelectTemplateCommand.Execute(ReportTemplateFactory.TemplateNames.IncomeStatement);
        await rec.HoldAsync(0.6);
        reports.SelectedDatePreset = DatePresetNames.YearToDate;
        await rec.HoldAsync(0.8);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.1);
        await rec.ClickAsync();
        await rec.HoldAsync(1.5);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.1);
        await rec.ClickAsync();
        await rec.HoldAsync(4.0);
        window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, "statement.png"));
        await rec.FinishAsync();
    }

    /// <summary>
    /// An expense made into a bill to pay later: opened from the Expenses page, Paid unticked so it takes a due date, and
    /// saved. Then the Accounts Payable Aging report, which lists what is owed to each supplier.
    /// </summary>
    private static async Task BillAsync(Window window, TakeOptions options)
    {
        App.NavigationService!.NavigateTo(PageNames.Expenses);
        await Task.Delay(2500);
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "bill.mkv"), new Point(1250, 330));
        await rec.WarmUpAsync(3);
        rec.Mark("expenses");
        await rec.HoldAsync(1.2);

        var expenses = window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ExpensesPageViewModel>().First();
        var row = Ui.Text(window, "NetSwitch 1000-24T");
        var edit = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == expenses.OpenEditModalCommand && b.IsEffectivelyVisible && Math.Abs(Ui.CentreOf(window, b).Y - Ui.CentreOf(window, row).Y) < 16);
        await rec.MoveToAsync(Ui.LeftOf(window, row, 0.5), 1.0);
        rec.Mark("row");
        await rec.HoldAsync(0.6);
        await rec.MoveToAsync(Ui.CentreOf(window, edit), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_edit");
        await rec.ClickAsync();

        CheckBox? Paid() => window.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Content as string == "Paid" && c.IsEffectivelyVisible);
        await rec.WaitForAsync(() => Paid() != null && Ui.Shows(window, "Save Changes"), 10, "the expense form");
        rec.Mark("form");
        await rec.HoldAsync(1.2);
        Ui.Dump(window, "expense form");

        // The Paid box sits under the line items, so the form is scrolled down to it.
        var save = Ui.Text(window, "Save Changes");
        await rec.MoveToAsync(Ui.CentreOf(window, save) + new Point(-320, -260), 0.7);
        rec.Mark("scroll");
        for (var i = 0; i < 60 && Ui.CentreOf(window, Paid()!).Y > Ui.CentreOf(window, save).Y - 280; i++)
        {
            window.MouseWheel(rec.Cursor, new Vector(0, -1));
            await rec.HoldAsync(0.07);
        }
        await rec.HoldAsync(0.5);
        await rec.MoveToAsync(Ui.LeftOf(window, Paid()!, 0.08), 0.7);
        await rec.HoldAsync(0.2);
        rec.Mark("click_paid");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => Ui.Shows(window, "Due date"), 5, "the due date");
        rec.Mark("due");
        await rec.HoldAsync(2.8);
        Ui.Dump(window, "bill");
        await rec.MoveToAsync(Ui.CentreOf(window, save), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_save");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => !Ui.Shows(window, "Save Changes"), 10, "the form to close");
        rec.Mark("saved");
        await rec.HoldAsync(1.0);
        Ui.Dump(window, "expenses with the bill");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Bills to Pay")) + new Point(0, -14), 1.0);
        rec.Mark("bills_card");
        await rec.HoldAsync(2.5);

        App.NavigationService!.NavigateTo("Reports");
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == "Reports", 10, "the Reports page");
        await rec.HoldAsync(1.0);
        var reports = window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ReportsPageViewModel>().First();
        reports.SelectTemplateCommand.Execute(ReportTemplateFactory.TemplateNames.APaging);
        await rec.HoldAsync(0.8);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
        await rec.ClickAsync();
        await rec.HoldAsync(2.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
        await rec.ClickAsync();
        await rec.HoldAsync(5.0);
        rec.Mark("aging");
        Ui.Dump(window, "aging");
        await rec.HoldAsync(4.0);
        window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, "aging.png"));
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>The Sales Tax page from the sidebar: each period's tax, what is owed, and when its return is due.</summary>
    private static async Task SalesTaxAsync(Window window, TakeOptions options)
    {
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "salestax.mkv"), new Point(1100, 260));
        await rec.WarmUpAsync(3);
        rec.Mark("dashboard");
        await rec.HoldAsync(1.0);
        var item = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => Ui.TextOf(t) == "Sales Tax" && t.IsEffectivelyVisible && Ui.CentreOf(window, t).X < 300);
        await rec.MoveToAsync(Ui.LeftOf(window, item), 1.0);
        await rec.HoldAsync(0.15);
        rec.Mark("click_sales_tax");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == PageNames.SalesTax, 10, "the Sales Tax page");
        rec.Mark("sales_tax");
        await rec.HoldAsync(1.0);
        Ui.Dump(window, "sales tax");
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Owed now")) + new Point(30, 18), 1.0);
        rec.Mark("owed");
        await rec.HoldAsync(1.8);
        var due = Ui.Text(window, "Due");
        await rec.MoveToAsync(Ui.CentreOf(window, due) + new Point(10, 110), 1.0);
        rec.Mark("due");
        await rec.HoldAsync(1.0);
        await rec.MoveToAsync(Ui.CentreOf(window, due) + new Point(10, 320), 1.6);
        await rec.HoldAsync(2.5);
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// Opens a copy of the company the quickbooks take saved. It is a real company rather than the sample, so payments
    /// can be recorded and its reports carry its own name. The copy keeps the file's name, which the app shows.
    /// </summary>
    private static async Task OpenCompanyCopyAsync(Window window, TakeOptions options)
    {
        var source = options.Statement ?? throw new ArgumentException($"The {options.Take} take needs the company file the quickbooks take saved");
        var folder = Path.Combine(options.OutFolder, options.Take);
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Path.GetFileName(source));
        File.Copy(source, copy, true);
        var open = typeof(App).GetMethod("OpenCompanyWithRetryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)open.Invoke(null, [copy, "video", true])!;
        for (var i = 0; i < 600 && App.NavigationService?.CurrentPageName != PageNames.Dashboard; i++)
            await Task.Delay(100);
        await Task.Delay(2000);
        AppHooks.Audit("opened");
    }

    private static T PageModel<T>(Window window) =>
        window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<T>().First();

    /// <summary>An unpaid invoice paid: Record Payment on its row, the payment form, and the invoice shown as paid.</summary>
    private static async Task PaymentAsync(Window window, TakeOptions options)
    {
        App.NavigationService!.NavigateTo(PageNames.Invoices);
        await Task.Delay(2500);
        var invoices = PageModel<InvoicesPageViewModel>(window);
        Ui.Dump(window, "invoices");
        // An overdue invoice, the kind a payment most often closes.
        var rowY = Ui.CentreOf(window, Ui.Text(window, "Priya Nair")).Y;
        var pay = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Command == invoices.RecordPaymentCommand && b.IsEffectivelyVisible && b.IsEnabled && Math.Abs(Ui.CentreOf(window, b).Y - rowY) < 16);

        var rec = new Recorder(window, Path.Combine(options.OutFolder, "payment.mkv"), new Point(1250, 330));
        await rec.WarmUpAsync(3);
        rec.Mark("invoices");
        await rec.HoldAsync(1.5);
        await rec.MoveToAsync(new Point(700, rowY + 4), 1.1);
        rec.Mark("row");
        await rec.HoldAsync(1.2);
        await rec.MoveToAsync(Ui.CentreOf(window, pay), 0.9);
        await rec.HoldAsync(0.3);
        rec.Mark("click_pay");
        await rec.ClickAsync();

        var payments = App.PaymentModalsViewModel!;
        await rec.WaitForAsync(() => Ui.Shows(window, "Reference Number"), 10, "the payment form");
        rec.Mark("form");
        Ui.Dump(window, "payment form");
        await rec.HoldAsync(2.5);
        var save = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == payments.SaveFormCommand && b.IsEffectivelyVisible);
        await rec.MoveToAsync(Ui.CentreOf(window, save), 0.9);
        await rec.HoldAsync(0.3);
        rec.Mark("click_save");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => !Ui.Shows(window, "Reference Number"), 10, "the form to close");
        rec.Mark("paid");
        await rec.HoldAsync(1.0);
        Ui.Dump(window, "invoices after the payment");
        AppHooks.Audit("after the payment");
        await rec.MoveToAsync(new Point(1000, rowY + 20), 1.2);
        await rec.HoldAsync(3.5);
        window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, "payment.png"));
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// October's statement checked against the books on the Bank Matching page, in a company that checked the months
    /// before it already: the statement chosen, its lines matched, a transfer to savings ignored, a suggestion accepted, the
    /// months on the calendar, and what is in the books but not the bank.
    /// </summary>
    private static async Task MatchingAsync(Window window, TakeOptions options)
    {
        var folder = Path.Combine(options.OutFolder, "matching");
        var statement = WriteStatement(Path.Combine(folder, "October statement.csv"));
        AppHooks.AnswerFilePickers(window, Path.Combine(folder, "unused.argo"), statement);
        App.NavigationService!.NavigateTo("BankMatching");
        await Task.Delay(2500);
        var page = PageModel<BankMatchingPageViewModel>(window);
        Ui.Dump(window, "bank matching");

        var rec = new Recorder(window, Path.Combine(options.OutFolder, "matching.mkv"), new Point(1100, 300));
        await rec.WarmUpAsync(3);
        rec.Mark("page");
        await rec.HoldAsync(1.5);
        Button Visible(System.Windows.Input.ICommand command) =>
            window.GetVisualDescendants().OfType<Button>().First(b => b.Command == command && b.IsEffectivelyVisible);
        await rec.MoveToAsync(Ui.CentreOf(window, Visible(page.ImportStatementCommand)), 1.0);
        await rec.HoldAsync(0.2);
        rec.Mark("click_import");
        await rec.ClickAsync();

        var prompt = AppHooks.ImportPrompt();
        await rec.WaitForAsync(() => AppHooks.IsOpen(prompt), 10, "the import prompt");
        rec.Mark("prompt");
        Ui.Dump(window, "prompt");
        await rec.HoldAsync(3.0);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Choose statement")), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_choose");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => page.HasSession && Ui.Shows(window, "OK"), 180, "the import's message");
        rec.Mark("imported");
        await rec.HoldAsync(1.8);
        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "OK")), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_ok");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => !Ui.Shows(window, "OK"), 10, "the message to close");
        await rec.HoldAsync(1.0);
        rec.Mark("matched");
        Ui.Dump(window, "matched");
        Console.WriteLine($"matched {page.MatchedCount}, unmatched lines {page.UnmatchedLineCount}, unmatched records {page.UnmatchedBookCount}, difference {page.MatchedDifferenceDisplay}");
        window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, "matching.png"));

        foreach (var (label, key) in new[] { ("Matched", "look_matched"), ("Unmatched bank lines", "look_lines"), ("Unmatched book records", "look_records") })
        {
            if (Ui.FindText(window, label) is not { } card)
                continue;
            await rec.MoveToAsync(Ui.CentreOf(window, card) + new Point(0, 30), 0.9);
            rec.Mark(key);
            await rec.HoldAsync(1.6);
        }

        // Money moved to the owner's own savings is neither income nor an expense, so it is set aside rather than matched.
        if (Ui.FindText(window, "TRANSFER TO SAVINGS") is { } transfer)
        {
            var y = Ui.CentreOf(window, transfer).Y;
            await rec.MoveToAsync(Ui.LeftOf(window, transfer, 0.4) + new Point(0, 14), 1.0);
            rec.Mark("transfer");
            await rec.HoldAsync(1.5);
            var ignore = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Command == page.IgnoreLineCommand && b.IsEffectivelyVisible && Math.Abs(Ui.CentreOf(window, b).Y - y) < 16);
            await rec.MoveToAsync(Ui.CentreOf(window, ignore), 0.8);
            await rec.HoldAsync(0.3);
            rec.Mark("click_ignore");
            await rec.ClickAsync();
            await rec.HoldAsync(1.5);
        }
        if (Ui.FindText(window, "CITY FUEL #4412") is { } fuel)
        {
            await rec.MoveToAsync(Ui.LeftOf(window, fuel, 0.4) + new Point(0, 14), 1.0);
            rec.Mark("fuel");
            await rec.HoldAsync(3.0);
        }
        // The suggested lines sit further down the table, so it is scrolled until one is in view.
        Button? Suggestion() => window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Command == page.AcceptSuggestionCommand && b.IsEffectivelyVisible && Ui.CentreOf(window, b).Y is > 490 and < 790);
        if (Suggestion() == null)
        {
            await rec.MoveToAsync(new Point(900, 700), 0.8);
            rec.Mark("scroll");
            for (var i = 0; i < 40 && Suggestion() == null; i++)
            {
                window.MouseWheel(rec.Cursor, new Vector(0, -1));
                await rec.HoldAsync(0.07);
            }
            await rec.HoldAsync(0.6);
        }
        if (Suggestion() is { } accept)
        {
            var row = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.IsEffectivelyVisible && Ui.TextOf(t) == "Suggested" && Math.Abs(Ui.CentreOf(window, t).Y - Ui.CentreOf(window, accept).Y) < 16);
            if (row != null)
            {
                await rec.MoveToAsync(Ui.CentreOf(window, row) + new Point(160, 14), 0.9);
                rec.Mark("suggested");
                await rec.HoldAsync(1.8);
            }
            await rec.MoveToAsync(Ui.CentreOf(window, accept), 1.0);
            await rec.HoldAsync(0.4);
            rec.Mark("click_accept");
            await rec.ClickAsync();
            await rec.HoldAsync(1.5);
        }
        Ui.Dump(window, "lines handled");

        await rec.MoveToAsync(Ui.CentreOf(window, Visible(page.ShowLinesCalendarCommand)), 1.0);
        await rec.HoldAsync(0.2);
        rec.Mark("click_calendar");
        await rec.ClickAsync();
        await rec.HoldAsync(1.0);
        rec.Mark("calendar");
        Ui.Dump(window, "calendar");
        await rec.HoldAsync(4.0);

        await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Missing from statement")), 1.0);
        await rec.HoldAsync(0.2);
        rec.Mark("click_missing");
        await rec.ClickAsync();
        await rec.HoldAsync(1.0);
        rec.Mark("missing");
        Ui.Dump(window, "missing");
        await rec.HoldAsync(5.0);
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>
    /// Writes October's bank statement as a bank's website gives it, and gives the company the months before it as
    /// already checked, each line matched to the record it came from. Every paid sale and expense is on the day it was
    /// recorded, except the parts orders, whose card charges post two days later. The invoices still unpaid are left out,
    /// and October has a fuel stop that was never recorded and a transfer to the owner's savings for tax.
    /// </summary>
    private static string WriteStatement(string path)
    {
        var data = App.CompanyManager!.CompanyData!;
        string Supplier(string? id) => data.Suppliers.FirstOrDefault(s => s.Id == id)?.Name ?? "";
        string Customer(string? id) => data.Customers.FirstOrDefault(c => c.Id == id)?.Name ?? "";
        // A card charge reads as the store's name, and an e-transfer as the sender's name and their message.
        var stores = new Dictionary<string, string>
        {
            ["Northwave Internet"] = "NORTHWAVE INTERNET", ["CloudHost Services"] = "CLOUDHOST HOSTING", ["TechSource Parts"] = "TECHSOURCE PARTS",
            ["Brightline Office Supply"] = "BRIGHTLINE PAPER & TONER", ["City Fuel"] = "CITY FUEL #4412", ["Parkway Parking"] = "PARKWAY PARKING",
        };
        var unpaid = data.Invoices.Where(i => i.Balance > 0).Select(i => i.Id).ToHashSet();
        var lines = new List<(DateTime Date, string Text, decimal Amount, BookRecordType Type, string? Id)>();
        foreach (var expense in data.Expenses)
        {
            var name = Supplier(expense.SupplierId);
            lines.Add((expense.Date.AddDays(name == "TechSource Parts" ? 2 : 0), stores.GetValueOrDefault(name, name.ToUpperInvariant()),
                -expense.Total, BookRecordType.Expense, expense.Id));
        }
        var paid = data.Revenues.Where(r => r.PaymentStatus is RevenuePaymentStatus.Paid or RevenuePaymentStatus.Complete
                                            && (r.InvoiceId == null || !unpaid.Contains(r.InvoiceId))).ToList();
        foreach (var revenue in paid)
            lines.Add((revenue.Date, $"E-TRANSFER {Customer(revenue.CustomerId).ToUpperInvariant()} {revenue.Description.ToUpperInvariant()}",
                revenue.Total, BookRecordType.Revenue, revenue.Id));
        lines.Add((new DateTime(2026, 10, 17), "CITY FUEL #4412", -61.30m, BookRecordType.Expense, null));
        lines.Add((new DateTime(2026, 10, 31), "TRANSFER TO SAVINGS", -1500.00m, BookRecordType.Expense, null));
        lines = lines.OrderBy(l => l.Date).ToList();

        var october = new DateTime(2026, 10, 1);
        var earlier = new BankImportSession { Id = Guid.NewGuid().ToString("N"), SourceFileName = "January to September statement.csv" };
        var matcher = new BankMatchingService();
        foreach (var (line, n) in lines.Where(l => l.Date < october).Select((l, n) => (l, n)))
        {
            var bankLine = new BankStatementLine { Id = Guid.NewGuid().ToString("N"), Date = line.Date, Description = line.Text, Amount = line.Amount, SourceRowIndex = n };
            earlier.Lines.Add(bankLine);
            matcher.ConfirmMatch(bankLine, new BankMatchCandidate { LineId = bankLine.Id, RecordType = line.Type, RecordId = line.Id!, Confidence = 1.0 }, data);
        }
        data.BankImportSessions.Add(earlier);
        Console.WriteLine($"statement: {data.Expenses.Count} expenses, {paid.Count} of {data.Revenues.Count} revenues, {unpaid.Count} unpaid invoices left out, " +
                          $"{earlier.Lines.Count} lines before October matched already");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var money = System.Globalization.CultureInfo.InvariantCulture;
        var balance = 18250.00m + lines.Where(l => l.Date < october).Sum(l => l.Amount);
        var csv = new System.Text.StringBuilder("Date,Description,Withdrawals,Deposits,Balance\n");
        foreach (var (date, text, amount, _, _) in lines.Where(l => l.Date >= october))
        {
            balance += amount;
            csv.Append(money, $"{date:yyyy-MM-dd},{text},{(amount < 0 ? (-amount).ToString("0.00", money) : "")},{(amount > 0 ? amount.ToString("0.00", money) : "")},{balance.ToString("0.00", money)}\n");
        }
        File.WriteAllText(path, csv.ToString());
        return path;
    }

    /// <summary>The Income Statement for the year so far, then the Accounts Receivable Aging report of who still owes what.</summary>
    private static async Task ReportsAsync(Window window, TakeOptions options)
    {
        App.NavigationService!.NavigateTo("Reports");
        await Task.Delay(2500);
        var reports = PageModel<ReportsPageViewModel>(window);
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "reports.mkv"), new Point(1100, 420));
        await rec.WarmUpAsync(3);
        rec.Mark("reports");
        foreach (var (template, key) in new[]
                 {
                     (ReportTemplateFactory.TemplateNames.IncomeStatement, "statement"),
                     (ReportTemplateFactory.TemplateNames.ARaging, "receivable"),
                 })
        {
            while (reports.CurrentStep > 1)
            {
                var step = reports.CurrentStep;
                await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Previous")), 0.8);
                await rec.ClickAsync();
                await rec.WaitForAsync(() => reports.CurrentStep < step, 10, "the step before");
                await rec.HoldAsync(0.8);
            }
            await rec.HoldAsync(0.8);
            reports.SelectTemplateCommand.Execute(template);
            await rec.HoldAsync(0.8);
            if (key == "statement")
            {
                reports.SelectedDatePreset = DatePresetNames.YearToDate;
                await rec.HoldAsync(0.8);
            }
            rec.Mark(key + "_choice");
            await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
            await rec.ClickAsync();
            await rec.HoldAsync(2.0);
            await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Next")), 0.9);
            await rec.ClickAsync();
            await rec.HoldAsync(5.0);
            rec.Mark(key);
            Ui.Dump(window, key);
            await rec.MoveToAsync(new Point(780, 520), 1.5);
            await rec.HoldAsync(4.0);
            window.CaptureRenderedFrame()?.Save(Path.Combine(options.OutFolder, key + ".png"));
        }
        rec.Mark("end");
        await rec.FinishAsync();
    }

    /// <summary>Records nothing: prints the forecast the Insights page shows for the sample company, for tuning its data.</summary>
    private static async Task ForecastAsync(Window window)
    {
        App.NavigationService!.NavigateTo("Insights");
        await Task.Delay(4000);
        var insights = typeof(App).GetField("_insightsPageViewModel", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string Read(string name) => insights.GetType().GetProperty(name)!.GetValue(insights)?.ToString() ?? "";
        Console.WriteLine($"forecast: revenue {Read("ForecastedRevenue")}, expenses {Read("ForecastedExpenses")}, profit {Read("ForecastedProfit")} ({Read("ForecastDateRangeLabel")})");
    }

    /// <summary>Opens the sample company from the welcome screen, then dresses it as an ordinary business.</summary>
    private static async Task OpenSampleCompanyAsync(Window window)
    {
        var explore = Ui.CentreOf(window, Ui.Text(window, "Explore Sample Company"));
        window.MouseMove(explore);
        window.MouseDown(explore, MouseButton.Left);
        window.MouseUp(explore, MouseButton.Left);

        for (var i = 0; i < 600 && App.NavigationService?.CurrentPageName != PageNames.Dashboard; i++)
            await Task.Delay(100);
        // The sample's insights are worked out behind a loading screen once the dashboard is up.
        for (var i = 0; i < 600 && Ui.Shows(window, "Opening sample company..."); i++)
            await Task.Delay(100);
        await Task.Delay(1500);

        AppHooks.PresentAsBusiness(BusinessName);
        await Task.Delay(300);
        AppHooks.HideSampleNotices(window);
        await Task.Delay(300);
    }

    /// <summary>
    /// Scan Receipt from the dashboard, a receipt photo dropped in, the scan's result reviewed and
    /// saved as an expense, then the expense in the list.
    /// </summary>
    private static async Task ReceiptAsync(Window window, TakeOptions options)
    {
        var receipts = App.ReceiptsModalsViewModel!;
        var rec = new Recorder(window, Path.Combine(options.OutFolder, "receipt.mkv"), new Point(1340, 250));
        await rec.WarmUpAsync(4);

        rec.Mark("dashboard");
        await rec.HoldAsync(1.5);
        var scan = Ui.Text(window, "Scan Receipt");
        await rec.MoveToAsync(Ui.CentreOf(window, scan), 1.1);
        await rec.HoldAsync(0.25);
        rec.Mark("click_scan");
        await rec.ClickAsync();

        await rec.WaitForAsync(() => receipts.IsBulkDropZoneOpen, 20, "the drop zone");
        AppHooks.UseScanCache(options.ScanCache);
        await rec.HoldAsync(0.6);
        rec.Mark("drop_zone");
        await rec.HoldAsync(0.4);

        var zone = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "DropZoneBorder");
        var dropAt = Ui.CentreOf(window, zone) + new Point(0, 20);
        await rec.DragFileAsync(options.ReceiptImage, new Point(WindowSize.Width + 140, dropAt.Y + 160), dropAt, 1.4);
        rec.Mark("dropped");
        await rec.WaitForAsync(() => receipts.BulkItems.Count == 1, 10, "the receipt in the queue");
        await rec.HoldAsync(1.0);

        var scanAll = Ui.Text(window, "Scan All (1)");
        await rec.MoveToAsync(Ui.CentreOf(window, scanAll), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_scan_all");
        await rec.ClickAsync();

        await rec.WaitForAsync(() => receipts.IsScanReviewModalOpen, 10, "the scan window");
        rec.Mark("scanning");
        await rec.WaitForAsync(() => receipts.HasScanResult, 120, "the scan's result");
        rec.Mark("scan_done");
        await rec.WaitForAsync(() => !Ui.Shows(window, "AI is suggesting supplier..."), 60, "the supplier suggestion");
        rec.Mark("suggested");
        await rec.HoldAsync(1.2);

        // Over the figures it read, then down the line items, as someone checking them would.
        var tour = new JsonObject();
        foreach (var (label, key) in new[] { ("Total", "look_total"), ("Line Items", "look_items") })
        {
            var target = Ui.Text(window, label);
            var at = Ui.LeftOf(window, target, 0.5) + new Point(40, 30);
            tour[key] = new JsonArray(Math.Round(at.X * Recorder.Scale), Math.Round(at.Y * Recorder.Scale));
            await rec.MoveToAsync(at, 0.9);
            rec.Mark(key);
            await rec.HoldAsync(1.3);
        }

        // The receipt's items are new to the company, so the products the scan suggests are made in one go.
        var suggested = Ui.Text(window, "Create All Suggested Products");
        await rec.MoveToAsync(Ui.CentreOf(window, suggested), 0.8);
        await rec.HoldAsync(0.2);
        rec.Mark("click_products");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => !Ui.Shows(window, "Please select a product."), 10, "the products to be made");
        await rec.HoldAsync(1.0);

        // The supplier sits below the line items, and the store is new to the company, so its suggested supplier is made too.
        var makeSupplier = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Command == receipts.CreateSuggestedSupplierCommand && b.IsEffectivelyVisible);
        if (makeSupplier != null)
        {
            var footerTop = Ui.CentreOf(window, Ui.Text(window, "Create Expense")).Y - 40;
            await rec.MoveToAsync(Ui.CentreOf(window, Ui.Text(window, "Line Items")) + new Point(300, 160), 0.6);
            rec.Mark("scroll");
            for (var i = 0; i < 60 && Ui.CentreOf(window, makeSupplier).Y > footerTop - 180; i++)
            {
                window.MouseWheel(rec.Cursor, new Vector(0, -1));
                await rec.HoldAsync(0.07);
            }
            await rec.HoldAsync(0.5);
            await rec.MoveToAsync(Ui.CentreOf(window, makeSupplier), 0.7);
            await rec.HoldAsync(0.2);
            rec.Mark("click_supplier");
            await rec.ClickAsync();
            await rec.WaitForAsync(() => receipts.SelectedSupplier != null, 10, "the supplier to be made");
            await rec.HoldAsync(1.2);
        }

        var create = Ui.Text(window, "Create Expense");
        await rec.MoveToAsync(Ui.CentreOf(window, create), 0.9);
        await rec.HoldAsync(0.25);
        rec.Mark("click_create");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => !receipts.IsScanReviewModalOpen, 15, "the scan window to close");
        rec.Event("saved");
        rec.Mark("saved");
        await rec.HoldAsync(1.2);

        var expenses = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => Ui.TextOf(t) == "Expenses" && t.IsEffectivelyVisible && Ui.CentreOf(window, t).X < 300);
        await rec.MoveToAsync(Ui.LeftOf(window, expenses), 1.0);
        await rec.HoldAsync(0.2);
        rec.Mark("click_expenses");
        await rec.ClickAsync();
        await rec.WaitForAsync(() => App.NavigationService?.CurrentPageName == PageNames.Expenses, 10, "the expenses page");
        rec.Mark("expenses");
        await rec.HoldAsync(1.0);

        var row = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && Ui.TextOf(t).Contains("Brightline", StringComparison.OrdinalIgnoreCase));
        if (row != null)
        {
            var at = Ui.LeftOf(window, row, 0.4);
            tour["new_row"] = new JsonArray(Math.Round(at.X * Recorder.Scale), Math.Round(at.Y * Recorder.Scale));
            await rec.MoveToAsync(at + new Point(10, 18), 1.0);
        }
        rec.Mark("look_row");
        await rec.HoldAsync(2.5);
        rec.Mark("end");

        await rec.FinishAsync(new JsonObject { ["places"] = tour });
    }
}

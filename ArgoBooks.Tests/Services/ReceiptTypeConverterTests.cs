using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Switching a receipt between expense and revenue moves the linked transaction to the other side
/// of the books rather than relabelling the receipt, so the two must stay in step.
/// </summary>
public class ReceiptTypeConverterTests
{
    private static (CompanyData data, Receipt receipt) WithExpenseReceipt()
    {
        var data = new CompanyData();
        data.IdCounters.Expense = 7;

        var expense = new Expense
        {
            Id = "PUR-2026-00007",
            Date = new DateTime(2026, 3, 4),
            Description = "Coffee beans",
            SupplierId = "SUP-001",
            Amount = 80m,
            TaxAmount = 10m,
            Total = 90m,
            ReceiptId = "RCP-001",
            LineItems = [new LineItem { Description = "Beans", Quantity = 2, UnitPrice = 40m }]
        };
        data.Expenses.Add(expense);

        var receipt = new Receipt
        {
            Id = "RCP-001",
            TransactionId = expense.Id,
            TransactionType = "Expense",
            Supplier = "Roasters Ltd",
            Amount = 90m
        };
        data.Receipts.Add(receipt);

        return (data, receipt);
    }

    [Fact]
    public void Switch_ExpenseToRevenue_ReplacesTransactionAndRepointsReceipt()
    {
        var (data, receipt) = WithExpenseReceipt();

        ReceiptTypeConverter.Switch(data, receipt);

        Assert.Empty(data.Expenses);
        var revenue = Assert.Single(data.Revenues);
        Assert.Equal("REV-2026-00001", revenue.Id);
        Assert.Equal("Revenue", receipt.TransactionType);
        Assert.Equal(revenue.Id, receipt.TransactionId);
        Assert.Equal(receipt.Id, revenue.ReceiptId);
    }

    private static (CompanyData data, Receipt receipt) WithRevenueReceipt()
    {
        var data = new CompanyData();
        var revenue = new Revenue
        {
            Id = "REV-2026-00003",
            Date = new DateTime(2026, 5, 9),
            Description = "Market stall",
            Amount = 200m,
            Subtotal = 200m,
            TaxAmount = 20m,
            Total = 220m,
            ReceiptId = "RCP-002"
        };
        data.Revenues.Add(revenue);

        var receipt = new Receipt
        {
            Id = "RCP-002",
            TransactionId = revenue.Id,
            TransactionType = "Revenue",
            Supplier = "Market Co",
            Amount = 220m
        };
        data.Receipts.Add(receipt);
        return (data, receipt);
    }

    [Fact]
    public void Switch_RevenueToExpense_MatchesExistingSupplierByName()
    {
        var (data, receipt) = WithRevenueReceipt();

        data.Suppliers.Add(new Supplier { Id = "SUP-042", Name = "market co" });

        ReceiptTypeConverter.Switch(data, receipt);

        Assert.Empty(data.Revenues);
        var expense = Assert.Single(data.Expenses);
        Assert.Equal("SUP-042", expense.SupplierId);
        Assert.Equal("Expense", receipt.TransactionType);
        Assert.Equal(expense.Id, receipt.TransactionId);
    }

    [Fact]
    public void Switch_CarriesOverAmountsAndLineItems()
    {
        var (data, receipt) = WithExpenseReceipt();

        ReceiptTypeConverter.Switch(data, receipt);

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal(80m, revenue.Amount);
        Assert.Equal(10m, revenue.TaxAmount);
        Assert.Equal(90m, revenue.Total);
        Assert.Equal(new DateTime(2026, 3, 4), revenue.Date);
        Assert.Equal("Coffee beans", revenue.Description);
        Assert.Equal("Beans", Assert.Single(revenue.LineItems).Description);
    }

    [Fact]
    public void GetBlockReason_UnlinkedReceipt_IsBlocked()
    {
        var (data, receipt) = WithExpenseReceipt();
        receipt.TransactionId = string.Empty;

        Assert.Equal(ReceiptSwitchBlock.NoTransaction, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void GetBlockReason_RevenueWithPayment_IsBlocked()
    {
        var (data, receipt) = WithRevenueReceipt();
        data.Payments.Add(new Payment { Id = "PAY-001", RevenueId = receipt.TransactionId, Amount = 220m });

        Assert.Equal(ReceiptSwitchBlock.HasPayments, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void GetBlockReason_RevenueFromInvoice_IsBlocked()
    {
        var (data, receipt) = WithRevenueReceipt();
        data.Revenues[0].InvoiceId = "INV-2026-00001";

        Assert.Equal(ReceiptSwitchBlock.FromInvoice, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void GetBlockReason_TransactionWithReturn_IsBlocked()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Returns.Add(new Return { Id = "RET-001", OriginalTransactionId = receipt.TransactionId });

        Assert.Equal(ReceiptSwitchBlock.HasReturns, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void GetBlockReason_ExpenseUsedByPayRun_IsBlocked()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.PayRuns.Add(new ArgoBooks.Core.Models.Payroll.PayRun
        {
            Lines = [new ArgoBooks.Core.Models.Payroll.PayRunLine { ExpenseId = receipt.TransactionId }]
        });

        Assert.Equal(ReceiptSwitchBlock.UsedByPayRun, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void GetBlockReason_PlainScannedReceipt_IsAllowed()
    {
        var (data, receipt) = WithExpenseReceipt();

        Assert.Equal(ReceiptSwitchBlock.None, ReceiptTypeConverter.GetBlockReason(data, receipt));
    }

    [Fact]
    public void Revert_RestoresTheOriginalTransactionAndReceipt()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        Assert.Empty(data.Revenues);
        var expense = Assert.Single(data.Expenses);
        Assert.Equal("PUR-2026-00007", expense.Id);
        Assert.Equal("SUP-001", expense.SupplierId);
        Assert.Equal("Expense", receipt.TransactionType);
        Assert.Equal("PUR-2026-00007", receipt.TransactionId);
        Assert.Equal(receipt.Id, expense.ReceiptId);
    }

    [Fact]
    public void Switch_RevenueToExpense_CreatesSupplierWhenNoMatch()
    {
        var (data, receipt) = WithRevenueReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);

        var supplier = Assert.Single(data.Suppliers);
        Assert.Equal("Market Co", supplier.Name);
        Assert.Equal(supplier.Id, Assert.Single(data.Expenses).SupplierId);
        Assert.Equal(supplier.Id, result.CreatedSupplier?.Id);
    }

    [Fact]
    public void Revert_RemovesTheSupplierTheSwitchCreated()
    {
        var (data, receipt) = WithRevenueReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        Assert.Empty(data.Suppliers);
    }

    [Fact]
    public void Revert_KeepsASupplierItDidNotCreate()
    {
        var (data, receipt) = WithRevenueReceipt();
        data.Suppliers.Add(new Supplier { Id = "SUP-042", Name = "Market Co" });

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        Assert.Single(data.Suppliers);
        Assert.Null(result.CreatedSupplier);
    }

    /// <summary>
    /// The switch files new products under a category of the target side, and reuses one the
    /// company already has rather than minting a second. Reverting must not take that one with it:
    /// every product and transaction already pointing at it would be orphaned.
    /// </summary>
    [Fact]
    public void Revert_KeepsACategoryItDidNotCreate()
    {
        var (data, receipt) = WithExpenseReceipt();
        var existing = new Category { Id = "CAT-SAL-001", Name = "Consulting", Type = CategoryType.Revenue };
        data.Categories.Add(existing);
        data.Products.Add(new Product
        {
            Id = "PRD-900", Name = "Advice", Type = CategoryType.Revenue, CategoryId = existing.Id
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        Assert.Null(result.CreatedCategory);
        Assert.Contains(data.Categories, c => c.Id == existing.Id);
    }

    /// <summary>
    /// A transaction entered without its date's rate carries the pending flag and a queued
    /// conversion. The switch copies the flag onto the replacement, so the queue has to follow
    /// or the rate arrives, finds nothing, and leaves the replacement reporting zero in USD
    /// for good.
    /// </summary>
    [Fact]
    public void Switch_MovesAQueuedConversionOntoTheNewTransaction()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Expenses[0].IsPendingConversion = true;
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "PUR-2026-00007", TransactionType = "Expense", OriginalCurrency = "CAD"
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);

        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal(result.Created.Id, entry.TransactionId);
        Assert.Equal("Revenue", entry.TransactionType);
    }

    [Fact]
    public void Revert_PutsAQueuedConversionBackOnTheOriginal()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Expenses[0].IsPendingConversion = true;
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "PUR-2026-00007", TransactionType = "Expense", OriginalCurrency = "CAD"
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal("PUR-2026-00007", entry.TransactionId);
        Assert.Equal("Expense", entry.TransactionType);
    }

    [Fact]
    public void Reapply_MovesAQueuedConversionForwardAgain()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Expenses[0].IsPendingConversion = true;
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "PUR-2026-00007", TransactionType = "Expense", OriginalCurrency = "CAD"
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);
        ReceiptTypeConverter.Reapply(data, receipt, result);

        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal(result.Created.Id, entry.TransactionId);
        Assert.Equal("Revenue", entry.TransactionType);
    }

    // Once a conversion pass had converted the new transaction and taken its entry off the queue,
    // undo pointed the entry back at the original without queuing it, and the original, which was
    // never converted, stayed pending for good.
    [Fact]
    public void Revert_AfterTheNewTransactionConverted_QueuesTheOriginalAgain()
    {
        var (data, receipt) = WithExpenseReceipt();
        var original = data.Expenses[0];
        original.IsPendingConversion = true;
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = original.Id, TransactionType = "Expense", OriginalCurrency = "CAD", Total = 90m
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);
        result.Created.IsPendingConversion = false;
        data.PendingConversions.Clear();

        ReceiptTypeConverter.Revert(data, receipt, result);

        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal((original.Id, "Expense"), (entry.TransactionId, entry.TransactionType));

        ReceiptTypeConverter.Reapply(data, receipt, result);

        Assert.Empty(data.PendingConversions);
    }

    /// <summary>A switch with nothing queued must not invent an entry.</summary>
    [Fact]
    public void Switch_LeavesTheQueueAloneWhenNothingIsPending()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);

        Assert.Null(result.MovedConversion);
        Assert.Empty(data.PendingConversions);
    }

    /// <summary>The reused category is still what the new products are filed under.</summary>
    [Fact]
    public void Switch_FilesNewProductsUnderTheExistingCategory()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Categories.Add(new Category
        {
            Id = "CAT-SAL-001", Name = "Consulting", Type = CategoryType.Revenue
        });

        var result = ReceiptTypeConverter.Switch(data, receipt);

        Assert.Equal("CAT-SAL-001", Assert.Single(result.CreatedProducts).CategoryId);
    }

    [Fact]
    public void Reapply_PutsTheSwitchBackAfterARevert()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);
        ReceiptTypeConverter.Reapply(data, receipt, result);

        Assert.Empty(data.Expenses);
        var revenue = Assert.Single(data.Revenues);
        Assert.Equal(result.Created.Id, revenue.Id);
        Assert.Equal("Revenue", receipt.TransactionType);
        Assert.Equal(revenue.Id, receipt.TransactionId);
    }

    [Fact]
    public void Reapply_RestoresASupplierTheSwitchHadCreated()
    {
        var (data, receipt) = WithRevenueReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);
        ReceiptTypeConverter.Reapply(data, receipt, result);

        Assert.Equal(result.CreatedSupplier?.Id, Assert.Single(data.Suppliers).Id);
    }

    [Fact]
    public void Switch_ExpenseToRevenue_CreatesCustomerFromTheReceiptName()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);

        var customer = Assert.Single(data.Customers);
        Assert.Equal("Roasters Ltd", customer.Name);
        Assert.Equal(customer.Id, Assert.Single(data.Revenues).CustomerId);
        Assert.Same(customer, result.CreatedCustomer);
    }

    [Fact]
    public void Switch_ExpenseToRevenue_ReusesAnExistingCustomerByName()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Customers.Add(new Customer { Id = "CUS-009", Name = "roasters ltd" });

        var result = ReceiptTypeConverter.Switch(data, receipt);

        Assert.Single(data.Customers);
        Assert.Equal("CUS-009", Assert.Single(data.Revenues).CustomerId);
        Assert.Null(result.CreatedCustomer);
    }

    [Fact]
    public void Switch_ExpenseToRevenue_CreatesRevenueProductsForTheLineItems()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);

        var product = Assert.Single(data.Products);
        Assert.Equal("Beans", product.Name);
        Assert.Equal(CategoryType.Revenue, product.Type);
        Assert.Equal(40m, product.UnitPrice);
        Assert.Equal(product.Id, Assert.Single(Assert.Single(data.Revenues).LineItems).ProductId);
        Assert.Same(product, Assert.Single(result.CreatedProducts));
    }

    [Fact]
    public void Switch_ReusesAnExistingProductOfTheTargetType()
    {
        var (data, receipt) = WithExpenseReceipt();
        data.Products.Add(new Product { Id = "PRD-050", Name = "beans", Type = CategoryType.Revenue });

        var result = ReceiptTypeConverter.Switch(data, receipt);

        Assert.Single(data.Products);
        Assert.Equal("PRD-050", Assert.Single(Assert.Single(data.Revenues).LineItems).ProductId);
        Assert.Empty(result.CreatedProducts);
    }

    [Fact]
    public void Switch_LeavesTheOriginalLineItemsAlone()
    {
        var (data, receipt) = WithExpenseReceipt();
        var original = data.Expenses[0].LineItems[0];

        ReceiptTypeConverter.Switch(data, receipt);

        Assert.Null(original.ProductId);
    }

    [Fact]
    public void Revert_RemovesTheCustomerAndProductsTheSwitchCreated()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);

        Assert.Empty(data.Customers);
        Assert.Empty(data.Products);
        Assert.Empty(data.Categories);
    }

    [Fact]
    public void Reapply_RestoresTheCustomerAndProductsTheSwitchCreated()
    {
        var (data, receipt) = WithExpenseReceipt();

        var result = ReceiptTypeConverter.Switch(data, receipt);
        ReceiptTypeConverter.Revert(data, receipt, result);
        ReceiptTypeConverter.Reapply(data, receipt, result);

        Assert.Single(data.Customers);
        Assert.Single(data.Products);
    }
}

using System.Collections.Concurrent;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Defines a single column in the import schema.
/// </summary>
/// <param name="Name">Column name as expected by the importer.</param>
/// <param name="Type">Data type description (string, decimal, int, datetime, enum:Value1,Value2).</param>
/// <param name="Description">Human-readable description for the LLM.</param>
/// <param name="Required">Whether this column is required for import.</param>
/// <param name="JsonName">JSON property name for Tier 2 LLM output (must match C# model's JsonPropertyName).</param>
public record SchemaColumn(string Name, string Type, string Description, bool Required = false, string? JsonName = null);

/// <summary>
/// Defines the expected column schema for each entity type that can be imported.
/// Used to build LLM prompts so the AI knows what columns to map to.
/// Derived from the GetString/GetDecimal/etc. calls in SpreadsheetImportService.Import* methods.
/// </summary>
public static class ImportSchemaDefinition
{
    // Cache per country key (null key = default). Thread-safe for concurrent import operations.
    private static readonly ConcurrentDictionary<string, Dictionary<SpreadsheetSheetType, List<SchemaColumn>>> SchemaCache = new();

    /// <summary>
    /// Returns country-specific labels for address fields.
    /// </summary>
    public static (string StateLabel, string StateDescription, string PostalCodeLabel, string PostalCodeDescription) GetAddressLabels(string? country)
    {
        var normalized = (country ?? "").Trim().ToUpperInvariant();

        return normalized switch
        {
            "UNITED STATES" or "US" or "USA" =>
                ("State", "State", "ZIP Code", "ZIP code"),
            "CANADA" or "CA" =>
                ("Province", "Province", "Postal Code", "Postal code"),
            "UNITED KINGDOM" or "UK" or "GB" or "GREAT BRITAIN" =>
                ("County", "County", "Postcode", "Postcode"),
            "AUSTRALIA" or "AU" =>
                ("State", "State/territory", "Postcode", "Postcode"),
            "GERMANY" or "DE" or "DEUTSCHLAND" =>
                ("State", "Bundesland", "Postal Code", "Postleitzahl"),
            "FRANCE" or "FR" =>
                ("Region", "Région", "Postal Code", "Code postal"),
            "JAPAN" or "JP" =>
                ("Prefecture", "Prefecture", "Postal Code", "Postal code"),
            "CHINA" or "CN" =>
                ("Province", "Province", "Postal Code", "Postal code"),
            "ITALY" or "IT" =>
                ("Province", "Provincia", "Postal Code", "CAP"),
            "BRAZIL" or "BR" =>
                ("State", "Estado", "Postal Code", "CEP"),
            "INDIA" or "IN" =>
                ("State", "State", "PIN Code", "PIN code"),
            "MEXICO" or "MX" =>
                ("State", "Estado", "Postal Code", "Código postal"),
            _ =>
                ("State/Province", "State or province", "Postal Code", "Postal code"),
        };
    }

    /// <summary>
    /// Gets the complete import schema for all entity types.
    /// </summary>
    public static Dictionary<SpreadsheetSheetType, List<SchemaColumn>> GetSchema(string? country = null)
    {
        var key = (country ?? "").Trim().ToUpperInvariant();
        return SchemaCache.GetOrAdd(key, _ => BuildSchema(country));
    }

    /// <summary>
    /// Gets the schema for a specific entity type.
    /// </summary>
    public static List<SchemaColumn>? GetSchemaForType(SpreadsheetSheetType type, string? country = null)
    {
        return GetSchema(country).GetValueOrDefault(type);
    }

    /// <summary>
    /// Formats the schema as a readable string for LLM prompts.
    /// </summary>
    public static string FormatSchemaForPrompt(string? country = null)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (type, columns) in GetSchema(country))
        {
            sb.AppendLine($"### {type}");
            sb.AppendLine("| Column | Type | Required | Description |");
            sb.AppendLine("|--------|------|----------|-------------|");
            foreach (var col in columns)
            {
                var req = col.Required ? "Yes" : "No";
                sb.AppendLine($"| {col.Name} | {col.Type} | {req} | {col.Description} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static Dictionary<SpreadsheetSheetType, List<SchemaColumn>> BuildSchema(string? country = null)
    {
        var (stateLabel, stateDesc, postalLabel, postalDesc) = GetAddressLabels(country);

        return new Dictionary<SpreadsheetSheetType, List<SchemaColumn>>
        {
            [SpreadsheetSheetType.Customers] =
            [
                new("ID", "string", "Unique identifier (e.g., CUS-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Customer name", Required: true, JsonName: "name"),
                new("Company", "string", "Customer's company name", JsonName: "companyName"),
                new("Email", "string", "Email address", JsonName: "email"),
                new("Phone", "string", "Contact phone number", JsonName: "phone"),
                new("Street", "string", "Mailing street address", JsonName: "address.street"),
                new("City", "string", "City", JsonName: "address.city"),
                new(stateLabel, "string", stateDesc, JsonName: "address.state"),
                new(postalLabel, "string", postalDesc, JsonName: "address.zipCode"),
                new("Country", "string", "Country", JsonName: "address.country"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
                new("Status", "enum:Active,Inactive", "Active or inactive customer", JsonName: "status"),
                new("Total Purchases", "decimal", "Total purchase amount", JsonName: "totalPurchases"),
            ],

            [SpreadsheetSheetType.Suppliers] =
            [
                new("ID", "string", "Unique identifier (e.g., SUP-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Name of the supplier", Required: true, JsonName: "name"),
                new("Email", "string", "Email address", JsonName: "email"),
                new("Phone", "string", "Contact phone number", JsonName: "phone"),
                new("Website", "string", "Website URL", JsonName: "website"),
                new("Street", "string", "Mailing street address", JsonName: "address.street"),
                new("City", "string", "City", JsonName: "address.city"),
                new(stateLabel, "string", stateDesc, JsonName: "address.state"),
                new(postalLabel, "string", postalDesc, JsonName: "address.zipCode"),
                new("Country", "string", "Country", JsonName: "address.country"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
            ],

            [SpreadsheetSheetType.Products] =
            [
                new("ID", "string", "Unique identifier (e.g., PRD-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Name of the product or service", Required: true, JsonName: "name"),
                new("Type", "enum:Revenue,Expenses,Rental", "Product category type", JsonName: "type"),
                new("Item Type", "enum:Product,Service", "Whether this is a product or service", JsonName: "itemType"),
                new("SKU", "string", "Stock keeping unit code", JsonName: "sku"),
                new("Description", "string", "Product description", JsonName: "description"),
                new("Category ID", "string", "Category identifier", JsonName: "categoryId"),
                new("Category Name", "string", "Name of the category the product is in", JsonName: "categoryName"),
                new("Supplier ID", "string", "Supplier identifier", JsonName: "supplierId"),
                new("Supplier Name", "string", "Name of the supplier (alternative to ID)"),
                new("Reorder Point", "decimal", "Stock level that triggers reorder", JsonName: "reorderPoint"),
                new("Overstock Threshold", "decimal", "Stock level considered overstock", JsonName: "overstockThreshold"),
            ],

            [SpreadsheetSheetType.Categories] =
            [
                new("ID", "string", "Unique identifier (e.g., CAT-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Name of the category", Required: true, JsonName: "name"),
                new("Type", "enum:Revenue,Expenses,Rental", "Category type", JsonName: "type"),
                new("Parent ID", "string", "Parent category ID for subcategories", JsonName: "parentId"),
                new("Description", "string", "Category description", JsonName: "description"),
                new("Icon", "string", "Emoji icon for the category", JsonName: "icon"),
            ],

            [SpreadsheetSheetType.Invoices] =
            [
                // Two different values. ID is what payments and line items point at; Invoice # is
                // what the customer sees on the paperwork, and in this app it is the id with a
                // hash in front. A sheet that only has Invoice # still works: the importer falls
                // back to it for the id.
                new("ID", "string", "Unique identifier (e.g., INV-2024-00001)", JsonName: "id"),
                new("Invoice #", "string", "Invoice number shown on the invoice (e.g., #INV-2024-00001). Used as the identifier when there is no ID column", Required: true, JsonName: "invoiceNumber"),
                new("Customer ID", "string", "Customer identifier", Required: true, JsonName: "customerId"),
                new("Issue Date", "datetime", "Date invoice was issued", JsonName: "issueDate"),
                new("Due Date", "datetime", "Payment due date", JsonName: "dueDate"),
                new("Subtotal", "decimal", "Amount before tax", JsonName: "subtotal"),
                new("Tax", "decimal", "Tax amount", JsonName: "taxAmount"),
                new("Total", "decimal", "Total amount due", JsonName: "total"),
                new("Paid", "decimal", "Amount already paid", JsonName: "amountPaid"),
                new("Balance", "decimal", "Remaining balance", JsonName: "balance"),
                new("Status", "enum:Draft,Sent,Paid,Overdue,Cancelled", "Invoice status", JsonName: "status"),
                new("Currency", "string", "ISO currency code the amounts are in (e.g., USD, EUR, GBP). Map when the sheet has a per-row currency column, OR when an amount cell itself contains a currency symbol or code (e.g. '£100', '$10 CAD'): output the ISO code, or the raw symbol if the code is unclear. Leave unmapped if all amounts are plainly in the company currency", JsonName: "originalCurrency"),
            ],

            [SpreadsheetSheetType.Expenses] =
            [
                new("ID", "string", "Unique identifier (e.g., PUR-001)", Required: true, JsonName: "id"),
                new("Date", "datetime", "Transaction date", Required: true, JsonName: "date"),
                new("Supplier ID", "string", "Supplier identifier", JsonName: "supplierId"),
                new("Product", "string", "Product or description of expense", JsonName: "description"),
                new("Description", "string", "Description (alternative to Product)", JsonName: "description"),
                new("Quantity", "decimal", "Number of units. Map a separate quantity/qty column when present; leave unmapped (defaults to 1) when each row is a single line amount", JsonName: "quantity"),
                new("Unit Price", "decimal", "Price per unit before tax. When there is no quantity column, this is the row's amount before tax", JsonName: "unitPrice"),
                new("Tax", "decimal", "Tax amount", JsonName: "taxAmount"),
                new("Total", "decimal", "Total amount including tax", JsonName: "total"),
                new("Reference", "string", "External reference number", JsonName: "referenceNumber"),
                new("Payment Method", "enum:Cash,CreditCard,DebitCard,BankTransfer,Check,PayPal,Other", "How payment was made", JsonName: "paymentMethod"),
                new("Shipping", "decimal", "Cost of shipping", JsonName: "shippingCost"),
                new("Currency", "string", "ISO currency code the amounts are in (e.g., USD, EUR, GBP). Map when the sheet has a per-row currency column, OR when an amount cell itself contains a currency symbol or code (e.g. '£100', '$10 CAD'): output the ISO code, or the raw symbol if the code is unclear. Leave unmapped if all amounts are plainly in the company currency", JsonName: "originalCurrency"),
            ],

            [SpreadsheetSheetType.Revenue] =
            [
                new("ID", "string", "Unique identifier (e.g., SAL-001)", Required: true, JsonName: "id"),
                new("Date", "datetime", "Transaction date", Required: true, JsonName: "date"),
                new("Customer ID", "string", "Customer identifier", JsonName: "customerId"),
                new("Invoice ID", "string", "Invoice this revenue was collected on, if any", JsonName: "invoiceId"),
                new("Product", "string", "Product or description of sale", JsonName: "description"),
                new("Description", "string", "Description (alternative to Product)", JsonName: "description"),
                new("Quantity", "decimal", "Number of units. Map a separate quantity/qty column when present; leave unmapped (defaults to 1) when each row is a single line amount", JsonName: "quantity"),
                new("Unit Price", "decimal", "Price per unit before tax. When there is no quantity column, this is the row's amount before tax", JsonName: "unitPrice"),
                new("Tax", "decimal", "Tax amount", JsonName: "taxAmount"),
                new("Total", "decimal", "Total amount including tax", JsonName: "total"),
                new("Reference", "string", "External reference number", JsonName: "referenceNumber"),
                new("Payment Status", "enum:Paid,Unpaid,Partial,Pending,Overdue", "Status of the payment", JsonName: "paymentStatus"),
                new("Shipping", "decimal", "Cost of shipping", JsonName: "shippingCost"),
                new("Kept Deposit", "bool", "True when the row is a rental security deposit the business kept", JsonName: "isKeptDeposit"),
                new("Currency", "string", "ISO currency code the amounts are in (e.g., USD, EUR, GBP). Map when the sheet has a per-row currency column, OR when an amount cell itself contains a currency symbol or code (e.g. '£100', '$10 CAD'): output the ISO code, or the raw symbol if the code is unclear. Leave unmapped if all amounts are plainly in the company currency", JsonName: "originalCurrency"),
            ],

            [SpreadsheetSheetType.Inventory] =
            [
                new("ID", "string", "Unique identifier (e.g., INV-ITM-001)", Required: true, JsonName: "id"),
                new("Product ID", "string", "Associated product identifier", Required: true, JsonName: "productId"),
                new("Location ID", "string", "Storage location identifier", JsonName: "locationId"),
                new("In Stock", "decimal", "Current stock quantity", JsonName: "inStock"),
                new("Reserved", "decimal", "Reserved/allocated quantity", JsonName: "reserved"),
                new("Reorder Point", "decimal", "Stock level that triggers reorder", JsonName: "reorderPoint"),
                new("Unit Cost", "decimal", "Cost per unit", JsonName: "unitCost"),
                new("Last Updated", "datetime", "When stock was last counted", JsonName: "lastUpdated"),
            ],

            [SpreadsheetSheetType.Payments] =
            [
                new("ID", "string", "Unique identifier (e.g., PAY-001)", Required: true, JsonName: "id"),
                new("Invoice ID", "string", "Associated invoice identifier", JsonName: "invoiceId"),
                new("Customer ID", "string", "Customer identifier", JsonName: "customerId"),
                new("Date", "datetime", "Payment date", JsonName: "date"),
                new("Amount", "decimal", "Payment amount", JsonName: "amount"),
                new("Payment Method", "enum:Cash,CreditCard,DebitCard,BankTransfer,Check,PayPal,Other", "How payment was made", JsonName: "paymentMethod"),
                new("Reference", "string", "Payment reference number", JsonName: "referenceNumber"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
                new("Currency", "string", "ISO currency code the amount is in (e.g., USD, EUR, GBP). Map when the sheet has a per-row currency column, OR when the amount cell itself contains a currency symbol or code (e.g. '£100', '$10 CAD'): output the ISO code, or the raw symbol if the code is unclear. Leave unmapped if all amounts are plainly in the company currency", JsonName: "originalCurrency"),
            ],

            [SpreadsheetSheetType.Locations] =
            [
                new("ID", "string", "Unique identifier (e.g., LOC-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Name of the storage location", Required: true, JsonName: "name"),
                new("Contact Person", "string", "Contact person at location", JsonName: "contactPerson"),
                new("Phone", "string", "Contact phone number", JsonName: "phone"),
                new("Street", "string", "Mailing street address", JsonName: "address.street"),
                new("City", "string", "City", JsonName: "address.city"),
                new(stateLabel, "string", stateDesc, JsonName: "address.state"),
                new(postalLabel, "string", postalDesc, JsonName: "address.zipCode"),
                new("Country", "string", "Country", JsonName: "address.country"),
                new("Capacity", "int", "Storage capacity", JsonName: "capacity"),
                new("Utilization", "int", "Current utilization", JsonName: "currentUtilization"),
            ],

            [SpreadsheetSheetType.RentalInventory] =
            [
                new("ID", "string", "Unique identifier (e.g., RNT-ITM-001)", Required: true, JsonName: "id"),
                new("Inventory Item ID", "string", "Linked inventory item identifier", Required: true, JsonName: "inventoryItemId"),
                new("Daily Rate", "decimal", "Daily rental rate", JsonName: "dailyRate"),
                new("Weekly Rate", "decimal", "Weekly rental rate", JsonName: "weeklyRate"),
                new("Monthly Rate", "decimal", "Monthly rental rate", JsonName: "monthlyRate"),
                new("Deposit", "decimal", "Security deposit required", JsonName: "securityDeposit"),
                new("Status", "enum:Active,Inactive", "Item status", JsonName: "status"),
            ],

            [SpreadsheetSheetType.RentalRecords] =
            [
                new("ID", "string", "Unique identifier (e.g., RNT-001)", Required: true, JsonName: "id"),
                new("Customer ID", "string", "Customer identifier", Required: true, JsonName: "customerId"),
                new("Rental Item ID", "string", "Rental inventory item ID", JsonName: "rentalItemId"),
                new("Start Date", "datetime", "Rental start date", JsonName: "startDate"),
                new("Due Date", "datetime", "Expected return date", JsonName: "dueDate"),
                new("Return Date", "datetime", "Actual return date (if returned)", JsonName: "returnDate"),
                new("Quantity", "int", "Quantity rented", JsonName: "quantity"),
                new("Rate Type", "enum:Daily,Weekly,Monthly", "Rental rate type", JsonName: "rateType"),
                new("Rate Amount", "decimal", "Rate amount per period", JsonName: "rateAmount"),
                new("Security Deposit", "decimal", "Security deposit amount", JsonName: "securityDeposit"),
                new("Total Cost", "decimal", "Total cost of the rental", JsonName: "totalCost"),
                new("Status", "enum:Reserved,Active,Returned,Overdue,Cancelled", "Rental status", JsonName: "status"),
                new("Paid", "enum:Yes,No", "Whether the rental has been paid", JsonName: "paid"),
            ],

            [SpreadsheetSheetType.RecurringInvoices] =
            [
                new("ID", "string", "Unique identifier (e.g., REC-INV-001)", Required: true, JsonName: "id"),
                new("Customer ID", "string", "Customer identifier", Required: true, JsonName: "customerId"),
                new("Amount", "decimal", "Invoice amount", JsonName: "amount"),
                new("Description", "string", "Invoice description", JsonName: "description"),
                new("Frequency", "enum:Weekly,BiWeekly,Monthly,Quarterly,Annually", "Billing frequency", JsonName: "frequency"),
                new("Next Date", "datetime", "Next invoice date", JsonName: "nextInvoiceDate"),
                new("Status", "enum:Active,Paused,Cancelled", "Recurring invoice status", JsonName: "status"),
            ],

            [SpreadsheetSheetType.StockAdjustments] =
            [
                new("ID", "string", "Unique identifier (e.g., ADJ-001)", Required: true, JsonName: "id"),
                new("Inventory Item ID", "string", "Inventory item identifier", Required: true, JsonName: "inventoryItemId"),
                new("Type", "enum:Set,Add,Remove", "Type of stock adjustment", JsonName: "adjustmentType"),
                new("Quantity", "decimal", "Adjustment quantity", JsonName: "quantity"),
                new("Previous Stock", "decimal", "Stock before adjustment", JsonName: "previousStock"),
                new("New Stock", "decimal", "Stock after adjustment", JsonName: "newStock"),
                new("Reason", "string", "Reason for adjustment", JsonName: "reason"),
                new("Timestamp", "datetime", "When adjustment was made", JsonName: "timestamp"),
            ],

            [SpreadsheetSheetType.PurchaseOrders] =
            [
                new("ID", "string", "Unique identifier (e.g., PO-001)", Required: true, JsonName: "id"),
                new("Supplier ID", "string", "Supplier identifier", Required: true, JsonName: "supplierId"),
                new("Order Date", "datetime", "Date order was placed", JsonName: "orderDate"),
                new("Expected Date", "datetime", "Expected delivery date", JsonName: "expectedDeliveryDate"),
                new("Total", "decimal", "Order total", JsonName: "total"),
                new("Status", "enum:Draft,Submitted,Approved,Received,Cancelled", "Order status", JsonName: "status"),
                new("Currency", "string", "ISO currency code the amounts are in (e.g., USD, EUR, GBP). Map when the sheet has a per-row currency column, OR when an amount cell itself contains a currency symbol or code (e.g. '£100', '$10 CAD'): output the ISO code, or the raw symbol if the code is unclear. Leave unmapped if all amounts are plainly in the company currency", JsonName: "originalCurrency"),
            ],

            [SpreadsheetSheetType.InvoiceLineItems] =
            [
                new("Invoice ID", "string", "Identifier of the invoice these lines belong to", Required: true, JsonName: "invoiceId"),
                new("Product ID", "string", "Product identifier, if the line is linked to a product", JsonName: "productId"),
                new("Description", "string", "What the line is for", JsonName: "description"),
                new("Quantity", "decimal", "Number of units", JsonName: "quantity"),
                new("Unit Price", "decimal", "Price per unit before tax and discount", JsonName: "unitPrice"),
                new("Tax Rate", "decimal", "Tax rate as a decimal (e.g., 0.08 for 8%)", JsonName: "taxRate"),
                new("Discount", "decimal", "Discount applied to this line", JsonName: "discount"),
                // Amount is quantity x price less discount, plus tax. It is exported so the sheet
                // reads on its own and is deliberately not imported: the line is rebuilt from its
                // parts, so a stale total in the file cannot contradict them.
            ],

            [SpreadsheetSheetType.PurchaseOrderLineItems] =
            [
                new("PO ID", "string", "Purchase order identifier", Required: true, JsonName: "poId"),
                new("Product ID", "string", "Product identifier", Required: true, JsonName: "productId"),
                new("Quantity", "decimal", "Ordered quantity", JsonName: "quantity"),
                new("Unit Cost", "decimal", "Cost per unit", JsonName: "unitCost"),
                new("Quantity Received", "decimal", "Quantity received so far", JsonName: "quantityReceived"),
            ],

            [SpreadsheetSheetType.Employees] =
            [
                new("ID", "string", "Unique identifier (e.g., EMP-001)", Required: true, JsonName: "id"),
                new("Name", "string", "Full name, as it should appear on the T4", Required: true, JsonName: "name"),
                new("Employee #", "string", "Optional payroll number", JsonName: "employeeNumber"),
                new("SIN", "string", "Social insurance number, digits only", JsonName: "sin"),
                new("Province of Employment", "string", "Two letter code for where the employee WORKS, which decides the tax table and is not necessarily where they live", JsonName: "province"),
                new("Pay Type", "enum:Salary,Hourly", "Whether the pay rate is an annual salary or an hourly rate", JsonName: "payType"),
                new("Pay Rate", "decimal", "Annual salary, or the hourly rate, depending on Pay Type", JsonName: "payRate"),
                new("Pay Frequency", "enum:Weekly,Biweekly,SemiMonthly,Monthly", "How often the employee is paid", JsonName: "payFrequency"),
                new("Standard Hours Per Week", "decimal", "Contract hours in a normal week, for salaried staff only. Leave blank when unknown rather than entering zero", JsonName: "standardHoursPerWeek"),
                new("Federal Claim Amount", "decimal", "Total claim amount from the federal TD1. Zero means none was filed", JsonName: "federalClaimAmount"),
                new("Provincial Claim Amount", "decimal", "Total claim amount from the provincial or territorial TD1", JsonName: "provincialClaimAmount"),
                new("Federal Claims Zero", "bool", "The federal TD1 claims nothing, as for a second job. Not the same as no TD1 on file", JsonName: "federalClaimIsZero"),
                new("Provincial Claims Zero", "bool", "The provincial or territorial TD1 claims nothing. Not the same as no TD1 on file", JsonName: "provincialClaimIsZero"),
                new("CPP Exempt", "bool", "Under 18, over 70, or already drawing a CPP retirement pension", JsonName: "isCppExempt"),
                new("EI Exempt", "bool", "Typically an owner controlling more than 40% of the voting shares", JsonName: "isEiExempt"),
                new("Dental Benefit", "enum:NotEligible,PayeeOnly,PayeeAndSpouse,PayeeAndChildren,PayeeSpouseAndChildren", "Box 45 on the T4: what dental coverage the employer offered", JsonName: "dentalBenefit"),
                new("Start Date", "datetime", "First day worked", JsonName: "startDate"),
                new("End Date", "datetime", "Last day worked, if they have left", JsonName: "endDate"),
                // The home address, which the T4 is posted to. Not the same as Province of
                // Employment above: someone can live in one province and work in another.
                new("Street", "string", "Home address street", JsonName: "address.street"),
                new("City", "string", "City", JsonName: "address.city"),
                new(stateLabel, "string", stateDesc, JsonName: "address.state"),
                new(postalLabel, "string", postalDesc, JsonName: "address.zipCode"),
                new("Country", "string", "Country", JsonName: "address.country"),
                new("Status", "enum:Active,Archived", "Archived employees stay in the file but are hidden from pay runs"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
            ],

            [SpreadsheetSheetType.Returns] =
            [
                new("ID", "string", "Unique identifier (e.g., RET-001)", Required: true, JsonName: "id"),
                new("Original Transaction ID", "string", "ID of the original transaction", JsonName: "originalTransactionId"),
                new("Return Type", "enum:Customer,Supplier", "Type of return", JsonName: "returnType"),
                new("Customer ID", "string", "Customer identifier (for customer returns)", JsonName: "customerId"),
                new("Supplier ID", "string", "Supplier identifier (for supplier returns)", JsonName: "supplierId"),
                new("Return Date", "datetime", "Date of return", JsonName: "returnDate"),
                new("Product ID", "string", "Returned product ID"),
                new("Product", "string", "Returned product name (alternative to ID)"),
                new("Quantity", "int", "Quantity returned"),
                new("Reason", "string", "Reason for return"),
                new("Refund Amount", "decimal", "Amount being refunded", JsonName: "refundAmount"),
                new("Restocking Fee", "decimal", "Restocking fee charged", JsonName: "restockingFee"),
                new("Status", "enum:Pending,Approved,Rejected,Completed", "Return status", JsonName: "status"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
                new("Processed By", "string", "Employee who processed the return", JsonName: "processedBy"),
            ],

            [SpreadsheetSheetType.LostDamaged] =
            [
                new("ID", "string", "Unique identifier (e.g., LOST-001)", Required: true, JsonName: "id"),
                new("Product ID", "string", "Product identifier", JsonName: "productId"),
                new("Product", "string", "Product name (alternative to ID)"),
                new("Inventory Item ID", "string", "Inventory item identifier", JsonName: "inventoryItemId"),
                new("Quantity", "int", "Quantity lost or damaged", JsonName: "quantity"),
                new("Reason", "enum:Lost,Damaged,Stolen,Expired,Other", "Reason for loss", JsonName: "reason"),
                new("Date Discovered", "datetime", "Date loss was discovered", JsonName: "dateDiscovered"),
                new("Date", "datetime", "Date (alternative to Date Discovered)", JsonName: "dateDiscovered"),
                new("Value Lost", "decimal", "Monetary value of the loss", JsonName: "valueLost"),
                new("Notes", "string", "Additional notes", JsonName: "notes"),
                new("Insurance Claim", "enum:Yes,No", "Whether an insurance claim was filed", JsonName: "insuranceClaim"),
            ],

            [SpreadsheetSheetType.BankStatement] =
            [
                new("Date", "datetime", "Date the bank posted the transaction", Required: true, JsonName: "date"),
                new("Description", "string", "Transaction description / memo from the bank", Required: true, JsonName: "description"),
                new("Amount", "decimal", "Signed amount: negative for money out, positive for money in. Map a single signed amount column here when present", JsonName: "amount"),
                new("Debit", "decimal", "Money out of the account (use when the statement has separate debit/credit columns)", JsonName: "debit"),
                new("Credit", "decimal", "Money into the account (use when the statement has separate debit/credit columns)", JsonName: "credit"),
                new("Balance", "decimal", "Running account balance after the transaction", JsonName: "balance"),
                new("Reference", "string", "Bank reference, transaction id, or check number", JsonName: "rawReference"),
            ],
        };
    }
}

"""Writes the made-up QuickBooks exports the migration video imports, laid out like real QuickBooks reports: heading lines
at the top, group headings in the middle and subtotal rows mixed in with the data. TestData/MainImporter holds a real
export to compare the layout with. The company and its people are invented.

Usage: python make_qb_exports.py OUT_FOLDER

It prints the income, expense and open invoice totals, which the app's own reports must match after the import.
"""
import random
import sys
from datetime import date, timedelta
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Font

COMPANY = "Cedar Ridge IT"
FIRST, LAST = date(2026, 1, 1), date(2026, 10, 31)
CUSTOMERS = [
    ("Harbor Dental Clinic", "Dana Whitfield", "office@harbordental.example", "(905) 555-0142", "212 Harbor Rd, Oakville ON"),
    ("Maple Street Bakery", "Tom Okafor", "hello@maplestreetbakery.example", "(905) 555-0178", "41 Maple St, Burlington ON"),
    ("Lakeside Law", "Priya Nair", "accounts@lakesidelaw.example", "(905) 555-0119", "900 Lakeshore Blvd, Mississauga ON"),
    ("Northgate Veterinary", "Sam Keller", "admin@northgatevet.example", "(905) 555-0163", "15 Northgate Dr, Oakville ON"),
    ("Pine Hill Accounting", "Rosa Lindqvist", "rosa@pinehill.example", "(905) 555-0127", "3 Pine Hill Ct, Milton ON"),
    ("Summit Roofing", "Marc Dubois", "office@summitroofing.example", "(905) 555-0186", "78 Industrial Ave, Hamilton ON"),
    ("Bluewater Marina", "Jess Tran", "dock@bluewatermarina.example", "(905) 555-0134", "1 Pier Way, Oakville ON"),
    ("Greenfield Landscaping", "Owen Reid", "owen@greenfield.example", "(905) 555-0155", "220 Garden Ln, Burlington ON"),
]
VENDORS = [
    ("Northwave Internet", "billing@northwave.example", "(800) 555-0110", "Utilities"),
    ("CloudHost Services", "invoices@cloudhost.example", "(800) 555-0121", "Software"),
    ("TechSource Parts", "orders@techsource.example", "(905) 555-0190", "Parts"),
    ("Brightline Office Supply", "sales@brightline.example", "(905) 555-0104", "Office supplies"),
    ("City Fuel", "fleet@cityfuel.example", "(905) 555-0171", "Vehicle"),
    ("Parkway Parking", "help@parkway.example", "(905) 555-0116", "Vehicle"),
]
ITEMS = [
    ("Monthly IT Support", "Service", "Support plan, per month", 450.00),
    ("Network Assessment", "Service", "Full network review and report", 900.00),
    ("Server Setup", "Service", "New server installed and configured", 1600.00),
    ("Laptop Repair", "Service", "Diagnosis and repair, per device", 180.00),
    ("Backup Service", "Service", "Offsite backup, per month", 95.00),
    ("Business Laptop", "Inventory", "14 inch laptop, configured", 1299.00),
    ("Wireless Access Point", "Inventory", "Installed and configured", 340.00),
]
EXPENSES = [
    ("Utilities", "Northwave Internet", "Internet service", 89.99, 89.99),
    ("Software", "CloudHost Services", "Cloud hosting", 42.50, 42.50),
    ("Parts", "TechSource Parts", "Parts for client jobs", 120.00, 640.00),
    ("Office supplies", "Brightline Office Supply", "Paper and toner", 40.00, 240.00),
    ("Vehicle", "City Fuel", "Fuel", 45.00, 80.00),
    ("Vehicle", "Parkway Parking", "Client site parking", 12.00, 30.00),
]


def money(value):
    return round(value, 2)


def sales():
    """Every sale of the period, the same each run."""
    rng = random.Random(2026)
    lines, number = [], 1001
    day = FIRST
    while day <= LAST:
        for customer in CUSTOMERS[:5]:
            lines.append((day.replace(day=rng.randint(1, 5)), "Invoice", number, customer[0], "Monthly IT Support", 450.00))
            number += 1
        for _ in range(rng.randint(1, 2)):
            item = rng.choice(ITEMS[1:])
            kind = rng.choice(["Invoice", "Sales Receipt"])
            when = day.replace(day=rng.randint(6, 27))
            lines.append((when, kind, number, rng.choice(CUSTOMERS)[0], item[0], item[3]))
            number += 1
        day = (day + timedelta(days=32)).replace(day=1)
    # One invoice with two lines, which a report shows as two rows with the same number.
    first = next(s for s in lines if s[1] == "Invoice" and s[4] != "Monthly IT Support")
    lines.append((first[0], "Invoice", first[2], first[3], "Backup Service", 95.00))
    return sorted(lines, key=lambda s: s[0])


def costs():
    """Every expense of the period, the same each run."""
    rng = random.Random(1402)
    lines, number = [], 5001
    day = FIRST
    while day <= LAST:
        for group, vendor, memo, low, high in EXPENSES:
            amount = low if low == high else money(rng.uniform(low, high))
            lines.append((day.replace(day=rng.randint(2, 26)), "Expense", number, vendor, memo, group, amount))
            number += 1
        day = (day + timedelta(days=32)).replace(day=1)
    return sorted(lines, key=lambda c: c[0])


def report(title, period, header):
    book = Workbook()
    sheet = book.active
    sheet.title = "Sheet1"
    sheet["A1"], sheet["A2"], sheet["A3"] = COMPANY, title, period
    for cell in (sheet["A1"], sheet["A2"]):
        cell.font = Font(bold=True)
    sheet.append([])
    sheet.append(header)
    for cell in sheet[5]:
        cell.font = Font(bold=True)
    return book, sheet


def contact_lists(folder):
    book, sheet = report("Customer Contact List", "All dates", ["Customer", "Main Contact", "Main Email", "Main Phone", "Bill to"])
    for row in CUSTOMERS:
        sheet.append(list(row))
    book.save(folder / "Customer Contact List.xlsx")

    book, sheet = report("Vendor Contact List", "All dates", ["Vendor", "Main Email", "Main Phone", "Account"])
    for row in VENDORS:
        sheet.append(list(row))
    book.save(folder / "Vendor Contact List.xlsx")

    book, sheet = report("Item Listing", "All dates", ["Item", "Type", "Description", "Price"])
    for row in ITEMS:
        sheet.append(list(row))
    book.save(folder / "Item Listing.xlsx")


def open_invoices(folder, sold):
    book, sheet = report("Open Invoices", f"As of {LAST:%B %d, %Y}", ["", "Date", "Num", "Customer", "Due Date", "Open Balance"])
    unpaid = [s for s in sold if s[1] == "Invoice"][-4:]
    total = 0.0
    for when, _, number, customer, _, amount in unpaid:
        sheet.append([None, when.strftime("%m/%d/%Y"), number, customer, (when + timedelta(days=30)).strftime("%m/%d/%Y"), amount])
        total += amount
    sheet.append(["TOTAL", None, None, None, None, money(total)])
    book.save(folder / "Open Invoices.xlsx")
    return money(total)


def profit_and_loss_detail(folder, sold, spent):
    header = ["", "Transaction date", "Transaction type", "Num", "Name", "Description", "Item split account", "Amount", "Balance"]
    book, sheet = report("Profit and Loss Detail", f"{FIRST:%B %d}-{LAST:%B %d, %Y}", header)
    sheet.append(["Ordinary Income/Expenses"])
    sheet.append(["Income"])
    income = 0.0
    for group, wanted in (("Service income", ITEMS[:5]), ("Hardware sales", ITEMS[5:])):
        names = {item[0] for item in wanted}
        sheet.append([group])
        balance = 0.0
        for when, kind, number, customer, item, amount in (s for s in sold if s[4] in names):
            balance = money(balance + amount)
            account = "Accounts Receivable (A/R)" if kind == "Invoice" else "Checking"
            sheet.append([None, when.strftime("%m/%d/%Y"), kind, number, customer, item, account, amount, balance])
        sheet.append([f"Total for {group}", None, None, None, None, None, None, balance])
        income = money(income + balance)
    sheet.append(["Total for Income", None, None, None, None, None, None, income])
    sheet.append(["Expenses"])
    expenses = 0.0
    for group in dict.fromkeys(row[0] for row in EXPENSES):
        sheet.append([group])
        balance = 0.0
        for when, kind, number, vendor, memo, wanted, amount in spent:
            if wanted == group:
                balance = money(balance + amount)
                sheet.append([None, when.strftime("%m/%d/%Y"), kind, number, vendor, memo, "Checking", amount, balance])
        sheet.append([f"Total for {group}", None, None, None, None, None, None, balance])
        expenses = money(expenses + balance)
    sheet.append(["Total for Expenses", None, None, None, None, None, None, expenses])
    sheet.append(["Net Income", None, None, None, None, None, None, money(income - expenses)])
    book.save(folder / "Profit and Loss Detail.xlsx")
    return income, expenses


def main():
    folder = Path(sys.argv[1])
    folder.mkdir(parents=True, exist_ok=True)
    sold, spent = sales(), costs()
    contact_lists(folder)
    owed = open_invoices(folder, sold)
    income, expenses = profit_and_loss_detail(folder, sold, spent)
    print(f"{len(sold)} sales, {len(spent)} expenses. Income {income:,.2f}, expenses {expenses:,.2f}, "
          f"net {income - expenses:,.2f}, open invoices {owed:,.2f}")


if __name__ == "__main__":
    main()

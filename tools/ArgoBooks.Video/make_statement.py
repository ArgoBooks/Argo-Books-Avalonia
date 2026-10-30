"""Writes the bank statement the videos import: a month of a made-up IT business's chequing account, as the CSV a
bank's website hands out, ending the day before it is made so its lines are the newest in the sample company.

Usage: python make_statement.py OUT.csv
"""
import csv
import sys
from datetime import date, timedelta
from pathlib import Path

# Days before the statement's last day, what the bank shows, and the amount: negative for money out.
LINES = [
    (26, "NORTHWAVE INTERNET", -89.99),
    (25, "E-TRANSFER RECEIVED HARBOR DENTAL CLINIC", 1850.00),
    (22, "CITY FUEL #4412", -64.12),
    (20, "BRIGHTLINE OFFICE SUPPLY", -238.80),
    (18, "DEPOSIT MAPLE STREET BAKERY", 1240.00),
    (14, "CLOUDHOST SERVICES", -42.50),
    (12, "PARKWAY PARKING", -18.00),
    (8, "E-TRANSFER RECEIVED LAKESIDE LAW", 2600.00),
    (6, "TECHSOURCE PARTS", -412.37),
    (1, "MONTHLY ACCOUNT FEE", -6.95),
]
OPENING = 8420.15


def main():
    out = Path(sys.argv[1])
    out.parent.mkdir(parents=True, exist_ok=True)
    last = date.today() - timedelta(days=1)
    balance = OPENING
    with out.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["Date", "Description", "Withdrawals", "Deposits", "Balance"])
        for days_before, description, amount in LINES:
            balance = round(balance + amount, 2)
            writer.writerow([(last - timedelta(days=days_before)).isoformat(), description,
                             f"{-amount:.2f}" if amount < 0 else "", f"{amount:.2f}" if amount > 0 else "", f"{balance:.2f}"])
    print(f"{out}: {len(LINES)} lines to {last}, closing balance {balance:.2f}")


if __name__ == "__main__":
    main()

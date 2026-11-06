"""Make YouTube video 16, "Best QuickBooks Desktop Alternatives (2026)": five accounting programs that still install on your
computer, what each one costs, whether your QuickBooks data comes with you, and the catch in each, narrated by an Azure voice.

Usage:
  python qb_desktop_video.py voice          fetches and measures the voice, and prints when each part starts
  python qb_desktop_video.py frame 12 95    saves single frames at those seconds to work/qbvideo/check/, for checking
  python qb_desktop_video.py                renders the video

It needs the takes `welcome`, `tour`, `quickbooks`, `bill` and `salestax` in work/qbvideo/takes, recorded from the released
app (see README.md), and the pages captured with `node capture_pages.js work/qbvideo/pages <names>`. GnuCash's features page
is the one captured for video 15. The finished video and its captions go to work/videos/.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import catches_short as short
import mac_video as mac
import make_video as mv
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "qbvideo"
OUT_FILE = HERE / "work" / "videos" / "video_16_quickbooks_desktop.mp4"
RATE, FPS = r.RATE, r.FPS
SIZE = (1920, 1080)
GAP = 0.55
SECTION_GAP = 1.0
FADE_FRAMES = 6
COBALT, RED = r.COBALT, r.RED

PARTS = {
    "hook": "If you're on QuickBooks Desktop, you've probably noticed that Intuit wants you off it. Most of it isn't sold to new customers anymore, and everything points you to QuickBooks Online, which runs in your browser, by the month.",
    "hook2": "But a lot of people want their books on their own computer, the way Desktop has always worked.",
    "promise": "So these are five accounting programs you still install in 2026, what each one costs, whether your QuickBooks data comes with you, and the catch in each one. One thing up front: I make the fifth one, so I'll hold it to the same standard.",
    "qb": "First, where QuickBooks Desktop stands. Intuit stopped selling Pro Plus, Premier Plus and Mac Plus to new US customers on September 30th, 2024.",
    "qb2": "If you already have it, you can keep renewing, and Enterprise is still sold. But Desktop prices went up again this year.",
    "sage": "Number one: Sage 50. It's the closest thing to QuickBooks Desktop: real Windows software, with a payroll add-on.",
    "sage_qb": "The US edition has a wizard that converts your QuickBooks company file, if your QuickBooks is no more than three years old and both programs are on the same computer. Your bank reconciliation history doesn't come across.",
    "sage_catch": "The catch: it's Windows only, and it's a yearly subscription. Pro is 737 US dollars a year for one user, and the software stops working if you stop paying.",
    "accountedge": "Number two: AccountEdge Pro. It's desktop software for both Mac and Windows, and your company file stays on your own computer.",
    "accountedge_qb": "Its free import tool brings across your accounts, customers, vendors and items from QuickBooks, but not your transactions. For those, AccountEdge points you to a migration service, from 389 dollars.",
    "accountedge_catch": "The catch: it's a subscription too. Pro is 20 US dollars a month for one user, or 30 for two users on the Network Edition, and payroll is an extra service.",
    "gnucash": "Number three: GnuCash. It's free and open source, for Windows, Mac and Linux, and your books are a file on your own computer.",
    "gnucash_qb": "But it can't import QuickBooks. It reads Quicken and bank files, so your customers, vendors and open invoices don't come across, and you'd set them up again by hand.",
    "gnucash_catch": "The catch: it's built around double-entry bookkeeping, so it expects you to know your debits from your credits.",
    "manager": "Number four: Manager. The desktop edition is free forever, it works offline, and it runs on Windows, Mac and Linux.",
    "manager_qb": "There's no QuickBooks import here either, so your lists come across by pasting them in from a spreadsheet.",
    "manager_catch": "The catch: the desktop edition is for one person on one computer. Sharing the books means the cloud edition, at 59 US dollars a month. And it's double-entry, like GnuCash.",
    "argo": "Number five: Argo Books, the one I make. It's a desktop app for Windows, Mac and Linux, and there's no account to create. Your company is a file on your computer.",
    "argo_qb": "To move from QuickBooks Desktop, you export five reports to Excel: your customers, vendors, items, open invoices, and profit and loss detail. Then you drop them all in at once.",
    "argo_qb2": "It reads each one and shows you what it found. Your customers, suppliers, products, open invoices and transactions come across, and the totals match QuickBooks to the cent.",
    "argo_qb_free": "That fits in the free plan, which includes 100 spreadsheet imports a month.",
    "argo_bills": "Bills work the way you're used to. Untick Paid on an expense, and it becomes a bill with a due date. It shows as unpaid until you pay it, and the accounts payable aging report lists what you owe each supplier.",
    "argo_tax": "And the Sales Tax page lists each period, what you collected, what you owe, and when the return is due.",
    "argo_free": "The free plan has no time limit. It also gives you 25 invoices a month with card payments, 10 receipt scans and 25 bank statement imports.",
    "argo_catch": "The catch: those monthly limits. Past them, Premium is 15 Canadian dollars a month. Payroll is only for Canadian businesses, there's no automatic bank connection, just statement imports, and it's one person in a company file at a time.",
    "pick": "So which one should you pick? If you want the closest thing to QuickBooks Desktop on Windows, and the price is fine, Sage 50. On a Mac, or for less, AccountEdge. If you know bookkeeping and want it free, GnuCash or Manager. And if you want your QuickBooks data across for free, without learning accounting first, try Argo Books.",
    "ask": "Argo Books is the first link in the description. It's free, and there's nothing to sign up for.",
}
SECTIONS = {"qb", "sage", "accountedge", "gnucash", "manager", "argo", "pick"}
CHAPTERS = [("hook", "QuickBooks Desktop alternatives"), ("qb", "Where QuickBooks Desktop stands"), ("sage", "1. Sage 50"),
            ("accountedge", "2. AccountEdge Pro"), ("gnucash", "3. GnuCash"), ("manager", "4. Manager"), ("argo", "5. Argo Books"),
            ("pick", "Which one to pick")]
# The line on GnuCash's features page about importing, placed by hand on the page as captured for video 15.
BOXES = {"gnucash_features": {"import": [780, 5196, 2080, 64]}}
PAGE_FOLDERS = {"gnucash_features": HERE / "work" / "mac" / "pages"}
PRODUCTS = [("Sage 50", "Windows", "Converts your file", "$737 USD a year"),
            ("AccountEdge", "Mac and Windows", "Lists free, history $389+", "$20 USD a month"),
            ("GnuCash", "Windows, Mac, Linux", "No QuickBooks import", "Free"),
            ("Manager", "Windows, Mac, Linux", "No QuickBooks import", "Free"),
            ("Argo Books", "Windows, Mac, Linux", "Imports your reports", "Free plan")]


def voice():
    return mac.Voice(PARTS, SECTIONS, WORK)


class Pages(r.Pages):
    def get(self, name):
        if name not in self.images:
            folder = PAGE_FOLDERS.get(name, WORK / "pages")
            Image.MAX_IMAGE_PIXELS = None
            self.images[name] = Image.open(folder / f"{name}.png").convert("RGB")
            self.boxes[name] = {**json.loads((folder / f"{name}.json").read_text())["boxes"], **BOXES.get(name, {})}
        return self.images[name], self.boxes[name]


def load_take(name):
    log = json.loads((WORK / "takes" / f"{name}.json").read_text())
    log["path"] = WORK / "takes" / f"{name}.mkv"
    return log


# ---------------------------------------------------------------------------------------------------------------- cards

def recap(lit):
    """The five side by side, as a table, with the ones the voice is recommending lit while it talks about them."""
    def frame_at(t):
        frame = qv.backdrop()
        draw = ImageDraw.Draw(frame)
        draw.text((150, 170), "Which one should you pick?", font=mv.font("Bold", 70), fill=mv.INK, anchor="ls")
        heads, xs = ["", "Runs on", "Your QuickBooks data", "Costs"], [150, 540, 960, 1440]
        for x, head in zip(xs, heads):
            draw.text((x, 260), head, font=mv.font("Bold", 34), fill=mv.GREY, anchor="ls")
        y = 290
        on = next((rows for at, rows in reversed(lit) if t >= at), set())
        for n, row in enumerate(PRODUCTS):
            if n in on:
                draw.rounded_rectangle((120, y + 6, 1800, y + 128), radius=18, fill=(219, 234, 254), outline=mv.BLUE, width=4)
            colour = mv.INK if not on or n in on else (148, 163, 184)
            draw.text((xs[0], y + 67), row[0], font=mv.font("Bold", 48), fill=colour, anchor="lm")
            for x, text in zip(xs[1:], row[1:]):
                draw.text((x, y + 67), text, font=mv.font("Regular", 38), fill=colour, anchor="lm")
            y += 136
            draw.line((150, y, 1770, y), fill=qv.LINE, width=2)
        draw.text((150, 1000), "As listed on each product's own site, November 2026. Sage 50 Pro, US desktop edition.", font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
        return frame
    return frame_at


def end_card():
    frame = qv.backdrop().convert("RGBA")
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((780, round(780 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((1920 - 780) // 2, 300))
    draw.text((960, 560), "Desktop accounting for Windows, Mac and Linux. Free.", font=mv.font("SemiBold", 48), fill=mv.INK, anchor="mm")
    draw.text((960, 650), "argorobots.com", font=mv.font("Bold", 70), fill=mv.BLUE, anchor="mm")
    draw.text((960, 750), "Your QuickBooks Desktop reports import on the free plan. Link in the description.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
    return frame.convert("RGB")


# ---------------------------------------------------------------------------------------------------------------- edit

def shot_list(v):
    S, E, W = v.start, v.end, v.at

    def page(at, name, keys, marks=(), cursor=()):
        return {"at": at, "page": name, "keys": keys, "marks": list(marks), "cursor": list(cursor)}

    def take(at, name, mark, offset, keys, until=None):
        shot = {"at": at, "take": name, "from": (mark, offset), "keys": keys}
        if until:
            shot["until"] = until
        return shot

    def still(at, image):
        return {"at": at, "still": image}

    def clip(at, name, first, last, start_box, end_box):
        return {"at": at, "clip": name, "from": first, "until": last, "boxes": (start_box, end_box)}

    lineup = qv.card("Five that still install, for 2026", [
        (1, "Sage 50: Windows"), (2, "AccountEdge Pro: Mac and Windows"), (3, "GnuCash: Windows, Mac and Linux"),
        (4, "Manager: Windows, Mac and Linux"), (5, "Argo Books: Windows, Mac and Linux, and the one I make")], kicker="QuickBooks Desktop alternatives")
    qb_sales = qv.card("QuickBooks Desktop", [
        ("no", "Pro Plus, Premier Plus and Mac Plus: not sold to new US customers since September 30, 2024"),
        ("no", "Intuit sends new customers to QuickBooks Online, in the browser, by the month")],
        footnote="Source: Intuit's QuickBooks Desktop sales announcement, November 2026.", kicker="Where it stands")
    qb_renew = qv.card("If you already have it", [
        ("yes", "You can keep renewing your Desktop subscription"), ("yes", "QuickBooks Desktop Enterprise is still sold"),
        ("no", "Desktop prices went up again in 2026")],
        footnote="Sources: Intuit, and Intuit's 2026 Desktop price change notices, November 2026.", kicker="Where it stands")
    sage_what = qv.card("Sage 50", [("yes", "Real Windows desktop software"), ("yes", "Payroll as an add-on")],
                        footnote="Source: sage.com, November 2026.", kicker="1. The closest thing to QuickBooks Desktop")
    sage_qb = qv.card("Your QuickBooks data", [
        ("yes", "The US edition's wizard converts your QuickBooks company file"),
        ("no", "Only from QuickBooks up to three years old, with both on the same computer"),
        ("no", "Bank reconciliation history doesn't convert")],
        footnote="Source: Sage 50 US help, Converting from QuickBooks, November 2026.", kicker="Sage 50")
    sage_catch = qv.card("The catch in Sage 50", [
        ("no", "Windows only"), ("no", "A yearly subscription: Pro is $737 USD a year for one user"),
        ("no", "The software stops working if you stop paying")],
        footnote="Source: sage.com, Sage 50 desktop pricing, November 2026.", kicker="Sage 50")
    manager_qb = qv.card("Your QuickBooks data", [("no", "No QuickBooks import"), ("yes", "Lists can be pasted in from a spreadsheet")],
                         footnote="Source: Manager's guides and forum, November 2026.", kicker="Manager")
    argo_catch = qv.card("The catch in Argo Books", [
        ("no", "Monthly limits on the free plan"), ("no", "Payroll is for Canadian businesses only"),
        ("no", "No automatic bank connection, just statement imports"), ("no", "One person in a company file at a time")],
        kicker="Holding it to the same standard")

    shots = [
        clip(0.0, "invoices", 0.5, 15.5, (0, 0, 2400, 1350), (300, 120, 1900, 1069)),
        page(S("hook2") - 0.1, "gnucash_download_win", [(S("hook2") - 0.1, 1.15, "windows"), (E("hook2") + 0.4, 1.25, "windows")],
             marks=[(W("hook2", "own"), "windows", COBALT)], cursor=[(W("hook2", "books"), "windows")]),
        still(S("promise") - 0.1, lineup),

        still(S("qb") - 0.3, qb_sales),
        still(S("qb2") - 0.1, qb_renew),

        still(S("sage") - 0.3, mac.section_card(1, "Sage 50", "Windows, a yearly subscription")),
        still(W("sage", "It's") - 0.2, sage_what),
        still(S("sage_qb") - 0.1, sage_qb),
        still(S("sage_catch") - 0.1, sage_catch),

        still(S("accountedge") - 0.3, mac.section_card(2, "AccountEdge Pro", "Mac and Windows, a monthly subscription")),
        page(W("accountedge", "It's") - 0.2, "accountedge_pricing", [(W("accountedge", "It's") - 0.2, 1.0, (1600, 900)), (W("accountedge", "Mac"), 1.4, "platforms"), (W("accountedge", "company") - 0.3, 1.3, "local"), (E("accountedge") + 0.4, 1.35, "local")],
             marks=[(W("accountedge", "Mac"), "platforms", COBALT), (W("accountedge", "company"), "local", COBALT)],
             cursor=[(W("accountedge", "Mac"), "platforms"), (W("accountedge", "company"), "local")]),
        page(S("accountedge_qb") - 0.1, "accountedge_qb", [(S("accountedge_qb") - 0.1, 1.2, "iif"), (W("accountedge_qb", "For") - 0.3, 1.3, "iif"), (W("accountedge_qb", "389"), 1.35, "from_389"), (E("accountedge_qb") + 0.5, 1.4, "from_389")],
             marks=[(W("accountedge_qb", "free"), "iif", COBALT), (W("accountedge_qb", "389"), "from_389", RED)],
             cursor=[(W("accountedge_qb", "free"), "iif"), (W("accountedge_qb", "389"), "from_389")]),
        page(S("accountedge_catch") - 0.1, "accountedge_pricing", [(S("accountedge_catch") - 0.1, 1.15, "pro_price"), (W("accountedge_catch", "30"), 1.15, "network_price"), (W("accountedge_catch", "payroll"), 1.3, "payroll"), (E("accountedge_catch") + 0.5, 1.35, "payroll")],
             marks=[(W("accountedge_catch", "20"), "pro_price", RED), (W("accountedge_catch", "30"), "network_price", RED), (W("accountedge_catch", "payroll"), "payroll", RED)],
             cursor=[(W("accountedge_catch", "20"), "pro_price"), (W("accountedge_catch", "30"), "network_price"), (W("accountedge_catch", "payroll"), "payroll")]),

        still(S("gnucash") - 0.3, mac.section_card(3, "GnuCash", "Windows, Mac and Linux, free and open source")),
        page(W("gnucash", "It's") - 0.2, "gnucash_download_win", [(W("gnucash", "It's") - 0.2, 1.0, (1600, 900)), (W("gnucash", "Windows"), 1.3, "windows"), (W("gnucash", "Linux"), 1.2, "linux"), (E("gnucash") + 0.4, 1.25, "linux")],
             marks=[(W("gnucash", "Windows"), "windows", COBALT), (W("gnucash", "Mac"), "silicon", COBALT), (W("gnucash", "Linux"), "linux", COBALT)],
             cursor=[(W("gnucash", "Windows"), "windows"), (W("gnucash", "Mac"), "silicon"), (W("gnucash", "Linux"), "linux")]),
        page(S("gnucash_qb") - 0.1, "gnucash_features", [(S("gnucash_qb") - 0.1, 1.35, (1800, 5230)), (E("gnucash_qb") + 0.4, 1.4, (1800, 5230))],
             marks=[(W("gnucash_qb", "Quicken"), "import", RED)], cursor=[(W("gnucash_qb", "Quicken"), "import")]),
        page(S("gnucash_catch") - 0.1, "gnucash_features", [(S("gnucash_catch") - 0.1, 1.3, "double_entry"), (E("gnucash_catch") + 0.5, 1.35, "double_entry")],
             marks=[(W("gnucash_catch", "built"), "double_entry", RED)], cursor=[(W("gnucash_catch", "built"), "double_entry")]),

        still(S("manager") - 0.3, mac.section_card(4, "Manager", "Windows, Mac and Linux, free forever")),
        page(W("manager", "The") - 0.2, "manager_download_win", [(W("manager", "The") - 0.2, 1.15, "free_forever"), (W("manager", "runs"), 1.3, "windows"), (E("manager") + 0.4, 1.35, "windows")],
             marks=[(W("manager", "free"), "free_forever", COBALT), (W("manager", "Windows"), "windows", COBALT)],
             cursor=[(W("manager", "free"), "free_forever"), (W("manager", "Windows"), "windows")]),
        still(S("manager_qb") - 0.1, manager_qb),
        page(S("manager_catch") - 0.1, "manager_download_win", [(S("manager_catch") - 0.1, 1.3, "single_user"), (W("manager_catch", "Sharing"), 1.3, "single_user")],
             marks=[(W("manager_catch", "one"), "single_user", RED)], cursor=[(W("manager_catch", "one"), "single_user")]),
        page(W("manager_catch", "Sharing") - 0.1, "manager_cloud", [(W("manager_catch", "Sharing") - 0.1, 1.0, (1600, 900)), (W("manager_catch", "59"), 1.25, "price"), (E("manager_catch") + 0.5, 1.25, "price")],
             marks=[(W("manager_catch", "59"), "price", RED)], cursor=[(W("manager_catch", "59"), "price")]),

        still(S("argo") - 0.3, mac.section_card(5, "Argo Books", "Windows, Mac and Linux, and the one I make")),
        page(W("argo", "It's") - 0.2, "argo_downloads", [(W("argo", "It's") - 0.2, 1.0, (1600, 1000)), (W("argo", "Windows"), 1.3, "mac"), (W("argo", "account") - 0.3, 1.3, "no_account"), (W("argo", "Your") - 0.2, 1.35, "no_account")],
             marks=[(W("argo", "Windows"), "windows", COBALT), (W("argo", "Mac"), "mac", COBALT), (W("argo", "Linux"), "linux", COBALT), (W("argo", "account"), "no_account", COBALT)],
             cursor=[(W("argo", "Windows"), "windows"), (W("argo", "Mac"), "mac"), (W("argo", "Linux"), "linux"), (W("argo", "account"), "no_account")]),
        take(W("argo", "Your") - 0.1, "welcome", "start", 0.0, [("start", 0.0, 1.0, 1600, 900), ("hover_create", 0.0, 1.25, 1450, 950)]),
        take(S("argo_qb") - 0.1, "quickbooks", "desktop_steps", 0.0, [("desktop_steps", 0.0, 1.35, 1600, 840), ("desktop_steps", 4.5, 1.45, 1600, 850)], until=("drag", -0.1)),
        take(W("argo_qb", "Then") - 0.1, "quickbooks", "drag", -0.2, [("drag", 0.0, 1.45, 1600, 960), ("dropped", 0.5, 1.6, 1600, 1060)], until=("click_continue", 0.2)),
        take(S("argo_qb2") - 0.1, "quickbooks", "review_1", -0.3, [("review_1", -0.3, 1.5, 1600, 930), ("review_1", 3.0, 1.6, 1600, 930)], until=("result_1", -0.8)),
        take(W("argo_qb2", "Your") - 0.1, "quickbooks", "page_customers", 0.3, [("page_customers", 0.3, 1.25, 1840, 1075)], until=("page_suppliers", -1.0)),
        take(W("argo_qb2", "open") - 0.1, "quickbooks", "page_invoices", 0.3, [("page_invoices", 0.3, 1.3, 1830, 1150)], until=("page_customers", -1.0)),
        take(W("argo_qb2", "transactions") - 0.1, "quickbooks", "result_4", -0.2, [("result_4", -0.2, 1.9, 1577, 932)], until=("result_4", 2.0)),
        take(W("argo_qb2", "totals") - 0.2, "quickbooks", "statement", -0.5, [("statement", -0.5, 1.5, 1570, 1150), ("statement", 3.0, 1.6, 1570, 1180)]),
        page(S("argo_qb_free") - 0.1, "argo_pricing", [(S("argo_qb_free") - 0.1, 1.15, "free_card"), (W("argo_qb_free", "100"), 1.35, "free_imports"), (E("argo_qb_free") + 0.5, 1.4, "free_imports")],
             marks=[(W("argo_qb_free", "100"), "free_imports", COBALT)], cursor=[(W("argo_qb_free", "100"), "free_imports")]),
        take(S("argo_bills") - 0.1, "bill", "scroll", 0.6, [("scroll", 0.6, 1.4, 1600, 1000), ("click_paid", 0.0, 1.7, 1550, 1080)], until=("click_save", 0.3)),
        take(W("argo_bills", "shows") - 0.1, "bill", "saved", 0.6, [("saved", 0.6, 1.75, 2250, 860)], until=("bills_card", 2.4)),
        take(W("argo_bills", "accounts") - 0.2, "bill", "aging", -0.5, [("aging", -0.5, 1.75, 1568, 1275)]),
        take(S("argo_tax") - 0.1, "salestax", "click_sales_tax", -0.6, [("click_sales_tax", -0.6, 1.0, 1600, 900), ("owed", 0.0, 1.2, 1900, 900), ("due", 1.0, 1.35, 2100, 1100)]),
        page(S("argo_free") - 0.1, "argo_pricing", [(S("argo_free") - 0.1, 1.15, "free_card"), (W("argo_free", "10"), 1.3, "free_scans"), (E("argo_free") + 0.4, 1.3, "free_bank")],
             marks=[(W("argo_free", "25"), "free_invoices", COBALT), (W("argo_free", "10"), "free_scans", COBALT), (W("argo_free", "25", nth=2), "free_bank", COBALT)],
             cursor=[(W("argo_free", "25"), "free_invoices"), (W("argo_free", "10"), "free_scans"), (W("argo_free", "25", nth=2), "free_bank")]),
        page(S("argo_catch") - 0.1, "argo_pricing", [(S("argo_catch") - 0.1, 1.1, (1600, 1500)), (W("argo_catch", "15"), 1.3, "premium_price"), (W("argo_catch", "Payroll") - 0.4, 1.3, "premium_price")],
             marks=[(W("argo_catch", "15"), "premium_price", RED)], cursor=[(W("argo_catch", "15"), "premium_price")]),
        still(W("argo_catch", "Payroll") - 0.3, argo_catch),

        still(S("pick") - 0.3, recap([(W("pick", "Sage"), {0}), (W("pick", "Mac"), {1}), (W("pick", "bookkeeping"), {2, 3}), (W("pick", "Argo"), {4})])),
        still(S("ask") - 0.1, end_card()),
    ]
    shots.sort(key=lambda s: s["at"])
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def render(shots, path):
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{SIZE[0]}x{SIZE[1]}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                                "-pix_fmt", "yuv420p", str(path)], stdin=subprocess.PIPE)
    pages, sprites = Pages(), {}
    last, spoken = None, []
    for shot, following in zip(shots, shots[1:]):
        first, stop = round(shot["at"] * FPS), round(following["at"] * FPS)
        count = stop - first
        if "outro" in shot:
            process = subprocess.Popen([mv.FFMPEG, "-v", "error", "-i", str(r.OUTRO), "-vf", f"scale={SIZE[0]}:{SIZE[1]}", "-frames:v", str(count),
                                        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
            size = SIZE[0] * SIZE[1] * 3
            previous = None
            for _ in range(count):
                data = process.stdout.read(size)
                if len(data) == size:
                    previous = data
                encoder.stdin.write(previous)
            process.stdout.close()
            process.wait()
            continue
        if "clip" in shot:
            played = min(count, round((shot["until"] - shot["from"]) * FPS))
            source = qv.clip_frames(shot["clip"], shot["from"], played)
        if "take" in shot:
            take = load_take(shot["take"])
            painter = mv.Painter("wide", take, [], [])
            start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
            end = shot.get("until")
            played = count if not end else min(count, max(1, round((take["marks"][end[0]] + end[1]) * FPS) - start))
            part = {"wide": shot["keys"], "frames": count}
            source = mv.take_frames(take, start, played)
            spoken.append((shot["at"], start / FPS, played / FPS, take["events"]))
        for age in range(count):
            if "take" in shot:
                if age < played:
                    pixels = next(source)
                index = start + min(age, played - 1)
                frame = painter.app_frame(pixels, part, 0, index, (start + age) / FPS).convert("RGB")
            elif "clip" in shot:
                if age < played:
                    pixels = next(source)
                frame = qv.clip_image(pixels, shot, age / max(1, count - 1))
            elif "page" in shot:
                frame = r.page_frame(shot, shot["at"] + age / FPS, pages, sprites, SIZE).convert("RGB")
            else:
                frame = mac.recap_still(shot, age / FPS)
            if last is not None and age < FADE_FRAMES:
                frame = Image.blend(last, frame, (age + 1) / (FADE_FRAMES + 1))
            encoder.stdin.write(frame.tobytes())
        last = frame
        print(f"  {shot['at']:6.1f} s done", flush=True)
    encoder.stdin.close()
    encoder.wait()
    return spoken


def frame_at(shots, second, pages, sprites):
    shot, following = next((s, f) for s, f in zip(shots, shots[1:]) if s["at"] <= second < f["at"])
    age = second - shot["at"]
    if "take" in shot:
        take = load_take(shot["take"])
        start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
        end = shot.get("until")
        played = 10 ** 6 if not end else round((take["marks"][end[0]] + end[1]) * FPS) - start
        index = start + min(round(age * FPS), played - 1)
        painter = mv.Painter("wide", take, [], [])
        pixels = next(mv.take_frames(take, index, 1))
        return painter.app_frame(pixels, {"wide": shot["keys"], "frames": 1}, 0, index, (start + round(age * FPS)) / FPS).convert("RGB")
    if "clip" in shot:
        index = min(round(age * FPS), round((shot["until"] - shot["from"]) * FPS) - 1)
        pixels = next(qv.clip_frames(shot["clip"], shot["from"] + index / FPS, 1))
        return qv.clip_image(pixels, shot, age / (following["at"] - shot["at"]))
    if "page" in shot:
        return r.page_frame(shot, second, pages, sprites, SIZE).convert("RGB")
    if "still" in shot:
        return mac.recap_still(shot, age)
    return None


def main():
    v = voice()
    if sys.argv[1:2] == ["voice"]:
        for key, (first, last) in v.spans.items():
            print(f"{first:6.1f} {last - first:5.1f} s  {key}")
        print(f"voice {v.length:.1f} s")
        print("heard:", " ".join(w["text"] for w in v.words))
        return
    shots = shot_list(v)
    if sys.argv[1:2] == ["frame"]:
        folder = WORK / "check"
        folder.mkdir(exist_ok=True)
        pages, sprites = Pages(), {}
        for second in map(float, sys.argv[2:]):
            frame = frame_at(shots, second, pages, sprites)
            if frame is not None:
                frame.save(folder / f"f{second:06.1f}.png")
        return
    picture = WORK / "picture.mp4"
    spoken = render(shots, picture)
    total = shots[-1]["at"]
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mac.mix(v, spoken, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    mac.write_captions(OUT_FILE.with_suffix(".srt"), v, PARTS)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    for key, name in CHAPTERS:
        print(f"{mac.clock(0 if key == 'hook' else v.start(key))} - {name}")



if __name__ == "__main__":
    main()

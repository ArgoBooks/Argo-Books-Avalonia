"""Make YouTube video 15, "Top 5 Free Accounting Software for Mac (2026)": five free options that work on a Mac, the catch in
each, and Argo Books as macOS shows it, narrated by an Azure voice.

Usage:
  python mac_video.py voice          fetches and measures the voice, and prints when each part starts
  python mac_video.py frame 12 95    saves single frames at those seconds to work/mac/check/, for checking
  python mac_video.py                renders the video

It needs the Mac takes (`welcome`, `tour`, `receipt` and `bank`) in work/mac/takes, recorded with ARGO_VIDEO_MAC=1 from a
copy of the released app with its macOS chrome switched on (see README.md), and the pages captured with
`node capture_pages.js work/mac/pages <names>`. The finished video and its captions go to work/videos/.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import catches_short as short
import mac_window as mw
import make_video as mv
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "mac"
OUT_FILE = HERE / "work" / "videos" / "video_15_mac.mp4"
RATE, FPS = r.RATE, r.FPS
SIZE = (1920, 1080)
GAP = 0.55
SECTION_GAP = 1.0
FADE_FRAMES = 6
COBALT, RED = r.COBALT, r.RED
TITLES = {"welcome": "Argo Books"}

PARTS = {
    "hook": "If you run your business on a Mac, most of the accounting software that says it works on a Mac is really a website you open in Safari.",
    "hook2": "That's fine for some people. But if you want an actual Mac app, with your books on your own computer, the list gets short, and the free list is even shorter.",
    "promise": "These are the five best free options for a Mac in 2026, what each free plan actually includes, and the catch in each one. One thing up front: I make the fifth one, so I'll hold it to the same standard.",
    "qb": "First, why QuickBooks isn't on this list. Intuit stopped selling QuickBooks Desktop for Mac to new customers in the US after September 30th, 2024. It now sends Mac users to QuickBooks Online, which runs in your browser and lists at 38 US dollars a month.",
    "qb_free": "There is a new free QuickBooks plan in the US, but it allows two invoices a month, unless you take payments through QuickBooks.",
    "wave": "Number one: Wave. Wave runs in your browser, so on a Mac you use it in Safari or Chrome, and it's only for businesses in the US and Canada.",
    "wave_free": "The free Starter plan gives you unlimited invoices, estimates and bills, and you can take card payments on them.",
    "wave_catch": "The catch: connecting your bank and scanning receipts are both in Pro, which is 250 Canadian dollars a year here, or 190 US.",
    "zoho": "Number two: Zoho Books. This one has a real app in the Mac App Store, although your books are kept in Zoho's cloud.",
    "zoho_free": "The free plan covers one user plus your accountant, with up to a thousand invoices a year.",
    "zoho_catch": "The catch: it's only free while your revenue stays under 50 thousand dollars a year. And connecting your bank feeds takes a paid plan, from 15 US dollars a month, billed yearly.",
    "gnucash": "Number three: GnuCash. It's free and open source, with no limits at all. It has its own download for Apple Silicon Macs, and your books are a file on your own computer.",
    "gnucash_catch": "The catch: GnuCash is built around double-entry bookkeeping, so it expects you to know your debits from your credits. It doesn't look or behave like a Mac app, and there's no automatic bank connection, so you import statement files instead.",
    "manager": "Number four: Manager. The desktop edition is free forever, with no time limits and no usage limits. It works offline, and it has builds for both Apple Silicon and Intel Macs.",
    "manager_catch": "The catch: the desktop edition is for one person on one computer. If your team needs to share the books, that's the cloud edition, at 59 US dollars a month. And like GnuCash, it's built around double-entry accounts.",
    "argo": "Number five: Argo Books, the one I make. It's a native Mac app for Apple Silicon and Intel Macs, on macOS 14 or later, and you can install it with Homebrew too.",
    "argo_account": "There's no account to create. You make a company, and it's a file on your Mac.",
    "argo_dash": "It's made for people who don't want to learn accounting first. You record what comes in and what goes out, and the dashboard and reports are worked out for you.",
    "argo_receipt": "To add an expense, drop in a photo of the receipt. It reads the date, the tax and every line, and you check it before you save.",
    "argo_bank": "Your bank statement comes in as a file, so there's no bank login to hand over.",
    "argo_free": "The free plan has no time limit. You get 25 invoices a month with card payments, 10 receipt scans and 25 bank statement imports.",
    "argo_catch": "The catch: those monthly limits. Past them, Premium is 15 Canadian dollars a month. It's also one person in a company file at a time, and there's no automatic bank connection, just the statement import.",
    "pick": "So which one should you pick? If you know bookkeeping and want it free with no limits, GnuCash or Manager. If the browser is fine and you're in the US or Canada, Wave or Zoho Books. And if you want a real Mac app that doesn't ask you to learn accounting first, try Argo Books.",
    "ask": "They all have free plans, so try one before you pay for anything. Argo Books is the first link in the description. It's free, and there's nothing to sign up for.",
}
SECTIONS = {"qb", "wave", "zoho", "gnucash", "manager", "argo", "pick"}
CHAPTERS = [("hook", "Free accounting software on a Mac"), ("qb", "Why QuickBooks isn't on the list"), ("wave", "1. Wave"),
            ("zoho", "2. Zoho Books"), ("gnucash", "3. GnuCash"), ("manager", "4. Manager"), ("argo", "5. Argo Books"),
            ("pick", "Which one to pick")]
# Lines the capture found in the wrong place, placed by hand on the page as captured on November 3, 2026.
BOXES = {"wave_us": {"starter_price": [840, 1372, 140, 120]}, "zoho_us": {"standard_yearly": [1236, 1508, 176, 104]},
         "gnucash_features": {"import": [780, 5196, 2080, 64]}}
PRODUCTS = [("Wave", "In your browser", "Bank feeds and receipts are paid"),
            ("Zoho Books", "App Store, cloud books", "Free only under $50K a year"),
            ("GnuCash", "Mac app", "Double-entry, not Mac-like"),
            ("Manager", "Mac app", "One person, one computer"),
            ("Argo Books", "Mac app", "Monthly limits on the free plan")]


def spoken(text):
    """The line as the voice is given it: GnuCash is said "guh-noo-cash", as its makers say it, while the captions keep its name."""
    return text.replace("GnuCash", "<sub alias='guh-noo-cash'>GnuCash</sub>")


class Voice:
    """The parts read one after another, with a longer pause before each section. Video 16 passes its own script."""
    def __init__(self, parts=PARTS, sections=SECTIONS, work=WORK):
        work.mkdir(parents=True, exist_ok=True)
        pieces, self.spans, t = [np.zeros(int(0.3 * RATE), dtype=np.float32)], {}, 0.3
        keys = list(parts)
        for n, key in enumerate(keys):
            samples = mv.voice_line(spoken(parts[key]))
            self.spans[key] = (t, t + len(samples) / RATE)
            pieces.append(samples)
            gap = SECTION_GAP if n + 1 < len(keys) and keys[n + 1] in sections else GAP
            pieces.append(np.zeros(int(gap * RATE), dtype=np.float32))
            t += len(samples) / RATE + gap
        self.samples = np.concatenate(pieces)
        self.length = t
        self.path = work / "voice.wav"
        r.write_mono(self.path, self.samples)
        self.words = short.timed_words(self.path, work)

    def start(self, key):
        return self.spans[key][0]

    def end(self, key):
        return self.spans[key][1]

    def at(self, key, word, nth=1):
        """When a word of a part is said, matched without its punctuation, so "1,000" and "1000" are the same word."""
        def plain(text):
            text = text.strip(".,?:;!$").replace(",", "").lower()
            return NUMBERS.get(text, text)
        first, last = self.spans[key]
        found = [w["at"] for w in self.words if first - 0.3 <= w["at"] <= last + 0.3 and plain(word) in map(plain, w["text"].split())]
        if len(found) < nth:
            raise KeyError(f"'{word}' is not heard in {key}: " + " ".join(w["text"] for w in self.words if first <= w["at"] <= last))
        return found[nth - 1]


# Azure writes small numbers as digits, so a word looked up as "one" is found as "1".
NUMBERS = {"one": "1", "two": "2", "three": "3", "four": "4", "five": "5", "ten": "10"}


class Pages(r.Pages):
    def get(self, name):
        if name not in self.images:
            Image.MAX_IMAGE_PIXELS = None
            self.images[name] = Image.open(WORK / "pages" / f"{name}.png").convert("RGB")
            self.boxes[name] = {**json.loads((WORK / "pages" / f"{name}.json").read_text())["boxes"], **BOXES.get(name, {})}
        return self.images[name], self.boxes[name]


def load_take(name):
    return mw.load(WORK / "takes", name, title=TITLES.get(name, "Cedar Ridge IT - Argo Books"), unsaved_after="saved" if name == "receipt" else None)


# ---------------------------------------------------------------------------------------------------------------- cards

def section_card(number, name, kind):
    frame = qv.backdrop()
    draw = ImageDraw.Draw(frame)
    draw.ellipse((890, 260, 1030, 400), fill=mv.BLUE)
    draw.text((960, 330), str(number), font=mv.font("Bold", 84), fill=(255, 255, 255), anchor="mm")
    draw.text((960, 540), name, font=mv.font("Bold", 130), fill=mv.INK, anchor="mm")
    draw.text((960, 680), kind, font=mv.font("Regular", 52), fill=mv.GREY, anchor="mm")
    return frame


def recap(lit):
    """The five side by side, as a table, with the ones the voice is recommending lit while it talks about them."""
    def frame_at(t):
        frame = qv.backdrop()
        draw = ImageDraw.Draw(frame)
        draw.text((170, 170), "Which one should you pick?", font=mv.font("Bold", 70), fill=mv.INK, anchor="ls")
        heads, xs = ["", "Runs as", "The catch"], [170, 600, 1110]
        for x, head in zip(xs, heads):
            draw.text((x, 260), head, font=mv.font("Bold", 34), fill=mv.GREY, anchor="ls")
        y = 290
        on = next((rows for at, rows in reversed(lit) if t >= at), set())
        for n, (name, kind, catch) in enumerate(PRODUCTS):
            if n in on:
                draw.rounded_rectangle((140, y + 6, 1780, y + 128), radius=18, fill=(219, 234, 254), outline=mv.BLUE, width=4)
            colour = mv.INK if not on or n in on else (148, 163, 184)
            draw.text((xs[0], y + 67), name, font=mv.font("Bold", 50), fill=colour, anchor="lm")
            draw.text((xs[1], y + 67), kind, font=mv.font("Regular", 40), fill=colour, anchor="lm")
            draw.text((xs[2], y + 67), catch, font=mv.font("Regular", 40), fill=colour, anchor="lm")
            y += 136
            draw.line((170, y, 1750, y), fill=qv.LINE, width=2)
        draw.text((170, 1000), "Free plans as listed on each product's own site, November 2026.", font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
        return frame
    return frame_at


def end_card():
    frame = qv.backdrop().convert("RGBA")
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((780, round(780 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((1920 - 780) // 2, 300))
    draw.text((960, 560), "A free accounting app for your Mac. Nothing to sign up for.", font=mv.font("SemiBold", 48), fill=mv.INK, anchor="mm")
    draw.text((960, 650), "argorobots.com", font=mv.font("Bold", 70), fill=mv.BLUE, anchor="mm")
    draw.text((960, 750), "Apple Silicon and Intel, macOS 14 or later. Link in the description.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
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

    lineup = qv.card("The five, for 2026", [
        (1, "Wave: in your browser"), (2, "Zoho Books: Mac App Store app, books in the cloud"), (3, "GnuCash: a Mac app"),
        (4, "Manager: a Mac app"), (5, "Argo Books: a Mac app, and the one I make")], kicker="Free accounting software for Mac")
    qb_desktop = qv.card("QuickBooks Desktop for Mac", [
        ("no", "Not sold to new US customers since September 30, 2024"),
        ("no", "Intuit now sends Mac users to QuickBooks Online"),
        ("no", "QuickBooks Online runs in your browser, from $38 USD a month")],
        footnote="Source: Intuit's QuickBooks Desktop sales article and quickbooks.intuit.com/pricing, November 2026.", kicker="Why it's not on the list")
    qb_free = qv.card("QuickBooks Free, US only", [
        ("no", "2 invoices a month, unless you take QuickBooks Payments"), ("no", "1 user, and no accountant access"),
        ("no", "1 bank account and 3 reports")],
        footnote="Source: quickbooks.intuit.com, November 2026.", kicker="The new free plan")
    argo_catch = qv.card("The catch in Argo Books", [
        ("no", "Monthly limits on the free plan"), ("no", "One person in a company file at a time"),
        ("no", "No automatic bank connection, just statement imports")], kicker="Holding it to the same standard")

    shots = [
        page(0.0, "wave_us", [(0.0, 1.0, (1600, 900)), (1.6, 1.0, (1600, 1000))]),
        page(1.6, "zoho_us", [(1.6, 1.0, (1600, 1300)), (3.2, 1.0, (1600, 1400))]),
        page(3.2, "manager_cloud", [(3.2, 1.0, (1600, 900)), (S("hook2"), 1.0, (1600, 1000))]),
        take(S("hook2") - 0.1, "tour", "dashboard", 0.3, [("dashboard", 0.3, 1.0, 1600, 900), ("dashboard", 9.0, 1.23, 1905, 830)], until=("click_expenses", -0.5)),
        still(S("promise") - 0.1, lineup),

        still(S("qb") - 0.3, qb_desktop),
        still(S("qb_free") - 0.1, qb_free),

        still(S("wave") - 0.3, section_card(1, "Wave", "Runs in your browser")),
        page(W("wave", "runs") - 0.2, "wave_us", [(W("wave", "runs") - 0.2, 1.0, (1600, 900)), (E("wave"), 1.0, (1600, 1300))]),
        page(S("wave_free") - 0.1, "wave_us", [(S("wave_free") - 0.1, 1.0, (1600, 1300)), (W("wave_free", "unlimited"), 1.4, "starter_unlimited"), (E("wave_free") + 0.4, 1.4, "starter_unlimited")],
             marks=[(S("wave_free") + 0.4, "starter_price", COBALT), (W("wave_free", "unlimited"), "starter_unlimited", COBALT)],
             cursor=[(S("wave_free"), "starter_price"), (W("wave_free", "unlimited"), "starter_unlimited")]),
        page(S("wave_catch") - 0.1, "wave_us", [(S("wave_catch") - 0.1, 1.35, "pro_bank"), (W("wave_catch", "scanning"), 1.35, "pro_receipts"), (W("wave_catch", "Pro"), 1.2, "pro_price"), (E("wave_catch") + 0.5, 1.2, "pro_price")],
             marks=[(W("wave_catch", "bank"), "pro_bank", RED), (W("wave_catch", "scanning"), "pro_receipts", RED), (W("wave_catch", "250"), "pro_price", RED)],
             cursor=[(W("wave_catch", "bank"), "pro_bank"), (W("wave_catch", "scanning"), "pro_receipts"), (W("wave_catch", "250"), "pro_price")]),

        still(S("zoho") - 0.3, section_card(2, "Zoho Books", "Mac App Store app, books in the cloud")),
        page(W("zoho", "This") - 0.2, "zoho_mac", [(W("zoho", "This") - 0.2, 1.0, (1600, 900)), (E("zoho") + 0.4, 1.08, (1700, 900))],
             marks=[(W("zoho", "Mac"), "only_mac", COBALT)], cursor=[(W("zoho", "Mac"), "only_mac")]),
        page(S("zoho_free") - 0.1, "zoho_us", [(S("zoho_free") - 0.1, 1.15, "free_card"), (W("zoho_free", "one"), 1.25, "free_users"), (W("zoho_free", "1000") - 0.3, 1.25, "free_users")],
             marks=[(W("zoho_free", "one"), "free_users", COBALT)], cursor=[(S("zoho_free") + 0.2, "free_price"), (W("zoho_free", "one"), "free_users")]),
        page(W("zoho_free", "1000") - 0.3, "zoho_us", [(W("zoho_free", "1000") - 0.3, 1.25, "faq_invoices"), (E("zoho_free") + 0.5, 1.3, "faq_invoices")],
             marks=[(W("zoho_free", "1000"), "faq_invoices", COBALT)], cursor=[(W("zoho_free", "1000"), "faq_invoices")]),
        page(S("zoho_catch") - 0.1, "zoho_us", [(S("zoho_catch") - 0.1, 1.25, "faq_free"), (W("zoho_catch", "And") - 0.3, 1.3, "faq_free")],
             marks=[(W("zoho_catch", "revenue"), "faq_free", RED)], cursor=[(W("zoho_catch", "revenue"), "faq_free")]),
        page(W("zoho_catch", "And") - 0.2, "zoho_us", [(W("zoho_catch", "And") - 0.2, 1.25, "bank_feeds"), (W("zoho_catch", "15"), 1.3, "standard_yearly"), (E("zoho_catch") + 0.5, 1.3, "standard_yearly")],
             marks=[(W("zoho_catch", "bank"), "bank_feeds", RED), (W("zoho_catch", "15"), "standard_yearly", RED)],
             cursor=[(W("zoho_catch", "bank"), "bank_feeds"), (W("zoho_catch", "15"), "standard_yearly")]),

        still(S("gnucash") - 0.3, section_card(3, "GnuCash", "A Mac app, free and open source")),
        page(W("gnucash", "It's") - 0.2, "gnucash_home", [(W("gnucash", "It's") - 0.2, 1.0, (1600, 900)), (W("gnucash", "Apple"), 1.3, "silicon"), (E("gnucash") + 0.4, 1.3, "silicon")],
             marks=[(W("gnucash", "Apple"), "silicon", COBALT)], cursor=[(W("gnucash", "Apple"), "silicon")]),
        page(S("gnucash_catch") - 0.1, "gnucash_features", [(S("gnucash_catch") - 0.1, 1.3, "double_entry"), (W("gnucash_catch", "look"), 1.35, (2400, 1450)), (W("gnucash_catch", "there's") - 0.3, 1.35, (2400, 1450))],
             marks=[(S("gnucash_catch") + 1.2, "double_entry", RED)],
             cursor=[(S("gnucash_catch") + 1.0, "double_entry"), (W("gnucash_catch", "look"), (2300, 1500))]),
        page(W("gnucash_catch", "there's") - 0.2, "gnucash_features", [(W("gnucash_catch", "there's") - 0.2, 1.35, (1800, 5330)), (E("gnucash_catch") + 0.5, 1.4, (1800, 5330))],
             marks=[(W("gnucash_catch", "import"), "import", COBALT)], cursor=[(W("gnucash_catch", "import"), "import")]),

        still(S("manager") - 0.3, section_card(4, "Manager", "A Mac app, free forever")),
        page(W("manager", "The") - 0.2, "manager_download", [(W("manager", "The") - 0.2, 1.1, "free_forever"), (W("manager", "Apple") - 0.3, 1.6, "mac"), (W("manager", "Intel"), 1.6, "intel"), (E("manager") + 0.4, 1.6, "intel")],
             marks=[(W("manager", "free"), "free_forever", COBALT), (W("manager", "Apple"), "mac", COBALT), (W("manager", "Intel"), "intel", COBALT)],
             cursor=[(W("manager", "free"), "free_forever"), (W("manager", "Apple"), "mac"), (W("manager", "Intel"), "intel")]),
        page(S("manager_catch") - 0.1, "manager_download", [(S("manager_catch") - 0.1, 1.3, "single_user"), (W("manager_catch", "If"), 1.3, "single_user")],
             marks=[(W("manager_catch", "one"), "single_user", RED)], cursor=[(W("manager_catch", "one"), "single_user")]),
        page(W("manager_catch", "If") - 0.1, "manager_cloud", [(W("manager_catch", "If") - 0.1, 1.0, (1600, 900)), (W("manager_catch", "59"), 1.25, "price"), (W("manager_catch", "like") - 0.2, 1.25, "price")],
             marks=[(W("manager_catch", "59"), "price", RED)], cursor=[(W("manager_catch", "59"), "price")]),
        page(W("manager_catch", "like") - 0.1, "manager_home", [(W("manager_catch", "like") - 0.1, 1.0, (1600, 1700)), (E("manager_catch") + 0.5, 1.12, (1600, 1800))]),

        still(S("argo") - 0.3, section_card(5, "Argo Books", "A Mac app, and the one I make")),
        page(W("argo", "It's") - 0.2, "argo_downloads_mac", [(W("argo", "It's") - 0.2, 1.0, (1600, 1000)), (W("argo", "Apple"), 1.35, "silicon"), (W("argo", "Homebrew") - 0.4, 1.35, "brew"), (E("argo") + 0.4, 1.4, "brew")],
             marks=[(W("argo", "Apple"), "silicon", COBALT), (W("argo", "Intel"), "intel", COBALT), (W("argo", "14"), "mac_card", COBALT), (W("argo", "Homebrew"), "brew", COBALT)],
             cursor=[(W("argo", "Apple"), "silicon"), (W("argo", "Intel"), "intel"), (W("argo", "14"), "mac_card"), (W("argo", "Homebrew"), "brew")]),
        take(S("argo_account") - 0.1, "welcome", "start", 0.0, [("start", 0.0, 1.0, 1600, 900), ("hover_create", 0.0, 1.25, 1450, 950)]),
        take(S("argo_dash") - 0.1, "tour", "dashboard", 0.3, [("dashboard", 0.3, 1.0, 1600, 900), ("dashboard", 6.0, 1.23, 1905, 830)], until=("click_expenses", -0.5)),
        take(W("argo_dash", "reports") - 0.2, "tour", "reports", 0.3, [("reports", 0.3, 1.0, 1600, 900), ("reports", 5.0, 1.1, 1700, 860)], until=("click_insights", -0.5)),
        take(S("argo_receipt") - 0.1, "receipt", "drop_zone", 0.3, [("drop_zone", 0.3, 1.35, 1600, 960)], until=("scanning", 0.6)),
        take(W("argo_receipt", "It") - 0.2, "receipt", "scan_done", 0.3, [("scan_done", 0.3, 1.15, 1600, 930), ("look_total", 0.0, 1.45, 2200, 600), ("look_items", 0.4, 1.3, 2000, 1250)], until=("click_products", -0.4)),
        take(S("argo_bank") - 0.1, "bank", "prompt", 0.0, [("prompt", 0.0, 1.3, 1600, 920), ("no_login", 0.3, 1.5, 1600, 900)], until=("click_choose", -0.3)),
        page(S("argo_free") - 0.1, "argo_pricing", [(S("argo_free") - 0.1, 1.15, "free_card"), (W("argo_free", "10"), 1.3, "free_scans"), (E("argo_free") + 0.4, 1.3, "free_bank")],
             marks=[(W("argo_free", "25"), "free_invoices", COBALT), (W("argo_free", "10"), "free_scans", COBALT), (W("argo_free", "25", nth=2), "free_bank", COBALT)],
             cursor=[(W("argo_free", "25"), "free_invoices"), (W("argo_free", "10"), "free_scans"), (W("argo_free", "25", nth=2), "free_bank")]),
        page(S("argo_catch") - 0.1, "argo_pricing", [(S("argo_catch") - 0.1, 1.1, (1600, 1500)), (W("argo_catch", "15"), 1.3, "premium_price"), (W("argo_catch", "also") - 0.4, 1.3, "premium_price")],
             marks=[(W("argo_catch", "15"), "premium_price", RED)], cursor=[(W("argo_catch", "15"), "premium_price")]),
        still(W("argo_catch", "also") - 0.3, argo_catch),

        still(S("pick") - 0.3, recap([(W("pick", "limits") + 0.3, {2, 3}), (W("pick", "Wave"), {0, 1}), (W("pick", "Argo"), {4})])),
        still(S("ask") - 0.1, end_card()),
    ]
    shots.sort(key=lambda s: s["at"])
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def recap_still(shot, age):
    still = shot["still"]
    return still(age + shot["at"]) if callable(still) else still


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
        if "take" in shot:
            take = load_take(shot["take"])
            painter = mv.Painter("wide", take, [], [])
            start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
            end = shot.get("until")
            played = count if not end else min(count, max(1, round((take["marks"][end[0]] + end[1]) * FPS) - start))
            part = {"wide": shot["keys"], "frames": count}
            source = mw.frames(take, start, played)
            spoken.append((shot["at"], start / FPS, played / FPS, take["events"]))
        for age in range(count):
            if "take" in shot:
                if age < played:
                    pixels = next(source)
                index = start + min(age, played - 1)
                frame = painter.app_frame(pixels, part, 0, index, (start + age) / FPS).convert("RGB")
            elif "page" in shot:
                frame = r.page_frame(shot, shot["at"] + age / FPS, pages, sprites, SIZE).convert("RGB")
            else:
                frame = recap_still(shot, age / FPS)
            if last is not None and age < FADE_FRAMES:
                frame = Image.blend(last, frame, (age + 1) / (FADE_FRAMES + 1))
            encoder.stdin.write(frame.tobytes())
        last = frame
        print(f"  {shot['at']:6.1f} s done", flush=True)
    encoder.stdin.close()
    encoder.wait()
    return spoken


def mix(v, spoken, total):
    n = int(total * RATE) + RATE
    voice = np.pad(v.samples, (0, max(0, n - len(v.samples))))[:n]

    def rms(x):
        return np.sqrt(np.mean(x * x) + 1e-12)

    held = mv.window_sum(np.abs(voice) > 10 ** (-40 / 20), 0.3) > 0.5
    track = mv.decode(mv.MUSIC).mean(axis=1)
    while len(track) < n:
        track = np.concatenate([track, track])
    bed = track[:n]
    level = rms(voice[held]) / rms(bed) * 10 ** (qv.MUSIC_BETWEEN_DB / 20)
    duck = mv.smooth(np.where(held, 10 ** ((qv.MUSIC_UNDER_VOICE_DB - qv.MUSIC_BETWEEN_DB) / 20), 1.0), 0.2)
    outro_at = int((v.length + 1.0) * RATE)
    fade = np.clip((outro_at + 1.5 * RATE - np.arange(n)) / (2.5 * RATE), 0, 1)
    bed = bed * level * duck * fade

    effects = np.zeros(n)
    for at, start, played, events in spoken:
        for event in events:
            kind = {"click": "click", "drop": "drop"}.get(event["kind"])
            if kind and start <= event["t"] < start + played:
                s = mv.sound(kind) * 10 ** (mv.SOUND_GAIN_DB[kind] / 20)
                i = int((at + event["t"] - start) * RATE)
                effects[i:i + len(s)] += s[: max(0, n - i)]

    tail = np.zeros(n)
    outro = mv.decode(r.OUTRO, channels=1)[:, 0]
    tail[outro_at:outro_at + len(outro)] = outro[: max(0, n - outro_at)]

    def db(x):
        return 20 * np.log10(rms(x))
    print(f"levels (RMS): voice {db(voice[held]):.1f} dB, music under it {db(bed[held]):.1f} dB, between lines {db(bed[~held & (fade > 0.99)]):.1f} dB")
    mono = voice + effects + bed + tail
    return np.stack([mono, mono], axis=1)


def write_captions(path, v, parts=PARTS):
    """The script itself, a short line at a time, timed by how far through its part each line falls."""
    def stamp(seconds):
        ms = int(round(seconds * 1000))
        return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"

    blocks = []
    for key, text in parts.items():
        first, last = v.spans[key]
        lines, line = [], []
        for word in text.split():
            line.append(word)
            if len(line) >= 10 or word.endswith((".", "?", ":")):
                lines.append(" ".join(line))
                line = []
        if line:
            lines.append(" ".join(line))
        done = 0
        for caption in lines:
            start = first + (last - first) * done / len(text)
            done += len(caption) + 1
            blocks.append((start, first + (last - first) * min(1, done / len(text)), caption))
    path.write_text("\n".join(f"{n}\n{stamp(a)} --> {stamp(b)}\n{c}\n" for n, (a, b, c) in enumerate(blocks, 1)), encoding="utf-8")


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
        pixels = next(mw.frames(take, index, 1))
        return painter.app_frame(pixels, {"wide": shot["keys"], "frames": 1}, 0, index, (start + round(age * FPS)) / FPS).convert("RGB")
    if "page" in shot:
        return r.page_frame(shot, second, pages, sprites, SIZE).convert("RGB")
    if "still" in shot:
        return recap_still(shot, age)
    return None


def clock(seconds):
    return f"{int(seconds) // 60}:{int(seconds) % 60:02}"


def main():
    v = Voice()
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
    mv.write_wav(mixed, mix(v, spoken, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    write_captions(OUT_FILE.with_suffix(".srt"), v)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    for key, name in CHAPTERS:
        print(f"{clock(0 if key == 'hook' else v.start(key))} - {name}")


if __name__ == "__main__":
    main()

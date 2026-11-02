"""Make the vertical Short "Still keeping your books in a spreadsheet?": a made-up spreadsheet, then the app's pages doing
the same jobs, narrated by an Azure voice, with big captions timed to each word.

Usage: python spreadsheet_short.py

The voice is fetched once and kept in work/voice/, and the words' timings in work/spreadsheet_short/. The take comes from
work/roundup/takes, so record it first. The finished video goes to work/videos/.
"""
from pathlib import Path

from PIL import Image, ImageDraw

import catches_short as short
import make_video as mv
import pc_short as pc
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "spreadsheet_short"
OUT_FILE = HERE / "work" / "videos" / "short_spreadsheet.mp4"
SCRIPT = """Still keeping your books in a spreadsheet?
Here's what you're working out by hand.
Your profit, every time you make a sale.
Who owes you money, and who's overdue.
Where your money comes from, and where it goes.
Reports for your accountant.
Argo Books does all of that for you, and it can import the spreadsheet you already have.
Free for Windows, Mac and Linux."""
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 90.0
# A made-up ledger, whose total has gone wrong the way a spreadsheet's does when a row is deleted.
SHEET = [
    ("Date", "Customer", "Invoice", "Paid?", "Amount"),
    ("Oct 2", "Harbor Dental", "1041", "yes", "1,850.00"),
    ("Oct 6", "Maple St Bakery", "1042", "yes", "1,240.00"),
    ("Oct 9", "fuel", "", "", "-64.12"),
    ("Oct 12", "Lakeside Law", "1043", "??", "2,600.00"),
    ("Oct 14", "toner + paper", "", "", "-238.80"),
    ("Oct 20", "Harbor Dental", "1044", "no", "1,850.00"),
    ("Oct 21", "CHECK THIS", "", "", ""),
    ("", "TOTAL", "", "", "#REF!"),
]
COLUMNS = (0, 150, 460, 620, 760, 1000)
LINE = (203, 213, 225, 255)
RED = (220, 38, 38, 255)
PUSH_IN = 1.45


def sheet_still():
    """The spreadsheet on the Short's background, in the window the app's pages use."""
    frame = short.background()
    x0, y0, _, _ = short.VIEW
    draw = ImageDraw.Draw(frame)
    left, top, row_h = x0 + 40, y0 + 60, 88
    font, bold = mv.font("Regular", 34), mv.font("Bold", 34)
    bottom = top + row_h * (len(SHEET) + 1)
    draw.rectangle((left, top, left + COLUMNS[-1], bottom), fill=(255, 255, 255, 255))
    draw.rectangle((left, top, left + COLUMNS[-1], top + row_h), fill=(226, 232, 240, 255))
    for n in range(len(COLUMNS) - 1):
        centre = left + (COLUMNS[n] + COLUMNS[n + 1]) / 2
        draw.text((centre, top + row_h / 2), "ABCDE"[n], font=bold, fill=mv.GREY + (255,), anchor="mm")
    for y, row in enumerate(SHEET):
        row_top = top + row_h * (y + 1)
        for n, text in enumerate(row):
            broken = text == "#REF!"
            cell = (left + COLUMNS[n], row_top, left + COLUMNS[n + 1], row_top + row_h)
            if broken:
                draw.rectangle(cell, fill=(254, 226, 226, 255))
            draw.text((cell[0] + 14, row_top + row_h / 2), text, font=bold if y == 0 or broken or text == "TOTAL" else font,
                      fill=RED if broken else mv.INK + (255,), anchor="lm")
    for n in range(len(SHEET) + 2):
        draw.line((left, top + n * row_h, left + COLUMNS[-1], top + n * row_h), fill=LINE, width=2)
    for x in COLUMNS:
        draw.line((left + x, top, left + x, bottom), fill=LINE, width=2)
    return frame


def pushing_in(still, length):
    """The still at any moment of a slow move towards the broken total in its bottom right corner."""
    x0, y0, width, height = short.VIEW
    view = still.crop((x0, y0, x0 + width, y0 + height))

    def frame_at(age):
        scale = 1 + (PUSH_IN - 1) * r.ease(age / length)
        box = (width * (1 - 1 / scale), height * (1 - 1 / scale) * 0.9, width, height - height * (1 - 1 / scale) * 0.1)
        frame = still.copy()
        frame.paste(view.resize((width, height), Image.LANCZOS, box=box), (x0, y0))
        return frame
    return frame_at


def shot_list(words, total):
    def at(text):
        return next(w["at"] for w in words if w["text"].strip(".,?").lower() == text.lower())

    return [
        {"at": 0.0, "still": pushing_in(sheet_still(), at("profit") - 0.35), "title": "Still using a spreadsheet?"},
        {"at": at("profit") - 0.35, "take": "tour", "from": ("dashboard", 1.0), "title": "Your profit",
         "keys": [("dashboard", 1.0, 1.0, 1240, 1000), ("dashboard", 5.0, 1.12, 1240, 1130)]},
        {"at": at("Who") - 0.1, "take": "tour", "from": ("invoices", 0.4), "title": "Who owes you",
         "keys": [("invoices", 0.4, 0.8, 1800, 950), ("invoices", 4.0, 0.86, 1760, 1020)]},
        {"at": at("Where") - 0.1, "take": "tour", "from": ("analytics", 0.4), "title": "Where your money goes",
         "keys": [("analytics", 0.4, 0.85, 1900, 1000), ("analytics", 5.0, 0.95, 2300, 1000)]},
        {"at": at("Reports") - 0.1, "take": "tour", "from": ("reports", 0.4), "title": "Reports, ready to send",
         "keys": [("reports", 0.4, 0.95, 1420, 950)]},
        {"at": at("Argo") - 0.1, "take": "tour", "from": ("revenue", 0.4), "title": "Bring your spreadsheet",
         "keys": [("revenue", 0.4, 0.9, 2050, 900), ("revenue", 2.0, 0.9, 2050, 900), ("revenue", 5.4, 1.35, 2450, 760)]},
        {"at": at("Free") - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(SCRIPT))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.caption_chunks(words, longest_pause=0.4)
    pc.finish(shot_list(words, total), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM)


if __name__ == "__main__":
    main()

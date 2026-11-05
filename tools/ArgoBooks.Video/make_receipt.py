"""Draws the receipt the video scans: a made-up office supply store's till slip, photographed on a desk. The store is
in Ontario because the sample company keeps its books in Canadian dollars.

Usage: python make_receipt.py OUT.jpg [YYYY-MM-DD]
The date defaults to yesterday, so the expense it becomes is the newest one in the sample company.
"""
import sys
from datetime import date, timedelta
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

STORE = ["BRIGHTLINE OFFICE SUPPLY", "418 Maple Ridge Rd, Oakville ON", "(905) 555-0148"]
ITEMS = [
    ("COPY PAPER 10 REAMS", 54.90),
    ("TONER CARTRIDGE 26A", 89.99),
    ("HDMI CABLE 6FT", 29.97, "3 @ 9.99"),
    ("STICKY NOTES 12PK", 11.49),
    ("LABEL TAPE 2PK", 24.98),
]
TAX_RATE = 0.13
MONO = "C:/Windows/Fonts/cour.ttf"
MONO_BOLD = "C:/Windows/Fonts/courbd.ttf"


def totals():
    subtotal = round(sum(item[1] for item in ITEMS), 2)
    tax = round(subtotal * TAX_RATE + 1e-9, 2)
    return subtotal, tax, round(subtotal + tax, 2)


def paper(day):
    width, margin = 760, 56
    font = ImageFont.truetype(MONO, 27)
    bold = ImageFont.truetype(MONO_BOLD, 30)
    big = ImageFont.truetype(MONO_BOLD, 36)
    lines = []

    def centred(text, f=font, gap=40):
        lines.append(("c", text, f, gap))

    def row(left, right, f=font, gap=40):
        lines.append(("r", (left, right), f, gap))

    centred(STORE[0], big, 54)
    centred(STORE[1])
    centred(STORE[2], gap=64)
    row(f"DATE {day:%m/%d/%Y}", "14:32", gap=40)
    row("STORE 0612  REG 3", "TRANS 48213", gap=34)
    lines.append(("line", None, None, 40))
    for item in ITEMS:
        row(item[0], f"{item[1]:.2f}")
        if len(item) > 2:
            row("   " + item[2], "", gap=40)
    lines.append(("line", None, None, 40))
    subtotal, tax, total = totals()
    row("SUBTOTAL", f"{subtotal:.2f}")
    row(f"HST {TAX_RATE * 100:.0f}%", f"{tax:.2f}", gap=50)
    row("TOTAL", f"{total:.2f}", bold, 56)
    row("VISA ************1187", f"{total:.2f}", gap=64)
    centred("THANK YOU FOR SHOPPING", gap=36)
    centred("WITH BRIGHTLINE!", gap=40)

    height = 70 + sum(line[3] for line in lines) + 60
    sheet = Image.new("RGB", (width, height), (250, 249, 245))
    draw = ImageDraw.Draw(sheet)
    y = 70
    for kind, text, f, gap in lines:
        ink = (38, 38, 42)
        if kind == "c":
            draw.text((width / 2, y), text, font=f, fill=ink, anchor="ma")
        elif kind == "r":
            draw.text((margin, y), text[0], font=f, fill=ink)
            draw.text((width - margin, y), text[1], font=f, fill=ink, anchor="ra")
        else:
            for x in range(margin, width - margin, 14):
                draw.line((x, y + 14, x + 7, y + 14), fill=ink, width=2)
        y += gap
    return sheet


def photo(sheet, out):
    w, h = 1080, 1440
    desk = Image.new("RGB", (w, h))
    px = desk.load()
    for yy in range(h):
        for xx in range(0, w):
            shade = 92 + int(28 * (1 - ((xx - w * 0.45) ** 2 + (yy - h * 0.4) ** 2) ** 0.5 / 1100))
            px[xx, yy] = (shade + 8, shade, shade - 10)
    noise = Image.effect_noise((w, h), 9).convert("RGB")
    desk = Image.blend(desk, noise, 0.06)

    # A slight warm wash and a soft gradient across the paper, so it reads as lit from the window side.
    lit = Image.new("RGB", sheet.size, (255, 250, 238))
    sheet = Image.blend(sheet, lit, 0.08)
    shade = Image.linear_gradient("L").rotate(90, expand=True).resize(sheet.size)
    sheet = Image.composite(sheet, Image.blend(sheet, Image.new("RGB", sheet.size, (205, 203, 198)), 0.5), shade.point(lambda v: 255 - v // 3))

    angle = -1.5
    paper_rgba = sheet.convert("RGBA").rotate(angle, resample=Image.BICUBIC, expand=True)
    shadow = Image.new("RGBA", paper_rgba.size, (0, 0, 0, 0))
    shadow.putalpha(paper_rgba.getchannel("A").point(lambda v: v * 0.55))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    x = (w - paper_rgba.width) // 2
    y = (h - paper_rgba.height) // 2
    desk.paste((0, 0, 0), (x + 16, y + 22), shadow)
    desk.paste(paper_rgba, (x, y), paper_rgba)
    desk = desk.filter(ImageFilter.GaussianBlur(0.6))
    desk.save(out, quality=92)


def main():
    out = Path(sys.argv[1])
    day = date.fromisoformat(sys.argv[2]) if len(sys.argv) > 2 else date.today() - timedelta(days=1)
    out.parent.mkdir(parents=True, exist_ok=True)
    photo(paper(day), out)
    subtotal, tax, total = totals()
    print(f"{out}: {day}, subtotal {subtotal:.2f}, tax {tax:.2f}, total {total:.2f}")


if __name__ == "__main__":
    main()

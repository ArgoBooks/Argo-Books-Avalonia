"""Make the vertical Short "Free invoice generator": an invoice typed into argorobots.com's free invoice generator and
downloaded as a PDF, narrated by the Azure voice, with big captions timed to each word.

Usage: python invoice_short.py

Record the take first with `node record_generator.js invoice work/invoice/takes`. The finished video goes to work/videos/.
estimate_short.py makes its Short with the same pieces.
"""
import bisect
import json
import subprocess
from pathlib import Path

import fitz
from PIL import Image, ImageDraw, ImageFilter

import catches_short as short
import make_video as mv
import pc_short as pc
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "invoice"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_free_invoice_generator.mp4"
FPS = mv.FPS
# Each line, and the pause after it, long enough for the typing its shot shows.
LINES = [
    ("Need a free invoice generator? No sign up, no limits.", 450),
    ("Type your business name.", 650),
    ("Then who you're billing.", 1000),
    ("What you did, and your rate.", 1000),
    ("Add as many lines as you need.", 500),
    ("It does all the math for you.", 800),
    ("Pick the template you like best.", 1100),
    ("And download it as a PDF.", 250),
    ("Your invoice never leaves your browser.", 500),
    ("It's free, from Argo Books.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 60.0


def spoken(lines=LINES):
    return "".join(text + (f"<break time='{pause}ms'/>" if pause else "") for text, pause in lines)


def build_take(takes=TAKES, name="invoice"):
    """Turns the recorded pictures and the hand's path into a take like the app's: a video at 30 frames a second and its log."""
    session_file, video, log_file = takes / f"{name}_session.json", takes / f"{name}.mkv", takes / f"{name}.json"
    if log_file.exists() and video.exists() and log_file.stat().st_mtime > session_file.stat().st_mtime:
        return
    session = json.loads(session_file.read_text())
    scale, (w, h) = session["scale"], session["size"]
    shot_times = [shot["t"] for shot in session["shots"]]
    path = sorted(session["path"], key=lambda p: p["t"])
    path_times = [p["t"] for p in path]
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{w}x{h}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264rgb", "-preset", "ultrafast", "-crf", "0", str(video)],
                               stdin=subprocess.PIPE)
    frames, shown, pixels = [], None, None
    for i in range(round(session["marks"]["end"] * FPS) + 1):
        t = i / FPS
        n = bisect.bisect_right(shot_times, t + 1e-6) - 1
        if n != shown:
            pixels = Image.open(takes / "frames" / session["shots"][n]["file"]).convert("RGB").tobytes()
            shown = n
        encoder.stdin.write(pixels)
        k = bisect.bisect_right(path_times, t + 1e-6) - 1
        a, b = path[k], path[min(k + 1, len(path) - 1)]
        p = (t - a["t"]) / (b["t"] - a["t"]) if b["t"] > a["t"] else 0
        frames.append([round((a["x"] + (b["x"] - a["x"]) * p) * scale, 1), round((a["y"] + (b["y"] - a["y"]) * p) * scale, 1), a["down"], None])
    encoder.stdin.close()
    encoder.wait()
    log = {"fps": FPS, "scale": scale, "frames": frames, "events": session["events"], "marks": session["marks"], "places": session["places"]}
    log_file.write_text(json.dumps(log))


def take_loader(takes):
    def load_take(name):
        log = json.loads((takes / f"{name}.json").read_text())
        log["path"] = takes / f"{name}.mkv"
        return log
    return load_take


load_take = take_loader(TAKES)


def prepare(takes, name):
    """Makes the take if the recording is newer, and has the painter read it at its own size, which is taller than the app's."""
    build_take(takes, name)
    mv.SOURCE = tuple(json.loads((takes / f"{name}_session.json").read_text())["size"])
    return take_loader(takes)(name)


def pdf_card(pdf):
    """The PDF the generator saved, cut below the invoice so the empty half of the page is left out, on a soft shadow."""
    page = fitz.open(pdf)[0]
    pixmap = page.get_pixmap(dpi=200)
    paper = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    grey = paper.convert("L").point(lambda v: 255 if v < 235 else 0)
    # The footer line sits in the bottom tenth of the page, so the invoice ends at the last ink above it.
    rows = [y for y in range(round(paper.height * 0.9)) if grey.crop((0, y, paper.width, y + 1)).getbbox()]
    paper = paper.crop((0, 0, paper.width, rows[-1] + round(paper.width * 0.06)))
    width = 940
    paper = paper.resize((width, round(paper.height * width / paper.width)), Image.LANCZOS)
    card = Image.new("RGBA", (paper.width + 80, paper.height + 80), (0, 0, 0, 0))
    card.paste((0, 0, 0, 60), (40, 50, 40 + paper.width, 50 + paper.height))
    card = card.filter(ImageFilter.GaussianBlur(16))
    card.paste(paper, (40, 40))
    return card


def pdf_still(base, pdf=TAKES / "invoice.pdf", name="invoice-1.pdf"):
    """The PDF, under the name the generator saves it with."""
    card = pdf_card(pdf)
    label_font = mv.font("SemiBold", 34)
    vx, vy, vw, vh = short.VIEW

    def frame(age):
        out = base.copy()
        grow = 0.965 + 0.035 * r.ease(age / 0.3) + 0.03 * min(1.0, age / 4)
        shown = card.resize((round(card.width * grow), round(card.height * grow)), Image.LANCZOS)
        top = vy + 90 + (vh - 90 - shown.height) // 2
        out.alpha_composite(shown, ((short.SIZE[0] - shown.width) // 2, top))
        draw = ImageDraw.Draw(out)
        chip_w = draw.textlength(name, font=label_font) + 56
        draw.rounded_rectangle(((short.SIZE[0] - chip_w) / 2, vy + 10, (short.SIZE[0] + chip_w) / 2, vy + 72), radius=31,
                               fill=(255, 255, 255, 255), outline=(226, 232, 240, 255), width=2)
        draw.text((short.SIZE[0] / 2, vy + 41), name, font=label_font, fill=mv.GREY + (255,), anchor="mm")
        return out
    return frame


def end_card_saying(line):
    """The end card: the logo, what the tool is, and the website."""
    def end_card(base, previous, age):
        frame = base.copy()
        draw = ImageDraw.Draw(frame)
        mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
        mark = mark.resize((840, round(840 * mark.height / mark.width)), Image.LANCZOS)
        frame.alpha_composite(mark, ((short.SIZE[0] - 840) // 2, 620))
        w = short.SIZE[0] / 2
        draw.text((w, 860), line, font=mv.font("Regular", 50), fill=mv.GREY + (255,), anchor="mm")
        draw.text((w, 960), "argorobots.com", font=mv.font("Bold", 68), fill=mv.BLUE + (255,), anchor="mm")
        fade = r.ease(age / 0.35)
        return Image.blend(previous, frame, fade) if fade < 1 and previous is not None else frame
    return end_card


def fit(take, names, pad=60, most=1.6, closer=1.0, below=None):
    """The camera's zoom and centre that show the named parts of the page whole, with room around them, and with the
    top of the picture under the part named `below`, so that part is not cut in half at the edge."""
    boxes = [take["places"][name] for name in names]
    x0, y0 = min(b[0] for b in boxes) - pad, min(b[1] for b in boxes) - pad
    x1, y1 = max(b[0] + b[2] for b in boxes) + pad, max(b[1] + b[3] for b in boxes) + pad
    aspect = short.VIEW[2] / short.VIEW[3]
    zoom = min(most, mv.TALL_SLICE / (x1 - x0), mv.TALL_SLICE / ((y1 - y0) * aspect)) * closer
    cy = (y0 + y1) / 2
    if below:
        _, top, _, height = take["places"][below]
        cy = max(cy, top + height + 20 + mv.TALL_SLICE / zoom / aspect / 2)
    return zoom, (x0 + x1) / 2, cy


def whole_page(take):
    """The camera standing back to show the document's whole width. It runs below the window, so its height is left out."""
    x, _, w, _ = take["places"]["invoice"]
    return mv.TALL_SLICE / (w + 60), x + w / 2, mv.SOURCE[1] / 2


def shot_maker(words, take_name):
    """Finds when a word is said, and makes a shot of the take that starts on a word, with its camera keys."""
    def at(text, nth=1):
        matches = [w["at"] for w in words if w["text"].strip(".,?").lower() == text.lower()]
        return matches[nth - 1]

    def shot(word, mark, offset, title, keys, nth=1):
        return {"at": at(word, nth) - 0.1, "take": take_name, "from": (mark, offset), "title": title,
                "keys": [(key_mark, key_offset, *framing) for key_mark, key_offset, framing in keys]}
    return at, shot


def shot_list(words, total, base, take):
    at, shot = shot_maker(words, "invoice")
    whole = whole_page(take)
    templates = shot("Pick", "click_elegant", -0.35, "5 templates", [("click_elegant", -0.35, whole)])
    # The download follows straight on from the templates, so the hand carries on to the button without a jump.
    carry_on = ("click_elegant", -0.35 + at("And", 2) - at("Pick"))
    return [
        {**shot("Need", "top", 0.0, "Free invoice generator", [("top", 0.0, fit(take, ["hero_title", "tagline"], pad=90)),
                                                               ("scroll", 0.0, fit(take, ["hero_title", "tagline"], pad=90, closer=1.08))]), "at": 0.0},
        shot("Type", "click_business", -0.45, "No sign up, just type",
             [("click_business", -0.45, fit(take, ["logo", "business"], pad=50)), ("business_done", 0.3, fit(take, ["logo", "business"], pad=50, closer=1.05))]),
        shot("Then", "click_billto", -0.45, "Add your client",
             [("click_billto", -0.45, fit(take, ["from", "billto"], below="business")),
              ("billto_done", 0.3, fit(take, ["from", "billto"], closer=1.05, below="business"))]),
        shot("What", "click_desc", -0.35, "Add your work",
             [("click_desc", -0.35, fit(take, ["desc"])), ("click_rate", -0.6, fit(take, ["desc"])),
              ("click_rate", 0.15, fit(take, ["rate", "amount", "total1"], most=1.35))]),
        shot("Add", "click_add", -0.4, "As many lines as you need",
             [("click_add", -0.4, fit(take, ["add", "desc2"])), ("click_qty2", 0.0, fit(take, ["add", "desc2"], closer=1.04))]),
        shot("It", "click_qty2", -0.15, "Totals worked out for you",
             [("click_qty2", -0.15, fit(take, ["qty2", "rate2", "totals"], pad=50)), ("look_total", 0.0, fit(take, ["totals"]))]),
        templates,
        shot("And", *carry_on, "Download PDF or Word", [(*carry_on, whole), ("click_pdf", -0.1, fit(take, ["pdf", "share"], pad=40))], nth=2),
        {"at": at("PDF") - 0.05, "still": pdf_still(base), "title": "Ready to send"},
        {"at": at("It's") - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    take = prepare(TAKES, "invoice")
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(spoken()))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.caption_chunks(words, longest_pause=0.4)
    pc.finish(shot_list(words, total, short.background(), take), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=end_card_saying("Free invoice generator"))


if __name__ == "__main__":
    main()

"""Make the marketing videos: record the app headless, then cut the take with a voiceover, captions, interface sounds and
music into a 16:9 video and a vertical one for Shorts, Reels and TikTok.

Usage:
  python make_video.py              records the take if it is missing, then renders both cuts
  python make_video.py record       records the take again (a scan is replayed from work/scan-cache when it can be)
  python make_video.py render       renders both cuts from the take already recorded
  python make_video.py render wide  renders one cut: wide or tall

Everything made goes to work/, which git ignores; the finished videos are in work/videos/. The voiceover comes from
Azure Speech, with AZURE_SPEECH_KEY and AZURE_SPEECH_REGION read from .env beside this file, and each line is kept in
work/voice/ so it is only paid for once. Credits for the music and font are in assets/CREDITS.txt.
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import urllib.request
import wave
from datetime import date, timedelta
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
ASSETS = HERE / "assets"
WORK = HERE / "work"
TAKES = WORK / "takes"
VIDEOS = WORK / "videos"
FPS = 30
RATE = 48000
SOURCE = (3200, 1800)
VOICE = "en-US-Andrew:DragonHDLatestNeural"
MUSIC = ASSETS / "music_wallpaper.mp3"
# Seconds into the track where the edit starts it, and its beat, measured from the track itself.
MUSIC_FROM = 0.0
BLUE = (37, 99, 235)
INK = (17, 24, 39)
GREY = (75, 85, 99)

# The voiceover, a line at a time. Each line starts where the edit below names it.
LINES = {
    "hook": "Still typing receipts in by hand?",
    "scan": "With Argo Books, you just scan them.",
    "drop": "Drop in a photo,",
    "read": "and A.I. reads the whole receipt for you.",
    "fields": "The date, the total, the tax, and every line item, filled in.",
    "new": "New products and suppliers? One click.",
    "save": "Then save it as an expense,",
    "attached": "with the receipt attached, ready for tax time.",
    "end": "Argo Books. Free for Windows, Mac and Linux.",
}
# What the captions show for a line, where it is written differently from how it is said.
CAPTIONS = {"read": "and AI reads the whole receipt for you."}

# The cut list. A take part plays the take from one mark to another, each with an offset in seconds. `say` starts a
# line at a number of seconds into the part, or at a mark of the take. `fade` dissolves into the part over that many
# seconds, for cuts that skip time. The photo and end parts are drawn here rather than recorded.
#
# `wide` and `tall` are where the camera is at moments of the take, for each cut: (mark, offset, zoom, x, y), with x
# and y the point it centres on in the take's pixels. Between two of them the camera eases from one to the other, and
# it holds before the first and after the last. Each part starts where the one before it ended, so the picture never
# jumps at a cut, and the camera never follows the cursor, so it holds still while the hand moves.
EDIT = [
    {"photo": 2.6, "say": [("hook", 0.25)]},
    {"from": ("dashboard", 0.2), "to": ("dropped", 0.9), "say": [("scan", 0.2), ("drop", ("drop_zone", 0.4))], "fade": 0.3,
     "wide": [("dashboard", 0, 1.0, 1600, 900), ("click_scan", -0.4, 1.12, 2222, 800),
              ("click_scan", 0.2, 1.12, 2222, 800), ("drop_zone", 0.6, 1.3, 1600, 930)],
     "tall": [("click_scan", 0.2, 1.0, 2222, 820), ("drop_zone", 0.6, 1.0, 1600, 930)]},
    {"from": ("click_scan_all", -0.5), "to": ("scanning", 1.0), "say": [("read", 0.3)], "fade": 0.2,
     "wide": [("click_scan_all", 0, 1.3, 1600, 930)],
     "tall": [("click_scan_all", 0, 1.0, 1600, 930)]},
    {"from": ("scan_done", -0.1), "to": ("click_products", 0.9), "say": [("fields", 1.4)], "fade": 0.25,
     "wide": [("scan_done", 0, 1.3, 1600, 930), ("scan_done", 1.8, 1.18, 2000, 760)],
     "tall": [("scan_done", 0, 1.0, 1600, 930), ("scan_done", 1.8, 1.0, 2007, 880)]},
    {"from": ("click_supplier", -1.6), "to": ("saved", 0.5), "say": [("new", -1.6), ("save", 2.5)], "fade": 0.25,
     "wide": [("click_supplier", -1.6, 1.18, 2000, 760), ("click_supplier", -0.5, 1.12, 2050, 1200),
              ("click_create", 0.2, 1.12, 2050, 1200), ("saved", 0.2, 1.0, 1600, 900)],
     "tall": [("click_supplier", -1.6, 1.0, 2007, 880), ("click_supplier", -0.5, 1.0, 2050, 1100),
              ("click_create", 0.2, 1.0, 2050, 1100), ("saved", 0.2, 1.0, 1500, 900)]},
    {"from": ("click_expenses", -0.5), "to": ("end", -0.3), "say": [("attached", 0.5)], "fade": 0.2,
     "wide": [("expenses", 0.6, 1.0, 1600, 900), ("end", -0.5, 1.3, 1700, 800)],
     "tall": [("expenses", 0, 1.0, 1500, 900)]},
    {"end": 4.6, "say": [("end", 0.5)]},
]
CUTS = {
    "wide": {"size": (1920, 1080), "file": "argo_books_receipt_scanning.mp4"},
    "tall": {"size": (1080, 1920), "file": "argo_books_receipt_scanning_vertical.mp4"},
}
# The tall cut's window onto the app: its place in the frame and how wide a slice of the take it shows at zoom 1.
TALL_VIEW = (0, 400, 1080, 1215)
TALL_SLICE = 1500


def ffmpeg_path(name="ffmpeg"):
    found = shutil.which(name)
    if found:
        return found
    winget = Path(os.environ["LOCALAPPDATA"]) / "Microsoft" / "WinGet" / "Packages"
    for exe in winget.glob(f"Gyan.FFmpeg*/**/bin/{name}.exe"):
        return str(exe)
    sys.exit(f"{name} not found; install it with: winget install Gyan.FFmpeg")


FFMPEG = ffmpeg_path()


def font(weight, size):
    return ImageFont.truetype(str(ASSETS / f"Inter-{weight}.ttf"), size)


# ---------------------------------------------------------------------------------------------------------- recording

def record():
    receipt = WORK / "receipt.jpg"
    subprocess.run([sys.executable, str(HERE / "make_receipt.py"), str(receipt)], check=True)
    subprocess.run(["dotnet", "build", "-c", "Release", "-v", "q", "-nologo"], cwd=HERE, check=True)
    exe = HERE / "bin" / "Release" / "net10.0" / "ArgoBooks.Video.dll"
    subprocess.run(["dotnet", str(exe), "receipt", str(TAKES), str(receipt), str(WORK / "scan-cache")], check=True)


# --------------------------------------------------------------------------------------------------------------- voice

def env():
    values = dict(os.environ)
    path = HERE / ".env"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if "=" in line and not line.lstrip().startswith("#"):
                key, value = line.split("=", 1)
                values.setdefault(key.strip(), value.strip().strip('"'))
    return values


def voice_line(text):
    """The line spoken, as mono samples at RATE; fetched from Azure once and then read from work/voice/."""
    folder = WORK / "voice"
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / (hashlib.sha1(f"{VOICE}|{text}".encode()).hexdigest()[:16] + ".wav")
    if not path.exists():
        keys = env()
        if not keys.get("AZURE_SPEECH_KEY"):
            sys.exit("AZURE_SPEECH_KEY is missing from .env")
        ssml = (f"<speak version='1.0' xml:lang='en-US'><voice name='{VOICE}'>{text}</voice></speak>").encode()
        request = urllib.request.Request(
            f"https://{keys['AZURE_SPEECH_REGION']}.tts.speech.microsoft.com/cognitiveservices/v1", data=ssml,
            headers={"Ocp-Apim-Subscription-Key": keys["AZURE_SPEECH_KEY"], "Content-Type": "application/ssml+xml",
                     "X-Microsoft-OutputFormat": "riff-48khz-16bit-mono-pcm", "User-Agent": "argo-books-video"})
        with urllib.request.urlopen(request, timeout=60) as response:
            path.write_bytes(response.read())
    with wave.open(str(path)) as w:
        samples = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    return trim_silence(samples)


def trim_silence(samples, floor_db=-45):
    loud = np.flatnonzero(np.abs(samples) > 10 ** (floor_db / 20))
    if len(loud) == 0:
        return samples
    start = max(0, loud[0] - int(0.02 * RATE))
    end = min(len(samples), loud[-1] + int(0.08 * RATE))
    return samples[start:end]


# --------------------------------------------------------------------------------------------------------------- music

def decode(path, channels=2):
    raw = subprocess.run([FFMPEG, "-v", "error", "-i", str(path), "-ac", str(channels), "-ar", str(RATE), "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, channels).copy()


def beat_grid(music):
    """The music's beat period and the time of its first beat, from where its onsets line up best."""
    mono = music.mean(axis=1)
    hop = 480
    frames = len(mono) // hop
    spectrum = np.abs(np.fft.rfft(mono[:frames * hop].reshape(frames, hop) * np.hanning(hop), axis=1))
    flux = np.maximum(np.diff(np.log1p(spectrum * 20), axis=0), 0).sum(axis=1)
    flux = np.concatenate([[0], flux - flux.mean()])
    rate = RATE / hop
    best = (0, 0.65, 0)
    for period in np.arange(0.55, 0.75, 0.0005):
        for phase in np.arange(0, period, 0.01):
            times = np.arange(phase, min(len(flux) / rate, 90), period)
            score = flux[(times * rate).astype(int)].sum()
            if score > best[0]:
                best = (score, period, phase)
    return best[1], best[2]


# ---------------------------------------------------------------------------------------------------------------- sounds

def envelope(n, attack, decay):
    t = np.arange(n) / RATE
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)


def sound(kind):
    """The interface sounds, made here so they need no licence."""
    rng = np.random.default_rng(3)
    if kind == "click":
        n = int(0.06 * RATE)
        t = np.arange(n) / RATE
        tick = np.diff(rng.standard_normal(n + 1)) * envelope(n, 0.0002, 0.0015) * 0.35
        body = np.sin(2 * np.pi * 1850 * t) * envelope(n, 0.0005, 0.006) * 0.5
        low = np.sin(2 * np.pi * 420 * t) * envelope(n, 0.001, 0.012) * 0.35
        return (tick + body + low) * 0.5
    if kind == "drop":
        n = int(0.35 * RATE)
        t = np.arange(n) / RATE
        thump = np.sin(2 * np.pi * (170 * t - 120 * t * t)) * envelope(n, 0.003, 0.07)
        rustle = np.convolve(rng.standard_normal(n), np.ones(12) / 12, "same") * envelope(n, 0.01, 0.06) * 0.25
        return (thump + rustle) * 0.45
    if kind in ("chime", "pop"):
        notes = [(1046.5, 0.0), (1568.0, 0.09)] if kind == "chime" else [(880.0, 0.0), (1318.5, 0.06)]
        n = int(1.2 * RATE)
        t = np.arange(n) / RATE
        out = np.zeros(n)
        for freq, at in notes:
            shift = int(at * RATE)
            tt = t[: n - shift]
            tone = (np.sin(2 * np.pi * freq * tt) + 0.25 * np.sin(2 * np.pi * freq * 2.76 * tt) * np.exp(-tt / 0.08))
            out[shift:] += tone * envelope(n - shift, 0.002, 0.35 if kind == "chime" else 0.18)
        return out * (0.22 if kind == "chime" else 0.14)
    raise ValueError(kind)


# ---------------------------------------------------------------------------------------------------------------- timeline

def load_take():
    log = json.loads((TAKES / "receipt.json").read_text())
    log["path"] = TAKES / "receipt.mkv"
    return log


def plan(take, beat, first_beat):
    """Lays the parts end to end with each cut moved to the nearest beat, and places the lines."""
    marks = take["marks"]
    parts = []
    t = 0.0
    for part in EDIT:
        if "photo" in part or "end" in part:
            length = part.get("photo") or part.get("end")
            start_frame = None
        else:
            start = marks[part["from"][0]] + part["from"][1]
            end = marks[part["to"][0]] + part["to"][1]
            length = end - start
            start_frame = round(start * FPS)
        cut = t + length
        if part is not EDIT[-1]:
            nearest = first_beat + round((cut - first_beat) / beat) * beat
            if abs(nearest - cut) < beat * 0.55:
                cut = nearest
        frames = round((cut - t) * FPS)
        parts.append({**part, "start": t, "frames": frames, "start_frame": start_frame})
        t += frames / FPS
    lines = []
    for part in parts:
        for key, offset in part.get("say", []):
            if isinstance(offset, tuple):
                offset = marks[offset[0]] + offset[1] - part["start_frame"] / FPS
            lines.append({"key": key, "at": part["start"] + offset})
    return parts, lines, t


def camera(keys, marks, t):
    """The camera's zoom and centre at second `t` of the take, eased between the moments it is given at."""
    points = sorted((marks[mark] + offset, (zoom, x, y)) for mark, offset, zoom, x, y in keys)
    if t <= points[0][0]:
        return points[0][1]
    for (t0, a), (t1, b) in zip(points, points[1:]):
        if t < t1:
            p = (t - t0) / (t1 - t0) if t1 > t0 else 1
            p = p * p * (3 - 2 * p)
            return tuple(u + (v - u) * p for u, v in zip(a, b))
    return points[-1][1]


# -------------------------------------------------------------------------------------------------------------- drawing

def cursor_sprite(height):
    """The arrow pointer, dark with an even white edge, drawn eight times larger and shrunk so its edges stay smooth.
    Returns the picture and where its tip is in it."""
    big = 8
    shape = [(0, 0), (0, 15.5), (3.8, 12.0), (6.3, 17.6), (8.5, 16.6), (6.0, 11.1), (11.0, 11.1)]
    unit = height * big / 18
    pad = round(3 * unit)
    size = (round(12 * unit) + 2 * pad, round(18 * unit) + 2 * pad)
    points = [(pad + x * unit, pad + y * unit) for x, y in shape]
    edge = max(2, round(1.3 * unit))

    shadow = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).polygon([(x + 0.5 * unit, y + 1.1 * unit) for x, y in points], fill=(0, 0, 0, 80))
    layer = shadow.filter(ImageFilter.GaussianBlur(1.1 * unit))
    draw = ImageDraw.Draw(layer)
    # A white line along the outline, half of it outside the shape, then the dark arrow over its inner half.
    draw.line(points + [points[0]], fill=(255, 255, 255, 255), width=edge * 2, joint="curve")
    draw.polygon(points, fill=(255, 255, 255, 255))
    draw.polygon(points, fill=(24, 24, 28, 255))
    small = layer.resize((max(1, round(size[0] / big)), max(1, round(size[1] / big))), Image.LANCZOS)
    return small, pad / big


class Painter:
    """Draws one cut's frames: the take through a moving camera, with the cursor, captions, photo and end card."""

    def __init__(self, cut, take, parts, lines):
        self.cut = cut
        self.size = CUTS[cut]["size"]
        self.take = take
        self.parts = parts
        self.lines = lines
        self.receipt = Image.open(WORK / "receipt.jpg").convert("RGB")
        self.logo = Image.open(ASSETS / "logo.png").convert("RGBA")
        self.sprites = {}
        self.clicks = [e["t"] for e in take["events"] if e["kind"] == "click"]
        self.carry = self.receipt_card(300)
        self.caption_font = font("SemiBold", 44 if cut == "wide" else 58)
        self.background = self.tall_background() if cut == "tall" else None
        self.photo_back = None

    # The camera ------------------------------------------------------------------------------------------------------

    def view(self):
        """Where the app is shown in the frame, and the shape of the slice of the take it shows."""
        if self.cut == "wide":
            return (0, 0) + self.size
        return TALL_VIEW

    def crop_for(self, zoom, focus):
        _, _, vw, vh = self.view()
        aspect = vw / vh
        width = SOURCE[0] / zoom if self.cut == "wide" else TALL_SLICE / zoom
        height = width / aspect
        if height > SOURCE[1]:
            height = SOURCE[1]
            width = height * aspect
        x = min(max(focus[0] - width / 2, 0), SOURCE[0] - width)
        y = min(max(focus[1] - height / 2, 0), SOURCE[1] - height)
        return x, y, width, height

    # Pieces ----------------------------------------------------------------------------------------------------------

    def receipt_card(self, height):
        card = self.receipt.crop(paper_box(self.receipt))
        card = card.resize((round(card.width * height / card.height), height), Image.LANCZOS).convert("RGBA")
        card = card.rotate(-6, resample=Image.BICUBIC, expand=True)
        shadow = Image.new("RGBA", (card.width + 40, card.height + 40), (0, 0, 0, 0))
        shadow.paste((0, 0, 0, 110), (20 + 6, 20 + 10), card.getchannel("A"))
        shadow = shadow.filter(ImageFilter.GaussianBlur(10))
        shadow.alpha_composite(card, (20, 20))
        # Slightly see-through, like the picture of a file being dragged.
        shadow.putalpha(shadow.getchannel("A").point(lambda a: a * 0.95))
        return shadow

    def sprite(self, height):
        key = round(height)
        if key not in self.sprites:
            self.sprites[key] = cursor_sprite(key)
        return self.sprites[key]

    def tall_background(self):
        w, h = self.size
        top = np.array([239, 246, 255], dtype=float)
        bottom = np.array([255, 255, 255], dtype=float)
        ramp = np.linspace(0, 1, h)[:, None, None]
        pixels = (top * (1 - ramp) + bottom * ramp) * np.ones((1, w, 1))
        image = Image.fromarray(pixels.astype(np.uint8))
        logo = self.logo.resize((420, round(420 * self.logo.height / self.logo.width)), Image.LANCZOS)
        image.paste(logo, ((w - logo.width) // 2, 1700), logo)
        return image

    # Frames ----------------------------------------------------------------------------------------------------------

    def app_frame(self, pixels, part, i, take_frame):
        zoom, *focus = camera(part[self.cut], self.take["marks"], take_frame / FPS)
        frame_info = self.take["frames"][min(take_frame, len(self.take["frames"]) - 1)]
        x, y, w, h = self.crop_for(zoom, focus)
        vx, vy, vw, vh = self.view()
        source = Image.frombuffer("RGB", SOURCE, pixels, "raw", "RGB", 0, 1)
        shown = source.resize((vw, vh), Image.LANCZOS, box=(x, y, x + w, y + h)).convert("RGBA")
        ratio = vw / w
        cx, cy = (frame_info[0] - x) * ratio, (frame_info[1] - y) * ratio
        t = take_frame / FPS
        for click in self.clicks:
            age = t - click
            if 0 <= age < 0.4:
                radius = (14 + 46 * age / 0.4) * ratio
                alpha = int(150 * (1 - age / 0.4))
                ring = Image.new("RGBA", shown.size, (0, 0, 0, 0))
                ImageDraw.Draw(ring).ellipse((cx - radius, cy - radius, cx + radius, cy + radius),
                                             outline=BLUE + (alpha,), width=max(2, round(5 * ratio)))
                shown.alpha_composite(ring)
        if frame_info[3] == "file":
            card = self.carry.resize((round(self.carry.width * ratio), round(self.carry.height * ratio)), Image.LANCZOS)
            # The cursor holds the receipt near the middle of its top, as a desktop shows a file being dragged.
            shown.alpha_composite(card, (round(cx - card.width * 0.45), round(cy - card.height * 0.28)))
        sprite, pad = self.sprite(44 * ratio * (0.9 if frame_info[2] else 1.0))
        shown.alpha_composite(sprite, (round(cx - pad), round(cy - pad)))

        if self.cut == "wide":
            return shown
        canvas = self.background.copy().convert("RGBA")
        mask = Image.new("L", (vw, vh), 0)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, vw - 1, vh - 1), radius=0, fill=255)
        canvas.paste(shown, (vx, vy), mask)
        return canvas

    def photo_frame(self, i, frames):
        w, h = self.size
        progress = i / max(1, frames - 1)
        zoom = 1.0 + 0.06 * progress
        if self.cut == "tall":
            # The photo fills the app's window in the tall layout, so the caption above it sits on the light band.
            vx, vy, vw, vh = TALL_VIEW
            back = self.background.copy().convert("RGBA")
            crop_w = self.receipt.width / zoom
            crop_h = crop_w * vh / vw
            left = (self.receipt.width - crop_w) / 2
            top = (self.receipt.height - crop_h) / 2
            back.paste(self.receipt.resize((vw, vh), Image.LANCZOS, box=(left, top, left + crop_w, top + crop_h)), (vx, vy))
            return back
        if self.photo_back is None:
            self.photo_back = self.blurred_receipt()
        back = self.photo_back.copy()
        target = h * 0.92 * zoom
        photo = self.receipt.resize((round(target * self.receipt.width / self.receipt.height), round(target)), Image.LANCZOS)
        back.paste(photo, ((w - photo.width) // 2, (h - photo.height) // 2))
        return back

    def blurred_receipt(self):
        w, h = self.size
        back = self.receipt.resize((w, round(w * self.receipt.height / self.receipt.width)), Image.LANCZOS)
        if back.height < h:
            back = self.receipt.resize((round(h * self.receipt.width / self.receipt.height), h), Image.LANCZOS)
        back = back.crop(((back.width - w) // 2, (back.height - h) // 2, (back.width - w) // 2 + w, (back.height - h) // 2 + h))
        back = back.filter(ImageFilter.GaussianBlur(28))
        return Image.blend(back, Image.new("RGB", (w, h), (20, 20, 24)), 0.35).convert("RGBA")

    def end_frame(self, i, frames, last):
        w, h = self.size
        card = Image.new("RGBA", (w, h), (255, 255, 255, 255))
        draw = ImageDraw.Draw(card)
        wide = self.cut == "wide"
        logo_w = 760 if wide else 760
        logo = self.logo.resize((logo_w, round(logo_w * self.logo.height / self.logo.width)), Image.LANCZOS)
        top = h * (0.30 if wide else 0.34)
        card.paste(logo, ((w - logo.width) // 2, round(top)), logo)
        y = top + logo.height + (70 if wide else 90)
        for text, f, colour, gap in [
            ("Scan receipts. Skip the typing.", font("SemiBold", 52 if wide else 58), INK, 86 if wide else 100),
            ("Free for Windows, Mac and Linux", font("Regular", 36 if wide else 42), GREY, 90 if wide else 110),
            ("argorobots.com", font("SemiBold", 44 if wide else 52), BLUE, 0),
        ]:
            draw.text((w / 2, y), text, font=f, fill=colour, anchor="ma")
            y += gap
        fade = min(1.0, i / (0.35 * FPS))
        if fade < 1 and last is not None:
            return Image.blend(last.convert("RGBA"), card, fade)
        return card

    def caption(self, frame, t, voice):
        line = next((l for l in self.lines if l["at"] - 0.05 <= t < l["at"] + voice[l["key"]] + 0.25), None)
        if line is None or line["key"] == "end":
            return frame
        text = CAPTIONS.get(line["key"], LINES[line["key"]])
        age = t - (line["at"] - 0.05)
        left = line["at"] + voice[line["key"]] + 0.25 - t
        alpha = min(1.0, age / 0.12, left / 0.12)
        w, h = self.size
        f = self.caption_font
        wrapped = wrap(text, f, w * (0.8 if self.cut == "wide" else 0.86))
        layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(layer)
        line_h = f.size * 1.3
        box_h = line_h * len(wrapped) + f.size * 0.7
        box_w = max(draw.textlength(s, font=f) for s in wrapped) + f.size * 1.4
        if self.cut == "wide":
            top = h - 70 - box_h
            draw.rounded_rectangle(((w - box_w) / 2, top, (w + box_w) / 2, top + box_h), radius=16, fill=(15, 18, 25, int(200 * alpha)))
            colour = (255, 255, 255, int(255 * alpha))
        else:
            top = TALL_VIEW[1] / 2 - box_h / 2
            colour = INK + (int(255 * alpha),)
        for n, s in enumerate(wrapped):
            draw.text((w / 2, top + f.size * 0.35 + n * line_h), s, font=f, fill=colour, anchor="ma")
        frame.alpha_composite(layer)
        return frame


def wrap(text, f, width):
    words, out, line = text.split(), [], ""
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    for word in words:
        test = f"{line} {word}".strip()
        if probe.textlength(test, font=f) <= width or not line:
            line = test
        else:
            out.append(line)
            line = word
    out.append(line)
    return out


def paper_box(image):
    """The bounding box of the white paper in the receipt photo, so the dragged card is the paper alone."""
    grey = np.asarray(image.convert("L"))
    rows = np.flatnonzero((grey > 200).mean(axis=1) > 0.25)
    cols = np.flatnonzero((grey > 200).mean(axis=0) > 0.25)
    return int(cols[0]), int(rows[0]), int(cols[-1]), int(rows[-1])


# The app swaps a whole page in one frame, which reads as a jolt in a video, so such a swap is dissolved over this long.
PAGE_SWITCH_FRAMES = 8


def page_switch(before, after):
    """Whether most of the picture changed from one frame to the next, as when the app opens another page."""
    small = (96, 54)
    a = np.asarray(before.convert("L").resize(small), dtype=float)
    b = np.asarray(after.convert("L").resize(small), dtype=float)
    return np.abs(a - b).mean() > 25


def take_frames(take, start_frame, count):
    """Reads `count` frames of the take from `start_frame` on; past the end the last frame repeats."""
    total = len(take["frames"])
    available = max(0, min(count, total - start_frame))
    process = subprocess.Popen([FFMPEG, "-v", "error", "-ss", f"{start_frame / FPS:.4f}", "-i", str(take["path"]),
                                "-frames:v", str(available), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                               stdout=subprocess.PIPE)
    size = SOURCE[0] * SOURCE[1] * 3
    last = None
    for _ in range(count):
        data = process.stdout.read(size) if available > 0 else b""
        if len(data) == size:
            last = data
            available -= 1
        yield last
    process.stdout.close()
    process.wait()


# --------------------------------------------------------------------------------------------------------------- render

def render(cut, take, voice, music, beat, first_beat):
    parts, lines, length = plan(take, beat, first_beat)
    painter = Painter(cut, take, parts, lines)
    VIDEOS.mkdir(parents=True, exist_ok=True)
    w, h = CUTS[cut]["size"]
    silent = WORK / f"{cut}_picture.mp4"
    encoder = subprocess.Popen([FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{w}x{h}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                                "-pix_fmt", "yuv420p", str(silent)], stdin=subprocess.PIPE)
    t_frame = 0
    last = None
    for part in parts:
        if "photo" in part:
            frames = (painter.photo_frame(i, part["frames"]) for i in range(part["frames"]))
        elif "end" in part:
            frames = (painter.end_frame(i, part["frames"], last) for i in range(part["frames"]))
        else:
            source = take_frames(take, part["start_frame"], part["frames"])
            frames = (painter.app_frame(pixels, part, i, part["start_frame"] + i) for i, pixels in enumerate(source))
        before = last
        fade = round(part.get("fade", 0) * FPS)
        switch_from, switch_left = None, 0
        for i, frame in enumerate(frames):
            if i < fade and before is not None:
                frame = Image.blend(before.convert("RGBA"), frame.convert("RGBA"), (i + 1) / (fade + 1))
            elif part.get("start_frame") is not None and i > 0 and switch_left == 0 and page_switch(last, frame):
                switch_from, switch_left = last, PAGE_SWITCH_FRAMES
            if switch_left:
                frame = Image.blend(switch_from.convert("RGBA"), frame.convert("RGBA"),
                                    1 - switch_left / (PAGE_SWITCH_FRAMES + 1))
                switch_left -= 1
            last = frame
            frame = painter.caption(frame.copy(), t_frame / FPS, voice)
            encoder.stdin.write(frame.convert("RGB").tobytes())
            t_frame += 1
        print(f"  {cut}: {t_frame / FPS:5.1f} s drawn")
    encoder.stdin.close()
    encoder.wait()

    audio = mix(parts, lines, take, voice, music, length)
    out = VIDEOS / CUTS[cut]["file"]
    mixed = WORK / f"{cut}_mix.wav"
    write_wav(mixed, audio)
    master(mixed, silent, out)
    write_captions(VIDEOS / (Path(CUTS[cut]["file"]).stem + ".srt"), lines, voice)
    report(out, lines, voice)


def mix(parts, lines, take, voice_lengths, music, length):
    n = int(length * RATE) + RATE
    voice = np.zeros(n)
    for line in lines:
        samples = VOICE_CACHE[line["key"]]
        start = int(line["at"] * RATE)
        voice[start:start + len(samples)] += samples[: n - start]
    voice *= 10 ** (VOICE_GAIN_DB / 20)

    effects = np.zeros(n)

    def place(kind, at):
        s = sound(kind) * 10 ** (SOUND_GAIN_DB[kind] / 20)
        start = int(at * RATE)
        if 0 <= start < n:
            effects[start:start + len(s)] += s[: n - start]

    for part in parts:
        if part.get("start_frame") is None:
            continue
        first = part["start_frame"] / FPS
        last = first + part["frames"] / FPS
        for event in take["events"]:
            if first <= event["t"] < last:
                at = part["start"] + event["t"] - first
                kind = {"click": "click", "drop": "drop", "saved": "chime"}.get(event["kind"])
                if kind:
                    place(kind, at)
        scan_done = take["marks"]["scan_done"]
        if first <= scan_done < last:
            place("pop", part["start"] + scan_done - first)

    track = music[int(MUSIC_FROM * RATE): int(MUSIC_FROM * RATE) + n]
    track = np.pad(track, ((0, n - len(track)), (0, 0)))
    # The music sits well under the voice and dips further while a line is spoken.
    speaking = np.abs(voice) > 0.003
    held = window_sum(speaking, 0.25) > 0.5
    duck = smooth(np.where(held, 10 ** (-7 / 20), 1.0), 0.18)
    fade_in = np.minimum(1, np.arange(n) / (0.4 * RATE))
    fade_out = np.clip((length + 0.3 - np.arange(n) / RATE) / 2.2, 0, 1)
    track = track * (10 ** (MUSIC_GAIN_DB / 20) * duck * fade_in * fade_out)[:, None]
    dry = (voice + effects)[:, None]
    print_levels(voice, effects, track.mean(axis=1), held)
    return track + dry


def print_levels(voice, effects, music, speaking):
    """How the voice, music and sounds sit against each other, so the balance is checked by measurement."""
    def db(x):
        return 20 * np.log10(np.sqrt(np.mean(x * x)) + 1e-9)
    quiet = ~speaking & (np.arange(len(music)) < len(music) - 6 * RATE)
    print(f"  levels (RMS): voice while speaking {db(voice[speaking]):.1f} dB, music under the voice {db(music[speaking]):.1f} dB, "
          f"music between lines {db(music[quiet]):.1f} dB, loudest sound peak {20 * np.log10(np.abs(effects).max() + 1e-9):.1f} dB, "
          f"voice peak {20 * np.log10(np.abs(voice).max() + 1e-9):.1f} dB")


def window_sum(values, seconds):
    """Each sample's sum over a window centred on it, from running totals, since a direct convolution with a window
    this long takes minutes on a whole video."""
    width = max(1, int(seconds * RATE))
    padded = np.pad(np.asarray(values, dtype=float), (width // 2, width - 1 - width // 2))
    totals = np.concatenate([[0.0], np.cumsum(padded)])
    return totals[width:] - totals[:-width]


def smooth(values, seconds):
    return window_sum(values, seconds) / max(1, int(seconds * RATE))


def write_wav(path, audio):
    peak = np.abs(audio).max()
    if peak > 0.99:
        audio = audio * 0.99 / peak
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((audio * 32767).astype(np.int16).tobytes())


def master(mixed, picture, out):
    """Brings the mix to YouTube's loudness, -14 LUFS with peaks under -1 dBTP, and puts it with the picture."""
    first = subprocess.run([FFMPEG, "-hide_banner", "-i", str(mixed), "-af", "loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json",
                            "-f", "null", "-"], capture_output=True, text=True).stderr
    stats = json.loads(first[first.rindex("{"):first.rindex("}") + 1])
    filter_ = (f"loudnorm=I=-14:TP=-1.5:LRA=11:measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
               f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:offset={stats['target_offset']}:linear=true")
    subprocess.run([FFMPEG, "-v", "error", "-y", "-i", str(picture), "-i", str(mixed), "-af", filter_ + ",aresample=48000",
                    "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", str(out)], check=True)


def write_captions(path, lines, voice):
    def stamp(seconds):
        ms = int(round(seconds * 1000))
        return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"
    blocks = []
    for n, line in enumerate(lines, 1):
        text = CAPTIONS.get(line["key"], LINES[line["key"]])
        blocks.append(f"{n}\n{stamp(line['at'])} --> {stamp(line['at'] + voice[line['key']] + 0.2)}\n{text}\n")
    path.write_text("\n".join(blocks), encoding="utf-8")


def report(out, lines, voice):
    """Prints the finished file's loudness and checks that no line runs into the next."""
    meter = subprocess.run([FFMPEG, "-hide_banner", "-nostats", "-i", str(out), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    integrated = summary.split("I:")[1].split("LUFS")[0].strip()
    peak = summary.split("Peak:")[1].split("dBFS")[0].strip()
    print(f"  {out.name}: {integrated} LUFS integrated, true peak {peak} dBFS")
    ordered = sorted(lines, key=lambda l: l["at"])
    for a, b in zip(ordered, ordered[1:]):
        gap = b["at"] - (a["at"] + voice[a["key"]])
        if gap < 0.12:
            print(f"  warning: \"{a['key']}\" runs {-gap:.2f} s into \"{b['key']}\"")


VOICE_CACHE = {}
# Each sound's level, set by measuring its loudest 50 ms against the voice's: clicks about 10 dB under it, the
# chime and the drop about 5 to 6 dB under, so they are heard without stepping on a word.
SOUND_GAIN_DB = {"click": -8.0, "drop": -15.0, "pop": -10.5, "chime": -12.0}
VOICE_GAIN_DB = 0.0
MUSIC_GAIN_DB = -19.0


def main():
    args = sys.argv[1:]
    WORK.mkdir(exist_ok=True)
    if args[:1] == ["record"] or not (TAKES / "receipt.json").exists():
        record()
        if args[:1] == ["record"]:
            return
    cuts = args[1:] if args[:1] == ["render"] and len(args) > 1 else list(CUTS)

    take = load_take()
    for key, text in LINES.items():
        VOICE_CACHE[key] = voice_line(text)
    voice = {key: len(samples) / RATE for key, samples in VOICE_CACHE.items()}
    music = decode(MUSIC)
    beat, first_beat = beat_grid(music)
    print(f"music: beat {beat:.3f} s ({60 / beat:.1f} BPM), first beat at {first_beat:.2f} s")
    for cut in cuts:
        render(cut, take, voice, music, beat, first_beat)


if __name__ == "__main__":
    main()

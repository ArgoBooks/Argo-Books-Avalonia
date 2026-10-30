"""Make the vertical Short "4 free accounting apps, and the catch in each", cut from the recap of YouTube video 13: Evan's
recording, the same pricing pages and app takes, and big captions timed to each word.

Usage: python catches_short.py

It uses what roundup.py made in work/roundup/ (the transcript, pages and takes), so run that first. The finished video
goes to work/videos/. The words' timings come from Azure's transcript of the Short's own voice, kept in work/short/.
"""
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import make_video as mv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "short"
OUT_FILE = HERE / "work" / "videos" / "short_free_accounting_catches.mp4"
RATE = r.RATE
FPS = r.FPS
SIZE = (1080, 1920)
# The window onto the pages and app, and how wide a slice of a page or take it shows at zoom 1.
VIEW = (0, 330, 1080, 1000)
CAPTION_Y = 1370
PAGE_ZOOM = 2.1

# The recap's phrases, in the order they are said here; each `parts` limits a phrase to its first stretches of speech.
SCRIPT = [
    {"phrase": 66}, {"phrase": 67}, {"phrase": 68}, {"phrase": 70}, {"phrase": 71}, {"phrase": 72},
    # The third stretch says the link's in the description, and a Short's description has no clickable links.
    {"phrase": 64, "parts": 2},
]
GAP = 0.28
END_CARD = 2.8
SPELLING = {"Zipbooks": "ZipBooks", "Zip": "ZipBooks", "ArgoBooks": "Argo Books", "Argobooks": "Argo Books"}
MUSIC_UNDER_VOICE_DB = -16.0
MUSIC_BETWEEN_DB = -11.0


def cut_voice():
    """The Short's voice from the recording's kept stretches, and when each phrase starts and ends in it."""
    WORK.mkdir(parents=True, exist_ok=True)
    source = mv.decode(r.VIDEO_FOLDER / "audio.mp3", channels=1)[:, 0]
    pieces, _ = r.voice_cut(source)
    out, spans, t = [np.zeros(int(0.15 * RATE), dtype=np.float32)], {}, 0.15
    for line in SCRIPT:
        chosen = [p for p in pieces if p["phrase"] == line["phrase"]][: line.get("parts")]
        for n, piece in enumerate(chosen):
            if t > 0.2:
                gap = GAP if n == 0 else min(0.3, piece["gap"])
                out.append(np.zeros(int(gap * RATE), dtype=np.float32))
                t += gap
            chunk = source[int((piece["start"] - r.LEAD) * RATE):int((piece["end"] + r.TAIL) * RATE)].copy()
            ramp = np.linspace(0, 1, int(0.012 * RATE), dtype=np.float32)
            chunk[: len(ramp)] *= ramp
            chunk[-len(ramp):] *= ramp[::-1]
            out.append(chunk)
            spans.setdefault(line["phrase"], [t + r.LEAD, 0])
            t += len(chunk) / RATE
            spans[line["phrase"]][1] = t - r.TAIL
    raw = WORK / "voice_cut.wav"
    clean = WORK / "voice.wav"
    r.write_mono(raw, np.concatenate(out))
    subprocess.run([mv.FFMPEG, "-v", "error", "-y", "-i", str(raw), "-af", r.VOICE_FILTER, "-ar", str(RATE), str(clean)], check=True)
    return clean, spans, t


def timed_words(path):
    """Azure's words with their times; the Short has no false starts left, so these timings can be trusted."""
    cache = WORK / "words.json"
    if not cache.exists():
        keys = mv.env()
        url = (f"https://{keys['AZURE_SPEECH_REGION']}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe"
               "?api-version=2024-11-15")
        subprocess.run(["curl", "-s", "-f", "-o", str(cache), "-X", "POST", url, "-H", f"Ocp-Apim-Subscription-Key: {keys['AZURE_SPEECH_KEY']}",
                        "-F", f"audio=@{path}", "-F", 'definition={"locales":["en-US"],"profanityFilterMode":"None"}'], check=True)
    words = []
    for phrase in json.loads(cache.read_text())["phrases"]:
        for word in phrase["words"]:
            words.append({"text": SPELLING.get(word["text"].strip(".,"), word["text"]), "at": word["offsetMilliseconds"] / 1000,
                          "end": (word["offsetMilliseconds"] + word["durationMilliseconds"]) / 1000})
    return words


def caption_chunks(words, longest_pause=0.15):
    """Groups the words into short lines, breaking at a pause or after four words."""
    chunks, line = [], []
    for n, word in enumerate(words):
        line.append(word)
        pause = n + 1 == len(words) or words[n + 1]["at"] - word["end"] > longest_pause
        if len(line) >= 4 or pause or word["text"].endswith((",", ".", "?")):
            chunks.append(line)
            line = []
    return chunks


def logo(name, height):
    file = {"Wave": "wave.png", "Zoho Books": "zoho.png", "Argo Books": "argo.png", "ZipBooks": "zipbooks.png"}[name]
    image = Image.open(r.WORK / "logos" / file).convert("RGBA")
    return image.resize((round(image.width * height / image.height), height), Image.LANCZOS)


def background():
    w, h = SIZE
    ramp = np.linspace(0, 1, h)[:, None, None]
    pixels = (np.array([239, 246, 255.0]) * (1 - ramp) + np.array([255, 255, 255.0]) * ramp) * np.ones((1, w, 1))
    return Image.fromarray(pixels.astype(np.uint8)).convert("RGBA")


def title_band(frame, text=None, name=None):
    """The top of the frame: a line of text, or a product's logo and name under a red THE CATCH tag."""
    draw = ImageDraw.Draw(frame)
    w = SIZE[0]
    if text:
        draw.text((w / 2, 215), text, font=mv.font("Bold", 68), fill=mv.INK + (255,), anchor="mm")
        return
    tag = mv.font("Bold", 34)
    tag_w = draw.textlength("THE CATCH", font=tag) + 56
    draw.rounded_rectangle(((w - tag_w) / 2, 110, (w + tag_w) / 2, 172), radius=14, fill=(253, 236, 236, 255))
    draw.text((w / 2, 141), "THE CATCH", font=tag, fill=r.RED + (255,), anchor="mm")
    title = mv.font("Bold", 76)
    icon = logo(name, 92)
    width = icon.width + 22 + draw.textlength(name, font=title)
    x = round((w - width) / 2)
    frame.alpha_composite(icon, (x, 196))
    draw.text((x + icon.width + 22, 242), name, font=title, fill=mv.INK + (255,), anchor="lm")


def caption(frame, chunks, t):
    """The line being said, with the word being said in blue."""
    line = next((c for c in chunks if c[0]["at"] - 0.05 <= t < c[-1]["end"] + 0.12), None)
    if line is None:
        return
    draw = ImageDraw.Draw(frame)
    font = mv.font("Bold", 72)
    space = draw.textlength(" ", font=font)
    rows, row, row_w = [], [], 0.0
    for word in line:
        width = draw.textlength(word["text"], font=font)
        if row and row_w + space + width > 960:
            rows.append((row, row_w))
            row, row_w = [], 0.0
        row_w += (space if row else 0) + width
        row.append((word, width))
    rows.append((row, row_w))
    for n, (row, row_w) in enumerate(rows):
        x = (SIZE[0] - row_w) / 2
        y = CAPTION_Y + 45 + n * 92
        for word, width in row:
            current = word["at"] - 0.02 <= t < word["end"] + 0.02
            draw.text((x, y), word["text"], font=font, fill=mv.BLUE + (255,) if current else mv.INK + (255,), anchor="lm")
            x += width + space


def shots(spans, voice_end):
    """The Short's shots, each starting on a moment of the voice and running until the next."""
    all_four, limit, wave, zoho, zip_books, argo, own = (spans[p] for p in (66, 67, 68, 70, 71, 72, 64))
    step = (all_four[1] + 0.2 - 0.0) / 4
    z = PAGE_ZOOM
    return [
        {"at": 0.0, "page": "wave", "title": "4 free accounting apps", "keys": [(0.0, 2.5, (1000, 1450)), (step, 2.6, (1000, 1450))]},
        {"at": step, "page": "zoho", "title": "4 free accounting apps", "keys": [(step, 2.5, "free_price"), (2 * step, 2.6, "free_price")]},
        {"at": 2 * step, "page": "argo_pricing", "title": "4 free accounting apps", "keys": [(2 * step, 2.5, (1160, 1340)), (3 * step, 2.6, (1160, 1340))]},
        {"at": 3 * step, "page": "zipbooks", "title": "4 free accounting apps", "keys": [(3 * step, 2.5, "starter_free"), (4 * step, 2.6, "starter_free")]},
        {"at": limit[0] - 0.14, "grid": True, "title": "What's the catch?"},
        {"at": wave[0] - 0.14, "page": "wave", "name": "Wave", "keys": [(wave[0], z, "pro_bank"), (zoho[0], z + 0.15, "pro_bank")],
         "marks": [(wave[0] + 0.3, "pro_bank", r.RED)]},
        {"at": zoho[0] - 0.14, "page": "zoho", "name": "Zoho Books", "keys": [(zoho[0], 2.2, "free_price"), (zip_books[0], 2.35, "free_price")],
         "marks": [(zoho[0] + 0.3, "free_price", r.RED)], "notes": [(zoho[0] + 0.5, "note_zoho_limit", zip_books[0] + 1)]},
        {"at": zip_books[0] - 0.14, "page": "zipbooks", "name": "ZipBooks", "keys": [(zip_books[0], z, "recurring"), (argo[0], z + 0.15, "reminders")],
         "marks": [(zip_books[0] + 0.5, "recurring", r.RED), (zip_books[0] + 1.3, "reminders", r.RED)]},
        {"at": argo[0] - 0.14, "page": "argo_pricing", "name": "Argo Books", "keys": [(argo[0], z, "free_scans"), (own[0], z + 0.15, "free_scans")],
         "marks": [(argo[0] + 0.3, "free_invoices", r.RED), (argo[0] + 0.9, "free_scans", r.RED), (argo[0] + 1.5, "free_bank", r.RED)]},
        {"at": own[0] - 0.14, "take": "tour", "from": ("dashboard", 2.0), "title": "Your books, on your computer",
         "keys": [("dashboard", 0.0, 1.0, 1650, 800)]},
        {"at": own[0] + 2.3, "take": "receipt", "from": ("scan_done", 1.0), "title": "Your books, on your computer",
         "keys": [("scan_done", 0.0, 1.0, 2007, 760)]},
        {"at": own[1] - 1.1, "end": True},
        {"at": voice_end + END_CARD, "stop": True},
    ]


def grid_frame(base):
    """The four products, each with its logo."""
    frame = base.copy()
    draw = ImageDraw.Draw(frame)
    font = mv.font("Bold", 64)
    for n, name in enumerate(["Wave", "Zoho Books", "Argo Books", "ZipBooks"]):
        y = VIEW[1] + 60 + n * 225
        draw.rounded_rectangle((110, y, 970, y + 190), radius=28, fill=(255, 255, 255, 255), outline=(226, 232, 240, 255), width=3)
        icon = logo(name, 112)
        frame.alpha_composite(icon, (160, y + 39))
        draw.text((330, y + 95), name, font=font, fill=mv.INK + (255,), anchor="lm")
    return frame


def end_frame(base, previous, age):
    frame = base.copy()
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((840, round(840 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((SIZE[0] - 840) // 2, 620))
    w = SIZE[0] / 2
    draw.text((w, 860), "Free for Windows, Mac and Linux", font=mv.font("Regular", 50), fill=mv.GREY + (255,), anchor="mm")
    draw.text((w, 960), "argorobots.com", font=mv.font("Bold", 68), fill=mv.BLUE + (255,), anchor="mm")
    fade = r.ease(age / 0.35)
    return Image.blend(previous, frame, fade) if fade < 1 and previous is not None else frame


def render_picture(path, plan, chunks):
    mv.TALL_VIEW = VIEW
    base = background()
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{SIZE[0]}x{SIZE[1]}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                                "-pix_fmt", "yuv420p", str(path)], stdin=subprocess.PIPE)
    pages, sprites, grid = r.Pages(), {}, grid_frame(base)
    vx, vy, vw, vh = VIEW
    frame_no, last = 0, None
    for shot, following in zip(plan, plan[1:]):
        first, stop = round(shot["at"] * FPS), round(following["at"] * FPS)
        source = None
        if "take" in shot:
            take = r.load_take(shot["take"])
            painter = mv.Painter("tall", take, [], [])
            painter.background = base.convert("RGB")
            start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
            part = {"tall": shot["keys"], "frames": stop - first}
            source = mv.take_frames(take, start, stop - first)
        for f in range(first, stop):
            t = f / FPS
            if "end" in shot:
                frame = end_frame(base, last, t - shot["at"])
            elif "grid" in shot:
                frame = grid.copy()
            elif "take" in shot:
                frame = painter.app_frame(next(source), part, 0, start + f - first).convert("RGBA")
            else:
                frame = base.copy()
                frame.paste(r.page_frame(shot, t, pages, sprites, (vw, vh)), (vx, vy))
            if "end" not in shot:
                last = frame.copy()
            frame = frame.copy()
            if "end" not in shot:
                title_band(frame, shot.get("title"), shot.get("name"))
            caption(frame, chunks, t)
            encoder.stdin.write(frame.convert("RGB").tobytes())
            frame_no += 1
    encoder.stdin.close()
    encoder.wait()
    return frame_no / FPS


def mix(voice_path, spans, total):
    n = int((total + 0.3) * RATE)
    voice = mv.decode(voice_path, channels=1)[:, 0]
    voice = np.pad(voice, (0, max(0, n - len(voice))))[:n]

    def rms(x):
        return np.sqrt(np.mean(x * x) + 1e-12)

    held = mv.window_sum(np.abs(voice) > 10 ** (-40 / 20), 0.3) > 0.5
    bed = mv.decode(mv.MUSIC)[:n].mean(axis=1)
    bed = np.pad(bed, (0, max(0, n - len(bed))))
    level = rms(voice[held]) / rms(bed) * 10 ** (MUSIC_BETWEEN_DB / 20)
    duck = mv.smooth(np.where(held, 10 ** ((MUSIC_UNDER_VOICE_DB - MUSIC_BETWEEN_DB) / 20), 1.0), 0.2)
    fade = np.clip((total - np.arange(n) / RATE) / 1.2, 0, 1)
    bed = bed * level * duck * fade

    effects = np.zeros(n)
    for phrase in (68, 70, 71, 72):
        s = mv.sound("pop") * 10 ** (mv.SOUND_GAIN_DB["pop"] / 20)
        at = int((spans[phrase][0] - 0.14) * RATE)
        effects[at:at + len(s)] += s[: n - at]
    s = mv.sound("chime") * 10 ** (mv.SOUND_GAIN_DB["chime"] / 20)
    at = int((spans[64][1] - 0.2) * RATE)
    effects[at:at + len(s)] += s[: n - at]

    def db(x):
        return 20 * np.log10(rms(x))
    print(f"levels (RMS): voice {db(voice[held]):.1f} dB, music under it {db(bed[held]):.1f} dB, between lines {db(bed[~held & (fade > 0.99)]):.1f} dB")
    mono = voice + effects + bed
    return np.stack([mono, mono], axis=1)


def main():
    voice, spans, length = cut_voice()
    words = timed_words(voice)
    chunks = caption_chunks(words)
    print("said:", " ".join(w["text"] for w in words))
    print("captions:", " / ".join(" ".join(w["text"] for w in c) for c in chunks))
    picture = WORK / "picture.mp4"
    total = render_picture(picture, shots(spans, length), chunks)
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mix(voice, spans, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")


if __name__ == "__main__":
    main()

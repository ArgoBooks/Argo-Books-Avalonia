"""Make the vertical Short "4 free accounting apps, and the catch in each", cut from the recap of YouTube video 13: Evan's
recording, the same pricing pages and app takes, and big captions timed to each word.

Usage: python catches_short.py

It uses what roundup.py made in work/roundup/ (the transcript, pages and takes), so run that first. The finished video
goes to work/videos/. The words' timings come from Azure's transcript of the Short's own voice, kept in work/short/.
"""
import difflib
import hashlib
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


def timed_words(path, work=WORK):
    """Azure's words with their times; the Short has no false starts left, so these timings can be trusted."""
    cache = work / "words.json"
    stamp = work / "words.audio"
    heard = hashlib.sha1(Path(path).read_bytes()).hexdigest()
    # A transcript saved before its audio was remembered is trusted, so the finished videos' timings stay as they were.
    if cache.exists() and stamp.exists() and stamp.read_text() != heard:
        cache.unlink()
    if not cache.exists():
        keys = mv.env()
        url = (f"https://{keys['AZURE_SPEECH_REGION']}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe"
               "?api-version=2024-11-15")
        work.mkdir(parents=True, exist_ok=True)
        # curl reads the key from its standard input, so a failure's message never shows it.
        done = subprocess.run(["curl", "-s", "-f", "-o", str(cache), "-X", "POST", url, "-H", "@-",
                               "-F", f"audio=@{path}", "-F", 'definition={"locales":["en-US"],"profanityFilterMode":"None"}'],
                              input=f"Ocp-Apim-Subscription-Key: {keys['AZURE_SPEECH_KEY']}\n", text=True)
        if done.returncode != 0:
            raise SystemExit(f"Azure transcription of {path} failed (curl exit code {done.returncode})")
        stamp.write_text(heard)
    words = []
    for phrase in json.loads(cache.read_text())["phrases"]:
        for word in phrase["words"]:
            words.append({"text": SPELLING.get(word["text"].strip(".,"), word["text"]), "at": word["offsetMilliseconds"] / 1000,
                          "end": (word["offsetMilliseconds"] + word["durationMilliseconds"]) / 1000})
    return words


def scripted_words(path, script, work):
    """Azure's timings with the script's own words, which keep the spelling and punctuation the captions need."""
    heard = []
    # A name spelled as two words, such as "Argo Books", is heard as one, so its time is shared between them.
    for word in timed_words(path, work):
        parts = word["text"].split()
        step = (word["end"] - word["at"]) / len(parts)
        heard += [{**word, "text": part, "at": word["at"] + n * step, "end": word["at"] + (n + 1) * step} for n, part in enumerate(parts)]
    written = script.split()
    if len(heard) == len(written):
        return [{**word, "text": text} for word, text in zip(heard, written)]
    return matched_words(heard, written, path)


def matched_words(heard, written, path):
    """Gives each written word a time when the transcript splits, joins or mishears some of them, such as "GNU Cash" for
    GnuCash or "$737" for "737 US dollars"."""
    def plain(text):
        return text.strip(".,?!$").lower()

    words = []
    blocks = difflib.SequenceMatcher(None, [plain(w["text"]) for w in heard], [plain(w) for w in written], autojunk=False)
    for kind, h0, h1, w0, w1 in blocks.get_opcodes():
        if kind == "equal":
            words += [{**heard[h0 + n], "text": written[w0 + n]} for n in range(w1 - w0)]
            continue
        if kind == "delete":
            continue
        joined = h0 == h1 and bool(words)
        if joined:
            spoken = words.pop()
            texts = [spoken["text"]] + written[w0:w1]
        elif h0 == h1:
            spoken = {"at": heard[0]["at"], "end": heard[0]["at"]}
            texts = written[w0:w1]
        else:
            spoken = {"at": heard[h0]["at"], "end": heard[h1 - 1]["end"]}
            texts = written[w0:w1]
        step = (spoken["end"] - spoken["at"]) / len(texts)
        words += [{"text": text, "at": spoken["at"] + n * step, "end": spoken["at"] + (n + 1) * step, "joined": joined}
                  for n, text in enumerate(texts)]
    return retimed_amounts(words, path)


def retimed_amounts(words, path):
    """Azure writes a spoken amount such as "737 US dollars" as one word, "$737", and places it at the end of the stretch
    it was said in, so the words of that stretch are spread over it again by how long each takes to say."""
    voice = mv.decode(path, channels=1)[:, 0]
    frame = RATE // 100
    level = np.sqrt(np.mean(voice[: len(voice) // frame * frame].reshape(-1, frame) ** 2, axis=1) + 1e-12)
    quiet = 20 * np.log10(level) < -45

    def pause_between(a, b):
        run = 0
        for q in quiet[int(a * 100):int(b * 100) + 1]:
            run = run + 1 if q else 0
            if run >= 6:
                return True
        return False

    n = 0
    while n < len(words):
        if not (words[n].get("joined") and any(c.isdigit() for c in words[n]["text"])):
            n += 1
            continue
        last = n
        while last + 1 < len(words) and words[last + 1].get("joined"):
            last += 1
        first = n
        while first > 0 and not pause_between(words[first - 1]["end"], words[first]["at"]):
            first -= 1
        if first == n and n > 0:
            after = int(words[n - 1]["end"] * 100)
            start = (after + int(np.argmax(~quiet[after:]))) / 100
        else:
            start = words[first]["at"]
        stop = words[last + 1]["at"] if last + 1 < len(words) else words[last]["end"]
        lengths = [said_length(w["text"]) for w in words[first:last + 1]]
        step, t = (stop - start) / sum(lengths), start
        for word, length in zip(words[first:last + 1], lengths):
            word["at"], word["end"] = t, t + step * length
            t += step * length
        n = last + 1
    return words


def phrase_timed_words(words, path, shortest_pause=0.15):
    """Times each phrase that holds an amount from the voice itself. Azure can give an amount it writes as "$1,400" almost no
    time and stretch the word before it, so when the voice has exactly one stretch of speech per phrase of the script, the
    words of those phrases are spread over their stretch by how long each takes to say."""
    voice = mv.decode(path, channels=1)[:, 0]
    frame = RATE // 100
    quiet = 20 * np.log10(np.sqrt(np.mean(voice[: len(voice) // frame * frame].reshape(-1, frame) ** 2, axis=1) + 1e-12)) < -45
    spans, start, run = [], None, 0
    for i, q in enumerate(quiet):
        if not q:
            if start is None:
                start = i
            elif run >= shortest_pause * 100:
                spans.append((start / 100, (i - run) / 100))
                start = i
            run = 0
        else:
            run += 1
    spans.append((start / 100, (len(quiet) - run) / 100))
    phrases, current = [], []
    for word in words:
        current.append(word)
        if word["text"].endswith((",", ".", "?", ":")):
            phrases.append(current)
            current = []
    if current:
        phrases.append(current)
    if len(phrases) != len(spans):
        print(f"phrase timing skipped: {len(phrases)} phrases, {len(spans)} stretches of speech")
        return words
    for phrase, (begin, end) in zip(phrases, spans):
        if not any(c.isdigit() for w in phrase for c in w["text"]):
            continue
        lengths = [said_length(w["text"]) for w in phrase]
        step, t = (end - begin) / sum(lengths), begin
        for word, length in zip(phrase, lengths):
            word["at"], word["end"] = t, t + step * length
            t += step * length
    return words


def said_length(text):
    """Roughly how long a word takes to say, in letters, with numbers and initials counted as they are said."""
    word = text.strip(".,?!$%")
    if word.replace(",", "").isdigit():
        return len(number_words(int(word.replace(",", ""))))
    if word.isupper() and len(word) <= 3:
        return 3 * len(word)
    return max(1, len(word))


def number_words(n):
    ones = ("zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen "
            "eighteen nineteen").split()
    tens = "twenty thirty forty fifty sixty seventy eighty ninety".split()
    if n < 20:
        return ones[n]
    if n < 100:
        return tens[n // 10 - 2] + ("" if n % 10 == 0 else " " + ones[n % 10])
    if n < 1000:
        return ones[n // 100] + " hundred" + ("" if n % 100 == 0 else " " + number_words(n % 100))
    return number_words(n // 1000) + " thousand" + ("" if n % 1000 == 0 else " " + number_words(n % 1000))


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


LEADING_WORDS = {"a", "an", "the", "to", "of", "and", "or", "for", "in", "on", "at", "is", "it's", "your", "my", "from", "with", "by"}


def balanced_chunks(words, longest_pause=0.4, most=4):
    """Groups each phrase's words into lines of about the same length, so a phrase never ends on a line of one word, and a
    line never ends on a word like "the" that leads into the next one."""
    chunks, run = [], []
    for n, word in enumerate(words):
        run.append(word)
        pause = n + 1 == len(words) or words[n + 1]["at"] - word["end"] > longest_pause
        if pause or word["text"].endswith((",", ".", "?", ":")):
            parts = -(-len(run) // most)
            cuts = [0]
            for k in range(1, parts):
                even = int(k * len(run) / parts + 0.5)
                options = [c for c in (even, even - 1, even + 1) if cuts[-1] < c < len(run) and c - cuts[-1] <= most + 1]
                cuts.append(next((c for c in options if run[c - 1]["text"].lower() not in LEADING_WORDS), even))
            cuts.append(len(run))
            chunks += [run[a:b] for a, b in zip(cuts, cuts[1:])]
            run = []
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


def render_picture(path, plan, chunks, load_take=r.load_take, take_frames=mv.take_frames, pages=None, end_card=end_frame):
    """Draws the Short. A Mac Short passes its own takes, frames and pages, from mac_window.py and mac_video.py."""
    mv.TALL_VIEW = VIEW
    base = background()
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{SIZE[0]}x{SIZE[1]}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                                "-pix_fmt", "yuv420p", str(path)], stdin=subprocess.PIPE)
    pages, sprites, grid = pages or r.Pages(), {}, grid_frame(base)
    vx, vy, vw, vh = VIEW
    frame_no, last = 0, None
    for shot, following in zip(plan, plan[1:]):
        first, stop = round(shot["at"] * FPS), round(following["at"] * FPS)
        source = None
        if "take" in shot:
            take = load_take(shot["take"])
            painter = mv.Painter("tall", take, [], [])
            painter.background = base.convert("RGB")
            start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
            part = {"tall": shot["keys"], "frames": stop - first}
            # A shot may run longer than its `play` seconds of the take: the last frame then holds while the camera keeps moving.
            played = min(stop - first, round(shot.get("play", stop - first) * FPS))
            source = take_frames(take, start, played)
        for f in range(first, stop):
            t = f / FPS
            if "end" in shot:
                frame = end_card(base, last, t - shot["at"])
            elif "grid" in shot:
                frame = grid.copy()
            elif "still" in shot:
                frame = shot["still"](t - shot["at"])
            elif "take" in shot:
                age = f - first
                if age < played:
                    pixels = next(source)
                frame = painter.app_frame(pixels, part, 0, start + min(age, played - 1), (start + age) / FPS).convert("RGBA")
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

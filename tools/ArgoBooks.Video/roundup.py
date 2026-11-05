"""Make YouTube video 13, "4 Free Accounting Software for Small Business, and What Each One Limits": Evan's own
recording cut to the script, laid over the pricing pages, the app and the comparison cards.

Usage:
  python roundup.py voice     cuts and cleans the voice only, and prints how it measures
  python roundup.py           renders the video (the voice is cut first if it is missing)
  python roundup.py redo 2 13 draws those shots again, then joins and mixes the whole video

The recording is read from the video's own folder (VIDEO_FOLDER); everything made goes to work/roundup/. The cuts
follow Azure's transcript of the recording, with its word timings, fetched once and kept in work/roundup/.
"""
import difflib
import json
import re
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

import make_video as mv

HERE = Path(__file__).resolve().parent
VIDEO_FOLDER = Path(r"C:\Users\evand\Desktop\Pictures\Argo\Argo Books\Videos\Video 13 - Free accounting software roundup")
WORK = HERE / "work" / "roundup"
RATE = 48000

# Phrases of the transcript that are false starts or earlier takes of the line after them.
RETAKES = {14, 17, 18, 22, 26, 28, 34, 38, 41, 42, 43, 49, 69}
# The first phrase of each section, which gets a longer pause before it so the picture has time to change.
SECTIONS = {3: "wave", 15: "zoho", 29: "argo", 52: "zipbooks", 62: "recap", 73: "close"}
# Pauses, in seconds: inside a sentence where the speaker hesitated, between sentences, and before a new section.
HESITATION = 0.18
BETWEEN = 0.55
SECTION_GAP = 1.0
# How much of the quiet around each stretch is kept, so no soft start or end of a word is clipped. Together they stay
# under the 0.2 s that always separates two stretches, so a cut never takes in any of its neighbour.
LEAD = 0.05
TAIL = 0.10


def transcript():
    path = WORK / "transcript.json"
    if not path.exists():
        WORK.mkdir(parents=True, exist_ok=True)
        keys = mv.env()
        url = (f"https://{keys['AZURE_SPEECH_REGION']}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe"
               "?api-version=2024-11-15")
        subprocess.run(["curl", "-s", "-f", "-o", str(path), "-X", "POST", url,
                        "-H", f"Ocp-Apim-Subscription-Key: {keys['AZURE_SPEECH_KEY']}",
                        "-F", f"audio=@{VIDEO_FOLDER / 'audio.mp3'}",
                        "-F", 'definition={"locales":["en-US"],"profanityFilterMode":"None"}'], check=True)
    return json.loads(path.read_text(encoding="utf-8"))


def stretches(source):
    """The stretches of sound in the recording: it was gated, so the speaker's pauses are near silence and every
    stretch between them is a clean place to cut."""
    frame = int(0.01 * RATE)
    level = 20 * np.log10(np.sqrt((source[: len(source) // frame * frame].reshape(-1, frame) ** 2).mean(1)) + 1e-12)
    loud = level > -55
    found, i, n = [], 0, len(loud)
    while i < n:
        if not loud[i]:
            i += 1
            continue
        j = i
        # A stretch ends at the first 0.2 s of quiet.
        while j < n and loud[j:j + 20].any():
            j += 1
        found.append((i / 100, j / 100))
        i = j
    return found


def heard(source, start, end):
    """What Azure hears in one stretch on its own, kept in work/roundup/stretches.json so each is only asked once.
    Alone, a false start is heard as itself rather than merged into the line that follows it."""
    path = WORK / "stretches.json"
    known = json.loads(path.read_text()) if path.exists() else {}
    key = f"{start:.2f}-{end:.2f}"
    if key not in known:
        clip = WORK / "stretch.wav"
        silence = np.zeros(int(0.1 * RATE), dtype=np.float32)
        write_mono(clip, np.concatenate([silence, source[int((start - 0.05) * RATE):int((end + 0.1) * RATE)], silence]))
        keys = mv.env()
        url = (f"https://{keys['AZURE_SPEECH_REGION']}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe"
               "?api-version=2024-11-15")
        for attempt in range(8):
            result = subprocess.run(["curl", "-s", "-w", "\n%{http_code}", "-X", "POST", url,
                                     "-H", f"Ocp-Apim-Subscription-Key: {keys['AZURE_SPEECH_KEY']}", "-F", f"audio=@{clip}",
                                     "-F", 'definition={"locales":["en-US"],"profanityFilterMode":"None"}'],
                                    capture_output=True, text=True).stdout
            body, code = result.rsplit("\n", 1)
            if code == "200":
                break
            # Azure allows only so many requests a minute, and says so with 429.
            time.sleep(5 * (attempt + 1))
        else:
            raise RuntimeError(f"Azure would not transcribe the stretch at {start:.2f} s")
        known[key] = " ".join(p["text"] for p in json.loads(body).get("phrases", []))
        path.write_text(json.dumps(known, indent=1))
        time.sleep(1.5)
    return known[key]


def words_of(text):
    return re.sub(r"[^a-z0-9 ]", "", text.lower().replace("-", " ").replace("'", "")).split()


def restarts(first, then):
    """Whether one stretch is a false start of the next: the next begins with the same word and says it again. A short
    start counts on its first word alone, as one cut off after a word or two is often heard as something else."""
    a, b = words_of(first), words_of(then)
    if not a or not b or a[0] != b[0]:
        return False
    return len(a) <= 3 or difflib.SequenceMatcher(None, " ".join(a), " ".join(b[: len(a) + 1])).ratio() >= 0.6


def voice_cut(source):
    """The kept stretches of the recording, in order, each with the pause to leave before it. Phrases in RETAKES go
    whole; inside a kept phrase, a stretch the next one starts over is dropped, as is a click or breath on its own."""
    phrases = transcript()["phrases"]
    spans = [(p["offsetMilliseconds"] / 1000, (p["offsetMilliseconds"] + p["durationMilliseconds"]) / 1000) for p in phrases]

    def phrase_of(start, end):
        overlap = [max(0.0, min(end, b) - max(start, a)) for a, b in spans]
        best = int(np.argmax(overlap))
        return best if overlap[best] > 0 else None

    by_phrase = {}
    for start, end in stretches(source):
        index = phrase_of(start, end)
        if index is not None and index not in RETAKES:
            by_phrase.setdefault(index, []).append((start, end))

    pieces, decisions = [], []
    for index in sorted(by_phrase):
        kept = [s for s in by_phrase[index] if s[1] - s[0] >= 0.25]
        if len(kept) > 1:
            texts = [heard(source, *s) for s in kept]
            keep = []
            for n, (stretch, text) in enumerate(zip(kept, texts)):
                if not text.strip() and stretch[1] - stretch[0] < 0.5:
                    decisions.append(f"phrase {index}: dropped a {stretch[1] - stretch[0]:.2f} s sound with no words in it")
                elif n + 1 < len(kept) and stretch[1] - stretch[0] <= 4.0 and restarts(text, texts[n + 1]):
                    decisions.append(f"phrase {index}: dropped false start \"{text}\" before \"{texts[n + 1]}\"")
                else:
                    keep.append(stretch)
            if len(keep) > 1:
                decisions.append(f"phrase {index}: kept {len(keep)} parts: " + " | ".join(
                    f"\"{t}\"" for s2, t in zip(kept, texts) if s2 in keep))
            kept = keep
        for n, (start, end) in enumerate(kept):
            if not pieces:
                gap = 0.4
            elif n > 0:
                # A pause inside a sentence, as at a comma, is kept but never longer than a breath.
                gap = min(0.45, max(HESITATION, start - pieces[-1]["end"]))
            elif index in SECTIONS:
                gap = SECTION_GAP
            else:
                gap = min(BETWEEN, max(0.25, start - pieces[-1]["end"]))
            pieces.append({"phrase": index, "start": start, "end": end, "gap": gap})
    return pieces, decisions


def cut_voice():
    """Builds the edited voice: the kept stretches with their new pauses, the s sounds softened, and a map from each
    phrase to where it now starts and ends."""
    WORK.mkdir(parents=True, exist_ok=True)
    source = mv.decode(VIDEO_FOLDER / "audio.mp3", channels=1)[:, 0]
    pieces, decisions = voice_cut(source)
    for decision in decisions:
        print("  " + decision)
    out = []
    timeline = {}
    t = 0.0
    fade = int(0.012 * RATE)
    for piece in pieces:
        t += piece["gap"]
        out.append(np.zeros(int(piece["gap"] * RATE), dtype=np.float32))
        a = max(0, int((piece["start"] - LEAD) * RATE))
        b = min(len(source), int((piece["end"] + TAIL) * RATE))
        chunk = source[a:b].copy()
        ramp = np.linspace(0, 1, fade, dtype=np.float32)
        chunk[:fade] *= ramp
        chunk[-fade:] *= ramp[::-1]
        out.append(chunk)
        span = timeline.setdefault(piece["phrase"], {"start": t + LEAD, "end": 0.0, "pieces": []})
        span["pieces"].append({"from": piece["start"], "to": piece["end"], "at": t + LEAD})
        t += len(chunk) / RATE
        span["end"] = t - TAIL
    voice = np.concatenate(out + [np.zeros(int(0.5 * RATE), dtype=np.float32)])

    raw = WORK / "voice_cut.wav"
    write_mono(raw, voice)
    clean = WORK / "voice.wav"
    subprocess.run([mv.FFMPEG, "-v", "error", "-y", "-i", str(raw), "-af", VOICE_FILTER, "-ar", str(RATE), str(clean)], check=True)
    (WORK / "voice.json").write_text(json.dumps({"phrases": timeline, "length": len(voice) / RATE}, indent=1))
    return clean


# The recording carries its s sounds about 8 dB hotter than a broadcast voice, so they are tamed until 5 to 9 kHz
# measures like the reference voice, with the clarity around 3.8 kHz lifted back to where the recording had it.
VOICE_FILTER = ("highpass=f=70,"
                "deesser=i=0.5:m=0.5:f=0.45:s=o,"
                "equalizer=f=6800:t=q:w=1.6:g=-2,"
                "highshelf=f=9000:g=-5,"
                "equalizer=f=3800:t=q:w=1.0:g=4,"
                "lowpass=f=12000")


def write_mono(path, samples):
    import wave
    peak = float(np.abs(samples).max()) or 1.0
    scale = min(1.0, 0.99 / peak)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((samples * scale * 32767).astype(np.int16).tobytes())


def measure(path):
    """The voice's tone, measured the way the original and the AI voice were: band levels against 300 to 3000 Hz, and
    how often a 10 ms slice is mostly s sound."""
    a = mv.decode(path, channels=1)[:, 0]
    speech = a[np.abs(a) > 10 ** (-45 / 20)]
    n = 4096
    seg = speech[: len(speech) // n * n].reshape(-1, n) * np.hanning(n)
    power = (np.abs(np.fft.rfft(seg, axis=1)) ** 2).mean(0)
    f = np.fft.rfftfreq(n, 1 / RATE)

    def band(lo, hi):
        return 10 * np.log10(power[(f >= lo) & (f < hi)].sum() + 1e-20)

    ref = band(300, 3000)
    bands = {f"{lo}-{hi} Hz": round(band(lo, hi) - ref, 1) for lo, hi in [(50, 120), (3000, 5000), (5000, 9000), (9000, 16000)]}
    w = 480
    seg = speech[: len(speech) // w * w].reshape(-1, w)
    spectrum = np.abs(np.fft.rfft(seg * np.hanning(w), axis=1))
    ff = np.fft.rfftfreq(w, 1 / RATE)
    hiss = spectrum[:, (ff >= 5000) & (ff < 9000)].sum(1) / (spectrum.sum(1) + 1e-9)
    level = 20 * np.log10(np.sqrt((seg ** 2).mean(1)) + 1e-12)
    overall = 20 * np.log10(np.sqrt((speech ** 2).mean()))
    per_minute = ((hiss > 0.5) & (level > overall - 1)).sum() / (len(speech) / RATE / 60)
    return bands, round(per_minute, 1)


# ------------------------------------------------------------------------------------------------------------ timing

class Voice:
    """Where the edited voice says things: a phrase's start and end, and a word's start, in seconds of the edit."""

    def __init__(self):
        info = json.loads((WORK / "voice.json").read_text())
        self.spans = {int(k): v for k, v in info["phrases"].items()}
        self.length = info["length"]
        self.phrases = transcript()["phrases"]

    def start(self, phrase, offset=0.0):
        return self.spans[phrase]["start"] + offset

    def end(self, phrase, offset=0.0):
        return self.spans[phrase]["end"] + offset

    def word(self, phrase, text, nth=1, offset=0.0):
        """When the nth word of the phrase that begins with `text` is said."""
        seen = 0
        for word in self.phrases[phrase]["words"]:
            if word["text"].lower().strip(".,?!'\"").startswith(text.lower()):
                seen += 1
                at = word["offsetMilliseconds"] / 1000
                # A word in a false start that was cut is passed over for the same word in the line kept.
                for piece in self.spans[phrase]["pieces"]:
                    if piece["from"] - LEAD <= at <= piece["to"] + TAIL:
                        if seen == nth:
                            return piece["at"] + at - piece["from"] + offset
                        break
                else:
                    seen -= 1
        raise KeyError(f"phrase {phrase} has no word {text!r} (occurrence {nth})")


# ------------------------------------------------------------------------------------------------------------ the cut

COBALT = (63, 99, 232)
RED = (220, 38, 38)


def shot_list(v):
    """Every shot of the video, in order, each starting at a moment in the voice and running until the next one.

    A page shot pans and zooms over a captured page: `keys` are (time, zoom, focus), where focus is a found line of
    the page or an (x, y) in its pixels; `marks` outline a line from a time on; `notes` lay a label over the page from
    one time to another; `cursor` glides the pointer to a line by a time. A take shot plays a recording of the app from
    a mark, framed as make_video.py frames its parts. A card shot holds one of the drawn cards, easing in slightly."""
    P, E, W = v.start, v.end, v.word
    return [
        # Intro: the pricing pages, the hand settling on Free, then who each one is for.
        {"at": 0.0, "page": "zipbooks", "keys": [(0.0, 1.0, (1600, 900)), (P(0, 3.2), 1.0, (1600, 1700)), (E(0), 1.15, "starter_free")],
         "cursor": [(P(0, 0.2), (2300, 1500)), (E(0, -0.6), "starter_free")]},
        {"at": P(1), "page": "wave", "keys": [(P(1), 1.0, (1600, 1950)), (P(1, 1.4), 1.0, (1600, 2050))]},
        {"at": P(1, 1.4), "page": "zoho", "keys": [(P(1, 1.4), 1.0, (1600, 1950)), (P(1, 2.8), 1.0, (1600, 2050))]},
        {"at": P(1, 2.8), "page": "argo_pricing", "keys": [(P(1, 2.8), 1.0, (1600, 1500)), (P(1, 4.2), 1.0, (1600, 1600))]},
        {"at": P(1, 4.2), "page": "zipbooks", "keys": [(P(1, 4.2), 1.0, "starter"), (E(1, 0.3), 1.08, "starter")]},
        {"at": P(2), "card": "card_opening", "zoom": (1.0, 1.04), "fade": 0.3},

        # 1. Wave
        {"at": P(3, -0.3), "card": "section_0", "zoom": (1.0, 1.03), "fade": 0.3},
        {"at": P(4), "page": "wave",
         "keys": [(P(4), 1.0, (1600, 1950)), (W(5, "unlimited"), 1.35, "starter_unlimited"), (E(6), 1.35, "starter_unlimited"),
                  (P(8), 1.0, (1600, 2050)), (P(9, -0.4), 1.0, (1600, 2050))],
         "marks": [(W(5, "unlimited"), "starter_unlimited", COBALT), (W(8, "import"), "pro_bank", RED)],
         "cursor": [(P(4, 0.3), (2600, 1500)), (W(5, "unlimited"), "starter_unlimited"), (W(8, "import"), "pro_bank")]},
        {"at": P(9), "page": "wave",
         "keys": [(P(9), 1.0, "receipts_row"), (P(9, 1.5), 1.25, (1600, 10020))],
         "marks": [(P(9, 0.6), "receipts_row", RED), (P(9, 1.2), "receipts_addon", RED)],
         "notes": [(P(9, 1.2), "note_wave_addon", E(9, 1.2))],
         "cursor": [(P(9, 1.0), "receipts_addon")]},
        {"at": P(10), "page": "wave",
         "keys": [(P(10), 1.15, "pro_price"), (W(10, "putting", offset=-0.3), 1.15, "pro_price"),
                  (W(10, "logo"), 1.25, "logo_row"), (E(10), 1.25, "logo_row")],
         "marks": [(P(10, 0.3), "pro_bank", RED), (W(10, "25"), "pro_price", RED), (W(10, "logo"), "logo_row", RED)],
         "cursor": [(W(10, "25"), "pro_price"), (W(10, "logo", offset=0.2), "logo_row")]},
        {"at": P(11), "page": "wave",
         "keys": [(P(11), 1.35, "starter_unlimited"), (E(13), 1.1, (1600, 1950))],
         "marks": [(P(11, 0.2), "starter_unlimited", COBALT)],
         "cursor": [(P(11, 0.5), "starter_unlimited")]},

        # 2. Zoho Books
        {"at": P(15, -0.3), "card": "section_1", "zoom": (1.0, 1.03), "fade": 0.3},
        {"at": P(16), "page": "zoho",
         "keys": [(P(16), 1.0, (1600, 1950)), (P(19, -0.4), 1.0, (1600, 1950))],
         "marks": [(P(16, 0.4), "free_price", COBALT)],
         "cursor": [(P(16, 0.6), "free_price")]},
        {"at": P(19), "page": "zoho",
         "keys": [(P(19), 1.25, "faq_invoices"), (E(19), 1.25, "faq_expenses")],
         "marks": [(W(19, "1000"), "faq_invoices", COBALT), (W(19, "1000", nth=2), "faq_expenses", COBALT)],
         "cursor": [(W(19, "1000"), "faq_invoices"), (W(19, "1000", nth=2), "faq_expenses")]},
        {"at": P(20), "page": "zoho",
         "keys": [(P(20), 1.3, "faq_free"), (E(23), 1.2, "faq_free")],
         "marks": [(W(20, "50"), "faq_free", RED)],
         "notes": [(W(20, "50", offset=-0.2), "note_zoho_limit", E(23, 0.3))],
         "cursor": [(W(20, "50"), "faq_free")]},
        {"at": P(24), "page": "zoho",
         "keys": [(P(24), 1.2, "standard_price"), (W(25, "one"), 1.1, "free_users"), (W(25, "bank"), 1.1, "bank_feeds"),
                  (P(27), 1.0, (1600, 1950)), (E(27, 0.8), 1.0, (1600, 1950))],
         "marks": [(W(24, "20"), "standard_price", RED), (W(25, "one"), "free_users", RED), (W(25, "bank"), "bank_feeds", RED)],
         "cursor": [(W(24, "20"), "standard_price"), (W(25, "one"), "free_users"), (W(25, "bank"), "bank_feeds")]},

        # 3. Argo Books
        {"at": P(29, -0.3), "card": "section_2", "zoom": (1.0, 1.03), "fade": 0.3},
        {"at": P(30), "page": "argo_home", "keys": [(P(30), 1.0, (1600, 900)), (E(31), 1.0, (1600, 1500))]},
        {"at": P(32), "page": "argo_downloads", "keys": [(P(32), 1.0, (1600, 900)), (E(32, 0.3), 1.05, (1600, 1000))]},
        {"at": P(33), "take": "welcome", "from": ("start", 0.0),
         "keys": [("start", 0.0, 1.0, 1600, 900)]},
        {"at": P(33, 5.6), "take": "tour", "from": ("dashboard", 0.0),
         "keys": [("dashboard", 0.0, 1.0, 1600, 900)]},
        {"at": P(35), "take": "tour", "from": ("analytics", 0.4),
         "keys": [("analytics", 0.0, 1.0, 1600, 900)]},
        {"at": P(36), "page": "argo_pricing",
         "keys": [(P(36), 1.15, "free_card"), (W(36, "25", nth=2), 1.3, "free_scans"), (E(36), 1.3, "free_scans")],
         "marks": [(W(36, "25"), "free_invoices", COBALT), (W(36, "10"), "free_scans", COBALT), (W(36, "25", nth=2), "free_bank", COBALT)],
         "cursor": [(W(36, "25"), "free_invoices"), (W(36, "10"), "free_scans"), (W(36, "25", nth=2), "free_bank")]},
        {"at": P(37), "take": "receipt", "from": ("dropped", -1.0),
         "keys": [("dropped", -1.0, 1.3, 1600, 930), ("scan_done", 0.0, 1.3, 1600, 930), ("scan_done", 1.5, 1.15, 2000, 760)],
         "skip": ("scanning", 1.0, "scan_done", -0.2)},
        {"at": P(39), "take": "tour", "from": ("invoices", 0.3),
         "keys": [("invoices", 0.0, 1.0, 1600, 900), ("invoices", 2.0, 1.0, 1600, 900), ("invoices", 4.5, 1.6, 1850, 940)]},
        {"at": P(44), "take": "bank", "from": ("prompt", -0.6),
         "keys": [("prompt", 0.0, 1.3, 1600, 900), ("no_login", 1.0, 1.45, 1600, 880), ("click_choose", 0.0, 1.3, 1600, 900),
                  ("rows", 0.6, 1.15, 1600, 1150)]},
        {"at": P(46), "take": "tour", "from": ("dashboard", 0.5),
         "keys": [("dashboard", 0.0, 1.0, 1600, 900)]},
        {"at": W(47, "expenses"), "take": "tour", "from": ("expenses", 0.4), "keys": [("expenses", 0.0, 1.0, 1600, 900)]},
        {"at": W(47, "income"), "take": "tour", "from": ("revenue", 0.4), "keys": [("revenue", 0.0, 1.0, 1600, 900)]},
        {"at": W(47, "scan"), "take": "receipt", "from": ("scan_done", -0.1),
         "keys": [("scan_done", 0.0, 1.0, 1600, 900), ("scan_done", 2.0, 1.12, 2000, 760)]},
        {"at": W(48, "send"), "take": "tour", "from": ("invoices", 0.4), "keys": [("invoices", 0.0, 1.0, 1600, 900)]},
        {"at": W(48, "create"), "take": "tour", "from": ("reports", 0.4), "keys": [("reports", 0.0, 1.0, 1600, 900)]},
        {"at": W(48, "much"), "take": "tour", "from": ("analytics", 0.4), "keys": [("analytics", 0.0, 1.0, 1600, 900)]},
        {"at": P(50), "page": "argo_pricing",
         "keys": [(P(50), 1.1, (1600, 1500)), (W(50, "15"), 1.3, "premium_price"), (P(51), 1.3, "premium_price")],
         "marks": [(W(50, "15"), "premium_price", COBALT)],
         "cursor": [(W(50, "15"), "premium_price")]},
        {"at": W(51, "adds"), "take": "tour", "from": ("insights", 0.3),
         "keys": [("insights", 0.0, 1.0, 1600, 900), ("insights", 2.5, 1.2, 2000, 800)]},

        # 4. ZipBooks
        {"at": P(52, -0.3), "card": "section_3", "zoom": (1.0, 1.03), "fade": 0.3},
        {"at": P(53), "page": "zipbooks",
         "keys": [(P(53), 1.0, "starter"), (W(53, "unlimited"), 1.3, "unlimited_contacts"), (E(54), 1.25, "payments")],
         "marks": [(W(53, "unlimited"), "unlimited_invoices", COBALT), (W(53, "customers"), "unlimited_contacts", COBALT),
                   (W(53, "basic"), "reports", COBALT), (W(53, "square"), "payments", COBALT)],
         "cursor": [(W(53, "unlimited"), "unlimited_invoices"), (W(53, "customers"), "unlimited_contacts"),
                    (W(53, "basic"), "reports"), (W(53, "square"), "payments")]},
        {"at": P(55), "page": "zipbooks",
         "keys": [(P(55), 1.15, (1600, 1700)), (W(56, "one"), 1.25, "one_bank"), (W(57, "recurring"), 1.25, "recurring"),
                  (W(58, "team"), 1.2, "team"), (W(59, "15"), 1.2, "smarter_price"), (E(61), 1.0, (1600, 1700))],
         "marks": [(W(56, "one"), "one_bank", RED), (W(57, "recurring"), "recurring", RED), (W(58, "reminders"), "reminders", RED),
                   (W(58, "time"), "time", RED), (W(58, "team"), "team", RED), (W(59, "15"), "smarter_price", RED)],
         "notes": [(W(59, "15"), "note_zip_smarter", E(59, 1.0))],
         "cursor": [(W(56, "one"), "one_bank"), (W(57, "recurring"), "recurring"), (W(58, "reminders"), "reminders"),
                    (W(58, "time"), "time"), (W(58, "team"), "team"), (W(59, "15"), "smarter_price")]},

        # Recap: each one lit as it is named, then each catch.
        {"at": P(62, -0.3), "card": "card_best_0", "zoom": (1.0, 1.02), "fade": 0.3},
        {"at": P(63), "card": "card_best_1", "zoom": (1.02, 1.03)},
        {"at": P(64), "card": "card_best_2", "zoom": (1.03, 1.04)},
        {"at": P(65), "card": "card_best_3", "zoom": (1.04, 1.05)},
        {"at": P(66), "card": "card_all", "zoom": (1.0, 1.02)},
        {"at": P(68), "card": "card_catch_0", "zoom": (1.02, 1.03)},
        {"at": P(70), "card": "card_catch_1", "zoom": (1.03, 1.04)},
        {"at": P(71), "card": "card_catch_3", "zoom": (1.04, 1.05)},
        {"at": P(72), "card": "card_catch_2", "zoom": (1.05, 1.06)},
        {"at": P(73), "page": "argo_home", "keys": [(P(73), 1.0, (1600, 900)), (v.length, 1.05, (1600, 900))]},
        {"at": v.length, "outro": True},
    ]


# ---------------------------------------------------------------------------------------------------------- drawing

OUT = (1920, 1080)
FPS = mv.FPS


class Pages:
    def __init__(self):
        self.images = {}
        self.boxes = {}

    def get(self, name):
        if name not in self.images:
            from PIL import Image
            Image.MAX_IMAGE_PIXELS = None
            self.images[name] = Image.open(WORK / "pages" / f"{name}.png").convert("RGB")
            self.boxes[name] = json.loads((WORK / "pages" / f"{name}.json").read_text())["boxes"]
        return self.images[name], self.boxes[name]


def ease(p):
    p = min(1.0, max(0.0, p))
    return p * p * (3 - 2 * p)


def keyed(keys, t):
    """The value at time t of a list of (time, value...) keys, eased between neighbours and held past the ends."""
    if t <= keys[0][0]:
        return keys[0][1:]
    for a, b in zip(keys, keys[1:]):
        if t < b[0]:
            p = ease((t - a[0]) / max(1e-6, b[0] - a[0]))
            return tuple(x + (y - x) * p for x, y in zip(a[1:], b[1:]))
    return keys[-1][1:]


def page_frame(shot, t, pages, sprites, size=OUT):
    from PIL import Image, ImageDraw
    image, boxes = pages.get(shot["page"])

    def point(focus):
        if isinstance(focus, str):
            x, y, w, h = boxes[focus]
            return x + w / 2, y + h / 2
        return focus

    keys = [(k[0], k[1], *point(k[2])) for k in shot["keys"]]
    zoom, fx, fy = keyed(keys, t)
    w = image.width / zoom
    h = w * size[1] / size[0]
    x = min(max(fx - w / 2, 0), image.width - w)
    y = min(max(fy - h / 2, 0), max(0, image.height - h))
    ratio = size[0] / w

    # The outlines are drawn on the page at its own size and shrunk along with it, so they stay fixed to the line they
    # mark; drawn on the shrunk frame they would snap to whole pixels while the page moved by fractions of one.
    left, top = int(x), int(y)
    right, bottom = min(image.width, int(x + w) + 2), min(image.height, int(y + h) + 2)
    marks = [(at, target, colour) for at, target, colour in shot.get("marks", []) if t >= at and target in boxes]
    region = image.crop((left, top, right, bottom))
    if marks:
        region = region.convert("RGBA")
        layer = Image.new("RGBA", region.size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(layer)
        for at, target, colour in marks:
            alpha = ease((t - at) / 0.25)
            bx, by, bw, bh = boxes[target]
            pad = 14
            draw.rounded_rectangle((bx - pad - left, by - pad - top, bx + bw + pad - left, by + bh + pad - top), radius=14,
                                   fill=colour + (int(26 * alpha),), outline=colour + (int(230 * alpha),), width=9)
        region.alpha_composite(layer)
    frame = region.resize(size, Image.LANCZOS, box=(x - left, y - top, x - left + w, y - top + h)).convert("RGBA")

    if shot.get("cursor"):
        moves = [(at, point(target)) for at, target in shot["cursor"]]
        cx, cy = moves[0][1]
        for (a0, p0), (a1, p1) in zip(moves, moves[1:]):
            if t >= a1:
                cx, cy = p1
            elif t > a1 - 0.7:
                q = ease((t - (a1 - 0.7)) / 0.7)
                cx, cy = p0[0] + (p1[0] - p0[0]) * q, p0[1] + (p1[1] - p0[1]) * q
                break
            else:
                cx, cy = p0
                break
        if t >= moves[0][0] - 0.01:
            # The pointer rests a little right of and below the line it points at, as a hand would leave it.
            sx, sy = (cx - x) * ratio + 18, (cy - y) * ratio + 14
            key = round(44 * ratio)
            if key not in sprites:
                sprites[key] = mv.cursor_sprite(key)
            sprite, tip = sprites[key]
            frame.alpha_composite(sprite, (round(sx - tip), round(sy - tip)))

    for at, name, until in shot.get("notes", []):
        if at <= t < until + 0.3:
            alpha = min(ease((t - at) / 0.3), ease((until + 0.3 - t) / 0.3))
            note = notes_image(name)
            # Bottom right, where pricing pages keep the least: their plans and answers read from the left.
            nx = size[0] - note.width - 30
            ny = round(size[1] - note.height - 30 + 30 * (1 - alpha))
            faded = note.copy()
            faded.putalpha(note.getchannel("A").point(lambda a: int(a * alpha)))
            frame.alpha_composite(faded, (nx, ny))
    return frame


_NOTES = {}


def notes_image(name):
    from PIL import Image
    if name not in _NOTES:
        im = Image.open(WORK / "cards" / f"{name}.png").convert("RGBA")
        _NOTES[name] = im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
    return _NOTES[name]


_CARDS = {}


def card_frame(shot, t, start, length):
    from PIL import Image
    if shot["card"] not in _CARDS:
        _CARDS[shot["card"]] = Image.open(WORK / "cards" / f"{shot['card']}.png").convert("RGB")
    image = _CARDS[shot["card"]]
    z0, z1 = shot["zoom"]
    zoom = z0 + (z1 - z0) * ease((t - start) / max(1e-6, length))
    w = image.width / zoom
    h = image.height / zoom
    x = (image.width - w) / 2
    y = (image.height - h) / 2
    frame = image.resize(OUT, Image.LANCZOS, box=(x, y, x + w, y + h)).convert("RGBA")
    fade = shot.get("fade", 0)
    if fade and t - start < fade:
        frame = Image.blend(Image.new("RGBA", OUT, (255, 255, 255, 255)), frame, ease((t - start) / fade))
    return frame


def take_part(shot, take):
    """The shot as a part make_video.py's painter can draw: its first frame of the take and its framing."""
    mark, offset = shot["from"]
    return {"wide": shot["keys"], "start_frame": round((take["marks"][mark] + offset) * FPS)}


def render_shot(job):
    """Draws one shot to its own file, so shots can be drawn side by side and joined after."""
    index, shot, start, length, out = job
    from PIL import Image
    frames = round(length * FPS)
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{OUT[0]}x{OUT[1]}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "fast", "-crf", "15", "-threads", "2",
                                "-pix_fmt", "yuv420p", str(out)], stdin=subprocess.PIPE)
    if "page" in shot:
        pages, sprites = Pages(), {}
        for i in range(frames):
            encoder.stdin.write(page_frame(shot, start + i / FPS, pages, sprites).convert("RGB").tobytes())
    elif "card" in shot:
        for i in range(frames):
            encoder.stdin.write(card_frame(shot, start + i / FPS, start, length).convert("RGB").tobytes())
    elif "take" in shot:
        take = load_take(shot["take"])
        painter = mv.Painter("wide", take, [], [])
        part = take_part(shot, take)
        first = part["start_frame"]
        order = list(range(first, first + frames))
        if "skip" in shot:
            # The wait for the scan is left out, the take jumping from just after the scan starts to just before it ends.
            m0, o0, m1, o1 = shot["skip"]
            cut_from = round((take["marks"][m0] + o0) * FPS)
            cut_to = round((take["marks"][m1] + o1) * FPS)
            order = [f if f < cut_from else f + (cut_to - cut_from) for f in order]
        last = None
        switch_from, switch_left = None, 0
        runs = []
        for f in order:
            if runs and f == runs[-1][1]:
                runs[-1][1] += 1
            else:
                runs.append([f, f + 1])
        for a, b in runs:
            for k, pixels in enumerate(mv.take_frames(take, a, b - a)):
                frame = painter.app_frame(pixels, part, 0, a + k)
                if last is not None and switch_left == 0 and mv.page_switch(last, frame):
                    switch_from, switch_left = last, mv.PAGE_SWITCH_FRAMES
                if switch_left:
                    frame = Image.blend(switch_from.convert("RGBA"), frame.convert("RGBA"), 1 - switch_left / (mv.PAGE_SWITCH_FRAMES + 1))
                    switch_left -= 1
                last = frame
                encoder.stdin.write(frame.convert("RGB").tobytes())
    elif "outro" in shot:
        clip = subprocess.run([mv.FFMPEG, "-v", "error", "-i", str(OUTRO), "-vf", f"fps={FPS},scale={OUT[0]}:{OUT[1]}",
                               "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, check=True).stdout
        encoder.stdin.write(clip)
    encoder.stdin.close()
    encoder.wait()
    return index


OUTRO = Path(r"C:\Users\evand\Desktop\Pictures\Argo\Argo Books\Videos\Assets\Outro.mp4")
TAKES = WORK / "takes"
RECEIPT_TAKES = HERE / "work" / "takes"


def load_take(name):
    folder = RECEIPT_TAKES if name == "receipt" else TAKES
    log = json.loads((folder / f"{name}.json").read_text())
    log["path"] = folder / f"{name}.mkv"
    return log


def outro_length():
    out = subprocess.run([mv.FFMPEG.replace("ffmpeg.exe", "ffprobe.exe"), "-v", "error", "-show_entries", "format=duration",
                          "-of", "csv=p=0", str(OUTRO)], capture_output=True, text=True).stdout
    return float(out.strip())


def render_picture(v, only=None, join=True):
    from multiprocessing import Pool
    shots = shot_list(v)
    total = v.length + outro_length()
    folder = WORK / "shots"
    folder.mkdir(parents=True, exist_ok=True)
    jobs = []
    for n, shot in enumerate(shots):
        end = shots[n + 1]["at"] if n + 1 < len(shots) else total
        # Lengths come from whole frames on a shared clock, so the joined shots stay on the voice's time.
        length = (round(end * FPS) - round(shot["at"] * FPS)) / FPS
        jobs.append((n, shot, round(shot["at"] * FPS) / FPS, length, folder / f"{n:03}.mp4"))
    todo = [j for j in jobs if only is None or j[0] in only]
    # Each shot's encoder is held to two threads, as several encoders left to take every core slow each other badly.
    with Pool(4) as pool:
        for done in pool.imap_unordered(render_shot, todo):
            print(f"  shot {done} drawn")
    if not join:
        return shots, None, total
    listing = folder / "list.txt"
    listing.write_text("".join(f"file '{j[4].name}'\n" for j in jobs))
    picture = WORK / "picture.mp4"
    subprocess.run([mv.FFMPEG, "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(listing), "-c", "copy", str(picture)], check=True)
    return shots, picture, total


# ------------------------------------------------------------------------------------------------------------ sound

MUSIC_UNDER_VOICE_DB = -18.0
MUSIC_BETWEEN_DB = -13.0


def mix(v, shots, total):
    """The voice, a quiet music bed that dips further under each sentence, the app's clicks, and the outro's own sound."""
    n = int((total + 0.5) * RATE)
    voice = mv.decode(WORK / "voice.wav", channels=1)[:, 0]
    voice = np.pad(voice, (0, max(0, n - len(voice))))[:n]

    effects = np.zeros(n)
    for index, shot in enumerate(shots):
        if "take" not in shot:
            continue
        take = load_take(shot["take"])
        end = shots[index + 1]["at"] if index + 1 < len(shots) else total
        first = take["marks"][shot["from"][0]] + shot["from"][1]
        for event in take["events"]:
            at = shot["at"] + event["t"] - first
            if shot.get("skip") and event["t"] > take["marks"][shot["skip"][2]] + shot["skip"][3]:
                at -= (take["marks"][shot["skip"][2]] + shot["skip"][3]) - (take["marks"][shot["skip"][0]] + shot["skip"][1])
            kind = {"click": "click", "drop": "drop", "saved": "chime"}.get(event["kind"])
            if kind and shot["at"] <= at < end:
                s = mv.sound(kind) * 10 ** (mv.SOUND_GAIN_DB[kind] / 20)
                i = int(at * RATE)
                effects[i:i + len(s)] += s[: n - i]

    music = mv.decode(mv.MUSIC)
    loop = int(3 * RATE)
    bed = music.copy()
    while len(bed) < n:
        ramp = np.linspace(0, 1, loop)[:, None]
        bed = np.concatenate([bed[:-loop], bed[-loop:] * (1 - ramp) + music[:loop] * ramp, music[loop:]])
    bed = bed[:n].mean(axis=1)

    def rms(x):
        return np.sqrt(np.mean(x * x) + 1e-12)

    speaking = np.abs(voice) > 10 ** (-40 / 20)
    held = mv.window_sum(speaking, 0.3) > 0.5
    voice_level = rms(voice[held])
    base = voice_level / rms(bed) * 10 ** (MUSIC_BETWEEN_DB / 20)
    duck = mv.smooth(np.where(held, 10 ** ((MUSIC_UNDER_VOICE_DB - MUSIC_BETWEEN_DB) / 20), 1.0), 0.25)
    outro_at = int(v.length * RATE)
    fade_out = np.clip((outro_at + 1.5 * RATE - np.arange(n)) / (2.5 * RATE), 0, 1)
    fade_in = np.minimum(1, np.arange(n) / (1.0 * RATE))
    bed = bed * base * duck * fade_in * fade_out

    outro = mv.decode(OUTRO, channels=1)[:, 0]
    tail = np.zeros(n)
    tail[outro_at:outro_at + len(outro)] = outro[: n - outro_at]

    def db(x):
        return 20 * np.log10(rms(x))
    quiet = ~held & (np.arange(n) > RATE) & (np.arange(n) < outro_at)
    print(f"  levels (RMS): voice {db(voice[held]):.1f} dB, music under the voice {db(bed[held]):.1f} dB, "
          f"music between sentences {db(bed[quiet]):.1f} dB, loudest click {20 * np.log10(np.abs(effects).max() + 1e-9):.1f} dB peak")
    mono = voice + effects + bed + tail
    return np.stack([mono, mono], axis=1)


# Where the transcript misheard a word, as a second transcription of the edited voice confirmed, and how the products
# and their plans are spelled.
CAPTION_FIXES = {
    "the great answer": "the right answer",
    "So what's the best free cloud product": "So it's the best free cloud product",
    "if invoice and volume": "if invoicing volume",
    "ArgoBooks": "Argo Books",
    "Zipbooks": "ZipBooks",
    "pro plan": "Pro plan",
    "on standard": "on Standard",
}


def write_captions(v, path):
    def stamp(seconds):
        ms = int(round(seconds * 1000))
        return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"
    blocks = []
    for n, (index, span) in enumerate(sorted(v.spans.items(), key=lambda kv: kv[1]["start"]), 1):
        text = v.phrases[index]["text"]
        for wrong, right in CAPTION_FIXES.items():
            text = text.replace(wrong, right)
        blocks.append(f"{n}\n{stamp(span['start'])} --> {stamp(span['end'] + 0.2)}\n{text}\n")
    path.write_text("\n".join(blocks), encoding="utf-8")


def chapters(v):
    names = {3: "Wave: best if you send a lot of invoices", 15: "Zoho Books: best if you're under C$50K a year",
             29: "Argo Books: best if you want your books on your own computer",
             52: "ZipBooks: best if you just want to invoice and get paid", 62: "Which one should you actually pick?"}
    lines = ["0:00 - Intro, and a disclosure"]
    for phrase, name in names.items():
        t = int(v.start(phrase, -0.3))
        lines.append(f"{t // 60}:{t % 60:02} - {name}")
    return "\n".join(lines)


def main():
    args = sys.argv[1:]
    if args[:1] == ["voice"] or not (WORK / "voice.wav").exists():
        clean = cut_voice()
        info = json.loads((WORK / "voice.json").read_text())
        print(f"voice: {info['length']:.1f} s after cutting (recording was 491.3 s)")
        print("  before:", measure(VIDEO_FOLDER / "audio.mp3"))
        print("  AI voice reference: 50-120 Hz -6.5, 3000-5000 Hz -14.7, 5000-9000 Hz -15.2, 9000-16000 Hz -24.6 dB, 0.0 a minute")
        print("  after: ", measure(clean))
        if args[:1] == ["voice"]:
            return

    v = Voice()
    # "shots" draws just the shots named, for checking; "redo" draws them and then joins and mixes the whole video.
    only = {int(a) for a in args[1:]} if args[:1] in (["shots"], ["redo"]) else None
    shots, picture, total = render_picture(v, only, join=args[:1] != ["shots"])
    if picture is None:
        return
    audio = mix(v, shots, total)
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, audio)
    videos = HERE / "work" / "videos"
    videos.mkdir(parents=True, exist_ok=True)
    out = videos / "video_13_free_accounting_software_roundup.mp4"
    mv.master(mixed, picture, out)
    write_captions(v, out.with_suffix(".srt"))
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(out), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"  {out.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    print("chapters:\n" + chapters(v))


if __name__ == "__main__":
    main()

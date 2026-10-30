"""Make the vertical Short "Free accounting software for PC": the app's main jobs in under 30 seconds, narrated by an
ElevenLabs voice, with big captions timed to each word.

Usage:
  python pc_short.py           renders the Short (the voice is only fetched if it is missing)
  python pc_short.py revoice   fetches the voice again first, as after moving to a plan that allows commercial use

ELEVENLABS_API_KEY is read from .env.local. The takes come from work/roundup/takes and work/takes, so record them first.
The finished video goes to work/videos/.
"""
import json
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np

import catches_short as short
import make_video as mv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "pc_short"
OUT_FILE = HERE / "work" / "videos" / "short_free_accounting_software_for_pc.mp4"
RATE = r.RATE
VOICE_ID = "iP95p4xoKVk53GoZ742B"  # Chris, charming and down to earth
SCRIPT = """Looking for free accounting software for your PC?
Argo Books runs on your own computer.
No account. No login. Your books are just a file.
Track your expenses and income.
Send invoices.
Scan a receipt, and it fills itself in.
Import a bank statement, without linking your bank.
See how your business is doing.
Free for Windows, Mac and Linux."""
HOLD_END = 1.8
# Where the track starts, past its opening, so this Short does not sound like the last one.
MUSIC_FROM = 30.0
MUSIC_UNDER_VOICE_DB = -16.0
MUSIC_BETWEEN_DB = -11.0


def fetch_voice():
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / "script.txt").write_text(SCRIPT, encoding="utf-8")
    node = shutil.which("node")
    subprocess.run([node, "--env-file=.env.local", "elevenlabs_voice.mjs", VOICE_ID, str(WORK / "script.txt"),
                    str(WORK / "voice.mp3"), str(WORK / "timing.json")], cwd=HERE, check=True)


def timed_words():
    """The words of the script with their times, from the characters' timings."""
    timing = json.loads((WORK / "timing.json").read_text())
    chars = timing["characters"]
    starts = timing["characterStartTimesSeconds"]
    ends = timing["characterEndTimesSeconds"]
    words, current = [], None
    for char, start, end in zip(chars, starts, ends):
        if char.isspace():
            current = None
            continue
        if current is None:
            current = {"text": "", "at": start, "end": end}
            words.append(current)
        current["text"] += char
        current["end"] = end
    return words


def shot_list(words, total):
    def at(text, nth=1):
        seen = 0
        for word in words:
            if word["text"].strip(".,?").lower() == text.lower():
                seen += 1
                if seen == nth:
                    return word["at"]
        raise KeyError(text)

    slice_of = lambda mark, x=1600, y=900: [(mark, 0.0, 1.0, x, y)]
    return [
        {"at": 0.0, "take": "tour", "from": ("dashboard", 1.0), "title": "Free accounting software", "keys": slice_of("dashboard", 1700, 800)},
        {"at": at("No") - 0.1, "take": "welcome", "from": ("hover_create", -1.6), "title": "No account. No login.", "keys": slice_of("start", 1650, 800)},
        {"at": at("Track") - 0.1, "take": "tour", "from": ("expenses", 0.4), "title": "Track expenses", "keys": slice_of("expenses", 1500, 900)},
        {"at": at("income") - 0.1, "take": "tour", "from": ("revenue", 0.4), "title": "Track income", "keys": slice_of("revenue", 1500, 900)},
        {"at": at("Send") - 0.1, "take": "tour", "from": ("invoices", 0.4), "title": "Send invoices", "keys": slice_of("invoices", 1800, 900)},
        {"at": at("Scan") - 0.1, "take": "receipt", "from": ("scan_done", 0.6), "title": "Scan receipts", "keys": slice_of("scan_done", 2007, 760)},
        {"at": at("Import") - 0.1, "take": "bank", "from": ("no_login", -0.4), "title": "No bank login", "keys": slice_of("start", 1600, 880)},
        {"at": at("See") - 0.1, "take": "tour", "from": ("analytics", 0.4), "title": "See how you're doing", "keys": slice_of("analytics", 1750, 800)},
        {"at": at("Free", 2) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def mix(plan, total):
    n = int((total + 0.3) * RATE)
    voice = mv.decode(WORK / "voice.mp3", channels=1)[:, 0]
    voice = np.pad(voice, (0, max(0, n - len(voice))))[:n]

    def rms(x):
        return np.sqrt(np.mean(x * x) + 1e-12)

    held = mv.window_sum(np.abs(voice) > 10 ** (-40 / 20), 0.3) > 0.5
    bed = mv.decode(mv.MUSIC)[int(MUSIC_FROM * RATE):][:n].mean(axis=1)
    level = rms(voice[held]) / rms(bed) * 10 ** (MUSIC_BETWEEN_DB / 20)
    duck = mv.smooth(np.where(held, 10 ** ((MUSIC_UNDER_VOICE_DB - MUSIC_BETWEEN_DB) / 20), 1.0), 0.2)
    fade = np.clip((total - np.arange(n) / RATE) / 1.2, 0, 1) * np.minimum(1, np.arange(n) / (0.3 * RATE))
    bed = bed * level * duck * fade

    effects = np.zeros(n)
    for shot in plan[1:-2]:
        s = mv.sound("pop") * 10 ** ((mv.SOUND_GAIN_DB["pop"] - 3) / 20)
        at = int(shot["at"] * RATE)
        effects[at:at + len(s)] += s[: n - at]
    s = mv.sound("chime") * 10 ** (mv.SOUND_GAIN_DB["chime"] / 20)
    at = int(plan[-2]["at"] * RATE)
    effects[at:at + len(s)] += s[: n - at]

    def db(x):
        return 20 * np.log10(rms(x))
    print(f"levels (RMS): voice {db(voice[held]):.1f} dB, music under it {db(bed[held]):.1f} dB, between lines {db(bed[~held & (fade > 0.99)]):.1f} dB")
    mono = voice + effects + bed
    return np.stack([mono, mono], axis=1)


def main():
    if sys.argv[1:] == ["revoice"] or not (WORK / "voice.mp3").exists():
        fetch_voice()
    words = timed_words()
    length = max(w["end"] for w in words)
    total = length + HOLD_END
    # This voice takes tiny breaths mid-sentence, which should not break a caption.
    chunks = short.caption_chunks(words, longest_pause=0.4)
    print("captions:", " / ".join(" ".join(w["text"] for w in c) for c in chunks))
    plan = shot_list(words, total)
    for shot, following in zip(plan, plan[1:]):
        print(f"{shot['at']:5.1f} s for {following['at'] - shot['at']:4.1f} s: {shot.get('title', 'end card')}")
    picture = WORK / "picture.mp4"
    shown = short.render_picture(picture, plan, chunks)
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mix(plan, shown))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {shown:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")


if __name__ == "__main__":
    main()

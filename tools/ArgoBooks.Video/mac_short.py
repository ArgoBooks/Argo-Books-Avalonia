"""Make the vertical Short "Free accounting software for Mac": the app as macOS shows it, its main jobs in under 30 seconds,
narrated by an Azure voice, with big captions timed to each word.

Usage: python mac_short.py

It uses the Mac takes and pages of mac_video.py, so make those first. The finished video goes to work/videos/.
"""
from pathlib import Path

import catches_short as short
import mac_video as mac
import mac_window as mw
import make_video as mv
import pc_short as pc
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "mac_short"
OUT_FILE = HERE / "work" / "videos" / "short_free_accounting_software_for_mac.mp4"
SCRIPT = """Looking for free accounting software for your Mac?
Most of them are just a website.
Argo Books is a real Mac app.
No account. No login. Your books are a file on your Mac.
Drop in a receipt. It fills itself in.
Import a bank statement, without linking your bank.
Your profit, worked out for you.
Free for Apple Silicon and Intel Macs."""
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 120.0


def shot_list(words, total):
    def at(text, nth=1):
        matches = [w["at"] for w in words if w["text"].strip(".,?").lower() == text.lower()]
        return matches[nth - 1]

    return [
        {"at": 0.0, "take": "tour", "from": ("dashboard", 0.5), "title": "Free accounting for Mac",
         "keys": [("dashboard", 0.5, 0.8, 880, 815), ("dashboard", 3.5, 0.88, 820, 760)]},
        {"at": at("Most") - 0.1, "page": "wave_us", "title": "Most are just a website",
         "keys": [(at("Most") - 0.1, 2.3, (1500, 1250)), (at("Argo") - 0.1, 2.45, (1500, 1300))]},
        {"at": at("Argo") - 0.1, "page": "argo_downloads_mac", "title": "A real Mac app",
         "keys": [(at("Argo") - 0.1, 2.3, "mac_card"), (at("No") - 0.1, 2.6, (1600, 1180))],
         "marks": [(at("Mac", 2), "silicon", r.COBALT), (at("Mac", 2) + 0.15, "intel", r.COBALT)]},
        {"at": at("No") - 0.1, "take": "welcome", "from": ("start", 0.6), "title": "No account. No login.",
         "keys": [("start", 0.6, 0.9, 1600, 1150), ("hover_create", 1.5, 1.0, 1550, 1180)]},
        {"at": at("Drop") - 0.1, "take": "receipt", "from": ("scan_done", 0.4), "title": "Receipts fill themselves in",
         "keys": [("scan_done", 0.4, 0.75, 1500, 1000), ("look_total", 0.0, 0.9, 1900, 800)]},
        {"at": at("Import") - 0.1, "take": "bank", "from": ("no_login", -0.4), "title": "No bank login",
         "keys": [("no_login", -0.4, 1.2, 1600, 930), ("no_login", 2.0, 1.3, 1600, 930)]},
        {"at": at("Your", 4) - 0.1, "take": "tour", "from": ("dashboard", 4.0), "title": "Your profit, worked out",
         "keys": [("dashboard", 4.0, 0.85, 1250, 1250), ("dashboard", 7.0, 0.95, 1250, 1250)]},
        {"at": at("Free", 2) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(SCRIPT))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.caption_chunks(words, longest_pause=0.4)
    pc.finish(shot_list(words, total), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=mac.load_take, take_frames=mw.frames, pages=mac.Pages())


if __name__ == "__main__":
    main()

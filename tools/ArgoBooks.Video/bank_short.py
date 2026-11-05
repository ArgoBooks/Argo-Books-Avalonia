"""Make the vertical Short "Import a bank statement without linking your bank": the app's import, start to finish, narrated
by an Azure voice, with big captions timed to each word.

Usage: python bank_short.py

The voice is fetched once and kept in work/voice/, and the words' timings in work/bank_short/. The take comes from
work/roundup/takes, so record it first. The finished video goes to work/videos/.
"""
from pathlib import Path

import catches_short as short
import make_video as mv
import pc_short as pc
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "bank_short"
OUT_FILE = HERE / "work" / "videos" / "short_import_bank_statement.mp4"
SCRIPT = """Don't want to link your bank to your accounting software?
You don't have to.
Download a statement from your bank's website, and open it in Argo Books.
There's no bank login, and nothing is connected to your account.
It reads every line, and fills in the supplier, the category and the amount.
You check it all, before anything is imported.
Free for Windows, Mac and Linux."""
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 60.0


def shot_list(words, total):
    def at(text, nth=1):
        matches = [w["at"] for w in words if w["text"].strip(".,?").lower() == text.lower()]
        return matches[nth - 1]

    download, login, reads, check, free = at("Download"), at("There's"), at("It", 2) - 0.1, at("You", 2) - 0.1, at("Free")
    # The hook ends on the click that opens the prompt, so the take runs straight on into the next line.
    opening = ("click_result", -(download - 0.1))
    return [
        {"at": 0.0, "take": "bank", "from": opening, "title": "Link your bank? No need.", "keys": [("start", 0.0, 1.0, 1650, 800)]},
        {"at": download - 0.1, "take": "bank", "from": ("prompt", 0.0), "play": 3.0, "title": "Download your statement",
         "keys": [("prompt", 0.0, 1.25, 1600, 930), ("prompt", 0.8, 1.25, 1600, 930), ("prompt", 4.9, 1.35, 1600, 930)]},
        {"at": login - 0.1, "take": "bank", "from": ("no_login", 0.0), "play": 2.0, "title": "No bank login",
         "keys": [("no_login", 0.0, 1.35, 1600, 930), ("no_login", 4.4, 1.44, 1600, 950)]},
        {"at": reads, "take": "bank", "from": ("click_choose", -0.8), "title": "It reads every line",
         "keys": [("click_choose", 0.0, 0.78, 1600, 930), ("rows", 0.4, 0.78, 1600, 930), ("rows", 2.8, 1.1, 1900, 830)]},
        {"at": check, "take": "bank", "from": ("rows", 3.0), "title": "You check it all",
         "keys": [("rows", 3.0, 1.1, 1900, 830), ("rows", 5.4, 0.78, 1600, 930)]},
        {"at": free - 0.1, "end": True},
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

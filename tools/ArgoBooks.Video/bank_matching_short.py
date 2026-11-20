"""Make the vertical Short "The bookkeeping step most people skip": October's bank statement checked against the books on
the Bank Matching page, which turns up a fuel stop that was never recorded. Narrated by the Azure voice, with big captions
timed to each word.

Usage: python bank_matching_short.py

It uses the `matching` take of bookkeeping_video.py in work/books/takes, so record that first. The finished video goes to
work/videos/.
"""
from pathlib import Path

import bookkeeping_video as bv
import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "bank_matching_short"
OUT_FILE = HERE / "work" / "videos" / "short_bank_matching.mp4"
LINES = [
    ("This check found an expense missing from my books.", 450),
    ("It's the bookkeeping step most people skip: checking your books against your bank.", 350),
    ("Once a month, download your statement from your bank's website.", 300),
    ("Argo Books matches each line to what you've already recorded.", 350),
    ("Two lines didn't match.", 300),
    ("One is a transfer to my own savings. That's not an expense, so I ignore it.", 400),
    ("The other is a fuel stop I never recorded.", 350),
    ("Without this check, I'd pay tax on money I already spent.", 450),
    ("And the calendar shows which months are done.", 450),
    ("The full step-by-step guide is linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 170.0


def shot_list(words, total, base):
    starts, n = [], 0
    for text, _ in LINES:
        starts.append(n)
        n += len(text.split())

    def line(k):
        return words[starts[k]]["at"]

    def word(k, text):
        last = starts[k + 1] if k + 1 < len(starts) else len(words)
        return next(w["at"] for w in words[starts[k]:last] if w["text"].strip(".,?:").lower() == text.lower())

    def take(k, start, title, keys, play=None):
        shot = {"at": line(k) - 0.1, "take": "matching", "from": start, "title": title, "keys": keys}
        if play:
            shot["play"] = play
        return shot

    # The transfer's ignore button is clicked on the word "ignore".
    ignore = ("click_ignore", -(word(5, "ignore") - line(5) + 0.1))
    missed = qs.facts_card(base, "$61.30 of fuel", "On the bank statement, October 17", [
        (0.3, "no", "Not in my books"),
        (word(7, "tax") - line(7) + 0.1, "no", "Counted as profit at tax time")], "")
    return [
        take(0, ("fuel", 0.3), "Missing from my books", [("fuel", 0.3, 1.26, 1550, 1250), ("fuel", 3.6, 1.29, 1550, 1260)], play=3.3),
        # The take stops just before the import button is clicked, and holds there while the camera reaches the button.
        take(1, ("page", 0.0), "The step most people skip",
             [("page", 0.0, 1.15, 1250, 900), ("page", 1.0, 1.15, 1250, 900), ("click_import", -0.4, 1.15, 2440, 900),
              ("click_import", 2.5, 1.2, 2440, 880)], play=2.65),
        take(2, ("click_import", -0.15), "Once a month",
             [("click_import", -0.15, 1.15, 2440, 900), ("prompt", 0.4, 1.4, 1600, 930), ("prompt", 3.9, 1.45, 1600, 930)]),
        take(3, ("imported", 1.5), "Matched for you",
             [("imported", 1.5, 1.4, 1600, 930), ("click_ok", 0.05, 1.4, 1600, 930), ("click_ok", 0.8, 1.35, 2270, 1290),
              ("click_ok", 3.0, 1.38, 2270, 1290)]),
        take(4, ("look_lines", -0.4), "2 lines didn't match", [("look_lines", -0.4, 1.2, 1585, 1222), ("look_lines", 1.8, 1.23, 1585, 1222)]),
        take(5, ignore, "A transfer to savings",
             [("transfer", -0.5, 1.2, 1585, 1150), ("click_ignore", -1.3, 1.2, 1585, 1150), ("click_ignore", -0.3, 1.2, 2480, 1150),
              ("click_ignore", 2.0, 1.2, 2480, 1150)]),
        take(6, ("fuel", -0.4), "Never recorded", [("fuel", -0.4, 1.1, 1585, 1222), ("fuel", 2.6, 1.2, 1585, 1222)]),
        {"at": line(7) - 0.1, "still": missed, "title": "Without this check"},
        take(8, ("calendar", -0.2), "Month by month", [("calendar", -0.2, 1.2, 1253, 1300), ("calendar", 3.2, 1.24, 1253, 1320)]),
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=bv.load_take, end_card=inv.end_card_saying("Full guide linked below"))


if __name__ == "__main__":
    main()

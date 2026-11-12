"""Make the vertical Short "Your bank balance is lying to you": an example balance with the sales tax owed, the unpaid bills
and the income tax to come taken off it one at a time, counting down to what is really the owner's, then the sales tax and
the bills in Argo Books. Narrated by the Azure voice, with big captions timed to each word.

Usage: python bank_balance_short.py

It uses the `salestax` and `bill` takes of qb_desktop_video.py in work/qbvideo/takes, so record those first. The amounts on
the card for sales tax and bills are the ones those takes show. The finished video goes to work/videos/.
"""
from pathlib import Path

from PIL import ImageDraw

import bookkeeping_video as bv
import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "bank_balance_short"
OUT_FILE = HERE / "work" / "videos" / "short_bank_balance.mp4"
LINES = [
    ("Your bank balance is lying to you.", 450),
    ("Say it shows 12,500 dollars.", 450),
    ("About 1,400 of that is sales tax, and it belongs to the government.", 400),
    ("About 2,100 is for bills you haven't paid yet.", 400),
    ("And you'll owe income tax on your profit, so put some aside. Say 2,500.", 450),
    ("What's really yours is about 6,500 dollars.", 500),
    ("Argo Books tracks the sales tax you owe,", 200),
    ("and the bills you still have to pay.", 450),
    ("The full bookkeeping guide is linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 45.0
VX, VY, VW, VH = short.VIEW
BALANCE = 12500.00
# What comes off the balance: the sales tax and the bills are the amounts the takes show.
TAKEN = [("Sales tax you owe", 1420.77), ("Unpaid bills", 2079.77), ("Income tax", 2500.00)]
COUNT = 0.7


def money(value):
    return f"${value:,.2f}"


def balance_card(base, times, final):
    """The balance, with each amount in TAKEN coming off it at its time, the figure counting down as it goes, and the label
    turning to "Really yours" at `final`. Times are seconds from the start of the Short."""
    def shown(t):
        value = BALANCE
        for (_, amount), at in zip(TAKEN, times):
            value -= amount * r.ease((t - at) / COUNT)
        return value

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 90, VY + 880
        qs.panel(draw, top, bottom)
        done = t >= final
        colour = qv.GREEN if done else mv.INK
        draw.text((130, top + 100), "REALLY YOURS" if done else "BANK BALANCE", font=mv.font("Bold", 38),
                  fill=(qv.GREEN if done else (124, 135, 158)) + (255,), anchor="ls")
        draw.text((126, top + 235), money(shown(t)), font=mv.font("Bold", 120), fill=colour + (255,), anchor="ls")
        draw.line((130, top + 300, 950, top + 300), fill=(238, 241, 246, 255), width=3)
        for n, ((label, amount), at) in enumerate(zip(TAKEN, times)):
            y = top + 400 + n * 150

            def row(d, label=label, amount=amount, y=y):
                d.text((130, y), label, font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="lm")
                d.text((950, y), "- " + money(amount), font=mv.font("Bold", 50), fill=r.RED + (255,), anchor="rm")
            qs.appear(out, t, at, row)
        draw.text((540, bottom + 60), "An example business", font=mv.font("Regular", 30), fill=mv.GREY + (255,), anchor="mm")
        return out
    return frame


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

    card = balance_card(base, [word(2, "1,400"), word(3, "2,100"), word(4, "2,500")], word(5, "really"))

    def still(at, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, "Your bank balance"),
        still(line(2) - 0.1, "Sales tax isn't yours"),
        still(line(3) - 0.1, "Bills you haven't paid"),
        still(line(4) - 0.1, "Income tax is coming"),
        still(line(5) - 0.1, "What's really yours"),
        {"at": line(6) - 0.1, "take": "salestax", "from": ("owed", -0.3), "title": "Sales tax you owe",
         "keys": [("owed", -0.3, 2.2, 2162, 400), ("owed", 2.5, 2.3, 2162, 390)]},
        {"at": line(7) - 0.1, "take": "bill", "from": ("bills_card", -0.3), "play": 3.0, "title": "Unpaid bills",
         "keys": [("bills_card", -0.3, 1.45, 2200, 1150), ("bills_card", 3.0, 1.55, 2230, 1220)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=bv.load_take, end_card=inv.end_card_saying("Full guide linked below"))


if __name__ == "__main__":
    main()

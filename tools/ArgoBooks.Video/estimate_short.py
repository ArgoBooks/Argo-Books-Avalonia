"""Make the vertical Short "Free estimate generator for contractors": a deck job's estimate typed into argorobots.com's free
estimate generator, with a line for the client to sign, downloaded as a PDF. Narrated by the Azure voice, with big captions
timed to each word.

Usage: python estimate_short.py

Record the take first with `node record_generator.js estimate work/estimate/takes`. The finished video goes to work/videos/.
"""
from pathlib import Path

import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "estimate"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_free_estimate_generator.mp4"
# Each line, and the pause after it, long enough for the typing its shot shows.
LINES = [
    ("Contractors, here's a free estimate generator. No sign up, no app.", 450),
    ("Type your company name.", 900),
    ("Then your client.", 1300),
    ("Describe the job, and your price.", 1300),
    ("Put the materials on a second line.", 600),
    ("It works out the total for you.", 400),
    ("Add a line for your client to sign.", 400),
    ("Pick a template.", 1000),
    ("And download it as a PDF.", 250),
    ("Your estimate never leaves your browser.", 500),
    ("It's free, from Argo Books.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 90.0


def shot_list(words, total, base, take):
    at, shot = inv.shot_maker(words, "estimate")
    fit = inv.fit
    whole = inv.whole_page(take)
    templates = shot("Pick", "click_modern", -0.35, "5 templates", [("click_modern", -0.35, whole)])
    # The download follows straight on from the templates, so the hand carries on to the button without a jump.
    carry_on = ("click_modern", -0.35 + at("And", 2) - at("Pick"))
    # ESTIMATE starts 22 points right of the tagline, so the tagline is framed close enough to leave the heading out.
    opening = fit(take, ["hero_title", "tagline"], pad=20)
    return [
        {**shot("Contractors", "top", 0.0, "Free estimate generator", [("top", 0.0, opening)]), "at": 0.0},
        shot("Type", "click_business", -0.45, "No sign up, just type",
             [("click_business", -0.45, fit(take, ["logo", "business"], pad=50)), ("business_done", 0.3, fit(take, ["logo", "business"], pad=50, closer=1.05))]),
        shot("Then", "click_billto", -0.45, "Add your client",
             [("click_billto", -0.45, fit(take, ["from", "billto"], below="business")),
              ("billto_done", 0.3, fit(take, ["from", "billto"], closer=1.05, below="business"))]),
        shot("Describe", "click_desc", -0.35, "The job and your price",
             [("click_desc", -0.35, fit(take, ["desc"])), ("click_rate", -0.6, fit(take, ["desc"])),
              ("click_rate", 0.15, fit(take, ["rate", "amount", "total1"], most=1.35))]),
        shot("Put", "click_add", -0.4, "Materials on their own line",
             [("click_add", -0.4, fit(take, ["add", "desc2"])), ("click_rate2", -0.6, fit(take, ["add", "desc2"], closer=1.04))]),
        shot("It", "click_rate2", -0.15, "Total worked out for you",
             [("click_rate2", -0.15, fit(take, ["rate2", "totals"], pad=50)), ("look_total", 0.0, fit(take, ["totals"]))]),
        shot("Add", "click_signature", -0.6, "A line for them to sign",
             [("click_signature", -0.6, fit(take, ["sign_button"], pad=300, most=1.3)), ("signature_shown", 0.2, fit(take, ["signature"], pad=40))]),
        templates,
        shot("And", *carry_on, "Download PDF or Word", [(*carry_on, whole), ("click_pdf", -0.1, fit(take, ["pdf", "share"], pad=40))], nth=2),
        {"at": at("PDF") - 0.05, "still": inv.pdf_still(base, TAKES / "estimate.pdf", "estimate-1.pdf"), "title": "Ready to send"},
        {"at": at("It's") - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    take = inv.prepare(TAKES, "estimate")
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.caption_chunks(words, longest_pause=0.4)
    pc.finish(shot_list(words, total, short.background(), take), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=inv.take_loader(TAKES), end_card=inv.end_card_saying("Free estimate generator"))


if __name__ == "__main__":
    main()

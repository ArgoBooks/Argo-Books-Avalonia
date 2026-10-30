# Marketing Video Tool

Makes the marketing videos from the real app. The app runs without a window, a script clicks, drags and types through it while every frame is saved, and the take is then cut into a finished video with a voiceover, captions, interface sounds and music. Each video comes out twice: 16:9 for YouTube and the website, and 9:16 for Shorts, Reels and TikTok.

The first video shows receipt scanning: a receipt photo dropped into the scanner, the result reviewed, and the expense saved with its receipt attached.

## Setup

- .NET 10 SDK and Python 3 with `numpy` and `Pillow`
- ffmpeg: `winget install Gyan.FFmpeg`
- A `.env` file beside this README (git ignores it) with the Azure Speech key for the voiceover:

```
AZURE_SPEECH_KEY=...
AZURE_SPEECH_REGION=eastus
```

## Commands

```
python make_video.py              records the take if there is none yet, then renders both videos
python make_video.py record       records the take again
python make_video.py render       renders both videos from the take already recorded
python make_video.py render wide  renders one video: wide or tall
```

The finished videos and their caption files (`.srt`, for uploading alongside) are in `work/videos/`. Everything the tool makes is kept in `work/`, which git ignores.

## What a recording touches

- It opens the sample company and shows it under the made-up name Cedar Ridge IT, with the sample company's banners hidden. Nothing is saved to it.
- Settings come from a fresh folder inside the build output, so your own settings, recent companies and window size are left alone.
- No usage statistics are sent, and no update check runs.
- The receipt is drawn by `make_receipt.py` and dated the day before the recording. The first scan of a receipt is a real one, made through the production server, and uses one of this computer's free monthly scans. Its result is kept in `work/scan-cache/`, so retakes replay it and cost nothing.

## Changing the video

- The script of the take is in `Takes.cs`. It leaves named marks as it goes (`click_scan`, `scan_done`, `saved`, ...).
- The edit is in `make_video.py`: `LINES` is the voiceover, and `EDIT` is the cut list, built from those marks, with the camera's zoom for each part and when each line starts. Voice lines are kept in `work/voice/`, so only a new or changed line goes to Azure.
- Each render prints the voice, music and sound levels and the finished loudness, and warns when a line runs into the next.

## The free accounting software roundup (YouTube video 13)

`roundup.py` makes the 6-minute comparison of Wave, Zoho Books, Argo Books and ZipBooks from Evan's own recording in the video's folder. Its takes, page captures and cards go to `work/roundup/`, and the video to `work/videos/`.

```
python roundup.py voice     cuts and cleans the recording only, and prints how it measures
python roundup.py           renders the video
python roundup.py shots 7 9 renders just those shots again, for checking
```

What it needs first, each made once:

- The takes: `welcome`, `tour` and `bank`, recorded with `dotnet bin/Release/net10.0/ArgoBooks.Video.dll <take> work/roundup/takes work/receipt.jpg work/scan-cache work/roundup/statement.csv`. The statement comes from `make_statement.py`. The receipt part reuses the receipt video's take.
- The pricing pages: `node capture_pages.js work/roundup/pages`, after `npm install` in this folder. It uses the Microsoft Edge already on the computer. Capture them again before a re-render, since prices change.
- The cards: `node render_cards.js <free-plan-limits.html> work/roundup/cards work/roundup/logos`, drawn from the comparison graphic's own HTML, with each product's icon from `work/roundup/logos/` (`wave.png`, `zoho.png`, `argo.png` and `zipbooks.svg`, taken from each product's own website).

The cut follows Azure's transcript of the recording. Whole sentences listed in `RETAKES` are dropped. Inside a sentence, the recording is split at its pauses (it was gated, so they are near silence), each part is transcribed on its own, and a part that the next one starts over is dropped as a false start; the cut prints every part it drops and every sentence it keeps in more than one part. Each shot, highlight and label then starts on the word it belongs to. The shot list is `shot_list()`.

## Credits

The music and font need crediting. See `assets/CREDITS.txt`. The music's credit goes in the description of every video that uses it.

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

## The QuickBooks migration (YouTube video 14)

`quickbooks_video.py` makes "How to Export Your QuickBooks Data and Switch to Free Software": a new company, the real QuickBooks import, and the app's Income Statement checked against the reports it read. Its files go to `work/qb/`, and the video to `work/videos/`.

```
python make_qb_exports.py work/qb/exports     writes the example reports, and prints their totals
python quickbooks_video.py voice              fetches and measures the voice
python quickbooks_video.py frame 12 95        saves single frames to work/qb/check/
python quickbooks_video.py                    renders the video
```

The take is recorded with `dotnet bin/Release/net10.0/ArgoBooks.Video.dll quickbooks work/qb/takes work/receipt.jpg work/scan-cache work/qb/exports`. It imports five files through the production server, so each recording uses five of this computer's monthly spreadsheet imports. It prints what the company holds after each file, which must match the totals `make_qb_exports.py` printed. The numbers on the cards are typed into `shot_list()`, so a change to the example reports means recording again and updating them.

To test real exports, put them in a folder and run `dotnet bin/Release/net10.0/ArgoBooks.Video.dll qbcheck work/qb/check work/receipt.jpg work/scan-cache <folder>`. It imports every file through the QuickBooks screen and prints what the company holds after each one. The clips of QuickBooks itself are screen recordings from Intuit's public test drive, kept in `work/qb/clips/`.

## Free accounting software for Mac (YouTube video 15)

`mac_video.py` makes "Top 5 Free Accounting Software for Mac": Wave, Zoho Books, GnuCash, Manager and Argo Books, the catch in each, and Argo Books as it looks on a Mac. Its files go to `work/mac/`, and the video to `work/videos/`.

```
python mac_video.py voice              fetches and measures the voice
python mac_video.py frame 12 95        saves single frames to work/mac/check/
python mac_video.py                    renders the video
```

On a Mac the app hides its own title bar and window buttons, leaves them to macOS, and shows Cmd where Windows shows Ctrl. The Mac takes are recorded from a copy of the released app with those macOS paths switched on, so the video shows what a Mac user downloads today:

1. `git worktree add --detach <folder> origin/main`, copy this tool into the copy's `tools/` folder, and add `Avalonia.Headless` and `Avalonia.Skia` to its `Directory.Packages.props` at the copy's Avalonia version.
2. In that copy only, make the `OperatingSystem.IsMacOS()` checks in `LanguageService.LocalizeShortcutModifier` and `MainWindow.ConfigureMacOsChrome` also pass when the environment variable `ARGO_VIDEO_MAC` is `1`. Never commit that change.
3. Build the tool there and record `welcome`, `tour`, `receipt` and `bank` with `ARGO_VIDEO_MAC=1` into `work/mac/takes`. The variable also makes the window 28 points shorter, the height of the title bar `mac_window.py` draws above each frame.

The pages are captured with `node capture_pages.js work/mac/pages wave_us zoho_mac zoho_us gnucash_home gnucash_download gnucash_features manager_download manager_home manager_cloud argo_downloads_mac argo_pricing`. Each is opened as a Mac's Safari, so download pages offer the Mac version. Intuit's pages refuse a headless browser, so the QuickBooks facts are on cards that name their source. The few lines the capture finds in the wrong place are placed by hand in `BOXES`, which needs checking after a fresh capture.

## Best QuickBooks Desktop alternatives (YouTube video 16)

`qb_desktop_video.py` makes "Best QuickBooks Desktop Alternatives (2026)": Sage 50, AccountEdge Pro, GnuCash, Manager and Argo Books, what each costs, whether your QuickBooks data comes with you, and the catch in each. Its files go to `work/qbvideo/`, and the video to `work/videos/`.

```
python qb_desktop_video.py voice              fetches and measures the voice
python qb_desktop_video.py frame 12 95        saves single frames to work/qbvideo/check/
python qb_desktop_video.py                    renders the video
```

The takes are recorded from a copy of the released app, the same way as the Mac takes but without the Mac changes: `git worktree add --detach <folder> origin/main`, copy this tool's `Takes.cs` over the copy's, build it there, and record `welcome`, `tour`, `bill`, `salestax` and `quickbooks` into `work/qbvideo/takes`. The `quickbooks` take needs `work/qb/exports` as its last argument and uses five of this computer's monthly spreadsheet imports. The `bill` take turns one of the sample company's expenses into a bill, then opens the Accounts Payable Aging report.

The pages are captured with `node capture_pages.js work/qbvideo/pages accountedge_pricing accountedge_qb gnucash_download_win manager_download_win manager_cloud argo_downloads argo_pricing`. Sage's site refuses a headless browser and Intuit's refuses the connection, so their facts are on cards that name their source. GnuCash's features page is the one captured for video 15. The QuickBooks Online clip is video 14's, from `work/qb/clips`.

## How to do your own bookkeeping (YouTube video 17)

`bookkeeping_video.py` makes "How to Do Your Own Bookkeeping": a small business's routine in seven steps, from the first receipt to the reports at the end of the month, with the bank statement checked on the Bank Matching page. Its files go to `work/books/`, and the video to `work/videos/`.

```
python bookkeeping_video.py voice              fetches and measures the voice
python bookkeeping_video.py frame 12 95        saves single frames to work/books/check/
python bookkeeping_video.py                    renders the video
```

The takes are recorded from a copy of the released app, as for video 16. `receipt` goes into `work/books/takes`. `payment`, `matching` and `reports` go there too, and each opens a copy of the company the `quickbooks` take saved, given as the last argument (`work/qbvideo/takes/Cedar Ridge IT.argo`). It is a real company rather than the sample, so a payment can be recorded and the reports carry its own name.

- `payment` records Priya Nair's overdue invoice as paid.
- `matching` writes October's statement from the company's own records, as a bank's website would give it, and gives the company January to September as already checked. It then imports October's statement on camera, ignores a transfer to savings, points at a fuel stop that was never recorded, and accepts a suggested match. Each recording uses one of this computer's monthly bank imports.
- `reports` shows the Income Statement for the year so far, then the Accounts Receivable Aging report.

`welcome`, `tour`, `bill` and `salestax` are video 16's.

## The Shorts

- `catches_short.py` makes "the catch in each" from the recap of the roundup, in Evan's own voice. Run `roundup.py` first.
- `pc_short.py` makes "Free accounting software for PC", narrated by an ElevenLabs voice. The key is `ELEVENLABS_API_KEY` in `.env.local` beside this README, which git ignores. Speech made on the ElevenLabs free plan cannot be used commercially, so run `python pc_short.py revoice` on a paid plan before publishing.
- `bank_short.py` makes "Import a bank statement without linking your bank" from the `bank` take, narrated by the Azure voice.
- `spreadsheet_short.py` makes "Still keeping your books in a spreadsheet?" from the `tour` take, with the same voice.
- `mac_short.py` makes "Free accounting software for Mac" from the Mac takes and pages, with the same voice. Make those for `mac_video.py` first.
- `invoice_short.py` makes "Free invoice generator" from argorobots.com's free invoice generator, and `estimate_short.py` makes "Free estimate generator for contractors" from the estimate generator, both with the same voice. Record each take first with `node record_generator.js invoice work/invoice/takes` or `node record_generator.js estimate work/estimate/takes`: it opens the live page in Edge, types the document into it and downloads the PDF, saving every change with the hand's path, and the Short turns that into a take like the app's. The recording blocks the page's own statistics, so it does not count as a visit or a download.
- `qb_desktop_short.py` makes "QuickBooks Desktop alternatives" from the pages and takes of `qb_desktop_video.py`, with the same voice. Make those first. Sage's facts and the import's totals are drawn as cards, since Sage's site refuses a headless browser and the app's Income Statement is too small to read in a Short.
- `bank_matching_short.py` makes "The bookkeeping step most people skip" from the `matching` take of `bookkeeping_video.py`, with the same voice. Record that take first.
- `render_thumbnail.js` saves a thumbnail's HTML as a PNG: `node render_thumbnail.js thumbnail.html 1280 720`.

## Credits

The music and font need crediting. See `assets/CREDITS.txt`. The music's credit goes in the description of every video that uses it.

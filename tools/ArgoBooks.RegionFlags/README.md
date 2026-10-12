# Region flag downloader

Downloads the small flag shown beside each province, state and county in the address pickers, and saves them to `ArgoBooks/Assets/RegionFlags`.

It does not touch the country flags in `ArgoBooks/Assets/CountryFlags`. Those were added by hand and nothing regenerates them.

## Running it

```powershell
dotnet run --project C:\Users\evand\Desktop\Argo-Books-Avalonia\tools\ArgoBooks.RegionFlags
```

It needs an internet connection and takes a few minutes, because it pauses briefly between downloads.

When to run it, and what to check afterwards, is in [Publishing](../../docs/Publishing.md#once-a-year-province-and-state-flags): once a year, and whenever a region is added to or removed from `ArgoBooks/Data/Regions.cs`.

## What it does

1. Reads every region from `Regions.FlagCodes`. Each is named by its country code and its ISO 3166-2 code, such as `CA-ON`, or by its Wikidata id where it has no ISO code, such as `GB-Q23298`.
2. Asks Wikidata, in one query, for each region's flag, coat of arms and logo.
3. Picks one image per region and downloads it from Wikimedia Commons.
4. Shrinks it to fit a 35x24 box, the size of the country flags, and saves it as a PNG named after the region.
5. Deletes any PNG in the folder that no longer belongs to a region.

It then prints how many images were saved, which regions have no image at all, and which downloads failed. A region whose download failed keeps the image it already had.

## How an image is chosen

A region's flag is used when it has one, then its coat of arms, then its logo. Three things change that order:

- **Irish and Northern Irish counties take their coat of arms first.** The flags Wikidata lists for them are sports colours, and some are attached to the wrong county.
- **Some files are never used**: a gonfalone, which is a tall ceremonial banner, a file with "proposed" in its name, and the French national flag standing in for a region.
- **The `overrides` list in `Program.cs` names the file outright** for a region where Wikidata lists several images or lacks the right one.

A region with no image shows its country's flag in the picker.

## Reading the result

Expect every file to show as changed in Git, even when no flag has been redesigned. Wikimedia only serves images at certain sizes, so the tool does not always receive the size it asked for: a request for 240 pixels was answered with 250 in October 2026. Shrinking from a slightly different size moves every pixel without changing how the flag looks. So the diff cannot be read by which files changed. Look at the images instead.

What to look for:

- **A region that now uses a different picture.** Wikidata is edited by the public, so this is usually a real redesign but can be a proposed design, or a flag swapped for a coat of arms. Add the region to `overrides` to pin the right file.
- **A failed download for a region in `overrides`.** The file it names has been renamed or removed on Wikimedia Commons, and the entry needs the new name.

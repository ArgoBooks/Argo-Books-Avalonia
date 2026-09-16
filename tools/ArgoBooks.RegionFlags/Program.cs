using System.Text.Json;
using System.Text.RegularExpressions;
using ArgoBooks.Data;
using SkiaSharp;

// Region Flag Downloader
// Usage: dotnet run --project tools/ArgoBooks.RegionFlags
//
// Downloads an image for each province, state and county in Regions, looked up on Wikidata by its ISO
// 3166-2 code, or by its Wikidata id where it has no code:
// its flag (P41), or its coat of arms (P94) where it has none, or its logo (P154) where it has neither.
// The image comes from Wikimedia Commons, shrunk to fit the same 35x24 box as the country flags. See
// docs/Publishing.md for when to run it.

const int FlagWidth = 35;
const int FlagHeight = 24;

var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ArgoBooks", "Assets", "RegionFlags"));

// Where a region lists more than one image, or Wikidata lacks the one to use. The Friuli provinces were
// abolished in 2017, but addresses still use their codes, and only their former entries have images.
var overrides = new Dictionary<string, string>
{
    ["AU-ACT"] = "Flag of the Australian Capital Territory.svg",
    ["DE-BY"] = "Flag of Bavaria (lozengy).svg",
    ["GB-Q21693433"] = "Arms of Bristol City Council.svg",
    ["GB-Q48790202"] = "Flag of Cornwall.svg",
    ["IT-CH"] = "Flag of the province of Chieti.svg",
    ["IT-EN"] = "Provincia di Enna-Stemma.svg",
    ["IT-GO"] = "Flag of the Province of Gorizia.svg",
    ["IT-PN"] = "Provincia di Pordenone-Bandiera.svg",
    ["IT-RG"] = "Provincia di Ragusa-Stemma.svg",
    ["IT-TS"] = "Provincia di Trieste-Bandiera.svg",
    ["IT-UD"] = "Provincia di Udine-Stemma.svg",
    ["IT-TV"] = "Flag of the Province of Treviso (2015 design).svg",
    ["IT-VE"] = "Flag of the Province of Venice.svg",
    ["NZ-AUK"] = "Auckland Council Logo.png",
    ["NZ-GIS"] = "Gisborne banner of arms.svg",
    ["NZ-NSN"] = "Nelson City Council logo.svg",
    ["NZ-STL"] = "Southland Region coat of arms (escutcheon).svg",
    ["NZ-WKO"] = "Waikato Regional Council logo.svg",
};

// Counties in Ireland and Northern Ireland have no official flags: the ones listed are GAA sports colours,
// some linked to the wrong county, so their coats of arms come first.
string[] armsFirst = ["IE-", "GB-Q189592", "GB-Q192761", "GB-Q190684", "GB-Q190678", "GB-Q192208", "GB-Q192229"];

// A gonfalone is a tall ceremonial banner, and the national flag or a proposal is not the region's own.
var rejected = new Regex(@"gonfalone|proposed|^Flag of France\.svg$", RegexOptions.IgnoreCase);

var wikidataKey = new Regex(@"^[A-Z]{2}-Q\d+$");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ArgoBooksRegionFlags/1.0 (https://argorobots.com)");

var codes = Regions.FlagCodes.ToList();
var images = await WikidataImagesAsync(codes);
Directory.CreateDirectory(outputDir);

var saved = new HashSet<string>();
var missing = new List<string>();
var failed = new Dictionary<string, string>();

foreach (var code in codes)
{
    var file = Choose(code);
    if (file == null)
    {
        missing.Add(code);
        continue;
    }

    try
    {
        var url = "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(file.Replace(' ', '_')) + "?width=240";
        var png = ShrinkToFlagBox(await FetchAsync(url));
        await File.WriteAllBytesAsync(Path.Combine(outputDir, code + ".png"), png);
        saved.Add(code);
    }
    catch (Exception ex)
    {
        failed[code] = $"{file}: {ex.Message}";
    }

    await Task.Delay(300);
}

foreach (var old in Directory.GetFiles(outputDir, "*.png"))
{
    var code = Path.GetFileNameWithoutExtension(old);
    if (!saved.Contains(code) && !failed.ContainsKey(code))
        File.Delete(old);
}

Console.WriteLine($"Saved {saved.Count} of {codes.Count} images to {outputDir}");
if (missing.Count > 0)
    Console.WriteLine("No image: " + string.Join(", ", missing));

if (failed.Count > 0)
{
    Console.WriteLine("Failed, previous image kept:");
    foreach (var (code, reason) in failed)
        Console.WriteLine($"  {code} ({reason})");
    return 1;
}

return 0;

string? Choose(string code)
{
    if (overrides.TryGetValue(code, out var chosen))
        return chosen;
    if (!images.TryGetValue(code, out var found))
        return null;

    string[] order = armsFirst.Any(code.StartsWith) ? ["arms", "flag", "logo"] : ["flag", "arms", "logo"];
    return order
        .Select(kind => found
            .Where(image => image.Kind == kind && !rejected.IsMatch(image.File))
            .Select(image => image.File)
            .OrderBy(f => f.Contains("variant", StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault())
        .FirstOrDefault(file => file != null);
}

async Task<Dictionary<string, List<(string Kind, string File)>>> WikidataImagesAsync(List<string> regionCodes)
{
    var byWikidataId = regionCodes.Where(c => wikidataKey.IsMatch(c)).ToList();
    var isoValues = string.Join(" ", regionCodes.Except(byWikidataId).Select(c => $"\"{c}\""));
    var itemValues = string.Join(" ", byWikidataId.Select(c => $"(wd:{c[3..]} \"{c}\")"));
    var query = "SELECT ?code ?kind ?file WHERE { " +
                $"{{ VALUES ?code {{ {isoValues} }} ?item wdt:P300 ?code. }} UNION {{ VALUES (?item ?code) {{ {itemValues} }} }} " +
                "FILTER NOT EXISTS { ?item wdt:P576 ?dissolved } " +
                "{ ?item wdt:P41 ?file BIND(\"flag\" AS ?kind) } UNION " +
                "{ ?item wdt:P94 ?file BIND(\"arms\" AS ?kind) } UNION " +
                "{ ?item wdt:P154 ?file BIND(\"logo\" AS ?kind) } }";
    var url = "https://query.wikidata.org/sparql?query=" + Uri.EscapeDataString(query);

    using var json = JsonDocument.Parse(await FetchAsync(url, "application/sparql-results+json"));
    var result = new Dictionary<string, List<(string Kind, string File)>>();
    foreach (var row in json.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
    {
        var code = row.GetProperty("code").GetProperty("value").GetString()!;
        var kind = row.GetProperty("kind").GetProperty("value").GetString()!;
        var fileUrl = row.GetProperty("file").GetProperty("value").GetString()!;
        var file = Uri.UnescapeDataString(fileUrl[(fileUrl.LastIndexOf('/') + 1)..]);

        if (!result.TryGetValue(code, out var found))
            result[code] = found = [];
        found.Add((kind, file));
    }
    return result;
}

async Task<byte[]> FetchAsync(string url, string? accept = null)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept != null)
                request.Headers.Accept.ParseAdd(accept);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch when (attempt < 3)
        {
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
        }
    }
}

static byte[] ShrinkToFlagBox(byte[] image)
{
    var bitmap = SKBitmap.Decode(image) ?? throw new InvalidDataException("Not an image");
    try
    {
        var scale = Math.Min(1.0, Math.Min((double)FlagWidth / bitmap.Width, (double)FlagHeight / bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        // Halve first: a single cubic pass from 240px down to 35px skips most source pixels and shimmers.
        while (bitmap.Width >= width * 2 && bitmap.Height >= height * 2)
        {
            var half = bitmap.Resize(bitmap.Info.WithSize(bitmap.Width / 2, bitmap.Height / 2), new SKSamplingOptions(SKFilterMode.Linear));
            bitmap.Dispose();
            bitmap = half;
        }

        using var resized = bitmap.Resize(bitmap.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var data = resized.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    finally
    {
        bitmap.Dispose();
    }
}

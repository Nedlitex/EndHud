using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndHud.Vision.Operators;

/// <summary>
/// Downloads the operator roster from the community wiki (endfield.wiki.gg): every operator's
/// icon and Chinese name. The icons are the game's own art, so they stay on this machine
/// (in the library folder) and are never bundled with EndHud.
/// </summary>
public static partial class WikiOperatorSource
{
    private const string Api = "https://endfield.wiki.gg/api.php";
    private const string IconSuffix = "_icon.png";
    private const int TitlesPerRequest = 50; // MediaWiki API limit

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Wikis ask API clients to identify themselves.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EndHud/0.1 (personal game HUD; reads operator icons)");
        return http;
    }

    /// <summary>Fetch the roster into <paramref name="folder"/> (icons + operators.json), replacing the old roster.</summary>
    public static async Task<IReadOnlyList<OperatorInfo>> DownloadAsync(string folder, HttpClient http,
        CancellationToken ct = default)
    {
        // 1. The "Operator" page's navbox shows one "<Name>_icon.png" per operator (plus facility icons, skipped).
        var parse = await GetJsonAsync(http, $"{Api}?action=parse&page=Operator&prop=images&format=json&formatversion=2", ct);
        var iconFiles = parse["parse"]!["images"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .Where(f => f.EndsWith(IconSuffix, StringComparison.Ordinal) && !f.Contains("_facility", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        if (iconFiles.Count == 0) throw new InvalidDataException("The wiki's Operator page listed no operator icons.");

        var names = iconFiles.Select(f => f[..^IconSuffix.Length].Replace('_', ' ')).ToList();

        // 2. Chinese names: the "cnname" field of each operator page's infobox.
        var chinese = new Dictionary<string, string>();
        foreach (var batch in names.Chunk(TitlesPerRequest))
        {
            var json = await GetJsonAsync(http,
                $"{Api}?action=query&prop=revisions&rvprop=content&rvslots=main&redirects=1&format=json&formatversion=2&titles="
                + Uri.EscapeDataString(string.Join('|', batch)), ct);
            var redirects = RedirectMap(json);
            foreach (var page in json["query"]!["pages"]!.AsArray())
            {
                string title = page!["title"]!.GetValue<string>();
                string? content = page["revisions"]?[0]?["slots"]?["main"]?["content"]?.GetValue<string>();
                if (content is null) continue;
                var m = ChineseNameField().Match(content);
                if (!m.Success) continue;
                string requested = redirects.GetValueOrDefault(title, title);
                chinese[requested] = m.Groups[1].Value.Trim();
            }
        }

        // 3. Icon download URLs.
        var urls = new Dictionary<string, string>();
        foreach (var batch in iconFiles.Chunk(TitlesPerRequest))
        {
            var json = await GetJsonAsync(http,
                $"{Api}?action=query&prop=imageinfo&iiprop=url&format=json&formatversion=2&titles="
                + Uri.EscapeDataString(string.Join('|', batch.Select(f => "File:" + f))), ct);
            foreach (var page in json["query"]!["pages"]!.AsArray())
            {
                string? url = page!["imageinfo"]?[0]?["url"]?.GetValue<string>();
                if (url is null) continue;
                string file = page["title"]!.GetValue<string>()["File:".Length..].Replace(' ', '_');
                urls[file] = url;
            }
        }

        string iconFolder = Path.Combine(folder, OperatorLibrary.IconFolderName);
        Directory.CreateDirectory(iconFolder);
        var roster = new List<OperatorInfo>();
        for (int i = 0; i < iconFiles.Count; i++)
        {
            if (!urls.TryGetValue(iconFiles[i], out string? url)) continue;
            string id = names[i].Replace(' ', '_');
            string iconFile = id + ".png";
            byte[] bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(Path.Combine(iconFolder, iconFile), bytes, ct);
            roster.Add(new OperatorInfo(id, names[i], chinese.GetValueOrDefault(names[i], names[i]), iconFile));
        }

        OperatorLibrary.SaveManifest(folder, roster);
        return roster;
    }

    private static Dictionary<string, string> RedirectMap(JsonNode json)
    {
        var map = new Dictionary<string, string>();
        if (json["query"]?["redirects"] is JsonArray redirects)
            foreach (var r in redirects)
                map[r!["to"]!.GetValue<string>()] = r["from"]!.GetValue<string>();
        return map;
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct)
               ?? throw new InvalidDataException($"Empty response from {url}");
    }

    [GeneratedRegex(@"^\|\s*cnname\s*=\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ChineseNameField();
}

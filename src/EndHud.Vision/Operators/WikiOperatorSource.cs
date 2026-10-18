using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndHud.Vision.Operators;

/// <param name="Operators">Operators in the downloaded roster.</param>
/// <param name="Downloaded">Image files fetched from the wiki.</param>
/// <param name="Reused">Image files copied from the previous copy, unchanged on the wiki.</param>
/// <param name="Failed">Image files that couldn't be fetched or weren't valid images. Their operator
/// is still listed if the previous copy had the file (kept as it was) or it was only a skill icon.</param>
public sealed record WikiDownload(int Operators, int Downloaded, int Reused, int Failed);

/// <summary>
/// Downloads the operator roster from the community wiki (endfield.wiki.gg): every operator's
/// avatar, skill icons and Chinese name. The images are the game's own art, so they're fetched
/// onto the user's machine and never bundled with EndHud.
/// <para>
/// New operators need nothing special: the roster is whatever the wiki's Operator page lists.
/// Requests follow <see cref="WikiPoliteness"/> (one at a time, paced, backing off when the wiki is
/// busy); one image that can't be fetched, or isn't a valid image, doesn't sink the whole download.
/// </para>
/// </summary>
public static partial class WikiOperatorSource
{
    private const string Api = "https://endfield.wiki.gg/api.php";

    private const string IconSuffix = "_icon.png";
    private const int TitlesPerRequest = 50; // MediaWiki API limit

    /// <summary>Where EndHud lives, for the User-Agent (wikis ask API clients to say who they are and how to reach them).</summary>
    public const string ProjectUrl = "https://github.com/Nedlitex/EndHud";

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        string version = typeof(WikiOperatorSource).Assembly.GetName().Version?.ToString(3) ?? "0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"EndHud/{version} (+{ProjectUrl}; operator icons for a game HUD)");
        return http;
    }

    /// <summary>
    /// Fetch the roster into <paramref name="folder"/> (avatars, skill icons, operators.json). Files
    /// that <paramref name="previous"/> (an earlier download) already has, unchanged on the wiki,
    /// are copied from it instead of downloaded; if a file can't be fetched, the previous copy's
    /// version is kept. Use <see cref="OperatorReference.UpdateAsync"/> to update a library in place.
    /// </summary>
    public static async Task<WikiDownload> DownloadAsync(string folder, HttpClient http, string? previous = null,
        WikiPoliteness? politeness = null, CancellationToken ct = default)
    {
        var wiki = new WikiClient(http, politeness ?? WikiPoliteness.Default);
        // 1. The "Operator" page's navbox shows one "<Name>_icon.png" per operator (plus facility icons, skipped).
        var parse = await wiki.GetJsonAsync($"{Api}?action=parse&page=Operator&prop=images&format=json&formatversion=2", ct);
        var iconFiles = parse["parse"]!["images"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .Where(f => f.EndsWith(IconSuffix, StringComparison.Ordinal) && !f.Contains("_facility", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        if (iconFiles.Count == 0) throw new InvalidDataException("The wiki's Operator page listed no operator icons.");

        var names = iconFiles.Select(f => f[..^IconSuffix.Length].Replace('_', ' ')).ToList();

        // 2. Chinese names, elements and hobbies: the "cnname", "element", "hb1" and "hb2" fields of
        //    each operator page's infobox ("???" is the wiki's placeholder for one not known yet).
        var chinese = new Dictionary<string, string>();
        var elements = new Dictionary<string, string>();
        var hobbies = new Dictionary<string, List<string>>();
        foreach (var batch in names.Chunk(TitlesPerRequest))
        {
            var json = await wiki.GetJsonAsync(
                $"{Api}?action=query&prop=revisions&rvprop=content&rvslots=main&redirects=1&format=json&formatversion=2&titles="
                + Uri.EscapeDataString(string.Join('|', batch)), ct);
            var redirects = RedirectMap(json);
            foreach (var page in json["query"]!["pages"]!.AsArray())
            {
                string title = page!["title"]!.GetValue<string>();
                string? content = page["revisions"]?[0]?["slots"]?["main"]?["content"]?.GetValue<string>();
                if (content is null) continue;
                string requested = redirects.GetValueOrDefault(title, title);
                if (InfoboxField(content, "cnname") is { } zh) chinese[requested] = zh;
                if (InfoboxField(content, "element") is { } element) elements[requested] = element;
                hobbies[requested] = [.. new[] { "hb1", "hb2" }.Select(f => InfoboxField(content, f)).OfType<string>()];
            }
        }

        // 3. Skill icons: each operator page shows its battle skill, combo and ultimate icons as Skill-*/Combo-*/Ult-*.
        //    File names don't follow the operator name reliably (Skill-Lastrite, Skill-Endmin,
        //    Skill-Arcane1 + Skill-Arcane2), so take whatever the page itself uses.
        var skillFiles = await SkillFilesByPageAsync(wiki, names, ct);

        // 4. Download URLs and checksums for everything.
        var files = await ResolveFilesAsync(wiki, iconFiles.Concat(skillFiles.Values.SelectMany(f => f)), ct);

        int downloaded = 0, reused = 0, failed = 0;
        // The wiki's SHA-1 of each file we hold. The wiki serves a re-compressed copy, so a file's
        // own bytes never match the wiki's checksum; we compare with what the wiki said last time.
        var checksums = new Dictionary<string, string>();
        var previousChecksums = previous is null ? [] : ReadChecksums(previous);
        var roster = new List<OperatorInfo>();
        for (int i = 0; i < iconFiles.Count; i++)
        {
            string id = names[i].Replace(' ', '_');
            string iconFile = id + ".png";
            string iconPath = $"{OperatorLibrary.IconFolderName}/{iconFile}";
            // Without its avatar an operator can't be recognised, so it's left out.
            if (!await FetchAsync(files.GetValueOrDefault(iconFiles[i]), iconPath)) continue;

            var skills = new List<SkillIcon>();
            foreach (string file in skillFiles.GetValueOrDefault(names[i], []))
            {
                if (ClassifySkillFile(file) is not { } kind) continue;
                string relative = $"{OperatorLibrary.SkillFolderName}/{id}/{file}";
                if (await FetchAsync(files.GetValueOrDefault(file), relative)) skills.Add(new SkillIcon(kind, relative));
            }

            roster.Add(new OperatorInfo(id, names[i], chinese.GetValueOrDefault(names[i], names[i]), iconFile, skills)
            {
                Element = elements.GetValueOrDefault(names[i]),
                Hobbies = hobbies.GetValueOrDefault(names[i], []),
            });
        }

        // 5. Status icons (enemy debuffs, team buffs) for rotation conditions. The wiki's glossary
        //    module maps every status to its icon, so a new status needs nothing here. If the glossary
        //    can't be read, the previous copy's statuses are kept; the operators still update.
        var statuses = new List<StatusInfo>();
        IReadOnlyList<StatusInfo> wanted;
        Dictionary<string, WikiFile> statusFiles;
        try
        {
            wanted = await StatusIconsAsync(wiki, ct);
            statusFiles = await ResolveFilesAsync(wiki, wanted.Select(s => s.IconFile), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException && !ct.IsCancellationRequested)
        {
            wanted = previous is null ? [] : OperatorLibrary.ReadStatuses(previous);
            statusFiles = [];
        }
        foreach (var status in wanted)
        {
            if (await FetchAsync(statusFiles.GetValueOrDefault(status.IconFile), $"{OperatorLibrary.StatusFolderName}/{status.IconFile}"))
                statuses.Add(status);
        }

        OperatorLibrary.SaveManifest(folder, roster);
        OperatorLibrary.SaveStatuses(folder, statuses);
        File.WriteAllText(Path.Combine(folder, ChecksumsName), JsonSerializer.Serialize(checksums, new JsonSerializerOptions { WriteIndented = true }));
        return new WikiDownload(roster.Count, downloaded, reused, failed);

        // One image into folder/relative: copied from the previous copy when unchanged on the wiki,
        // else downloaded and checked to be a valid image. If that fails, the previous copy's
        // version (if any) is kept. False when the file ends up missing.
        async Task<bool> FetchAsync(WikiFile? file, string relative)
        {
            string target = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            string? old = previous is null ? null : Path.Combine(previous, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            string? oldSha1 = previousChecksums.GetValueOrDefault(relative);
            if (file is not null && file.Sha1 != "" && old is not null && File.Exists(old) && oldSha1 == file.Sha1)
            {
                File.Copy(old, target, overwrite: true);
                checksums[relative] = file.Sha1;
                reused++;
                return true;
            }
            if (file is not null)
            {
                try
                {
                    byte[] bytes = await wiki.GetBytesAsync(file.Url, ct);
                    await ImageFile.LoadAsync(bytes); // a valid image, not an error page
                    await File.WriteAllBytesAsync(target, bytes, ct);
                    checksums[relative] = file.Sha1;
                    downloaded++;
                    return true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                               or InvalidDataException or System.Runtime.InteropServices.COMException
                                           && !ct.IsCancellationRequested)
                {
                    // Fall through to the previous copy.
                }
            }
            failed++;
            if (old is null || !File.Exists(old)) return false;
            File.Copy(old, target, overwrite: true);
            // Keep the old checksum: this is still the old file, so the next update tries again.
            if (oldSha1 is not null) checksums[relative] = oldSha1;
            return true;
        }
    }

    /// <summary>The wiki's SHA-1 of each downloaded file, by library-relative path.</summary>
    private const string ChecksumsName = "wiki-sha1.json";

    private static Dictionary<string, string> ReadChecksums(string folder)
    {
        string path = Path.Combine(folder, ChecksumsName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (JsonException)
        {
            return []; // everything is downloaded again
        }
    }

    /// <summary>The wiki module holding the glossary, including which icon each status has.</summary>
    internal const string GlossaryModule = "Module:Glossary/misc";

    /// <summary>
    /// Every status with an icon, from the glossary module's icon tags (lines like
    /// <c>text:gsub("&lt;iheat&gt;", "[[File:Heat Infliction icon.png|15px|link=]]")</c>). The status is
    /// named after its icon file ("Heat Infliction icon.png" → "Heat Infliction").
    /// </summary>
    private static async Task<IReadOnlyList<StatusInfo>> StatusIconsAsync(WikiClient wiki, CancellationToken ct)
    {
        var json = await wiki.GetJsonAsync(
            $"{Api}?action=query&prop=revisions&rvprop=content&rvslots=main&format=json&formatversion=2&titles="
            + Uri.EscapeDataString(GlossaryModule), ct);
        string text = json["query"]?["pages"]?[0]?["revisions"]?[0]?["slots"]?["main"]?["content"]?.GetValue<string>()
                      ?? throw new InvalidDataException($"{GlossaryModule} has no content.");
        var statuses = new List<StatusInfo>();
        foreach (Match m in StatusIconTag().Matches(text))
        {
            string file = m.Groups[1].Value.Trim().Replace(' ', '_');
            string id = file[..^"_icon.png".Length].Replace('_', ' ');
            if (statuses.All(s => s.Id != id)) statuses.Add(new StatusInfo(id, file));
        }
        return statuses;
    }

    [GeneratedRegex(@"""<i\w+>""\s*,\s*""\[\[File:\s*([^|\]]+? icon\.png)\s*[|\]]")]
    private static partial Regex StatusIconTag();

    /// <summary>"Skill-Laevatain.png" → battle skill, "Combo-…" → combo, "Ult-…" → ultimate; anything else → null.</summary>
    internal static SkillKind? ClassifySkillFile(string file)
    {
        if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
        if (file.StartsWith("Skill-", StringComparison.Ordinal)) return SkillKind.BattleSkill;
        if (file.StartsWith("Combo-", StringComparison.Ordinal)) return SkillKind.Combo;
        if (file.StartsWith("Ult-", StringComparison.Ordinal)) return SkillKind.Ultimate;
        return null;
    }

    /// <summary>The skill icon files each operator page displays, keyed by operator name.</summary>
    private static async Task<Dictionary<string, List<string>>> SkillFilesByPageAsync(WikiClient wiki,
        IReadOnlyList<string> pages, CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var batch in pages.Chunk(TitlesPerRequest))
        {
            string query = $"{Api}?action=query&prop=images&imlimit=max&redirects=1&format=json&formatversion=2&titles="
                           + Uri.EscapeDataString(string.Join('|', batch));
            string? next = null;
            do
            {
                // Pages have ~100 images each, so a batch spans several responses ("imcontinue").
                var json = await wiki.GetJsonAsync(next is null ? query : $"{query}&imcontinue={Uri.EscapeDataString(next)}", ct);
                var redirects = RedirectMap(json);
                foreach (var page in json["query"]!["pages"]!.AsArray())
                {
                    if (page!["images"] is not JsonArray images) continue;
                    string title = page["title"]!.GetValue<string>();
                    string name = redirects.GetValueOrDefault(title, title);
                    if (!result.TryGetValue(name, out var files)) result[name] = files = [];
                    foreach (var image in images)
                    {
                        string file = image!["title"]!.GetValue<string>()["File:".Length..].Replace(' ', '_');
                        if (ClassifySkillFile(file) is not null && !files.Contains(file)) files.Add(file);
                    }
                }
                next = json["continue"]?["imcontinue"]?.GetValue<string>();
            } while (next is not null);
        }
        return result;
    }

    /// <summary>Where a wiki file can be downloaded, and its SHA-1 (to skip files that haven't changed).</summary>
    private sealed record WikiFile(string Url, string Sha1);

    private static async Task<Dictionary<string, WikiFile>> ResolveFilesAsync(WikiClient wiki, IEnumerable<string> files,
        CancellationToken ct)
    {
        var result = new Dictionary<string, WikiFile>();
        foreach (var batch in files.Distinct().Chunk(TitlesPerRequest))
        {
            var json = await wiki.GetJsonAsync(
                $"{Api}?action=query&prop=imageinfo&iiprop=url|sha1&format=json&formatversion=2&titles="
                + Uri.EscapeDataString(string.Join('|', batch.Select(f => "File:" + f))), ct);
            foreach (var page in json["query"]!["pages"]!.AsArray())
            {
                var info = page!["imageinfo"]?[0];
                string? url = info?["url"]?.GetValue<string>();
                if (url is null) continue;
                string file = page["title"]!.GetValue<string>()["File:".Length..].Replace(' ', '_');
                result[file] = new WikiFile(url, info!["sha1"]?.GetValue<string>()?.ToLowerInvariant() ?? "");
            }
        }
        return result;
    }

    private static Dictionary<string, string> RedirectMap(JsonNode json)
    {
        var map = new Dictionary<string, string>();
        if (json["query"]?["redirects"] is JsonArray redirects)
            foreach (var r in redirects)
                map[r!["to"]!.GetValue<string>()] = r["from"]!.GetValue<string>();
        return map;
    }

    /// <summary>An infobox field's value ("|name = value" on its own line), or null when it's missing, empty or "???".</summary>
    internal static string? InfoboxField(string content, string name)
    {
        var m = Regex.Match(content, $@"^\|\s*{Regex.Escape(name)}\s*=[ \t]*(.*?)\s*$", RegexOptions.Multiline);
        string value = m.Success ? m.Groups[1].Value : "";
        return value is "" or "???" ? null : value;
    }
}

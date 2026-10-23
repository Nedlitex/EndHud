using EndHud.Vision;
using EndHud.Vision.Operators;

// Developer tool.
//   endhud-cli analyze <screenshot.png> [--debug-dir <dir>]
//       Read the squad panel (levels, HP, combo, ultimate). --debug-dir writes the exact images handed to OCR.
//   endhud-cli fetch-operators <library-dir>
//       Download every operator's icon and Chinese name from the wiki into a library folder.
//   endhud-cli analyze-video <recording.mp4> [--every <seconds>] [--from <seconds>] [--to <seconds>]
//       Per sampled frame: HP, combo, ultimate per slot, the layout, the controlled slot and Laevatain's petals.
//   endhud-cli trace-video <recording.mp4> [--every <seconds>] [--from <seconds>] [--to <seconds>]
//       One CSV row per sampled frame: skill points and, per slot, combo, ultimate and battle skill
//       button, for studying when abilities are used.
//   endhud-cli trace-inn <recording.mp4> [--every <seconds>] [--from <seconds>] [--to <seconds>] [--end-margin <seconds>] [--reference <folder>]
//       Text only (never an image): per sampled frame the inn screen, the timer and each table card
//       (state and item ids); then every order once and how often each dish and drink was ordered,
//       the menu and the theme as the screens state them. --end-margin leaves out the recording's last seconds.
//   endhud-cli identify <screenshot.png> --library <library-dir> [--debug-dir <dir>]
//       Match each squad portrait against the library and print the top candidates.
//   endhud-cli build-portrait-references <squads.json> <out.json>
//       Describe the labelled 4K squad screenshots' portraits for EndHud's built-in references.
//   endhud-cli build-operator-package <out.zip> --inn <inn folder>
//       Download the roster from the wiki, add the inn's data (inn/: inn-data.json and the dish and
//       drink pictures in pictures.zip, kept by hand) and
//       write the operator package EndHud ships and updates from (operators/operator-library.zip).
//       Unchanged content keeps the same bytes. Inn data with problems stops the build.
//       --keep-roster: don't ask the wiki; keep the roster, avatars, skills and statuses of the
//       package already at <out.zip> and only put the inn's data in afresh (the wiki down, an inn-only edit).

Console.OutputEncoding = System.Text.Encoding.UTF8;

return args.FirstOrDefault() switch
{
    "analyze" when args.Length >= 2 => await AnalyzeAsync(args[1], Option("--debug-dir")),
    "fetch-operators" when args.Length >= 2 => await FetchAsync(args[1]),
    "identify" when args.Length >= 2 && Option("--library") is { } lib => await IdentifyAsync(args[1], lib, Option("--debug-dir")),
    "analyze-video" when args.Length >= 2 => await AnalyzeVideoAsync(args[1], double.Parse(Option("--every") ?? "1"), double.Parse(Option("--from") ?? "0"), Option("--to") is { } to ? double.Parse(to) : null),
    "trace-video" when args.Length >= 2 => await TraceVideoAsync(args[1], double.Parse(Option("--every") ?? "0.25"), double.Parse(Option("--from") ?? "0"), Option("--to") is { } to ? double.Parse(to) : null),
    "trace-skills" when args.Length >= 2 => await TraceSkillsAsync(args[1], double.Parse(Option("--every") ?? "0.25", System.Globalization.CultureInfo.InvariantCulture),
        double.Parse(Option("--from") ?? "0", System.Globalization.CultureInfo.InvariantCulture), Option("--to") is { } skillsTo ? double.Parse(skillsTo, System.Globalization.CultureInfo.InvariantCulture) : null),
    "trace-inn" when args.Length >= 2 =>await EndHud.Cli.InnTrace.RunAsync(args[1], double.Parse(Option("--every") ?? "0.5", System.Globalization.CultureInfo.InvariantCulture),
        double.Parse(Option("--from") ?? "0", System.Globalization.CultureInfo.InvariantCulture), Option("--to") is { } innTo ? double.Parse(innTo, System.Globalization.CultureInfo.InvariantCulture) : null,
        double.Parse(Option("--end-margin") ?? "0", System.Globalization.CultureInfo.InvariantCulture), Option("--reference")),
    "read-bubbles" when args.Length >= 2 => await EndHud.Cli.InnTrace.ReadBubblesAsync(args[1..], Option("--reference")),
    "capture" when args.Length >= 3 => await CaptureAsync(args[1], args[2]),
    "build-portrait-references" when args.Length >= 3 => await BuildReferencesAsync(args[1], args[2]),
    "build-operator-package" when args.Length >= 2 && Option("--inn") is { } inn => await BuildPackageAsync(args[1], inn, args.Contains("--keep-roster")),
    "learn" when args.Length >= 2 && Option("--library") is { } lib && Option("--slots") is { } ids => await LearnAsync(args[1], lib, ids),
    _ => Usage(),
};

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          endhud-cli analyze <screenshot.png> [--debug-dir <dir>]
          endhud-cli fetch-operators <library-dir>
          endhud-cli identify <screenshot.png> --library <library-dir> [--debug-dir <dir>]
          endhud-cli analyze-video <recording.mp4> [--every <seconds>] [--from <seconds>] [--to <seconds>]
          endhud-cli trace-inn <recording.mp4> [--every <seconds>] [--from <seconds>] [--to <seconds>] [--end-margin <seconds>] [--reference <folder>]
          endhud-cli read-bubbles <frame.png|jpg>... [--reference <folder>]
          endhud-cli learn <screenshot.png> --library <library-dir> --slots <id1>,<id2>,<id3>,<id4>
          endhud-cli capture <window-title-part> <out.png>
          endhud-cli build-portrait-references <squads.json> <out.json>
          endhud-cli build-operator-package <out.zip> --inn <inn folder> [--keep-roster]
        """);
    return 2;
}

// Grab one frame of a window through the same capture path the HUD uses (e.g. the game, for tuning).
static async Task<int> CaptureAsync(string titlePart, string outPath)
{
    IntPtr hwnd = WindowFinder.Find(titlePart);
    if (hwnd == IntPtr.Zero)
    {
        Console.Error.WriteLine($"no visible window with \"{titlePart}\" in its title");
        return 1;
    }
    using var capture = new EndHud.Vision.Capture.WindowCapture(hwnd);
    await capture.GetFrameAsync();
    var frame = await capture.GetFrameAsync();
    await ImageFile.SavePngAsync(frame, outPath);
    Console.WriteLine($"{frame.Width}x{frame.Height} -> {Path.GetFullPath(outPath)}");
    return 0;
}

// HP, combo cooldown and ultimate charge per slot over time, one line per sampled frame.
static async Task<int> TraceVideoAsync(string path, double everySeconds, double from, double? to)
{
    var video = await VideoFile.OpenAsync(path);
    var reader = new PartyReader(new TextRecognizer());
    static string F(double? v) => v is { } x ? x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "";
    Console.WriteLine("t,panel,layout,active,sp," + string.Join(",", Enumerable.Range(1, 4).Select(i => $"combo{i},comboReady{i},ult{i},ultReady{i},bs{i}")));
    for (double t = from; t < Math.Min(to ?? double.MaxValue, video.Duration.TotalSeconds); t += everySeconds)
    {
        var state = await reader.ReadAsync(await video.GetFrameAsync(TimeSpan.FromSeconds(t)));
        var cells = state.Slots.Select(s => s.Dead || s.Empty ? ",,,," :
            $"{F(s.Combo?.Progress)},{s.Combo?.Ready},{F(s.Ultimate?.Progress)},{s.Ultimate?.Ready},{s.BattleSkill}");
        Console.WriteLine($"{F(t)},{state.PanelVisible},{state.Layout},{state.ActiveSlot},{F(state.SkillPoints)},{string.Join(",", cells)}");
    }
    return 0;
}

// What the gauges' timers would show, frame by frame, through the app's own steps: read, track,
// name the squad (the shipped operator package), read the lasting battle skills' buttons and the
// lasting ultimate's bar. One CSV row per sampled frame.
static async Task<int> TraceSkillsAsync(string path, double everySeconds, double from, double? to)
{
    var video = await VideoFile.OpenAsync(path);
    var reader = new PartyReader(new TextRecognizer());
    var tracker = new PartyTracker();
    string root = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(root, "EndHud.sln"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
    string reference = Path.Combine(root, ".cache", "operator-package");
    byte[] package = File.ReadAllBytes(Path.Combine(root, "operators", OperatorPackage.FileName));
    if (OperatorPackage.Installed(reference) != OperatorPackage.ReadInfo(package)) OperatorPackage.Install(reference, package);
    string user = Path.Combine(Path.GetTempPath(), "endhud-trace-skills-user");
    Directory.CreateDirectory(user);
    var identifier = new SquadIdentifier(await OperatorLibrary.LoadAsync(user, reference));
    var timers = new SkillTimers();
    static string F(double? v) => v is { } x ? x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "";
    Console.WriteLine("t,panel,layout,active,squad,ultimates,ultBar,ultTracked,lasting,skill,skillButton,timer");
    bool prepared = false;
    for (double t = from; t < Math.Min(to ?? double.MaxValue, video.Duration.TotalSeconds); t += everySeconds)
    {
        var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
        var raw = await reader.ReadAsync(frame);
        var state = tracker.Track(raw);
        if (!prepared && state.PanelVisible)
        {
            await identifier.PrepareAsync(state);
            prepared = true;
        }
        state = await identifier.IdentifyAsync(state);
        state = state with { SkillRings = SkillRing.Read(frame, state) };
        var now = TimeSpan.FromSeconds(t);
        var views = timers.Update(state, now);
        string squad = string.Join("/", state.Slots.Select(s => s.Operator?.Operator.Id ?? "-"));
        string skills = string.Join(" ", state.SkillRings.Select(r => $"{r.OperatorId}:{r.Icon}:{F(r.LeftShare)}"));
        string buttons = string.Join(" ", state.Slots.Where(s => s.Operator?.Operator.Id == "Si").Select(s => $"slot{s.Number}:{s.BattleSkill}"));
        string shown = string.Join(" ", views.Select(v => $"{v.Key}:{(v.Value.Estimated ? "~" : "")}{v.Value.SecondsLeft:F1}"));
        string ultShown = "";
        string ultimates = string.Join("/", state.Slots.Select(s => s.Ultimate is { } u ? (u.Ready ? "R" : F(u.Progress)) : "-"));
        Console.WriteLine($"{F(t)},{raw.PanelVisible},{state.Layout},{state.ActiveSlot},{squad},{ultimates},{F(raw.UltimateTimeLeft)},{F(state.UltimateTimeLeft)},{ultShown},{skills},{buttons},{shown}");
    }
    return 0;
}

static async Task<int> AnalyzeVideoAsync(string path, double everySeconds, double from, double? to)
{
    var video = await VideoFile.OpenAsync(path);
    var reader = new PartyReader(new TextRecognizer());
    Console.WriteLine($"video: {video.Width}x{video.Height}, {video.Duration.TotalSeconds:F1} s");
    Console.WriteLine("   t  | slot: hp  combo      ultimate");
    for (double t = from; t < Math.Min(to ?? double.MaxValue, video.Duration.TotalSeconds); t += everySeconds)
    {
        var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
        var state = await reader.ReadAsync(frame);
        var cells = state.Slots.Select(s =>
        {
            if (s.Dead) return " DEAD            ";
            string hp = s.Hp is { } h ? $"{h * 100,3:F0}%" : "  - ";
            string combo = s.Combo is { } c ? (c.Ready ? "ready" : $"{c.Progress * 100,3:F0}% ") : "  -  ";
            string ult = s.Ultimate is { } u ? (u.Ready ? "ready" : $"{u.Progress * 100,3:F0}% ") : "  -  ";
            return $"{hp} {combo,-5} {ult}";
        });
        // Laevatain's petals, as if she were controlled: what her counter would read on this frame.
        string petals = OperatorCounters.Read(frame, "Laevatain")[0].Value?.ToString() ?? "-";
        Console.WriteLine($"{t,5:F1} | {(state.PanelVisible ? "" : "(no panel) ")}{string.Join(" | ", cells)} | {state.Layout} active {state.ActiveSlot?.ToString() ?? "-"} petals {petals} buffs {string.Join(",", state.Buffs.Select(b => $"{b.Id}×{b.Stacks}"))}");
    }
    return 0;
}

// Teach the library the squad in a screenshot (slot order), as a user correction would.
static async Task<int> LearnAsync(string imagePath, string libraryDir, string ids)
{
    var library = await OperatorLibrary.LoadAsync(libraryDir);
    var identifier = new SquadIdentifier(library);
    var state = await identifier.IdentifyAsync(await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(imagePath)));
    if (!state.PanelVisible)
    {
        Console.Error.WriteLine("no squad panel in that image");
        return 1;
    }
    var slotIds = ids.Split(',');
    for (int i = 0; i < slotIds.Length && i < state.Slots.Count; i++)
    {
        if (slotIds[i] is "" or "-") continue;
        var op = library.Find(slotIds[i]) ?? throw new ArgumentException($"unknown operator id {slotIds[i]}");
        await identifier.CorrectAsync(i + 1, op);
        Console.WriteLine($"slot {i + 1}: learned {op.NameZh} ({op.Id})");
    }
    return 0;
}

static async Task<int> AnalyzeAsync(string imagePath, string? debugDir)
{
    var frame = await ImageFile.LoadAsync(imagePath);
    var ocr = new TextRecognizer();
    Console.WriteLine($"image: {frame.Width}x{frame.Height}   ocr language: {ocr.LanguageTag}");

    var state = await new PartyReader(ocr).ReadAsync(frame);
    foreach (var slot in state.Slots)
    {
        if (slot.Empty)
        {
            Console.WriteLine($"slot {slot.Number}: empty (a squad of {state.SquadSize})");
            continue;
        }
        if (slot.Dead)
        {
            Console.WriteLine($"slot {slot.Number}: DEAD (death mark)");
            continue;
        }
        string hp = slot.Hp is { } h ? $"{h:P0}" : "-";
        string combo = slot.Combo is { } c ? (c.Ready ? "ready" : $"{c.Progress:P0}") : "-";
        string ult = slot.Ultimate is { } u ? (u.Ready ? "ready" : $"{u.Progress:P0}") : "-";
        Console.WriteLine($"slot {slot.Number}: level={slot.Level?.ToString() ?? "-",-3} hp={hp,-5} combo={combo,-6} ult={ult,-10} skill={slot.BattleSkill?.ToString() ?? "-",-9} ocr=\"{slot.LevelText}\"");
    }
    Console.WriteLine($"layout: {state.Layout}; panel visible: {state.PanelVisible} (HP-bar marks found: {state.PanelMarks}/8, keyboard-and-mouse layout only)");
    Console.WriteLine($"skill points: {state.SkillPoints?.ToString("F2") ?? "-"}   controlled slot: {state.ActiveSlot?.ToString() ?? "-"}");

    if (debugDir is not null)
    {
        foreach (var (name, region) in PartyRegions.All(frame))
        {
            if (TextRecognizer.Prepare(frame, region) is { } prepared)
                await ImageFile.SavePngAsync(prepared, Path.Combine(debugDir, $"{name}-ocr.png"));
            await ImageFile.SavePngAsync(frame.Crop(region), Path.Combine(debugDir, $"{name}-raw.png"));
        }
        Console.WriteLine($"debug images: {Path.GetFullPath(debugDir)}");
    }
    return 0;
}

// Download (or update) the wiki's roster, avatars and skill icons into <library>/reference, as the
// app does on first start. Learned portraits in <library>/learned are kept.
static async Task<int> FetchAsync(string libraryDir)
{
    string reference = Path.Combine(libraryDir, OperatorLibrary.ReferenceFolderName);
    using var http = WikiOperatorSource.CreateHttpClient();
    var result = await OperatorReference.UpdateAsync(reference, http);
    var library = await OperatorLibrary.LoadAsync(libraryDir);
    foreach (var op in library.Operators) Console.WriteLine($"{op.Id,-16} {op.NameZh}");
    Console.WriteLine($"{result.Operators} operators, {result.Added.Count} new ({result.Download.Downloaded} images downloaded, {result.Download.Reused} unchanged, {result.Download.Failed} failed) -> {Path.GetFullPath(reference)}");
    return 0;
}

// The operator package: the wiki's roster zipped as EndHud ships it. Starts from the previous
// package's files (so unchanged images aren't downloaded again) and keeps its date when nothing changed.
static async Task<int> BuildPackageAsync(string outZip, string innFolder, bool keepRoster)
{
    // The inn data first: it's kept by hand, so a mistake in it stops the build before the wiki is asked.
    string innFile = Path.Combine(innFolder, EndHud.Vision.Inn.InnData.FileName);
    byte[] innJson = File.ReadAllBytes(innFile);
    var inn = EndHud.Vision.Inn.InnData.Parse(innJson) ?? throw new InvalidDataException($"{innFile} is in a newer format than this build reads.");
    if (inn.Problems() is { Count: > 0 } problems)
    {
        Console.Error.WriteLine($"{innFile} has problems:" + string.Concat(problems.Select(p => $"{Environment.NewLine}  {p}")));
        return 1;
    }

    string work = Path.Combine(Path.GetTempPath(), "endhud-operator-package", "reference");
    if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    byte[]? previous = File.Exists(outZip) ? File.ReadAllBytes(outZip) : null;
    if (previous is not null)
    {
        try
        {
            OperatorPackage.Install(work, previous);
        }
        catch (InvalidDataException ex)
        {
            // The previous package can't be read by this build (its inn data in an older shape): download everything afresh.
            Console.WriteLine($"previous package not reused: {ex.Message}");
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    if (keepRoster && !File.Exists(Path.Combine(work, "operators.json")))
    {
        Console.Error.WriteLine($"--keep-roster needs a readable package at {outZip} to keep the roster of.");
        return 1;
    }
    using var http = WikiOperatorSource.CreateHttpClient();
    var result = keepRoster ? null : await OperatorReference.UpdateAsync(work, http);
    // The inn is written afresh from inn/: nothing of the previous package's (a removed dish's picture, a renamed folder) is kept.
    string innWork = Path.GetDirectoryName(EndHud.Vision.Inn.InnData.PathIn(work))!;
    if (Directory.Exists(innWork)) Directory.Delete(innWork, recursive: true);
    Directory.CreateDirectory(innWork);
    File.WriteAllBytes(EndHud.Vision.Inn.InnData.PathIn(work), innJson);
    using (var pictures = System.IO.Compression.ZipFile.OpenRead(Path.Combine(innFolder, EndHud.Vision.Inn.InnData.PicturesZipName)))
    {
        foreach (string id in inn.Dishes.Select(d => d.Id).Concat(inn.Drinks.Select(d => d.Id)))
        {
            if (pictures.GetEntry(id + ".png") is not { } picture) continue; // a gap, said below
            string target = EndHud.Vision.Inn.InnData.PicturePath(work, id);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.Compression.ZipFileExtensions.ExtractToFile(picture, target);
        }
    }
    byte[] zip = OperatorPackage.Build(work, DateTime.UtcNow, previous);
    bool changed = previous is null || !zip.AsSpan().SequenceEqual(previous);
    if (changed) File.WriteAllBytes(outZip, zip);
    var info = OperatorPackage.ReadInfo(zip)!;
    if (inn.MissingChinese() is { Count: > 0 } missing) Console.WriteLine($"inn: {inn.Dishes.Count} dishes, {inn.Themes.Count} themes; not yet seen in Chinese: {string.Join(", ", missing)}");
    if (inn.Dishes.Select(d => d.Id).Concat(inn.Drinks.Select(d => d.Id)).Where(id => !File.Exists(EndHud.Vision.Inn.InnData.PicturePath(work, id))).ToList() is { Count: > 0 } unpictured)
        Console.WriteLine($"inn: no picture yet for {string.Join(", ", unpictured)}");
    Console.WriteLine($"{info.Operators} operators ({(result is null ? "the roster kept, the wiki not asked" : $"{result.Added.Count} new, {result.Download.Downloaded} images downloaded, {result.Download.Failed} failed")}), "
        + $"content of {info.ContentAtUtc:yyyy-MM-dd HH:mm} UTC: {(changed ? "written" : "unchanged")} {Path.GetFullPath(outZip)} ({zip.Length / 1024} KB)");
    return result is { Download.Failed: > 0, Operators: 0 } ? 1 : 0;
}

static async Task<int> IdentifyAsync(string imagePath, string libraryDir, string? debugDir)
{
    var frame = await ImageFile.LoadAsync(imagePath);
    var library = await OperatorLibrary.LoadAsync(libraryDir);
    if (library.IsEmpty)
    {
        Console.Error.WriteLine($"library {libraryDir} is empty; run fetch-operators first");
        return 1;
    }

    var state = await new PartyReader(new TextRecognizer()).ReadAsync(frame);
    var matcher = new PortraitMatcher(library);
    foreach (var slot in state.Slots)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var screenPortrait = slot.ScreenPortrait ?? slot.Portrait;
        await matcher.PrepareWikiAvatars(screenPortrait.Width);
        var learned = matcher.MatchLearned(slot.Portrait);
        var differences = matcher.Differences(screenPortrait)!;
        var ranking = Enumerable.Range(0, differences.Length).OrderBy(j => differences[j]).Take(4)
            .Select(j => $"{library.Operators[j].NameZh}({library.Operators[j].Id}) {differences[j]:F3}");
        Console.WriteLine($"slot {slot.Number}: learned={learned?.Operator.NameZh ?? "-"}  [{sw.ElapsedMilliseconds} ms]");
        Console.WriteLine($"         closest first: {string.Join("  ", ranking)}");
        if (debugDir is not null)
            await ImageFile.SavePngAsync(slot.Portrait, Path.Combine(debugDir, $"slot{slot.Number}-portrait.png"));
    }
    return 0;
}

// Labelled 4K squad screenshots (squads.json: [{ "file", "slots": [4 operator ids] }]) -> the
// character model's description of every portrait, at each reference size (PortraitReferences).
static async Task<int> BuildReferencesAsync(string squadsJson, string outFile)
{
    var squads = System.Text.Json.JsonSerializer.Deserialize<List<SquadScreenshot>>(File.ReadAllText(squadsJson),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var reader = new PartyReader(new TextRecognizer());
    var portraits = new List<(string, Frame)>();
    foreach (var squad in squads)
    {
        var frame = await ImageFile.LoadAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(squadsJson))!, squad.File));
        if (frame.Width != 3840 || frame.Height != 2160)
        {
            Console.Error.WriteLine($"{squad.File}: {frame.Width}x{frame.Height}; references are made from 4K screenshots");
            return 1;
        }
        var state = await reader.ReadAsync(frame);
        for (int i = 0; i < squad.Slots.Length; i++)
            portraits.Add((squad.Slots[i], state.Slots[i].ScreenPortrait ?? state.Slots[i].Portrait));
    }
    int count = PortraitReferences.Build(portraits, outFile);
    Console.WriteLine($"{portraits.Count} portraits of {portraits.Select(p => p.Item1).Distinct().Count()} operators, {count} references -> {Path.GetFullPath(outFile)}");
    return 0;
}

record SquadScreenshot(string File, string[] Slots);

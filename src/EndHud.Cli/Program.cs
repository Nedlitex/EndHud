using EndHud.Vision;
using EndHud.Vision.Operators;

// Developer tool.
//   endhud-cli analyze <screenshot.png> [--debug-dir <dir>]
//       Read the squad panel (levels, HP, combo, ultimate). --debug-dir writes the exact images handed to OCR.
//   endhud-cli fetch-operators <library-dir>
//       Download every operator's icon and Chinese name from the wiki into a library folder.
//   endhud-cli identify <screenshot.png> --library <library-dir> [--debug-dir <dir>]
//       Match each squad portrait against the library and print the top candidates.

Console.OutputEncoding = System.Text.Encoding.UTF8;

return args.FirstOrDefault() switch
{
    "analyze" when args.Length >= 2 => await AnalyzeAsync(args[1], Option("--debug-dir")),
    "fetch-operators" when args.Length >= 2 => await FetchAsync(args[1]),
    "identify" when args.Length >= 2 && Option("--library") is { } lib => await IdentifyAsync(args[1], lib, Option("--debug-dir")),
    "analyze-video" when args.Length >= 2 => await AnalyzeVideoAsync(args[1], double.Parse(Option("--every") ?? "1")),
    "capture" when args.Length >= 3 => await CaptureAsync(args[1], args[2]),
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
          endhud-cli analyze-video <recording.mp4> [--every <seconds>]
          endhud-cli learn <screenshot.png> --library <library-dir> --slots <id1>,<id2>,<id3>,<id4>
          endhud-cli capture <window-title-part> <out.png>
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
static async Task<int> AnalyzeVideoAsync(string path, double everySeconds)
{
    var video = await VideoFile.OpenAsync(path);
    var reader = new PartyReader(new TextRecognizer());
    Console.WriteLine($"video: {video.Width}x{video.Height}, {video.Duration.TotalSeconds:F1} s");
    Console.WriteLine("   t  | slot: hp  combo      ultimate");
    for (double t = 0; t < video.Duration.TotalSeconds; t += everySeconds)
    {
        var state = await reader.ReadAsync(await video.GetFrameAsync(TimeSpan.FromSeconds(t)));
        var cells = state.Slots.Select(s =>
        {
            if (s.Dead) return " DEAD            ";
            string hp = s.Hp is { } h ? $"{h * 100,3:F0}%" : "  - ";
            string combo = s.Combo is { } c ? (c.Ready ? "ready" : $"{c.Progress * 100,3:F0}% ") : "  -  ";
            string ult = s.Ultimate is { } u ? (u.Ready ? "ready" : $"{u.Progress * 100,3:F0}% ") : "  -  ";
            return $"{hp} {combo,-5} {ult}";
        });
        Console.WriteLine($"{t,5:F1} | {(state.PanelVisible ? "" : "(no panel) ")}{string.Join(" | ", cells)}");
    }
    return 0;
}

// Teach the library the squad in a screenshot (slot order), as a user correction would.
static async Task<int> LearnAsync(string imagePath, string libraryDir, string ids)
{
    var library = await OperatorLibrary.LoadFolderAsync(libraryDir);
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
        if (slot.Dead)
        {
            Console.WriteLine($"slot {slot.Number}: DEAD (death mark)");
            continue;
        }
        string hp = slot.Hp is { } h ? $"{h:P0}" : "-";
        string combo = slot.Combo is { } c ? (c.Ready ? "ready" : $"{c.Progress:P0}") : "-";
        string ult = slot.Ultimate is { } u ? (u.Ready ? "ready" : $"{u.Progress:P0}") : "-";
        Console.WriteLine($"slot {slot.Number}: level={slot.Level?.ToString() ?? "-",-3} hp={hp,-5} combo={combo,-6} ult={ult,-10} ocr=\"{slot.LevelText}\"");
    }
    Console.WriteLine($"panel visible: {state.PanelVisible} (HP-bar marks found: {state.PanelMarks}/8)");

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

static async Task<int> FetchAsync(string folder)
{
    using var http = WikiOperatorSource.CreateHttpClient();
    var roster = await WikiOperatorSource.DownloadAsync(folder, http);
    foreach (var op in roster) Console.WriteLine($"{op.Id,-16} {op.NameZh}");
    Console.WriteLine($"{roster.Count} operators -> {Path.GetFullPath(folder)}");
    return 0;
}

static async Task<int> IdentifyAsync(string imagePath, string libraryDir, string? debugDir)
{
    var frame = await ImageFile.LoadAsync(imagePath);
    var library = await OperatorLibrary.LoadFolderAsync(libraryDir);
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
        var learned = matcher.MatchLearned(slot.Portrait);
        var (match, ranking) = matcher.MatchIcon(slot.Portrait);
        string top = string.Join("  ", ranking.Take(4).Select(r =>
            $"{r.Operator.NameZh}({r.Operator.Id}) {r.Score:F3} [shape {r.Shape:F2} colour {r.Colour:F2}]"));
        Console.WriteLine($"slot {slot.Number}: learned={learned?.Operator.NameZh ?? "-"} icon={match?.Operator.NameZh ?? "-"}  [{sw.ElapsedMilliseconds} ms]");
        Console.WriteLine($"         {top}");
        if (debugDir is not null)
            await ImageFile.SavePngAsync(slot.Portrait, Path.Combine(debugDir, $"slot{slot.Number}-portrait.png"));
    }
    Console.WriteLine($"calibration: {library.Calibration}");
    return 0;
}

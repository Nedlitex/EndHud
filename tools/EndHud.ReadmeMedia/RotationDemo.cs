// The rotation strip (排轴) over a battle recording:
//
//   dotnet run --project tools/EndHud.ReadmeMedia -c Release -- rotation <recording.mp4> <out-dir> <rotation.json>
//       [--stills <seconds,seconds,...>] [--clip <from>-<to>] [--gauges-size <0.5-2>]
//
// --stills: only those moments, as still-<seconds>s.png (no video). --clip: only that stretch, as
// clip.mp4. Either way the whole recording is still read, so what's remembered (counters, buffs
// seen) is as at that moment. --gauges-size: the menu's Size slider.
//
// The recording is read four times a second (EndHud's default) through the same steps as the app's
// PartyMonitor: squad reader, tracker, operator recognition, the controlled operator's counter, and
// the boss's statuses once a second. Each reading moves a RotationTracker on. Every frame of the
// output video then has EndHud's real strip (RotationStripView, RotationStrip) drawn at its default
// spot, as it was at that moment, with the combo hints and the gauges. Off-screen: no window is opened. The player UID is blacked out.
// Writes rotation-demo.mp4 (the whole recording), readings.tsv (what was read and where the strip
// was) and full-size PNGs of a few moments. Needs ffmpeg on PATH.

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Rotation = EndHud.Vision.Rotations.Rotation;
using Frame = EndHud.Vision.Frame;

static class RotationDemo
{
    private const int Width = 1920, Height = 1080, Fps = 15;
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(HudConfig.DefaultOcrIntervalMs);

    private sealed record Reading(double At, PartyState State, RotationPosition? Position, bool InBattle, IReadOnlyList<RememberedCounter> Counters,
        IReadOnlyList<string> BuffsSeen);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: EndHud.ReadmeMedia rotation <recording.mp4> <out-dir> <rotation.json> [--stills <s,s,...>] [--gauges-size <x>]");
            return 2;
        }
        string video = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[1]);
        var rotation = RotationFormat.FromJson(File.ReadAllText(args[2]));
        HashSet<int>? stillFrames = null;
        (int From, int To)? clip = null;
        double gaugesSize = 1;
        for (int i = 3; i + 1 < args.Length; i += 2)
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (args[i] == "--stills") stillFrames = args[i + 1].Split(',').Select(s => (int)Math.Round(double.Parse(s, invariant) * Fps)).ToHashSet();
            else if (args[i] == "--gauges-size") gaugesSize = double.Parse(args[i + 1], invariant);
            else if (args[i] == "--clip" && args[i + 1].Split('-') is [var from, var to])
                clip = ((int)Math.Round(double.Parse(from, invariant) * Fps), (int)Math.Round(double.Parse(to, invariant) * Fps));
        }
        Directory.CreateDirectory(outDir);

        // ---- 1. Read the recording as the app would ----
        string reference = Path.Combine(RepositoryRoot(), ".cache", "operator-library");
        if (!OperatorReference.Exists(reference) || !File.Exists(Path.Combine(reference, "statuses.json")))
        {
            using var http = WikiOperatorSource.CreateHttpClient();
            await OperatorReference.UpdateAsync(reference, http);
        }
        var library = await OperatorLibrary.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-rotation-demo"), reference);
        var file = await VideoFile.OpenAsync(video);
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        var identifier = new SquadIdentifier(library);
        var boss = new BossStatusReader(library);
        var teamBuffs = new TeamBuffReader(library);
        var buffsSeen = new List<string>(); // as the overlay: this battle's, in the order first seen
        var rotationTracker = new PhaseSelector(rotation);
        var stripVisibility = new StripVisibility();
        var counterMemory = new CounterMemory(); // as the overlay: forgotten out of battle
        bool readBoss = rotation.Phases.Any(p => p.Steps.Any(s => s.Expect.Any(c => c is BossStatusCondition)));
        var readings = new List<Reading>();
        double lastBoss = double.MinValue;
        double duration = file.Duration.TotalSeconds;
        var clock = Stopwatch.StartNew();
        for (double t = 0; t < duration; t += Every.TotalSeconds)
        {
            var frame = await file.GetFrameAsync(TimeSpan.FromSeconds(t));
            var state = tracker.Track(await reader.ReadAsync(frame));
            state = await identifier.IdentifyAsync(state);
            state = state with { Counters = OperatorCounters.ReadControlled(frame, state) };
            if (state.InCombat) state = state with { Buffs = teamBuffs.Read(frame) };
            if (readBoss && t - lastBoss >= 1)
            {
                state = state with { BossStatuses = boss.Read(frame) };
                lastBoss = t;
            }
            else if (readings.Count > 0)
            {
                state = state with { BossStatuses = readings[^1].State.BossStatuses }; // as the app keeps the last one
            }

            // As the overlay: in battle (StripVisibility), only for the squad the rotation is for.
            bool inBattle = stripVisibility.Update(state);
            RotationPosition? position = inBattle && rotation.IsFor(state.Slots.Select(s => s.Operator?.Operator.Id).ToList())
                ? rotationTracker.Update(state, null) : null;
            if (!inBattle)
            {
                counterMemory.Clear();
                buffsSeen.Clear();
            }
            else
            {
                buffsSeen.AddRange(state.Buffs.Select(b => b.Id).Except(buffsSeen).ToList());
            }
            readings.Add(new Reading(t, state, position, inBattle, counterMemory.Update(state), buffsSeen.ToList()));
            if (readings.Count % 40 == 0) Console.WriteLine($"read {t:0} / {duration:0} s ({clock.Elapsed.TotalSeconds:0} s)");
        }
        WriteLog(readings, Path.Combine(outDir, "readings.tsv"));

        // Icons, decoded off the UI thread.
        var avatars = new Dictionary<string, Frame?>();
        var icons = new Dictionary<(string, StepAction), Frame?>();
        foreach (var step in rotation.Phases.SelectMany(p => p.Steps))
        {
            if (library.Find(step.OperatorId) is not { } op) continue;
            avatars[op.Id] = library.FindAvatar(op.Id);
            var kind = step.Action switch { StepAction.Combo => SkillKind.Combo, StepAction.Ultimate => SkillKind.Ultimate, _ => SkillKind.BattleSkill };
            if (op.Skills.FirstOrDefault(s => s.Kind == kind) is { } icon) icons[(op.Id, step.Action)] = await library.LoadSkillIconAsync(icon);
        }
        foreach (string id in rotation.Squad)
        {
            if (library.Find(id) is not { } op) continue;
            avatars[op.Id] = library.FindAvatar(op.Id);
            if (!icons.ContainsKey((op.Id, StepAction.Combo)) && op.Skills.FirstOrDefault(s => s.Kind == SkillKind.Combo) is { } combo)
                icons[(op.Id, StepAction.Combo)] = await library.LoadSkillIconAsync(combo);
            if (!icons.ContainsKey((op.Id, StepAction.Ultimate)) && op.Skills.FirstOrDefault(s => s.Kind == SkillKind.Ultimate) is { } ultimate)
                icons[(op.Id, StepAction.Ultimate)] = await library.LoadSkillIconAsync(ultimate);
        }

        // ---- 2. Background frames, downscaled ----
        string backgrounds = Path.Combine(outDir, "background");
        Directory.CreateDirectory(backgrounds);
        Run("ffmpeg", $"-v error -y -i \"{video}\" -vf \"fps={Fps},scale={Width}:{Height}\" \"{Path.Combine(backgrounds, "%05d.png")}\"");

        // ---- 3. The strip over every frame (WPF, on an STA thread) ----
        string frames = Path.Combine(outDir, "frames");
        Directory.CreateDirectory(frames);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Render(rotation, library, readings, avatars, icons, backgrounds, frames, outDir, stillFrames, clip, gaugesSize);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        if (stillFrames is not null) return 0;

        string mp4 = Path.Combine(outDir, clip is null ? "rotation-demo.mp4" : "clip.mp4");
        Run("ffmpeg", $"-v error -y -framerate {Fps} -i \"{Path.Combine(frames, "%05d.png")}\" -c:v libx264 -pix_fmt yuv420p -crf 22 \"{mp4}\"");
        Console.WriteLine($"{mp4}: {new FileInfo(mp4).Length / 1024 / 1024} MB");
        return 0;
    }

    private static void Render(Rotation rotation, OperatorLibrary library, List<Reading> readings,
        Dictionary<string, Frame?> avatars, Dictionary<(string, StepAction), Frame?> icons, string backgrounds, string frames, string outDir,
        HashSet<int>? stillFrames, (int From, int To)? clip, double gaugesSize)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/EndHud;component/i18n/zh-CN.xaml") });
        var bitmaps = new Dictionary<object, BitmapSource?>();
        BitmapSource? Avatar(OperatorInfo op) => Cached(op.Id, () => avatars.GetValueOrDefault(op.Id));
        BitmapSource? Icon(OperatorInfo op, StepAction action) => Cached((op.Id, action), () => icons.GetValueOrDefault((op.Id, action)));
        BitmapSource? Status(string id) => Cached(("status", id), () => library.StatusIcon(id));
        BitmapSource? Cached(object key, Func<Frame?> frame) =>
            bitmaps.TryGetValue(key, out var b) ? b : bitmaps[key] = frame() is { } f ? ComboBanner.ToBitmap(f) : null;

        // The overlay's layout: the strip at its default spot, scaled as for a game window this high.
        var host = new ContentControl();
        double scale = ComboHintLayout.Scale(Height, 1.0);
        host.LayoutTransform = new ScaleTransform(scale, scale);
        // The combo-ready hints as the overlay shows them by default: 3 s once ready, 1 s ahead with a countdown.
        var hints = new ComboHints { Mode = ComboHintMode.WhenReady, ShowFor = TimeSpan.FromSeconds(HudConfig.DefaultComboHintSeconds),
            Lead = TimeSpan.FromSeconds(HudConfig.DefaultComboHintLeadSeconds) };
        var hintStack = new StackPanel { LayoutTransform = new ScaleTransform(scale, scale) };
        var hintLog = new StringBuilder("seconds\thints\n");
        string lastHints = "";
        var soonUntil = new Dictionary<int, double>(); // slot → when its countdown turned ready (for the pulse)
        var wasSoon = new HashSet<int>();
        // The gauges, centred at their default spot like the strip.
        var gauges = new ContentControl { LayoutTransform = new ScaleTransform(scale * gaugesSize, scale * gaugesSize) };
        gauges.SizeChanged += (_, _) =>
        {
            // Centred, standing on its bottom edge, like the overlay.
            Canvas.SetLeft(gauges, HudConfig.DefaultGaugesCenterX * Width - gauges.ActualWidth * scale * gaugesSize / 2);
            Canvas.SetTop(gauges, HudConfig.DefaultGaugesBottomY * Height - gauges.ActualHeight * scale * gaugesSize);
        };
        string? lastGauges = null;
        var canvas = new Canvas { Width = Width, Height = Height, Children = { host, hintStack, gauges } };
        Canvas.SetLeft(hintStack, HudConfig.DefaultComboHintX * Width);
        Canvas.SetTop(hintStack, HudConfig.DefaultComboHintY * Height);
        Canvas.SetTop(host, HudConfig.DefaultRotationY * Height);
        // Centred like the overlay: placed again whenever the strip's width changes.
        host.SizeChanged += (_, _) => Canvas.SetLeft(host, HudConfig.DefaultRotationCenterX * Width - host.ActualWidth * scale / 2);

        var files = Directory.GetFiles(backgrounds, "*.png").Order(StringComparer.Ordinal).ToArray();
        string? lastKey = null;
        var stills = new HashSet<string>();
        for (int n = 0; n < files.Length; n++)
        {
            double t = n / (double)Fps;
            var reading = readings.LastOrDefault(r => r.At <= t);
            StripView? view = reading?.Position is { } position
                ? RotationStripView.Build(rotation, position, reading.State, library, Icon, Avatar)
                : null;
            string key = RotationStripView.Key(view) ?? "";
            if (key != lastKey)
            {
                host.Content = view is null ? null : RotationStrip.Build(view);
                lastKey = key;
            }

            GaugeBoard? board = reading is { InBattle: true } ? GaugesView.Build(reading.State, reading.Counters, reading.BuffsSeen, Icon, Avatar, Status) : null;
            string? gaugeKey = board is null ? null : GaugesView.Key(board);
            if (gaugeKey != lastGauges)
            {
                gauges.Content = board is null ? null : Gauges.Build(board);
                lastGauges = gaugeKey;
            }

            // Combo hints, re-checked every frame like the overlay's timer (named slots only, as the overlay).
            var shown = new List<string>();
            hintStack.Children.Clear();
            if (reading is not null)
            {
                foreach (int number in hints.Update(reading.State, TimeSpan.FromSeconds(t)))
                {
                    var slot = reading.State.Slots[number - 1];
                    if (slot.Operator?.Operator is not { } op) continue;
                    bool soon = hints.SecondsLeft(number, TimeSpan.FromSeconds(t)) is { } left && slot.Combo is { Ready: false };
                    double secondsLeft = soon ? hints.SecondsLeft(number, TimeSpan.FromSeconds(t))!.Value : 0;
                    string status = !soon ? (string)app.Resources["ComboHint.Ready"]
                        : secondsLeft < 0.1 ? (string)app.Resources["ComboHint.Almost"]
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, (string)app.Resources["ComboHint.Soon"], secondsLeft);
                    var banner = ComboBanner.Create(ComboBanner.Content(Avatar(op), Icon(op, StepAction.Combo), op.NameZh, status, soon));
                    ComboBanner.SetSoon(banner, soon);
                    if (soon) wasSoon.Add(number);
                    else if (wasSoon.Remove(number)) soonUntil[number] = t;
                    if (!soon && soonUntil.TryGetValue(number, out double readyAt)) ComboBanner.PoseBecomeReady(banner, TimeSpan.FromSeconds(t - readyAt));
                    hintStack.Children.Add(new Grid { Children = { banner } });
                    shown.Add($"{op.Id}:{status}");
                }
            }
            string hintsNow = string.Join(" ", shown);
            if (hintsNow != lastHints)
            {
                hintLog.Append($"{t:0.00}\t{(hintsNow.Length == 0 ? "-" : hintsNow)}\n");
                lastHints = hintsNow;
            }
            if (stillFrames is not null && !stillFrames.Contains(n)) continue;
            if (clip is { } c && (n < c.From || n >= c.To)) continue;
            canvas.Measure(new Size(Width, Height));
            canvas.Arrange(new Rect(0, 0, Width, Height));
            canvas.UpdateLayout();
            canvas.UpdateLayout(); // again: centring moves the strip once its width is known
            var overlay = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            overlay.Render(canvas);
            var hud = new byte[Width * Height * 4];
            overlay.CopyPixels(hud, Width * 4, 0);

            var background = Decode(files[n]);
            Composite(background, hud);
            BlackOutUid(background);
            if (stillFrames is not null)
            {
                Save(background, Path.Combine(outDir, $"still-{t:000.0}s.png"));
                continue;
            }
            Save(background, Path.Combine(frames, $"{(clip is { } range ? n - range.From : n) + 1:D5}.png"));

            // A still the first time each kind of moment shows (ready, waiting with a reason).
            if (view?.Steps.FirstOrDefault(s => s.State is StripStepState.Ready or StripStepState.Waiting) is { } current)
            {
                string kind = current.State == StripStepState.Ready ? "ready" : "waiting";
                if (stills.Add($"{kind}|{view.Phase}|{current.Action}")) Save(background, Path.Combine(outDir, $"still-{t:000.0}s-{kind}.png"));
            }
            if (n % 150 == 0) Console.WriteLine($"rendered {t:0} s");
        }
        File.WriteAllText(Path.Combine(outDir, "hints.tsv"), hintLog.ToString());
    }

    private static void WriteLog(List<Reading> readings, string path)
    {
        var text = new StringBuilder("seconds\tsquad\tskill points\tcontrolled\tcounters\tboss\tstep\tready\tchecks\n");
        foreach (var r in readings)
        {
            var s = r.State;
            text.Append($"{r.At:0.00}\t{string.Join(",", s.Slots.Select(x => x.Operator?.Operator.Id ?? "?"))}\t{s.SkillPoints?.ToString("0.0") ?? "-"}\t{s.ActiveSlot?.ToString() ?? "-"}\t")
                .Append($"{string.Join(",", s.Counters.Select(c => $"{c.CounterId}={c.Value?.ToString() ?? "?"}"))}{string.Concat(s.Buffs.Select(b => $" +{b.Id}×{b.Stacks}"))}\t{string.Join(",", s.BossStatuses?.Select(b => b.StatusId) ?? [])}\t")
                .Append(r.Position is { } p ? $"{p.Step.PhaseLabel} #{p.Step.StepInPhase + 1} {p.Step.Step.OperatorId} {p.Step.Step.Action}\t{p.Ready}\t{string.Join(" ", p.Checks.Select(c => $"{c.Kind}:{c.Holds?.ToString() ?? "?"}"))}" : "-\t-\t-")
                .Append('\n');
        }
        File.WriteAllText(path, text.ToString());
    }

    private static void Run(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })!;
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{exe} failed ({process.ExitCode})");
    }

    private static byte[] Decode(string png)
    {
        var source = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    // The overlay is premultiplied: colour already scaled by its alpha.
    private static void Composite(byte[] background, byte[] hud)
    {
        for (int i = 0; i < background.Length; i += 4)
        {
            int a = hud[i + 3];
            if (a == 0) continue;
            for (int c = 0; c < 3; c++) background[i + c] = (byte)Math.Min(255, hud[i + c] + background[i + c] * (255 - a) / 255);
        }
    }

    // The player UID, bottom-left (at 4K: x 170-560, 15-95 px from the bottom).
    private static void BlackOutUid(byte[] pixels)
    {
        double s = Width / 3840.0;
        for (int y = (int)(Height - 95 * s); y < (int)(Height - 15 * s); y++)
        for (int x = (int)(170 * s); x < (int)(560 * s); x++)
        {
            int i = (y * Width + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
        }
    }

    private static void Save(byte[] pixels, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }
}

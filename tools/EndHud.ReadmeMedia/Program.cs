// Renders the README's animated pictures of the combo-ready hint.
//
//   dotnet run --project tools/EndHud.ReadmeMedia -c Release -- <original-recording.mp4> <out-dir>
//
// The recording is read once a second through EndHud's own pipeline (squad reader, tracker,
// operator recognition, hint logic), which decides when each banner slides in and out. Each
// frame of the GIF is then the recording's frame with EndHud's real banners (ComboBanner) drawn
// over it at that moment, off-screen: no window is opened. The player UID is blacked out.
// Needs ffmpeg on PATH to assemble the GIFs.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud;
using EndHud.Vision;
using EndHud.Vision.Operators;

if (args.Length > 0 && args[0] == "rotation") return await RotationDemo.RunAsync(args[1..]);

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: EndHud.ReadmeMedia <original-recording.mp4> <out-dir>");
    return 2;
}
string video = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);

const int Width = 2560, Height = 1440, Fps = 12;
var clips = new[]
{
    // The hint as it normally behaves: each banner shows for 3 s when a cooldown finishes.
    new Clip("combo-hint", ComboHintMode.WhenReady, From: 21.5, To: 26.5, Crop: new(0, 380, 1140, 1060), OutWidth: 540),
    // Always-show mode, close up: a banner leaves when its combo skill is used, the rest glide up.
    new Clip("combo-hints-always", ComboHintMode.WhileReady, From: 6.5, To: 9.5, Crop: new(470, 505, 560, 450), OutWidth: 420),
};

// ---- 1. When each banner shows, from EndHud's own readings of the recording ----
var plans = await Task.Run(async () =>
{
    var file = await VideoFile.OpenAsync(video);
    // The roster and icons from the developer cache (downloaded if missing), as the tests use.
    string reference = Path.Combine(RepositoryRoot(), ".cache", "operator-library");
    if (!OperatorReference.Exists(reference))
    {
        using var http = WikiOperatorSource.CreateHttpClient();
        await OperatorReference.UpdateAsync(reference, http);
    }
    var library = await OperatorLibrary.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-readme-media"), reference);
    var result = new Dictionary<string, List<Banner>>();
    foreach (var clip in clips)
    {
        var reader = new PartyReader(new TextRecognizer());
        var tracker = new PartyTracker();
        var identifier = new SquadIdentifier(library);
        var hints = new ComboHints { Mode = clip.Mode, ShowFor = TimeSpan.FromSeconds(3) };
        var banners = new List<Banner>();
        var open = new Dictionary<int, Banner>();
        for (int t = 0; t <= (int)Math.Ceiling(clip.To); t++)
        {
            var frame = (await file.GetFrameAsync(TimeSpan.FromSeconds(t))).Resize(Width, Height);
            var state = await identifier.IdentifyAsync(tracker.Track(await reader.ReadAsync(frame)));
            var shown = hints.Update(state, TimeSpan.FromSeconds(t));
            foreach (int slot in open.Keys.Except(shown).ToList())
            {
                open[slot].OutAt = t;
                open.Remove(slot);
            }
            foreach (int slot in shown.Where(s => !open.ContainsKey(s)))
            {
                var op = state.Slots[slot - 1].Operator?.Operator;
                var banner = new Banner(slot, op, InAt: t);
                open[slot] = banner;
                banners.Add(banner);
            }
        }
        result[clip.Name] = banners;
        Console.WriteLine($"{clip.Name}: " + string.Join(", ", banners.Select(b => $"slot {b.Slot} ({b.Operator?.NameEn}) {b.InAt}-{b.OutAt?.ToString() ?? "end"} s")));

        // Background frames for the clip.
        string dir = Path.Combine(outDir, clip.Name + "-background");
        Directory.CreateDirectory(dir);
        int n = 0;
        for (double t = clip.From; t < clip.To; t += 1.0 / Fps)
            await ImageFile.SavePngAsync((await file.GetFrameAsync(TimeSpan.FromSeconds(t))).Resize(Width, Height), Path.Combine(dir, $"{n++:D4}.png"));
    }

    // Icons, decoded off the UI thread too.
    foreach (var banner in result.Values.SelectMany(b => b))
    {
        if (banner.Operator is not { } op) continue;
        banner.AvatarFrame = library.FindAvatar(op.Id);
        if (op.Skills.FirstOrDefault(s => s.Kind == SkillKind.Combo) is { } icon) banner.IconFrame = await library.LoadSkillIconAsync(icon);
    }
    return result;
});

// ---- 2. Draw the banners over each frame (WPF, on this STA thread) and assemble the GIFs ----
var thread = new Thread(() =>
{
    foreach (var clip in clips) RenderClip(clip, plans[clip.Name]);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return 0;

void RenderClip(Clip clip, List<Banner> banners)
{
    string backgrounds = Path.Combine(outDir, clip.Name + "-background");
    string framesDir = Path.Combine(outDir, clip.Name + "-frames");
    Directory.CreateDirectory(framesDir);

    // The overlay's layout: the hint stack at its default spot, scaled for a 1440-high window.
    var stack = new StackPanel();
    var frameBorder = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(6), Child = stack };
    double scale = ComboHintLayout.Scale(Height, 1.0);
    frameBorder.LayoutTransform = new ScaleTransform(scale, scale);
    var canvas = new Canvas { Width = Width, Height = Height, Children = { frameBorder } };
    Canvas.SetLeft(frameBorder, HudConfig.DefaultComboHintX * Width);
    Canvas.SetTop(frameBorder, HudConfig.DefaultComboHintY * Height);

    // The caption and slot label from the app's own Chinese UI strings.
    _ = System.Windows.Application.ResourceAssembly; // makes sure pack:// URIs are registered
    var strings = new ResourceDictionary { Source = new Uri("pack://application:,,,/EndHud;component/i18n/zh-CN.xaml") };
    string caption = (string)strings["ComboHint.Ready"], slotLabel = (string)strings["Party.SlotNumber"];
    foreach (var b in banners)
    {
        var avatar = b.AvatarFrame is { } a ? ComboBanner.ToBitmap(a) : null;
        var icon = b.IconFrame is { } i ? ComboBanner.ToBitmap(i) : null;
        b.Element = ComboBanner.Create(ComboBanner.Content(avatar, icon, b.Operator?.NameZh ?? string.Format(slotLabel, b.Slot), caption));
        b.Holder = ComboBanner.Holder(b.Element, b.Slot);
    }

    var files = Directory.GetFiles(backgrounds, "*.png").Order().ToArray();
    for (int n = 0; n < files.Length; n++)
    {
        double t = clip.From + n / (double)Fps;
        // Banners on screen at t, in slot order (one that is sliding out still takes its space).
        var visible = banners.Where(b => b.InAt <= t && (b.OutAt is not { } o || t < o + ComboBanner.OutDuration.TotalSeconds))
            .OrderBy(b => b.Slot).ThenBy(b => b.InAt).ToList();
        stack.Children.Clear();
        foreach (var b in visible)
        {
            ComboBanner.Pose(b.Holder!, b.Element!, TimeSpan.FromSeconds(t - b.InAt),
                b.OutAt is { } o && t >= o ? TimeSpan.FromSeconds(t - o) : null);
            stack.Children.Add(b.Holder);
        }
        canvas.Measure(new Size(Width, Height));
        canvas.Arrange(new Rect(0, 0, Width, Height));
        canvas.UpdateLayout();
        var overlay = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        overlay.Render(canvas);
        var hud = new byte[Width * Height * 4];
        overlay.CopyPixels(hud, Width * 4, 0);

        var background = Decode(files[n]);
        Composite(background, hud);
        BlackOutUid(background);
        SaveCropped(background, clip, Path.Combine(framesDir, $"{n:D4}.png"));
    }

    string gif = Path.Combine(outDir, clip.Name + ".gif");
    var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
        $"-v error -y -framerate {Fps} -i \"{Path.Combine(framesDir, "%04d.png")}\" " +
        "-vf \"split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle\" " +
        $"-loop 0 \"{gif}\"") { UseShellExecute = false })!;
    ffmpeg.WaitForExit();
    Console.WriteLine($"{gif}: {new FileInfo(gif).Length / 1024} KB");
}

static byte[] Decode(string png)
{
    var source = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
    var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
    source.CopyPixels(pixels, source.PixelWidth * 4, 0);
    return pixels;
}

// The overlay is premultiplied: colour already scaled by its alpha.
static void Composite(byte[] background, byte[] hud)
{
    for (int i = 0; i < background.Length; i += 4)
    {
        int a = hud[i + 3];
        if (a == 0) continue;
        for (int c = 0; c < 3; c++) background[i + c] = (byte)Math.Min(255, hud[i + c] + background[i + c] * (255 - a) / 255);
    }
}

// The player UID, bottom-left (at 4K: x 205-490, about 77 px from the bottom).
static void BlackOutUid(byte[] pixels)
{
    double s = Width / 3840.0;
    for (int y = (int)(Height - 95 * s); y < (int)(Height - 15 * s); y++)
    for (int x = (int)(170 * s); x < (int)(560 * s); x++)
    {
        int i = (y * Width + x) * 4;
        pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
    }
}

static void SaveCropped(byte[] pixels, Clip clip, string path)
{
    var full = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
    var cropped = new CroppedBitmap(full, clip.Crop);
    double k = clip.OutWidth / (double)clip.Crop.Width;
    var scaled = new TransformedBitmap(cropped, new ScaleTransform(k, k));
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(scaled));
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static string RepositoryRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
    throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
}

record Clip(string Name, ComboHintMode Mode, double From, double To, Int32Rect Crop, int OutWidth);

sealed class Banner(int slot, OperatorInfo? op, int InAt)
{
    public int Slot { get; } = slot;
    public OperatorInfo? Operator { get; } = op;
    public int InAt { get; } = InAt;
    public int? OutAt { get; set; }
    public EndHud.Vision.Frame? AvatarFrame { get; set; }
    public EndHud.Vision.Frame? IconFrame { get; set; }
    public Border? Element { get; set; }
    public Grid? Holder { get; set; }
}

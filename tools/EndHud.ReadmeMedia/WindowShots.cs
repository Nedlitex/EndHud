// The README's pictures of EndHud's own windows, drawn off-screen from the real WPF code:
//
//   dotnet run --project tools/EndHud.ReadmeMedia -c Release -- windows <out-dir>
//
// Writes, in the Chinese UI (i18n/zh-CN.xaml, the app's default language):
//   rotation-editor.png  the rotation editor on the published rotation 卡缪 莱万汀 祀 诀, its burst phase selected
//                        (entry condition, priority, finish condition) and the preview with the squad recognised;
//   alert-editor.png     the alert editor with a few sample rules, the first tied to that squad (指定小队);
//   menu-rotation.png    the menu's 排轴 card (with "show in open-world battles too");
//   menu-alerts.png      the menu's 战斗提示 card (with the pop-ups' size).
// No window is opened: each window is built, laid out and rendered to a bitmap. Everything EndHud writes goes to
// this tool's own folder (Program.cs: AppFolders.UseRoot), never the developer's %APPDATA%\EndHud. The operator
// pictures come from the package EndHud ships (operators/operator-library.zip).

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

static class WindowShots
{
    private const string RotationFile = "camille-laevatain-si-arcane.json";
    private static readonly string[] Squad = ["Camille", "Laevatain", "Si", "Arcane"];

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: EndHud.ReadmeMedia windows <out-dir>");
            return 2;
        }
        string outDir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outDir);

        // A fresh folder each run: the editors read and write what is in it.
        string root = Path.Combine(Path.GetTempPath(), "endhud-readme-media", "windows");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string reference = Path.Combine(root, "reference");
        OperatorPackage.Install(reference, File.ReadAllBytes(Path.Combine(RepositoryRoot(), "operators", OperatorPackage.FileName)));
        // Decoded here, not on the windows' thread (the image decoder fails on it).
        var library = OperatorLibrary.LoadAsync(Path.Combine(root, "user"), reference).GetAwaiter().GetResult();

        // The published rotation, where the app keeps the recommended ones (listed as 【推荐】).
        var rotations = new RotationLibrary(AppFolders.Rotations);
        Directory.CreateDirectory(Path.Combine(AppFolders.Rotations, "published"));
        File.Copy(Path.Combine(RepositoryRoot(), "rotations", RotationFile), Path.Combine(AppFolders.Rotations, "published", RotationFile), overwrite: true);
        var rotation = RotationFormat.FromJson(File.ReadAllText(Path.Combine(RepositoryRoot(), "rotations", RotationFile)));

        // A few alert rules like the README's examples; the first runs for that squad only.
        var alerts = new AlertStore(Path.Combine(root, "alerts.json"));
        alerts.Save(
        [
            AlertRule.New([new StaggeredCondition(true)], "失衡中：全力输出！") with { Squad = Squad },
            AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "可以放战技了"),
            AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "冻结准备好了"),
            AlertRule.New([new StaggerNodesCondition(Comparison.AtLeast, 1)], "小失衡"),
        ]);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/EndHud;component/i18n/zh-CN.xaml"),
                });

                var editor = new RotationEditor(rotations, library, Squad, EndHud.Vision.HudLayout.KeyboardMouse, rotation.Id,
                    () => Task.FromResult<RotationSyncResult?>(null), _ => { });
                var phases = (ListBox)editor.FindName("PhaseList");
                phases.SelectedIndex = rotation.Phases.ToList().FindIndex(p => p.Finish is { Count: > 0 });
                ((ListBox)editor.FindName("StepList")).SelectedIndex = 2;
                Pump(); // the preview's skill icons load in the background, then redraw it
                Save(Render(editor, 1200), Path.Combine(outDir, "rotation-editor.png"));

                var alertEditor = new AlertEditor(alerts, library, () => [rotation], Squad, () => { });
                Save(Render(alertEditor, 1100), Path.Combine(outDir, "alert-editor.png"));

                var overlay = new OverlayWindow(new HudOptions(null, false), new HudConfig());
                var menu = (Border)overlay.FindName("MenuPanel");
                menu.Visibility = Visibility.Visible;
                menu.MaxHeight = double.PositiveInfinity;
                menu.Measure(new Size(menu.Width, double.PositiveInfinity));
                menu.Arrange(new Rect(menu.DesiredSize));
                menu.UpdateLayout();
                Save(Card(menu, (FrameworkElement)overlay.FindName("RotationOpenWorldCheckBox")), Path.Combine(outDir, "menu-rotation.png"));
                Save(Card(menu, (FrameworkElement)overlay.FindName("AlertsOpenWorldCheckBox")), Path.Combine(outDir, "menu-alerts.png"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
        Console.WriteLine($"written to {outDir}");
        return 0;
    }

    /// <summary>
    /// A window's content as it would be drawn at <paramref name="width"/>, as tall as it needs to be (nothing scrolled
    /// out of sight), on the window's background. The content leaves the window for this; the window's own resources
    /// (its implicit styles) and font go with it.
    /// </summary>
    private static BitmapSource Render(Window window, double width)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources.MergedDictionaries.Add(window.Resources);
        var page = new Border
        {
            Child = content,
            Background = SystemColors.WindowBrush,
            Width = width,
        };
        TextElement(page, window);
        page.Measure(new Size(width, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();
        return Bitmap(page, page.ActualWidth, page.ActualHeight);
    }

    /// <summary>Lets the window's background work (icons loading, then redrawing) run for a moment.</summary>
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void TextElement(Border page, Window window)
    {
        System.Windows.Documents.TextElement.SetFontFamily(page, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, window.FontSize);
    }

    /// <summary>The menu card holding <paramref name="inside"/>, over a dark game-like backdrop with the menu's own fill.</summary>
    private static BitmapSource Card(Border menu, FrameworkElement inside)
    {
        DependencyObject node = inside;
        while (node is not Border { Style: not null } border || border.Parent is not StackPanel) node = VisualTreeHelper.GetParent(node);
        var card = (Border)node;
        var at = card.TransformToAncestor(menu).Transform(new Point(0, 0));
        const double PadX = 20, PadY = 10;
        var area = new Rect(at.X - PadX, at.Y - PadY, card.ActualWidth + 2 * PadX, card.ActualHeight + 2 * PadY);
        return Bitmap(menu, area.Width, area.Height, area.Location);
    }

    /// <summary>
    /// Draws the <paramref name="width"/> x <paramref name="height"/> area of <paramref name="visual"/> starting at
    /// <paramref name="from"/> (its own coordinates), over a dark backdrop (what shows through the menu's translucent fill).
    /// </summary>
    private static BitmapSource Bitmap(Visual visual, double width, double height, Point from = default)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            var brush = new VisualBrush(visual)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(from.X, from.Y, width, height),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, width, height),
            };
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x34)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"{path}: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }
}

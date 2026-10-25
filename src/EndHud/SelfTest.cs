using System.Diagnostics;
using System.IO;
using System.Reflection;
using EndHud.Vision;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// <c>EndHud.exe --self-test &lt;folder&gt; [--screenshot &lt;image&gt; --expect &lt;id1,id2,id3,id4&gt;]</c>:
/// checks that this build works on this machine, without a window and without touching the user's
/// data. It does what the app does on first start, with <c>&lt;folder&gt;</c> in place of
/// %APPDATA%\EndHud\operators: downloads the operator roster and avatars from the wiki, loads them,
/// and (given a screenshot of the game) names the squad. Results go to
/// <c>&lt;folder&gt;\self-test.log</c>; the exit code is 0 when every step passed. The release
/// workflow runs it on the installed MSI and on the portable exe.
/// </summary>
internal static class SelfTest
{
    public const string Flag = "--self-test";

    public static bool Requested(string[] args) => args.Contains(Flag);

    public static async Task<int> RunAsync(string[] args)
    {
        string? folder = Option(args, Flag);
        if (folder is null) return 2;
        Directory.CreateDirectory(folder);
        using var log = new StreamWriter(Path.Combine(folder, "self-test.log"), append: false) { AutoFlush = true };
        void Log(string line) => log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");

        try
        {
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log($"EndHud {version} at {Environment.ProcessPath}");

            // 1. The operator library built into EndHud, unpacked exactly as on first start (no network).
            string reference = Path.Combine(folder, OperatorLibrary.ReferenceFolderName);
            var sw = Stopwatch.StartNew();
            if (OperatorPackage.BuiltIn() is not { } package) return Fail("package: no operator package built in");
            OperatorPackage.InstallIfNewer(reference, package);
            var info = OperatorPackage.Installed(reference)!;
            Log($"PASS package: {info.Operators} operators, content of {info.ContentAtUtc:yyyy-MM-dd} (UTC), unpacked in {sw.Elapsed.TotalSeconds:F1} s");

            var library = await OperatorLibrary.LoadAsync(folder);
            if (library.IsEmpty) return Fail("load: the built-in library is empty");
            Log($"PASS load: {library.Operators.Count} operators, every one with an avatar");

            // 2. WPF draws (its native renderer loads), with Windows' DLL search limited to System32 (Program.Main).
            if (RenderedPixel() is not { } pixel || pixel.R < 200 || pixel.G > 50) return Fail($"render: WPF drew {pixel}, expected red");
            Log("PASS render: WPF draws");
            if (DllSearchedOutsideSystem32(folder) is { } searched) return Fail($"dll search: {searched}");
            Log("PASS dll search: a DLL loaded by name comes from System32 only");

            // 3. Recognition, if given a screenshot of the game.
            if (Option(args, "--screenshot") is not { } screenshot) return Pass();
            string[] expected = (Option(args, "--expect") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

            var state = await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(screenshot));
            Log($"PASS read: levels [{string.Join(", ", state.Slots.Select(s => s.Level?.ToString() ?? "-"))}], panel visible: {state.PanelVisible}");

            var identifier = new SquadIdentifier(library);
            sw.Restart();
            await identifier.PrepareAsync(state);
            Log($"PASS character model: {library.Operators.Count} avatars described in {sw.Elapsed.TotalSeconds:F1} s");

            var named = state;
            for (int n = 0; n < SquadIdentifier.MinReadings * state.Slots.Count; n++)
                named = await identifier.IdentifyAsync(state);
            string[] names = named.Slots.Select(s => s.Operator?.Operator.Id ?? "-").ToArray();
            Log($"identify: {string.Join(", ", names)}");
            if (expected.Length > 0 && !expected.SequenceEqual(names))
                return Fail($"identify: expected {string.Join(", ", expected)}");
            Log("PASS identify");
            return Pass();
        }
        catch (Exception ex)
        {
            return Fail(ex.ToString());
        }

        int Pass()
        {
            Log("SELF-TEST PASSED");
            return 0;
        }

        int Fail(string why)
        {
            Log($"FAIL {why}");
            Log("SELF-TEST FAILED");
            return 1;
        }
    }

    /// <summary>A red square drawn by WPF off screen: the middle pixel, or null if nothing was drawn.</summary>
    private static System.Windows.Media.Color? RenderedPixel()
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(System.Windows.Media.Brushes.Red, null, new System.Windows.Rect(0, 0, 8, 8));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(8, 8, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[8 * 8 * 4];
        bitmap.CopyPixels(pixels, 8 * 4, 0);
        int at = (4 * 8 + 4) * 4;
        return pixels[at + 3] == 0 ? null : System.Windows.Media.Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }

    /// <summary>
    /// Null when Windows looks for a DLL loaded by name in System32 only (<see cref="Program"/>): a file that isn't a
    /// DLL, named like one, in the current folder (searched by Windows' usual order) must not be found. Else why.
    /// </summary>
    private static string? DllSearchedOutsideSystem32(string folder)
    {
        string name = $"endhud-probe-{Guid.NewGuid():N}.dll";
        string probe = Path.Combine(folder, name);
        File.WriteAllText(probe, "not a DLL");
        string before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = folder;
            IntPtr module = LoadLibraryW(name);
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (module != IntPtr.Zero) return "a DLL in the current folder was loaded";
            const int NotFound = 126, BadFormat = 193;
            return error == NotFound ? null : error == BadFormat ? "the current folder was searched" : $"unexpected error {error}";
        }
        finally
        {
            Environment.CurrentDirectory = before;
            File.Delete(probe);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string fileName);

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

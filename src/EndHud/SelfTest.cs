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

            // 2. Recognition, if given a screenshot of the game.
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

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

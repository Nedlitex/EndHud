using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using EndHud.SkillInput;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The battle-skill key reader's privacy promises, held by tests rather than comments (owner,
/// 2026-10-23): Windows is asked about the four battle-skill keys and no other (a key that isn't one
/// never reaches EndHud at all), the pad decoder keeps nothing but each pad's five buttons, and nothing
/// about a key ever reaches the log. (That a press says only which slot and when is <c>SkillPressTests</c>;
/// that the keys are held only while reading is <c>SkillInputReaderTests</c>.)
/// </summary>
[Collection(WpfCollection.Name)] // the log is one for the process, like DiagnosticsTests'
public class SkillInputPrivacyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-skill-input-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public SkillInputPrivacyTests() => DiagnosticLog.UseFolder(_folder);

    public void Dispose()
    {
        DiagnosticLog.UseFolder(null); // back to the test run's own log (TestFolders), never the player's
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Only_the_four_bound_keys_are_asked_of_windows()
    {
        // Every bindable key, in every slot: the hotkeys asked for are that slot's key and nothing else.
        for (int code = 1; code <= SkillKeyCodes.Max; code++)
        {
            int[] keys = [code, 0x1FD, 0x1FE, 0x1FF];
            var asked = SkillHotkeys.For(keys, c => (uint)(c & 0xFF) + 0x100); // a stand-in layout: one virtual key per code
            Assert.All(asked, h => Assert.InRange(h.Slot, 1, 4));
            Assert.Equal([(uint)(code & 0xFF) + 0x100], asked.Where(h => h.Slot == 1 && SkillHotkeys.NumLockOnKey(code) != h.VirtualKey).Select(h => h.VirtualKey));
            Assert.True(asked.Count <= 8);
        }
        Assert.Equal([0x31u, 0x32u, 0x33u, 0x34u], SkillHotkeys.For(SkillKeyCodes.Defaults, SkillInputNative.VirtualKey).Select(h => h.VirtualKey));
    }

    [Fact]
    public void The_pad_decoder_keeps_nothing_but_each_pads_five_buttons()
    {
        var decoder = new SkillInputDecoder();
        decoder.Pad(1, PadButtons.Shoulder | PadButtons.West);
        decoder.Forget(1);

        Assert.Empty(Field<Dictionary<nint, PadButtons>>(decoder, "_pads"));
        // Nothing else to hide a button in: the decoder's only field is the one above.
        Assert.Equal(["_pads"], typeof(SkillInputDecoder)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.Name).Order());
    }

    [Fact]
    public void The_reader_logs_no_keys()
    {
        var random = new Random(20261023);
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);

        for (int i = 0; i < 200; i++) reader.Hotkey(random.Next());
        for (int i = 0; i < 10; i++) reader.Hotkey(SkillInputReaderQueueTests.IdOf(i % 4 + 1));
        Assert.Equal(10, reader.TakeUntil(TimeSpan.MaxValue).Count);
        reader.SetListening(false, SkillKeyCodes.Defaults);

        var messages = File.ReadAllLines(DiagnosticLog.FilePath).Select(Message).ToList();
        Assert.Equal(["skill input: listening", "skill input: stopped"], messages);
        string all = string.Join("\n", messages);
        Assert.DoesNotMatch(new Regex("0x[0-9A-Fa-f]{2}"), all);
        foreach (string word in new[] { "scan", "vkey", "virtual", "make", "key code" }) Assert.DoesNotContain(word, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\d"), all); // no key or id, as a number of any base
    }

    /// <summary>A log line's message: what follows the time, level and thread ("... INFO  [12] message").</summary>
    private static string Message(string line) => line[(line.IndexOf("] ", StringComparison.Ordinal) + 2)..];

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}

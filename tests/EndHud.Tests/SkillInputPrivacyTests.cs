using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using EndHud.SkillInput;
using Xunit;
using static EndHud.SkillInput.SkillInputNative;

namespace EndHud.Tests;

/// <summary>
/// The battle-skill key reader's privacy promises, held by tests rather than comments (owner,
/// 2026-10-23): a key that isn't one of the four battle-skill keys is dropped inside the decoder with
/// nothing kept, and nothing about a key ever reaches the log. (That a press says only which slot and
/// when is <c>SkillPressTests</c>.)
/// </summary>
[Collection(WpfCollection.Name)] // the log is one for the process, like DiagnosticsTests'
public class SkillInputPrivacyTests : IDisposable
{
    private const ushort Break = 0x01, E0 = 0x02;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-skill-input-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public SkillInputPrivacyTests() => DiagnosticLog.UseFolder(_folder);

    public void Dispose()
    {
        DiagnosticLog.UseFolder(Path.Combine(ConfigStore.Folder, "logs"));
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Every_unbound_key_is_dropped_and_leaves_nothing_behind()
    {
        var decoder = new SkillInputDecoder(SkillKeyCodes.Defaults);
        int fed = 0;

        for (int code = 0; code <= SkillKeyCodes.Max; code++)
        {
            if (SkillKeyCodes.Defaults.Contains(code)) continue;
            ushort extended = (code & SkillKeyCodes.Extended) != 0 ? E0 : (ushort)0;
            Assert.Null(decoder.Key((ushort)(code & 0xFF), extended));
            Assert.Null(decoder.Key((ushort)(code & 0xFF), (ushort)(extended | Break)));
            fed++;
        }

        Assert.Equal(508, fed);
        Assert.All(Field<bool[]>(decoder, "_held"), held => Assert.False(held));
        Assert.Empty(Field<Dictionary<nint, PadButtons>>(decoder, "_pads"));
        Assert.Equal(SkillKeyCodes.Defaults, Field<int[]>(decoder, "_keys"));
        // Nothing else to hide a key in: the decoder's only fields are the three above.
        Assert.Equal(["_held", "_keys", "_pads"], typeof(SkillInputDecoder)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.Name).Order());
    }

    [Fact]
    public void The_reader_logs_no_keys()
    {
        var random = new Random(20261023);
        var fedCodes = new HashSet<int>();
        using var reader = new SkillInputReader(readWindows: false);
        reader.SetListening(true, SkillKeyCodes.Defaults);

        for (int i = 0; i < 200; i++)
        {
            int code;
            do code = random.Next(0x01, 0x80); while (SkillKeyCodes.Defaults.Contains(code));
            fedCodes.Add(code);
            reader.Feed(new RawKeyboard((ushort)code, (ushort)(random.Next(2) == 0 ? 0 : Break)));
        }
        for (int i = 0; i < 10; i++)
        {
            ushort code = (ushort)SkillKeyCodes.Defaults[i % 4];
            reader.Feed(new RawKeyboard(code, 0));
            reader.Feed(new RawKeyboard(code, Break));
        }
        Assert.Equal(10, reader.TakeUntil(TimeSpan.MaxValue).Count);
        reader.SetListening(false, SkillKeyCodes.Defaults);

        var messages = File.ReadAllLines(DiagnosticLog.FilePath).Select(Message).ToList();
        Assert.Equal(["skill input: listening", "skill input: stopped"], messages);
        string all = string.Join("\n", messages);
        Assert.DoesNotMatch(new Regex("0x[0-9A-Fa-f]{2}"), all);
        foreach (string word in new[] { "scan", "vkey", "make", "key code" }) Assert.DoesNotContain(word, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\d"), all); // no fed code, as a number of any base
    }

    /// <summary>A log line's message: what follows the time, level and thread ("... INFO  [12] message").</summary>
    private static string Message(string line) => line[(line.IndexOf("] ", StringComparison.Ordinal) + 2)..];

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}

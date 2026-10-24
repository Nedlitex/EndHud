using System.IO;
using System.Text.RegularExpressions;
using EndHud.SkillInput;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Choosing a slot's battle-skill key in the menu: click Change, press the key (<see cref="SkillKeyCapture"/>).
/// Nothing is typed; Esc keeps the old key; a key another slot has is refused, saying which.
/// </summary>
public class SkillInputCaptureTests
{
    private const int Esc = 0x01, Q = 0x10, Key2 = 0x03;

    [Fact]
    public void Pressing_a_key_while_choosing_saves_it()
    {
        int[] keys = [.. SkillKeyCodes.Defaults];

        Assert.Equal((CaptureResult.Saved, 0), SkillKeyCapture.Capture(keys, 1, Q));
        Assert.Equal([Q, 0x03, 0x04, 0x05], keys);
    }

    [Fact]
    public void Esc_keeps_the_old_key()
    {
        int[] keys = [.. SkillKeyCodes.Defaults];

        Assert.Equal((CaptureResult.Cancelled, 0), SkillKeyCapture.Capture(keys, 3, Esc));
        Assert.Equal(SkillKeyCodes.Defaults, keys);
    }

    [Fact]
    public void A_key_another_slot_uses_is_refused_and_says_which()
    {
        int[] keys = [.. SkillKeyCodes.Defaults];

        Assert.Equal((CaptureResult.Taken, 2), SkillKeyCapture.Capture(keys, 1, Key2));
        Assert.Equal(SkillKeyCodes.Defaults, keys);
    }

    [Fact]
    public void Choosing_the_same_key_again_is_fine()
    {
        int[] keys = [.. SkillKeyCodes.Defaults];

        Assert.Equal((CaptureResult.Saved, 0), SkillKeyCapture.Capture(keys, 2, Key2));
        Assert.Equal(SkillKeyCodes.Defaults, keys);
    }

    [Fact]
    public void A_key_message_without_a_physical_key_is_not_taken()
    {
        int[] keys = [.. SkillKeyCodes.Defaults];

        Assert.Equal((CaptureResult.Ignored, 0), SkillKeyCapture.Capture(keys, 1, 0));
        Assert.Equal(SkillKeyCodes.Defaults, keys);
    }

    [Fact]
    public void A_key_message_says_its_physical_key()
    {
        Assert.Equal(0x02, SkillKeyCodes.FromKeyMessage(0x00020001));                // "1", repeat count 1
        Assert.Equal(0x48 | SkillKeyCodes.Extended, SkillKeyCodes.FromKeyMessage(0x01480001)); // the up arrow (E0 48)
    }

    /// <summary>Every refusal and state the card can show has its sentence in zh-CN.xaml.</summary>
    [Fact]
    public void Every_refusal_has_a_sentence()
    {
        string xaml = File.ReadAllText(Path.Combine(AppFolder(), "i18n", "zh-CN.xaml"));
        string? Value(string key) => Regex.Match(xaml, $@"x:Key=""{Regex.Escape(key)}"">([^<]+)<") is { Success: true } m ? m.Groups[1].Value : null;

        foreach (string key in new[] { "Menu.SkillInput.Taken", "Menu.SkillInput.Failed", "Menu.SkillInput.NeedsRotation", "Menu.SkillInput.UnnamedKey", "Menu.SkillInput.Waiting" })
            Assert.False(string.IsNullOrWhiteSpace(Value(key)), $"{key} has no sentence");
        Assert.Contains("{0}", Value("Menu.SkillInput.Taken"));
        Assert.Contains("{0}", Value("Menu.SkillInput.Slot"));
    }

    /// <summary>A key another program holds is named, with its slot, in a plain sentence; the slots read say nothing.</summary>
    [Fact]
    public void A_key_another_program_holds_is_named_with_its_slot()
    {
        string text = OverlayWindow.UnreadKeysText(
            [new UnreadKey(1, UnreadKey.HeldElsewhere), new UnreadKey(3, 5)], SkillKeyCodes.Defaults, code => $"{code - 1}", ZhCn);

        Assert.Equal(
            "按键 1 已被其他程序占用，无法读取 1 号位战技。请关闭占用它的程序，或在下面为 1 号位换一个键。其他号位的战技仍会读取。\n" +
            "按键 3 无法读取（Windows 拒绝），3 号位战技不会被标出。请在下面为 3 号位换一个键。其他号位的战技仍会读取。",
            text);
        Assert.Equal("", OverlayWindow.UnreadKeysText([], SkillKeyCodes.Defaults, _ => "1", ZhCn));
    }

    private static string ZhCn(string key)
    {
        string xaml = File.ReadAllText(Path.Combine(AppFolder(), "i18n", "zh-CN.xaml"));
        var match = Regex.Match(xaml, $@"x:Key=""{Regex.Escape(key)}"">([^<]+)<");
        return match.Success ? match.Groups[1].Value : throw new KeyNotFoundException(key);
    }

    private static string AppFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return Path.Combine(dir.FullName, "src", "EndHud");
        throw new DirectoryNotFoundException("EndHud.sln not found");
    }
}

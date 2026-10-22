using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace EndHud.Tests;

/// <summary>Every UI string the code and XAML use exists in i18n/zh-CN.xaml (none shows as a raw key).</summary>
public partial class I18nTests
{
    private static string AppFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return Path.Combine(dir.FullName, "src", "EndHud");
        throw new DirectoryNotFoundException("EndHud.sln not found");
    }

    [Fact]
    public void Every_string_used_is_translated()
    {
        string app = AppFolder();
        var defined = KeyDefinition().Matches(File.ReadAllText(Path.Combine(app, "i18n", "zh-CN.xaml"))).Select(m => m.Groups[1].Value).ToHashSet();

        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            foreach (Match m in CodeKey().Matches(File.ReadAllText(file))) used.Add(m.Groups[1].Value);
        foreach (string file in Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains("i18n")))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file))) used.Add(m.Groups[1].Value);
        // Keys built at run time.
        foreach (var action in Enum.GetValues<Vision.Rotations.StepAction>()) used.Add($"Rotation.Action.{action}");
        foreach (string error in new[] { "NotARotation", "DamagedShareCode", "NewerFormat" })
        {
            used.Add($"Editor.ReadError.{error}");
            used.Add($"Editor.ImportError.{error}");
        }
        foreach (string kind in new[] { "SkillPoints", "Combo", "Ultimate", "Counter", "BossStatus", "Controlling", "PhaseShown", "Group" }) used.Add($"Editor.Condition.{kind}");
        used.UnionWith(["Editor.Group.All", "Editor.Group.Any", "Editor.PhaseItem", "Editor.PhaseItemGated"]);
        foreach (var compare in Enum.GetValues<Vision.Rotations.Comparison>()) used.Add($"Editor.Compare.{compare}");

        Assert.NotEmpty(used);
        Assert.Equal([], used.Where(k => !defined.Contains(k)).ToArray());
    }

    /// <summary>
    /// Every entry loads as the app loads it. A string of only spaces can't (WPF builds an empty
    /// sys:String with no constructor): the inn's setup panel crashed the app on the first one it
    /// drew, and the key test above, which reads the file as text, couldn't see it.
    /// </summary>
    [Fact]
    public void Every_string_loads()
    {
        var failed = new List<string>();
        var thread = new Thread(() =>
        {
            // The compiled dictionary, as the app has it: reading the .xaml as text keeps the
            // whitespace that compiling drops, and loads fine.
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var dictionary = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(
                new Uri("/EndHud;component/i18n/zh-CN.xaml", UriKind.Relative));
            foreach (object key in dictionary.Keys.Cast<object>().ToList())
            {
                try { _ = dictionary[key]; }
                catch (Exception ex) { failed.Add($"{key}: {ex.GetType().Name}"); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Empty(failed);
    }

    /// <summary>
    /// Owner, live 2026-10-20: "do not display '还没看到厨房/菜单…', half of the time it is wrong and it is disturbing", and
    /// the "厨房都排满了…" and "没有库存了，做不了…" lines: "do not display these either". No string says any of it.
    /// </summary>
    [Fact]
    public void No_string_says_what_has_not_been_seen_a_full_kitchen_or_no_stock()
    {
        string xaml = File.ReadAllText(Path.Combine(AppFolder(), "i18n", "zh-CN.xaml"));

        string[] banned = ["还没看到", "还不知道", "看不清", "都排满", "没有库存", "做不了"];
        Assert.Equal([], xaml.Split('\n').Where(l => banned.Any(l.Contains)).Select(l => l.Trim()).ToArray());
    }

    [GeneratedRegex(@"x:Key=""([^""]+)""")]
    private static partial Regex KeyDefinition();

    [GeneratedRegex(@"Loc\.(?:Get|Format)\(""([^""]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{DynamicResource ([A-Za-z0-9_.]+)\}")]
    private static partial Regex XamlKey();
}

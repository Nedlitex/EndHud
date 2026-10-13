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
        foreach (string action in new[] { "BattleSkill", "Combo", "Ultimate" }) used.Add($"Rotation.Action.{action}");
        foreach (string error in new[] { "NotARotation", "DamagedShareCode", "NewerFormat" })
        {
            used.Add($"Editor.ReadError.{error}");
            used.Add($"Editor.ImportError.{error}");
        }
        foreach (string kind in new[] { "SkillPoints", "Combo", "Ultimate", "Counter", "BossStatus", "Controlling", "Group" }) used.Add($"Editor.Condition.{kind}");
        used.UnionWith(["Editor.Group.All", "Editor.Group.Any", "Editor.PhaseItem", "Editor.PhaseItemGated"]);
        foreach (string compare in new[] { "AtLeast", "AtMost", "Exactly" }) used.Add($"Editor.Compare.{compare}");

        Assert.NotEmpty(used);
        Assert.Equal([], used.Where(k => !defined.Contains(k)).ToArray());
    }

    [GeneratedRegex(@"x:Key=""([^""]+)""")]
    private static partial Regex KeyDefinition();

    [GeneratedRegex(@"Loc\.(?:Get|Format)\(""([^""]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{DynamicResource ([A-Za-z0-9_.]+)\}")]
    private static partial Regex XamlKey();
}

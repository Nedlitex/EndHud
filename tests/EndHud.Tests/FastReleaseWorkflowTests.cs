using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Tests;

/// <summary>
/// The fast release by hand (.github/workflows/fast-release.yml, 77134b7): a release that skips the tests, so the little it
/// does check must hold. Read without the network: the file parses as YAML (the subset GitHub's workflows use here), it is
/// started by hand only (workflow_dispatch) with a required version, and its version step, run with PowerShell as the
/// runner would, takes "0.6.3" and "v0.6.3" and refuses anything else before anything is built or tagged.
/// </summary>
[Collection(ProcessStartCollection.Name)]
public class FastReleaseWorkflowTests(ITestOutputHelper output)
{
    private static string WorkflowPath => Path.Combine(InnTestKit.RepositoryRoot(), ".github", "workflows", "fast-release.yml");

    private static Dictionary<string, object> Workflow() => Assert.IsType<Dictionary<string, object>>(MiniYaml.Parse(File.ReadAllText(WorkflowPath)));

    private static Dictionary<string, object> Map(object? node, string what) => node as Dictionary<string, object> ?? throw new Xunit.Sdk.XunitException($"{what} is not a mapping");

    private static List<object> Seq(object? node, string what) => node as List<object> ?? throw new Xunit.Sdk.XunitException($"{what} is not a list");

    [Fact]
    public void It_parses_and_is_started_by_hand_only_with_a_required_version()
    {
        var workflow = Workflow();
        var on = Map(workflow["on"], "on");
        Assert.Equal(["workflow_dispatch"], on.Keys.ToArray());
        var version = Map(Map(Map(on["workflow_dispatch"], "workflow_dispatch")["inputs"], "inputs")["version"], "the version input");
        Assert.Equal("true", version["required"]);
        Assert.Equal("string", version["type"]);

        var jobs = Map(workflow["jobs"], "jobs");
        Assert.Equal(["build", "release"], jobs.Keys.ToArray());
        var release = Map(jobs["release"], "release");
        Assert.Equal("build", release["needs"]);
        Assert.Equal("write", Map(release["permissions"], "release's permissions")["contents"]);
        // The rest of the workflow can only read.
        Assert.Equal("read", Map(workflow["permissions"], "permissions")["contents"]);

        // The version is checked before anything is built, and the build and the release use the checked one.
        var steps = Seq(Map(jobs["build"], "build")["steps"], "build's steps").Select(s => Map(s, "a step")).ToList();
        int check = steps.FindIndex(s => s.GetValueOrDefault("id") as string == "version");
        int publish = steps.FindIndex(s => s.GetValueOrDefault("name") as string == "Publish EndHud.exe");
        Assert.True(check >= 0 && publish > check, "the version step comes before the build");
        Assert.Contains("-p:Version=${{ env.VERSION }}", (string)steps[publish]["run"]);
        Assert.Contains("v${{ needs.build.outputs.version }}", (string)Map(Seq(release["steps"], "release's steps")[1], "create step")["run"]);
    }

    [Fact]
    public void The_typed_version_reaches_the_script_only_through_the_environment()
    {
        var step = VersionStep();
        Assert.Equal("${{ inputs.version }}", Map(step["env"], "the version step's env")["VERSION_INPUT"]);
        Assert.DoesNotContain("inputs.version", (string)step["run"]);
    }

    /// <summary>The version step's own script, run as the runner runs it (pwsh, else Windows PowerShell), with GITHUB_ENV and GITHUB_OUTPUT files.</summary>
    [Theory]
    [InlineData("0.6.3", "0.6.3")]
    [InlineData("v0.6.3", "0.6.3")]
    [InlineData(" 1.10.0 ", "1.10.0")]
    [InlineData("0.6", null)]
    [InlineData("0.6.3-beta", null)]
    [InlineData("0.0.1", null)]
    [InlineData("0.0.0", null)]
    [InlineData("", null)]
    [InlineData("1.2.3\"; exit 0; \"", null)]
    public void The_version_step_takes_three_numbers_above_0_0_1_and_refuses_the_rest(string typed, string? accepted)
    {
        string folder = Path.Combine(Path.GetTempPath(), "endhud-fast-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string script = Path.Combine(folder, "version.ps1"), env = Path.Combine(folder, "env"), outputs = Path.Combine(folder, "output");
            File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\n" + (string)VersionStep()["run"]);
            File.WriteAllText(env, "");
            File.WriteAllText(outputs, "");
            var start = new ProcessStartInfo(PowerShell(), $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.Environment["VERSION_INPUT"] = typed;
            start.Environment["GITHUB_ENV"] = env;
            start.Environment["GITHUB_OUTPUT"] = outputs;
            using var process = Process.Start(start)!;
            string said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "PowerShell didn't finish");
            output.WriteLine($"\"{typed}\": exit {process.ExitCode}; {said.Trim()}");

            if (accepted is null)
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.DoesNotContain("VERSION=", File.ReadAllText(env));
            }
            else
            {
                Assert.Equal(0, process.ExitCode);
                Assert.Contains($"VERSION={accepted}", File.ReadAllText(env));
                Assert.Contains($"version={accepted}", File.ReadAllText(outputs));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Dictionary<string, object> VersionStep() =>
        Seq(Map(Map(Workflow()["jobs"], "jobs")["build"], "build")["steps"], "steps").Select(s => Map(s, "a step")).Single(s => s.GetValueOrDefault("id") as string == "version");

    /// <summary>pwsh where it's installed (the runners have it), else Windows PowerShell.</summary>
    private static string PowerShell()
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (folder.Length > 0 && File.Exists(Path.Combine(folder, "pwsh.exe"))) return Path.Combine(folder, "pwsh.exe");
        return "powershell.exe";
    }
}

/// <summary>
/// The YAML GitHub's workflows use here, read into mappings (key order kept), lists and strings: block mappings and lists by
/// indentation, "|" and ">" block text, quoted and plain values, one-line lists of them ([main]), comments. Anything else (tabs, flow collections, a key twice,
/// a line that is none of these) is an error, so a broken workflow fails the test instead of passing unread.
/// </summary>
internal static class MiniYaml
{
    public static object Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select((l, i) => (Number: i + 1, Text: l))
            .ToList();
        int at = 0;
        Skip(lines, ref at);
        var root = Block(lines, ref at, Indent(lines[at].Text));
        Skip(lines, ref at);
        if (at < lines.Count) throw new FormatException($"line {lines[at].Number}: unexpected \"{lines[at].Text.Trim()}\"");
        return root;
    }

    private static int Indent(string line)
    {
        if (line.TakeWhile(c => c is ' ' or '\t').Contains('\t')) throw new FormatException("a tab in the indentation");
        return line.Length - line.TrimStart(' ').Length;
    }

    private static bool Blank(string line) => line.Trim().Length == 0 || line.TrimStart().StartsWith('#');

    private static void Skip(List<(int Number, string Text)> lines, ref int at)
    {
        while (at < lines.Count && Blank(lines[at].Text)) at++;
    }

    private static object Block(List<(int Number, string Text)> lines, ref int at, int indent) =>
        lines[at].Text.TrimStart().StartsWith("- ", StringComparison.Ordinal) || lines[at].Text.Trim() == "-" ? Sequence(lines, ref at, indent) : Mapping(lines, ref at, indent);

    private static List<object> Sequence(List<(int Number, string Text)> lines, ref int at, int indent)
    {
        var list = new List<object>();
        while (at < lines.Count)
        {
            Skip(lines, ref at);
            if (at >= lines.Count || Indent(lines[at].Text) < indent) break;
            var (number, line) = lines[at];
            if (Indent(line) != indent || !line.TrimStart().StartsWith('-')) throw new FormatException($"line {number}: expected a list item at column {indent}");
            string rest = line.TrimStart()[1..].TrimStart();
            if (rest.Length == 0)
            {
                at++;
                Skip(lines, ref at);
                list.Add(Block(lines, ref at, Indent(lines[at].Text)));
                continue;
            }
            // "- key: value" opens a mapping whose keys stand where "key" does.
            lines[at] = (number, new string(' ', indent + 2) + rest);
            list.Add(Value(rest) is { } scalar && !IsKey(rest) ? Advance(ref at, scalar) : Mapping(lines, ref at, indent + 2));
        }
        return list;
    }

    private static object Advance(ref int at, object value)
    {
        at++;
        return value;
    }

    private static bool IsKey(string text) => KeyEnd(text) > 0;

    /// <summary>Where "key:" ends in <paramref name="text"/> (the colon followed by a space or the end); -1 for no key.</summary>
    private static int KeyEnd(string text)
    {
        if (text.StartsWith('"') || text.StartsWith('\'')) return -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == ':' && (i + 1 == text.Length || text[i + 1] == ' ')) return i;
        return -1;
    }

    private static Dictionary<string, object> Mapping(List<(int Number, string Text)> lines, ref int at, int indent)
    {
        var map = new Dictionary<string, object>();
        while (at < lines.Count)
        {
            Skip(lines, ref at);
            if (at >= lines.Count || Indent(lines[at].Text) < indent) break;
            var (number, line) = lines[at];
            if (Indent(line) != indent) throw new FormatException($"line {number}: indented {Indent(line)}, expected {indent}");
            string body = line.Trim();
            int colon = KeyEnd(body);
            if (colon <= 0) throw new FormatException($"line {number}: not \"key: value\": \"{body}\"");
            string key = body[..colon].Trim(), rest = StripComment(body[(colon + 1)..]).Trim();
            if (!map.TryAdd(key, null!)) throw new FormatException($"line {number}: \"{key}\" twice");
            at++;
            if (rest is "|" or ">" or "|-" or ">-")
            {
                map[key] = Text(lines, ref at, indent, folded: rest.StartsWith('>'));
                continue;
            }
            if (rest.Length > 0)
            {
                map[key] = (object?)FlowList(rest) ?? Value(rest) ?? throw new FormatException($"line {number}: \"{rest}\" isn't a value this reader knows");
                continue;
            }
            Skip(lines, ref at);
            if (at >= lines.Count || Indent(lines[at].Text) <= indent && !lines[at].Text.TrimStart().StartsWith('-')) map[key] = "";
            else map[key] = Block(lines, ref at, Indent(lines[at].Text));
        }
        return map;
    }

    /// <summary>A block text's lines (more indented than its key), joined by newlines ("|") or spaces (">").</summary>
    private static string Text(List<(int Number, string Text)> lines, ref int at, int keyIndent, bool folded)
    {
        var body = new List<string>();
        int? indent = null;
        while (at < lines.Count && (lines[at].Text.Trim().Length == 0 || Indent(lines[at].Text) > keyIndent))
        {
            string line = lines[at].Text;
            if (line.Trim().Length > 0) indent ??= Indent(line);
            body.Add(line.Trim().Length == 0 ? "" : line[Math.Min(indent ?? 0, line.Length)..]);
            at++;
        }
        while (body.Count > 0 && body[^1].Length == 0) body.RemoveAt(body.Count - 1);
        return folded ? string.Join(" ", body.Select(l => l.Trim())) : string.Join("\n", body) + "\n";
    }

    private static string StripComment(string value)
    {
        bool single = false, @double = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\'' && !@double) single = !single;
            else if (value[i] == '"' && !single) @double = !@double;
            else if (value[i] == '#' && !single && !@double && (i == 0 || value[i - 1] == ' ')) return value[..i];
        }
        return value;
    }

    /// <summary>A one-line list of scalars, <c>[main]</c> or <c>["inn/**", x]</c>; null for anything else (nested or unclosed).</summary>
    private static List<object>? FlowList(string text)
    {
        text = StripComment(text).Trim();
        if (!text.StartsWith('[') || !text.EndsWith(']') || text.Count(c => c is '[' or ']' or '{' or '}') != 2) return null;
        string inside = text[1..^1].Trim();
        if (inside.Length == 0) return [];
        var items = inside.Split(',').Select(i => Value(i.Trim())).ToList();
        return items.Any(i => i is null or "") ? null : items.Select(i => (object)i!).ToList();
    }

    /// <summary>A plain or quoted scalar; null for what this reader doesn't take (a flow collection, an unclosed quote).</summary>
    private static string? Value(string text)
    {
        text = StripComment(text).Trim();
        if (text.StartsWith('[') || text.StartsWith('{')) return null;
        if (text.StartsWith('\'')) return text.Length >= 2 && text.EndsWith('\'') ? text[1..^1].Replace("''", "'") : null;
        if (text.StartsWith('"')) return text.Length >= 2 && text.EndsWith('"') ? text[1..^1] : null;
        return text;
    }
}

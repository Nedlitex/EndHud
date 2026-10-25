using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The build pipeline takes nothing it hasn't pinned and holds no more rights than each job needs
/// (.github/workflows, read without the network):
/// <list type="bullet">
/// <item>every action is pinned to a full commit, with its tag as a comment (a moved tag can't change what runs);</item>
/// <item>every workflow can only read by default; writing is granted per job, only to the jobs that publish;</item>
/// <item>NuGet packages are pinned by content hash (packages.lock.json) and restored in locked mode on GitHub;</item>
/// <item>the weekly operator package, rebuilt from a wiki anyone can edit, is proposed as a pull request, never
/// pushed to main (players' EndHud takes whatever is on main), and the job reading the wiki holds no token.</item>
/// </list>
/// </summary>
public partial class WorkflowHardeningTests
{
    private static string Root => InnTestKit.RepositoryRoot();

    private static IEnumerable<string> WorkflowFiles() => Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml").Order();

    private static Dictionary<string, object> Map(object? node, string what) =>
        node as Dictionary<string, object> ?? throw new Xunit.Sdk.XunitException($"{what} is not a mapping");

    private static Dictionary<string, object> Workflow(string file) => Map(MiniYaml.Parse(File.ReadAllText(file)), file);

    [Fact]
    public void Every_action_is_pinned_to_a_full_commit_with_its_tag_noted()
    {
        var uses = WorkflowFiles().SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(l => l.Line.StartsWith("uses:") || l.Line.StartsWith("- uses:"))
            .ToList();

        Assert.True(uses.Count >= 15, $"{uses.Count} uses: lines found");
        foreach (var (file, line) in uses)
        {
            string target = line[(line.IndexOf("uses:", StringComparison.Ordinal) + 5)..].Trim();
            if (target.StartsWith("./")) continue; // this repository's own workflow
            Assert.True(PinnedAction().IsMatch(target), $"{file}: \"{target}\" is not pinned to a commit SHA with its tag as a comment");
        }
    }

    [Fact]
    public void Workflows_read_by_default_and_only_the_publishing_jobs_write()
    {
        var allowedToWrite = new Dictionary<string, string[]>
        {
            ["release.yml/release"] = ["contents"],
            ["fast-release.yml/release"] = ["contents"],
            ["operator-package.yml/propose"] = ["contents", "pull-requests"],
        };
        foreach (string file in WorkflowFiles())
        {
            string name = Path.GetFileName(file);
            var workflow = Workflow(file);
            Assert.True(workflow.ContainsKey("permissions"), $"{name}: no top-level permissions (the repository default would apply)");
            Assert.All(Map(workflow["permissions"], $"{name} permissions").Values, v => Assert.Equal("read", v));

            foreach (var (job, body) in Map(workflow["jobs"], $"{name} jobs"))
            {
                var writes = Map(body, job).TryGetValue("permissions", out var p)
                    ? Map(p, $"{name}/{job} permissions").Where(kv => (string)kv.Value != "read" && (string)kv.Value != "none").Select(kv => kv.Key).Order().ToArray()
                    : [];
                Assert.Equal(allowedToWrite.GetValueOrDefault($"{name}/{job}", []).Order().ToArray(), writes);
            }
        }
    }

    [Fact]
    public void The_weekly_operator_package_is_proposed_as_a_pull_request_never_pushed_to_main()
    {
        string file = Path.Combine(Root, ".github", "workflows", "operator-package.yml");
        var jobs = Map(Workflow(file)["jobs"], "jobs");

        // The job that reads the wiki keeps no token in its checkout and can only read.
        var build = Map(jobs["build"], "build");
        Assert.Equal("read", Map(build["permissions"], "build permissions")["contents"]);
        var buildSteps = ((List<object>)build["steps"]).Select(s => Map(s, "step")).ToList();
        var checkout = buildSteps.Single(s => (s.GetValueOrDefault("uses") as string ?? "").StartsWith("actions/checkout@"));
        Assert.Equal("false", Map(checkout["with"], "checkout with")["persist-credentials"]);
        Assert.DoesNotContain(buildSteps, s => (s.GetValueOrDefault("run") as string ?? "").Contains("git push"));

        // The job that writes pushes its own branch and opens a pull request.
        var propose = Map(jobs["propose"], "propose");
        string branch = (string)Map(propose["env"], "propose env")["BRANCH"];
        Assert.NotEqual("main", branch);
        string script = string.Join("\n", ((List<object>)propose["steps"]).Select(s => Map(s, "step").GetValueOrDefault("run") as string ?? ""));
        Assert.Contains("git push --force origin \"$BRANCH\"", script);
        Assert.Contains("gh pr create --head \"$BRANCH\" --base main", script);
        Assert.Single(PushCommand().Matches(script));
    }

    [Fact]
    public void NuGet_packages_are_locked_and_restored_in_locked_mode_on_GitHub()
    {
        var props = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        Assert.Equal("true", props.Descendants("RestorePackagesWithLockFile").Single().Value);
        var locked = props.Descendants("RestoreLockedMode").Single();
        Assert.Equal("true", locked.Value);
        Assert.Contains("'$(CI)' == 'true'", (string?)locked.Attribute("Condition"));

        var projects = ProjectLine().Matches(File.ReadAllText(Path.Combine(Root, "EndHud.sln"))).Select(m => m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)).ToList();
        Assert.True(projects.Count >= 5, $"{projects.Count} projects");
        foreach (string project in projects)
            Assert.True(File.Exists(Path.Combine(Root, Path.GetDirectoryName(project)!, "packages.lock.json")), $"{project}: no packages.lock.json");
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("fast-release.yml")]
    public void A_release_checks_every_package_against_the_lock_files_before_it_publishes(string file)
    {
        var build = Map(Map(Workflow(Path.Combine(Root, ".github", "workflows", file))["jobs"], "jobs")["build"], "build");
        var steps = ((List<object>)build["steps"]).Select(s => Map(s, "step")).ToList();
        int restore = steps.FindIndex(s => (s.GetValueOrDefault("run") as string ?? "").Trim() == "dotnet restore EndHud.sln --locked-mode");
        int publish = steps.FindIndex(s => (s.GetValueOrDefault("run") as string ?? "").Contains("dotnet publish"));
        Assert.True(restore >= 0 && publish > restore, $"{file}: restore step {restore}, publish step {publish}");
    }

    /// <summary>owner/repo[/path]@40 hex digits # v1[.2[.3]]</summary>
    [GeneratedRegex(@"^[\w.-]+/[\w.-]+(/[\w./-]+)?@[0-9a-f]{40} # v\d+(\.\d+)*$")]
    private static partial Regex PinnedAction();

    [GeneratedRegex(@"git push")]
    private static partial Regex PushCommand();

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\) = ""[^""]+"", ""([^""]+\.csproj)""")]
    private static partial Regex ProjectLine();
}

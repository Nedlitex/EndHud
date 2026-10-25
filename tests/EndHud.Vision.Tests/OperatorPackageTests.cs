using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The operator library as a package (<see cref="OperatorPackage"/>): built into EndHud so a first
/// start needs no network, and updated from the repository's operators/ folder
/// (<see cref="OperatorPackageSync"/>).
/// </summary>
public sealed class OperatorPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private string Reference => Path.Combine(_root, "operators", "reference");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] RepositoryPackage() =>
        File.ReadAllBytes(Path.Combine(OperatorCache.RepositoryRoot(), "operators", OperatorPackage.FileName));

    /// <summary>A copy of <paramref name="zip"/> dated <paramref name="at"/>, optionally without some operators, or with an extra file.</summary>
    private byte[] Variant(byte[] zip, DateTime at, int dropOperators = 0, (string Path, byte[] Bytes)? extra = null)
    {
        string folder = Path.Combine(_root, "variant-" + Guid.NewGuid().ToString("N"));
        OperatorPackage.Install(folder, zip);
        if (dropOperators > 0)
        {
            string manifest = Path.Combine(folder, "operators.json");
            var roster = JsonNode.Parse(File.ReadAllText(manifest))!.AsArray();
            for (int i = 0; i < dropOperators; i++) roster.RemoveAt(roster.Count - 1);
            File.WriteAllText(manifest, roster.ToJsonString());
        }
        byte[] built = OperatorPackage.Build(folder, at);
        if (extra is not { } file) return built;
        using var buffer = new MemoryStream();
        buffer.Write(built);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        using (var stream = archive.CreateEntry(file.Path).Open()) stream.Write(file.Bytes);
        return buffer.ToArray();
    }

    [Fact]
    public void EndHud_carries_the_repositorys_operator_package()
    {
        // The roster, avatars, skill icons and statuses (game art, from the wiki) are built in, so a
        // first start needs no network; besides them, the recognition model and portrait references.
        Assert.Equal(["ccip-features-int8.onnx", "ccip-metrics.onnx", OperatorPackage.FileName, PortraitReferences.ResourceName],
            typeof(OperatorLibrary).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal));
        Assert.Equal(RepositoryPackage(), OperatorPackage.BuiltIn());
        Assert.True(OperatorPackage.ReadInfo(RepositoryPackage())!.Operators >= 30);
    }

    [Fact]
    public async Task A_first_start_installs_the_built_in_package_and_every_operator_loads()
    {
        Assert.NotNull(OperatorPackage.InstallIfNewer(Reference, OperatorPackage.BuiltIn()!));

        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "operators"));
        Assert.Equal(OperatorPackage.ReadInfo(RepositoryPackage())!.Operators, library.Operators.Count);
        Assert.All(library.Operators, op => Assert.NotNull(library.FindAvatar(op.Id)));
        Assert.NotEmpty(library.Statuses);
    }

    /// <summary>
    /// Skill icons: the ultimate's is the badge on an operator's gauge, the combo's is on the rotation
    /// strip. Si's were missing from the package of 2026-10-14 (the wiki added them days after her
    /// page) and her gauge had no badge. An operator whose wiki page has none yet (Argent Flow, on
    /// 2026-10-17) has none here until the weekly package picks them up; one with some has all three.
    /// </summary>
    [Fact]
    public async Task Operators_have_their_skill_icons()
    {
        Assert.NotNull(OperatorPackage.InstallIfNewer(Reference, OperatorPackage.BuiltIn()!));

        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "operators"));
        var kinds = new[] { SkillKind.Ultimate, SkillKind.Combo, SkillKind.BattleSkill };
        Assert.All(kinds, kind => Assert.Contains(library.Find("Si")!.Skills, s => s.Kind == kind));
        Assert.All(library.Operators.Where(op => op.Skills.Count > 0), op =>
            Assert.All(kinds, kind => Assert.True(op.Skills.Any(s => s.Kind == kind), $"{op.Id} has no {kind} icon")));
    }

    [Fact]
    public void The_same_content_builds_the_same_bytes_and_keeps_its_date()
    {
        OperatorPackage.Install(Reference, RepositoryPackage());

        var later = OperatorPackage.Build(Reference, DateTime.UtcNow.AddDays(30), previous: RepositoryPackage());

        Assert.Equal(RepositoryPackage(), later);
    }

    [Fact]
    public void Changed_content_gets_a_new_date()
    {
        var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var changed = Variant(RepositoryPackage(), now, dropOperators: 1);

        Assert.Equal(now, OperatorPackage.ReadInfo(changed)!.ContentAtUtc);
    }

    [Fact]
    public void Only_a_newer_package_replaces_the_installed_one_and_learned_portraits_stay()
    {
        var package = RepositoryPackage();
        var at = OperatorPackage.ReadInfo(package)!.ContentAtUtc;
        OperatorPackage.Install(Reference, package);
        string learned = Path.Combine(_root, "operators", "learned", "Arcane", "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(learned)!);
        File.WriteAllBytes(learned, [1, 2, 3]);

        Assert.Null(OperatorPackage.InstallIfNewer(Reference, Variant(package, at.AddDays(-1))));   // older: ignored
        Assert.Null(OperatorPackage.InstallIfNewer(Reference, package));                           // same: ignored
        Assert.NotNull(OperatorPackage.InstallIfNewer(Reference, Variant(package, at.AddDays(1)))); // newer: installed

        Assert.Equal(at.AddDays(1), OperatorPackage.Installed(Reference)!.ContentAtUtc);
        Assert.True(File.Exists(learned));
    }

    [Fact]
    public void A_wiki_download_from_before_packages_is_replaced_by_the_built_in_one()
    {
        OperatorPackage.Install(Reference, RepositoryPackage());
        File.Delete(Path.Combine(Reference, OperatorPackage.InfoName)); // as an older EndHud's wiki download

        Assert.NotNull(OperatorPackage.InstallIfNewer(Reference, RepositoryPackage()));
        Assert.NotNull(OperatorPackage.Installed(Reference));
    }

    [Fact]
    public void Broken_or_unsafe_packages_are_refused_and_the_current_copy_kept()
    {
        var package = RepositoryPackage();
        OperatorPackage.Install(Reference, package);
        var later = OperatorPackage.ReadInfo(package)!.ContentAtUtc.AddDays(1);

        Assert.Throws<InvalidDataException>(() => OperatorPackage.InstallIfNewer(Reference, [1, 2, 3, 4]));
        Assert.Throws<InvalidDataException>(() => OperatorPackage.InstallIfNewer(Reference,
            Variant(package, later, extra: ("../escaped.txt", Encoding.UTF8.GetBytes("x")))));
        Assert.Throws<InvalidDataException>(() => OperatorPackage.InstallIfNewer(Reference,
            Variant(package, later, dropOperators: OperatorPackage.ReadInfo(package)!.Operators / 2)));

        Assert.Equal(OperatorPackage.ReadInfo(package), OperatorPackage.Installed(Reference));
        Assert.False(File.Exists(Path.Combine(_root, "operators", "escaped.txt")));
    }

    // ---- From GitHub ----

    private const string Api = "https://api.example/repos/x/y/contents/operators?ref=main";

    private sealed class FakeGitHub(byte[]? package, HttpStatusCode listing = HttpStatusCode.OK, string? listedSha = null) : HttpMessageHandler
    {
        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsoluteUri == Api)
            {
                if (listing != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(listing));
                var entries = new JsonArray();
                if (package is not null)
                    entries.Add(new JsonObject
                    {
                        ["name"] = OperatorPackage.FileName, ["type"] = "file",
                        ["sha"] = listedSha ?? RotationSync.GitBlobSha(package), ["download_url"] = "https://raw.example/operators/" + OperatorPackage.FileName,
                    });
                entries.Add(new JsonObject { ["name"] = "README.md", ["type"] = "file", ["sha"] = "0", ["download_url"] = "https://raw.example/README.md" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(entries.ToJsonString()) });
            }
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package!) });
        }
    }

    private Task<OperatorPackageSyncResult?> SyncAsync(FakeGitHub github) =>
        OperatorPackageSync.SyncAsync(Reference, new HttpClient(github), Api, "EndHud-tests", _ => true);

    [Fact]
    public async Task A_newer_package_on_GitHub_is_installed_once()
    {
        var package = RepositoryPackage();
        OperatorPackage.Install(Reference, Variant(package, OperatorPackage.ReadInfo(package)!.ContentAtUtc.AddDays(-7), dropOperators: 2));
        var github = new FakeGitHub(package);

        var first = await SyncAsync(github);
        var second = await SyncAsync(github);

        Assert.True(first!.Installed);
        Assert.Equal(2, first.Added.Count);
        Assert.False(second!.Installed);
        Assert.Equal(1, github.Downloads); // unchanged listing: not downloaded again
    }

    [Fact]
    public async Task A_download_that_doesnt_match_its_listing_is_refused()
    {
        var package = RepositoryPackage();
        OperatorPackage.Install(Reference, Variant(package, OperatorPackage.ReadInfo(package)!.ContentAtUtc.AddDays(-7), dropOperators: 2));
        var before = OperatorPackage.Installed(Reference);

        await Assert.ThrowsAsync<InvalidDataException>(() => SyncAsync(new FakeGitHub(package, listedSha: new string('0', 40))));

        Assert.Equal(before, OperatorPackage.Installed(Reference));
    }

    [Fact]
    public async Task An_unreachable_or_empty_repository_changes_nothing()
    {
        OperatorPackage.Install(Reference, RepositoryPackage());
        var before = OperatorPackage.Installed(Reference);

        Assert.Null(await SyncAsync(new FakeGitHub(RepositoryPackage(), HttpStatusCode.NotFound)));
        Assert.False((await SyncAsync(new FakeGitHub(null)))!.Installed);

        Assert.Equal(before, OperatorPackage.Installed(Reference));
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>Published rotations: kept the same as the repository's rotations/ folder, without touching the player's own.</summary>
public sealed class RotationSyncTests : IDisposable
{
    private const string Api = "https://api.example/repos/x/y/contents/rotations?ref=main";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub _github = new();
    private RotationLibrary Library => new(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static Rotation Named(string id, string name) => RotationFormatTests.Guide() with { Id = id, Name = name };

    private Task<RotationSyncResult?> Sync() => RotationSync.SyncAsync(Library, _github.Client(), Api, "EndHud-tests");

    [Fact]
    public async Task New_rotations_are_downloaded_and_unchanged_ones_are_not_downloaded_again()
    {
        _github.Publish(Named("alpha", "甲"));
        _github.Publish(Named("beta", "乙"));

        var first = await Sync();
        Assert.Equal(["甲", "乙"], first!.Added);
        Assert.Equal(["alpha", "beta"], Library.Load().Rotations.Where(r => r.Published).Select(r => r.Rotation.Id).Order());

        _github.Downloads.Clear();
        var second = await Sync();
        Assert.False(second!.Changed);
        Assert.Empty(_github.Downloads); // only the listing was asked for
    }

    [Fact]
    public async Task A_corrected_rotation_replaces_the_local_copy()
    {
        _github.Publish(Named("alpha", "甲"));
        await Sync();

        _github.Publish(Named("alpha", "甲（修正）"));
        var result = await Sync();

        Assert.Equal(["甲（修正）"], result!.Updated);
        Assert.Equal("甲（修正）", Library.Load().Rotations.Single().Rotation.Name);
    }

    [Fact]
    public async Task A_rotation_removed_from_the_repository_is_removed()
    {
        _github.Publish(Named("alpha", "甲"));
        _github.Publish(Named("beta", "乙"));
        await Sync();

        _github.Unpublish("beta");
        var result = await Sync();

        Assert.Equal(["乙"], result!.Removed);
        Assert.Equal(["alpha"], Library.Load().Rotations.Select(r => r.Rotation.Id));
    }

    [Fact]
    public async Task The_players_own_rotations_are_never_touched()
    {
        Library.Own.Save(Named("mine", "我的"));
        Library.Own.Save(Named("alpha", "我改过的")); // even one with a published rotation's id
        _github.Publish(Named("alpha", "甲"));

        await Sync();
        _github.Unpublish("alpha");
        await Sync();

        Assert.Equal(["我改过的", "我的"], Library.Own.Load().Rotations.Select(r => r.Name).Order());
        Assert.All(Library.Load().Rotations, r => Assert.False(r.Published));
    }

    [Fact]
    public async Task An_own_rotation_with_a_published_id_is_shown_instead_of_the_published_one()
    {
        _github.Publish(Named("alpha", "甲"));
        await Sync();
        Library.Own.Save(Named("alpha", "我的甲"));

        var only = Assert.Single(Library.Load().Rotations);
        Assert.Equal("我的甲", only.Rotation.Name);
        Assert.False(only.Published);
    }

    [Fact]
    public async Task A_deleted_published_rotation_stays_deleted_until_shown_again()
    {
        _github.Publish(Named("alpha", "甲"));
        _github.Publish(Named("beta", "乙"));
        await Sync();

        Assert.True(Library.Delete("beta"));
        var result = await Sync();

        Assert.False(result!.Changed);
        Assert.Equal(["alpha"], Library.Load().Rotations.Select(r => r.Rotation.Id));
        Assert.True(Library.IsPublished("beta")); // an import of it still counts as published

        Library.ShowHidden();
        Assert.Equal(["乙"], (await Sync())!.Added);
        Assert.Equal(["alpha", "beta"], Library.Load().Rotations.Select(r => r.Rotation.Id).Order());
    }

    [Fact]
    public async Task Deleting_an_own_rotation_does_not_hide_anything()
    {
        Library.Own.Save(Named("mine", "我的"));

        Assert.True(Library.Delete("mine"));

        Assert.Empty(Library.Load().Rotations);
        Assert.Empty(Library.Hidden());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)] // but not a listing
    public async Task A_missing_or_strange_listing_changes_nothing(HttpStatusCode status)
    {
        _github.Publish(Named("alpha", "甲"));
        await Sync();

        _github.Listing = (status, status == HttpStatusCode.OK ? """{"message":"API rate limit exceeded"}""" : "");
        Assert.Null(await Sync());

        Assert.Equal(["alpha"], Library.Load().Rotations.Select(r => r.Rotation.Id));
    }

    [Fact]
    public async Task Unreachable_GitHub_throws_and_changes_nothing()
    {
        _github.Publish(Named("alpha", "甲"));
        await Sync();

        _github.Listing = (HttpStatusCode.ServiceUnavailable, "");
        await Assert.ThrowsAsync<HttpRequestException>(Sync);

        Assert.Single(Library.Load().Rotations);
    }

    [Fact]
    public async Task A_file_that_does_not_match_its_sha_or_its_name_is_refused_and_the_old_copy_kept()
    {
        _github.Publish(Named("alpha", "甲"));
        await Sync();

        _github.Publish(Named("alpha", "甲（新）"));
        _github.Tamper("alpha", Encoding.UTF8.GetBytes(RotationFormat.ToJson(Named("alpha", "被篡改"))));
        _github.Publish(Named("other-id", "乙"), file: "beta");

        var result = await Sync();

        Assert.Equal(2, result!.Failed);
        Assert.Equal("甲", Library.Load().Rotations.Single().Rotation.Name);
    }

    [Fact]
    public async Task Files_that_are_not_rotations_are_ignored()
    {
        _github.Publish(Named("alpha", "甲"));
        _github.AddRaw("README.md", "# Rotations");
        _github.AddRaw("Bad Name.json", "{}");

        var result = await Sync();

        Assert.Equal(1, result!.Total);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void Git_blob_ids_match_git()
    {
        // `printf 'hello\n' | git hash-object --stdin`
        Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", RotationSync.GitBlobSha(Encoding.ASCII.GetBytes("hello\n")));
    }

    [Fact]
    public async Task Every_rotation_published_in_the_repository_is_valid()
    {
        string folder = Path.Combine(OperatorCache.RepositoryRoot(), "rotations");
        var operators = await OperatorCache.LoadAsync(Path.Combine(_folder, "user"));
        var files = Directory.GetFiles(folder, "*.json");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            var rotation = RotationFormat.FromJson(File.ReadAllText(file));
            Assert.Equal(Path.GetFileNameWithoutExtension(file), rotation.Id);
            Assert.Matches("^[a-z0-9][a-z0-9-]*$", rotation.Id);
            Assert.Empty(rotation.Problems());
            var named = rotation.Squad.Concat(rotation.Phases.SelectMany(p => p.Steps.Select(s => s.OperatorId)));
            Assert.All(named, id => Assert.NotNull(operators.Find(id)));
        }
    }

    /// <summary>GitHub's contents API and raw downloads for one folder.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly SortedDictionary<string, (byte[] Served, string Sha)> _files = new(StringComparer.Ordinal);

        public (HttpStatusCode Status, string Body)? Listing { get; set; }
        public List<string> Downloads { get; } = [];

        public HttpClient Client() => new(this, disposeHandler: false);

        public void Publish(Rotation rotation, string? file = null) =>
            AddRaw((file ?? rotation.Id) + ".json", RotationFormat.ToJson(rotation));

        public void AddRaw(string name, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            _files[name] = (bytes, RotationSync.GitBlobSha(bytes));
        }

        public void Unpublish(string id) => _files.Remove(id + ".json");

        /// <summary>Serve different bytes than the listing's SHA says.</summary>
        public void Tamper(string id, byte[] served) => _files[id + ".json"] = (served, _files[id + ".json"].Sha);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            if (url == Api)
            {
                if (Listing is { } fixedAnswer) return Task.FromResult(new HttpResponseMessage(fixedAnswer.Status) { Content = new StringContent(fixedAnswer.Body) });
                var array = new JsonArray([.. _files.Select(f => (JsonNode)new JsonObject
                {
                    ["name"] = f.Key,
                    ["type"] = "file",
                    ["sha"] = f.Value.Sha,
                    ["download_url"] = $"https://raw.example/{f.Key}",
                })]);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(array.ToJsonString()) });
            }
            string name = Uri.UnescapeDataString(url["https://raw.example/".Length..]);
            Downloads.Add(name);
            return Task.FromResult(_files.TryGetValue(name, out var file)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Served) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

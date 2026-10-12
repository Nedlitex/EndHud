using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Downloading and updating the operator library (<see cref="OperatorReference"/>), against a
/// small stand-in for the wiki that answers the same API calls: new operators, changed and
/// unchanged images, a busy or broken wiki, and broken images.
/// </summary>
public sealed class OperatorDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private string Library => Path.Combine(_root, "reference");

    public OperatorDownloadTests() => WikiOperatorSource.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task First_download_builds_the_roster_with_avatars_skill_icons_and_names()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta");

        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(new WikiDownload(Operators: 2, Downloaded: 2 * 4, Reused: 0, Failed: 0), result);
        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "user"), Library);
        Assert.Equal(["Alpha", "Beta"], library.Operators.Select(o => o.Id));
        Assert.Equal("Alpha-cn", library.Find("Alpha")!.NameZh);
        Assert.Equal([SkillKind.BattleSkill, SkillKind.Combo, SkillKind.Ultimate], library.Find("Beta")!.Skills.Select(s => s.Kind).Order());
        Assert.NotNull(library.FindAvatar("Alpha"));
    }

    [Fact]
    public async Task A_new_operator_appears_and_unchanged_images_are_not_downloaded_again()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta");
        await OperatorReference.UpdateAsync(Library, wiki.Client());

        await wiki.AddOperatorAsync("Gamma");
        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(new WikiDownload(Operators: 3, Downloaded: 4, Reused: 2 * 4, Failed: 0), result);
        Assert.Equal(1, wiki.Downloads("Alpha_icon.png"));
        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "user"), Library);
        Assert.Contains("Gamma", library.Operators.Select(o => o.Id));
    }

    [Fact]
    public async Task A_changed_image_is_downloaded_again()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha");
        await OperatorReference.UpdateAsync(Library, wiki.Client());

        await wiki.ChangeImageAsync("Alpha_icon.png");
        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(2, wiki.Downloads("Alpha_icon.png"));
        Assert.Equal(wiki.Bytes("Alpha_icon.png"), File.ReadAllBytes(Path.Combine(Library, "icons", "Alpha.png")));
    }

    [Fact]
    public async Task An_image_that_fails_keeps_the_previous_copy_and_the_rest_still_updates()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta");
        await OperatorReference.UpdateAsync(Library, wiki.Client());
        byte[] before = File.ReadAllBytes(Path.Combine(Library, "icons", "Alpha.png"));

        await wiki.ChangeImageAsync("Alpha_icon.png");
        wiki.Broken.Add("Alpha_icon.png"); // the wiki errors on it every time
        await wiki.AddOperatorAsync("Gamma");
        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(1, result.Failed);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Library, "icons", "Alpha.png")));
        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "user"), Library);
        Assert.Equal(["Alpha", "Beta", "Gamma"], library.Operators.Select(o => o.Id));
    }

    [Fact]
    public async Task Without_a_previous_copy_an_operator_whose_avatar_fails_is_left_out_and_a_failed_skill_icon_is_skipped()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta");
        wiki.Broken.Add("Alpha_icon.png");
        wiki.NotImages.Add("Combo-Beta.png"); // an error page instead of a picture

        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(2, result.Failed);
        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "user"), Library);
        Assert.Equal(["Beta"], library.Operators.Select(o => o.Id));
        Assert.Equal([SkillKind.BattleSkill, SkillKind.Ultimate], library.Find("Beta")!.Skills.Select(s => s.Kind).Order());
    }

    [Fact]
    public async Task A_busy_wiki_is_retried()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha");
        wiki.BusyFor = 2; // the first two requests get "503 Service Unavailable"

        var result = await OperatorReference.UpdateAsync(Library, wiki.Client());

        Assert.Equal(1, result.Operators);
    }

    [Fact]
    public async Task A_failed_update_leaves_the_current_library_untouched()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta");
        await OperatorReference.UpdateAsync(Library, wiki.Client());
        byte[] manifest = File.ReadAllBytes(Path.Combine(Library, "operators.json"));

        wiki.Down = true;
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => OperatorReference.UpdateAsync(Library, wiki.Client()));

        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Library, "operators.json")));
        Assert.False(Directory.Exists(Library + ".new"));
        Assert.False(Directory.Exists(Library + ".old"));
    }

    [Fact]
    public async Task A_much_smaller_roster_does_not_replace_the_current_one()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha", "Beta", "Gamma", "Delta", "Epsilon");
        await OperatorReference.UpdateAsync(Library, wiki.Client());

        wiki.Listed = ["Alpha"]; // the wiki's page broke part-way: operators don't leave the game
        await Assert.ThrowsAsync<InvalidDataException>(() => OperatorReference.UpdateAsync(Library, wiki.Client()));

        var library = await OperatorLibrary.LoadAsync(Path.Combine(_root, "user"), Library);
        Assert.Equal(5, library.Operators.Count);
    }

    [Fact]
    public async Task An_interrupted_swap_is_undone()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha");
        await OperatorReference.UpdateAsync(Library, wiki.Client());
        Directory.Move(Library, Library + ".old"); // as if the process died between the two renames

        Assert.True(OperatorReference.Exists(Library));
        Assert.False(Directory.Exists(Library + ".old"));
    }

    [Fact]
    public async Task Ensure_downloads_when_missing_or_old_and_otherwise_leaves_it()
    {
        var wiki = await FakeWiki.CreateAsync("Alpha");
        Assert.NotNull(await OperatorReference.EnsureAsync(Library, TimeSpan.FromDays(7), wiki.Client()));
        Assert.Null(await OperatorReference.EnsureAsync(Library, TimeSpan.FromDays(7), wiki.Client()));

        File.SetLastWriteTimeUtc(Path.Combine(Library, "operators.json"), DateTime.UtcNow.AddDays(-8));
        Assert.NotNull(await OperatorReference.EnsureAsync(Library, TimeSpan.FromDays(7), wiki.Client()));
    }

    /// <summary>
    /// Answers the wiki API calls WikiOperatorSource makes, for a few made-up operators, each with an
    /// avatar and three skill icons, and serves their images.
    /// </summary>
    private sealed class FakeWiki : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _images = [];
        private readonly Dictionary<string, int> _versions = []; // which upload each file is
        private readonly Dictionary<string, int> _downloads = [];
        private int _variant;

        public List<string> Listed { get; set; } = [];
        public HashSet<string> Broken { get; } = [];
        public HashSet<string> NotImages { get; } = [];
        public int BusyFor { get; set; }
        public bool Down { get; set; }

        public static async Task<FakeWiki> CreateAsync(params string[] operators)
        {
            var wiki = new FakeWiki();
            foreach (string op in operators) await wiki.AddOperatorAsync(op);
            return wiki;
        }

        public HttpClient Client() => new(this, disposeHandler: false);

        public async Task AddOperatorAsync(string name)
        {
            Listed.Add(name);
            foreach (string file in FilesOf(name)) await UploadAsync(file);
        }

        public Task ChangeImageAsync(string file) => UploadAsync(file);

        private async Task UploadAsync(string file)
        {
            _versions[file] = ++_variant;
            _images[file] = await PngAsync(_variant);
        }

        // Like the real wiki: the SHA-1 of the original upload, which isn't the file it serves
        // (wiki.gg serves a re-compressed copy).
        private string Sha1Of(string file) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"original upload {file} #{_versions[file]}"))).ToLowerInvariant();

        public int Downloads(string file) => _downloads.GetValueOrDefault(file);

        public byte[] Bytes(string file) => _images[file];

        private static string[] FilesOf(string name) => [$"{name}_icon.png", $"Skill-{name}.png", $"Combo-{name}.png", $"Ult-{name}.png"];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Down) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            if (BusyFor > 0)
            {
                BusyFor--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var uri = request.RequestUri!;
            if (uri.Host == "files.test")
            {
                string file = uri.AbsolutePath.TrimStart('/');
                _downloads[file] = Downloads(file) + 1;
                if (Broken.Contains(file)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                byte[] body = NotImages.Contains(file) ? Encoding.UTF8.GetBytes("<html>error</html>") : _images[file];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }

            var q = HttpUtility.ParseQueryString(uri.Query);
            string[] titles = (q["titles"] ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            object json = (q["action"], q["prop"]) switch
            {
                ("parse", _) => new { parse = new { images = Listed.Select(n => $"{n}_icon.png").Append("Something_facility_icon.png") } },
                ("query", "revisions") => new { query = new { pages = titles.Select(t => new
                {
                    title = t,
                    revisions = new[] { new { slots = new { main = new { content = $"{{{{Operator\n|cnname = {t}-cn\n}}}}" } } } },
                }) } },
                ("query", "images") => new { query = new { pages = titles.Select(t => new
                {
                    title = t,
                    images = FilesOf(t).Skip(1).Select(f => new { title = "File:" + f }),
                }) } },
                ("query", "imageinfo") => new { query = new { pages = titles.Select(t => t["File:".Length..]).Select(f => new
                {
                    title = "File:" + f,
                    imageinfo = new[] { new { url = $"https://files.test/{f}", sha1 = Sha1Of(f) } },
                }) } },
                _ => throw new InvalidOperationException("unexpected wiki call: " + uri),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(json)) });
        }

        /// <summary>A small valid PNG, different for each <paramref name="variant"/>.</summary>
        private static async Task<byte[]> PngAsync(int variant)
        {
            var pixels = new byte[8 * 8 * 4];
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)variant, (byte)(variant * 7), 90, 255);
            string path = Path.Combine(Path.GetTempPath(), "endhud-tests", $"png-{Guid.NewGuid():N}.png");
            await ImageFile.SavePngAsync(new Frame(8, 8, pixels), path);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            File.Delete(path);
            return bytes;
        }
    }
}

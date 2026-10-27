using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Link kept on the gauge while it is there (owner, 2026-10-25: "look at 1:24 - 1:35, the [Link] buff is flashing and even
/// gone, but the actual buff is always there"). The reader's verdict per frame (<see cref="TeamBuffReader.Look"/>: shown,
/// clearly absent, or unclear) carried by <see cref="TeamBuffHold"/>: unclear keeps what was seen, and only clearly
/// absent readings end it. On the recording's own frames (Fixtures/buffs/link-pulse) and on made-up verdicts.
/// </summary>
public sealed class TeamBuffHoldTests : IDisposable
{
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    private async Task<TeamBuffReader> ReaderAsync() => new(await OperatorCache.LoadAsync(_userFolder));

    // The crops' place in the recording's 3838x2158 frames (Fixtures/buffs/link-pulse/README.md).
    private const int Width = 3838, Height = 2158, CropX = 1150, CropY = 1880;

    /// <summary>A crop pasted back at its spot into a black frame of the recording's size.</summary>
    private static async Task<Frame> PulseFrameAsync(string file)
    {
        var crop = await ImageFile.LoadAsync(file);
        var bgra = new byte[Width * Height * 4];
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        for (int y = 0; y < crop.Height; y++)
            Buffer.BlockCopy(crop.Bgra, y * crop.Width * 4, bgra, ((CropY + y) * Width + CropX) * 4, crop.Width * 4);
        return new Frame(Width, Height, bgra);
    }

    private static IEnumerable<string> Pulse(string folder) =>
        Directory.GetFiles(CombatFrameTests.Fixture(Path.Combine("buffs", "link-pulse", folder)), "*.jpg").Order(StringComparer.Ordinal);

    private static readonly IReadOnlyList<TeamBuff> LinkOne = [new TeamBuff("Link", 1)];

    /// <summary>
    /// The owner's stretch read four times a second, as the app reads: Link with one stack on every reading. Read one frame
    /// at a time it went missing on some of them (the pulse's dim end over the light floor, effects over it), which is the
    /// gauge flashing off.
    /// </summary>
    [Fact]
    public async Task Link_stays_on_every_reading_while_it_pulses_over_a_light_floor()
    {
        var reader = await ReaderAsync();
        var hold = new TeamBuffHold();
        var wrong = new List<string>();
        int missedAlone = 0;
        foreach (string file in Pulse("readings"))
        {
            var frame = await PulseFrameAsync(file);
            if (!reader.Read(frame, HpBarCounter.Drawn).SequenceEqual(LinkOne)) missedAlone++;
            var shown = hold.Track(reader.Look(frame, HpBarCounter.Drawn));
            if (!shown.SequenceEqual(LinkOne))
                wrong.Add($"{Path.GetFileNameWithoutExtension(file)}: {string.Join(",", shown.Select(b => $"{b.Id}x{b.Stacks?.ToString() ?? "?"}"))}");
        }
        Assert.True(wrong.Count == 0, $"Link not shown as one stack at: {string.Join("; ", wrong)}");
        // The fixtures still hold the trouble: one frame alone doesn't show it on some readings.
        Assert.True(missedAlone > 0, "every reading shows Link on its own: the fixtures no longer exercise the hold");
    }

    /// <summary>Every frame of the worst stretches: the icon is there in all of them, so none is "clearly absent".</summary>
    [Fact]
    public async Task No_frame_with_link_on_it_is_read_as_clearly_absent()
    {
        var reader = await ReaderAsync();
        var absent = new List<string>();
        foreach (string file in Pulse("frames"))
        {
            var look = Assert.Single(reader.Look(await PulseFrameAsync(file), HpBarCounter.Drawn));
            Assert.Equal("Link", look.Id);
            if (look.Sight == BuffSight.Absent) absent.Add(Path.GetFileNameWithoutExtension(file));
            if (look.Sight == BuffSight.Shown) Assert.Equal(1, look.Stacks);
        }
        Assert.True(absent.Count == 0, $"read as clearly absent: {string.Join(", ", absent)}");
    }

    /// <summary>
    /// The same stretch under the synthetic on-screen display (<see cref="SyntheticOsd"/>) at each position, with and
    /// without its backdrop: every frame's verdict is the clean one or unclear, never clearly absent for shown (or back),
    /// and the hold keeps Link on every reading. (A display moved onto the icon itself is not told apart: it reads as
    /// absent, as it did before the hold.)
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")] // sweeps synthetic on-screen displays: left out of the fast lane
    public async Task Under_an_on_screen_display_link_reads_the_same_or_unclear_and_stays()
    {
        var reader = await ReaderAsync();
        int px = SyntheticOsd.PixelFor(Height);
        var places = SyntheticOsd.Positions.Select(p => (Name: p.ToString(), At: SyntheticOsd.Place(p, Width, Height, px))).ToList();
        var variants = places.SelectMany(p => new[] { (p.Name, p.At, Background: true), (p.Name, p.At, Background: false) }).ToList();
        var holds = variants.Select(_ => new TeamBuffHold()).ToList();
        var wrong = new List<string>();
        foreach (string file in Pulse("readings"))
        {
            var frame = await PulseFrameAsync(file);
            var clean = Assert.Single(reader.Look(frame, HpBarCounter.Drawn));
            for (int v = 0; v < variants.Count; v++)
            {
                var (name, (x, y), background) = variants[v];
                var covered = reader.Look(SyntheticOsd.Draw(frame, x, y, px, background), HpBarCounter.Drawn);
                string where = $"{Path.GetFileNameWithoutExtension(file)} {name}{(background ? " bg" : "")}";
                var seen = Assert.Single(covered);
                if (seen.Sight != BuffSight.Unclear && seen != clean) wrong.Add($"{where}: {clean.Sight}:{clean.Stacks} read {seen.Sight}:{seen.Stacks}");
                if (!holds[v].Track(covered).SequenceEqual(LinkOne)) wrong.Add($"{where}: Link not kept");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>Frames of the owner's Laevatain fight without Link (Fixtures/buffs, labelled by eye) end a buff that was shown.</summary>
    [Theory]
    [InlineData("laevatain-40s.jpg", "laevatain-90s.jpg")]
    [InlineData("laevatain-90s.jpg", "laevatain-130s.jpg")]
    public async Task Frames_without_link_end_it(string first, string second)
    {
        var reader = await ReaderAsync();
        var hold = new TeamBuffHold();
        Assert.Equal(LinkOne, hold.Track(reader.Look(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", "laevatain-95s.jpg"))), HpBarCounter.Drawn)));
        hold.Track(reader.Look(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", first))), HpBarCounter.Drawn));
        Assert.Empty(hold.Track(reader.Look(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", second))), HpBarCounter.Drawn)));
    }

    private static TeamBuffSighting[] Shown(int? stacks) => [new("Link", BuffSight.Shown, stacks)];
    private static readonly TeamBuffSighting[] Unclear = [new("Link", BuffSight.Unclear)];
    private static readonly TeamBuffSighting[] Absent = [new("Link", BuffSight.Absent)];

    [Fact]
    public void Unclear_readings_keep_the_stacks_seen_then_say_the_count_is_unknown_never_gone()
    {
        var hold = new TeamBuffHold();
        Assert.Equal([new TeamBuff("Link", 2)], hold.Track(Shown(2)));
        for (int n = 0; n < TeamBuffHold.HoldReadings; n++) Assert.Equal([new TeamBuff("Link", 2)], hold.Track(Unclear));
        for (int n = 0; n < 20; n++) Assert.Equal([new TeamBuff("Link", null)], hold.Track(Unclear));
        Assert.Equal([new TeamBuff("Link", 3)], hold.Track(Shown(3)));
    }

    [Fact]
    public void One_clearly_absent_reading_doesnt_end_it_but_two_since_it_was_seen_do()
    {
        var hold = new TeamBuffHold();
        hold.Track(Shown(1));
        Assert.Equal(LinkOne, hold.Track(Absent));
        Assert.Equal(LinkOne, hold.Track(Shown(1)));          // seen again: the run starts over
        Assert.Equal(LinkOne, hold.Track(Absent));
        Assert.Equal(LinkOne, hold.Track(Unclear));           // unclear neither counts nor breaks the run
        Assert.Empty(hold.Track(Absent));
        Assert.Empty(hold.Track(Unclear));                    // gone stays gone while unclear: nothing made up
    }

    [Fact]
    public void A_buff_never_seen_isnt_made_up_from_unclear_readings()
    {
        var hold = new TeamBuffHold();
        for (int n = 0; n < 20; n++) Assert.Empty(hold.Track(Unclear));
    }

    [Fact]
    public void A_number_that_cant_be_read_keeps_a_count_of_two_or_more_a_while_but_never_turns_one_into_more()
    {
        var hold = new TeamBuffHold();
        hold.Track(Shown(2));
        Assert.Equal([new TeamBuff("Link", 2)], hold.Track(Shown(null)));
        var fromOne = new TeamBuffHold();
        fromOne.Track(Shown(1));
        Assert.Equal([new TeamBuff("Link", null)], fromOne.Track(Shown(null)));
    }

    [Fact]
    public void Reset_forgets_every_buff()
    {
        var hold = new TeamBuffHold();
        hold.Track(Shown(1));
        hold.Reset();
        Assert.Empty(hold.Track(Unclear));
    }
}

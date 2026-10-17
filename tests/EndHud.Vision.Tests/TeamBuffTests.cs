using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Link (the game's combo buff) left of the HP bar (<see cref="TeamBuffReader"/>), on frames of the owner's
/// Laevatain fight labelled by eye (Fixtures/buffs), at 4K and on smaller screens.
/// </summary>
public sealed class TeamBuffTests : IDisposable
{
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    private async Task<TeamBuffReader> ReaderAsync() => new(await OperatorCache.LoadAsync(_userFolder));

    
    // By eye: the white sword with feathers left of the Melting Flame flower, or not.
    public static TheoryData<string, bool> Labelled() => new()
    {
        { "laevatain-77_5s.jpg", true },
        { "laevatain-95s.jpg", true },
        { "laevatain-101_5s.jpg", true },
        { "laevatain-103_5s.jpg", true },
        { "laevatain-40s.jpg", false },
        { "laevatain-60s.jpg", false },
        { "laevatain-90s.jpg", false },
        { "laevatain-130s.jpg", false },
    };

    [Theory]
    [MemberData(nameof(Labelled))]
    public async Task Link_is_seen_exactly_when_it_is_shown(string file, bool shown)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", file)));

        Assert.Equal(shown ? ["Link"] : [], (await ReaderAsync()).Read(frame).Select(b => b.Id));
    }

    /// <summary>
    /// Link's stacks (the controller recordings, Fixtures/buffs/controller-*): one has no number, more
    /// show the count on a dark square at the icon's bottom right. Only 2 has been seen so far.
    /// </summary>
    public static TheoryData<string, int> Stacks() => new()
    {
        { "controller-link-two-113s.jpg", 2 },
        { "controller-link-two-116_5s.jpg", 2 },
        { "controller2-link-two-151s.jpg", 2 },
        { "controller-link-one-110s.jpg", 1 },
        { "controller-link-one-160s.jpg", 1 },
        { "laevatain-77_5s.jpg", 1 },
        { "laevatain-95s.jpg", 1 },
        { "laevatain-101_5s.jpg", 1 },
        { "laevatain-103_5s.jpg", 1 },
    };

    [Theory]
    [MemberData(nameof(Stacks))]
    public async Task Links_stacks_are_read(string file, int stacks)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", file)));

        Assert.Equal([new TeamBuff("Link", stacks)], (await ReaderAsync()).Read(frame));
    }

    /// <summary>A 3 hasn't been seen yet; rendered in Segoe UI Bold it reads as 5, which Link can't have: not a reading.</summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(5, null)]
    [InlineData(null, null)]
    public void Only_a_count_link_can_have_is_taken(int? digit, int? stacks) => Assert.Equal(stacks, TeamBuffReader.Stacks(digit));

    public static TheoryData<string, int, string> StacksOnSmallerScreens()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var screen in new[] { SimulatedScreen.Qhd, SimulatedScreen.FullHd })
        {
            data.Add("controller-link-two-113s.jpg", 2, screen.Name);
            data.Add("controller2-link-two-151s.jpg", 2, screen.Name);
            data.Add("controller-link-one-110s.jpg", 1, screen.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(StacksOnSmallerScreens))]
    public async Task Links_stacks_are_read_on_smaller_screens_too(string file, int stacks, string screenName)
    {
        var frame = SimulatedScreen.Named(screenName).Render(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", file))));

        Assert.Equal([new TeamBuff("Link", stacks)], (await ReaderAsync()).Read(frame));
    }

    public static TheoryData<string, bool, string> OnSmallerScreens()
    {
        var data = new TheoryData<string, bool, string>();
        foreach (var screen in new[] { SimulatedScreen.Qhd, SimulatedScreen.FullHd })
        {
            data.Add("laevatain-95s.jpg", true, screen.Name);
            data.Add("laevatain-77_5s.jpg", true, screen.Name);
            data.Add("laevatain-60s.jpg", false, screen.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(OnSmallerScreens))]
    public async Task Link_is_seen_on_smaller_screens_too(string file, bool shown, string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var frame = screen.Render(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("buffs", file))));

        Assert.Equal(shown ? ["Link"] : [], (await ReaderAsync()).Read(frame).Select(b => b.Id));
    }

    public static TheoryData<string> OtherSquads() =>
    [
        .. Directory.GetFiles(CombatFrameTests.Fixture("counters"), "*.jpg").Select(f => Path.Combine("counters", Path.GetFileName(f))),
    ];

    /// <summary>Typhoeus's and Zhuang Fangyi's fights (their counters where Link would be, no Link in them): nothing made up.</summary>
    [Theory]
    [MemberData(nameof(OtherSquads))]
    public async Task Other_counters_and_scenery_arent_read_as_link(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

        Assert.Empty((await ReaderAsync()).Read(frame));
    }
}

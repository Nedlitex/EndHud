using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The squad is named with nothing taught, as on a fresh install: the downloaded wiki roster and
/// avatars (<see cref="OperatorCache"/>), the character model and EndHud's built-in in-game
/// portrait references. The bar: at no reading is a slot given a wrong name, and every living slot
/// is named within a few seconds of play.
/// <list type="bullet">
/// <item>The recording fixtures (squad: Laevatain, Camille, Arcane, Ardelia) are held out: the
/// built-in references come from other screenshots (Fixtures/squads). They're also checked with
/// no references at all, as a newly released operator is recognised (wiki avatar only).</item>
/// <item>The squad screenshots are where the references come from, so naming them checks that
/// every operator's reference works at every screen size; operators in two screenshots are also
/// checked against the reference from the other one only.</item>
/// </list>
/// </summary>
public sealed class OutOfTheBoxRecognitionTests : IDisposable
{
    private static readonly string[] Truth = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private readonly string _userFolder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { Directory.Delete(_userFolder, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> FixtureNames() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures")).Select(Path.GetFileName).Order()!];

    public static TheoryData<string> Screens() => [.. SimulatedScreen.All.Append(SimulatedScreen.Uhd).Select(s => s.Name)];

    private static readonly Lazy<Task<List<(string Name, Frame Frame)>>> Frames4K = new(async () =>
    {
        var frames = new List<(string, Frame)>();
        foreach (string name in FixtureNames())
            frames.Add((name, await ImageFile.LoadAsync(CombatFrameTests.Fixture(name))));
        return frames;
    });

    private async Task<SquadIdentifier> NewIdentifierAsync(PartyState first, IReadOnlyList<PortraitReference>? references = null)
    {
        var identifier = new SquadIdentifier(await OperatorCache.LoadAsync(_userFolder), () => _clock, references);
        await identifier.PrepareAsync(first);
        return identifier;
    }

    /// <summary>One reading: the clock moves on by the app's shortest interval and up.</summary>
    private async Task<PartyState> ReadingAsync(SquadIdentifier identifier, PartyState state)
    {
        _clock += TimeSpan.FromSeconds(0.5);
        return await identifier.IdentifyAsync(state);
    }

    private static void AssertNoWrongName(PartyState state, string context)
    {
        foreach (var slot in state.Slots)
        {
            if (slot.Operator is { } match)
                Assert.True(match.Operator.Id == Truth[slot.Number - 1],
                    $"{context}: slot {slot.Number} named {match.Operator.Id}, but it's {Truth[slot.Number - 1]}");
        }
    }

    public static TheoryData<string, bool> FixturesWithAndWithoutReferences()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in FixtureNames())
        {
            data.Add(name, true);
            data.Add(name, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FixturesWithAndWithoutReferences))]
    public async Task Names_the_squad_in_every_fixture(string fixture, bool builtInReferences)
    {
        var state = await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(fixture)));
        var identifier = await NewIdentifierAsync(state, builtInReferences ? null : []);

        // Enough readings of this one frame for every slot to be compared MinReadings times.
        PartyState named = state;
        for (int n = 0; n < SquadIdentifier.MinReadings * 4; n++)
        {
            named = await ReadingAsync(identifier, state);
            AssertNoWrongName(named, fixture);
        }

        foreach (var slot in named.Slots.Where(s => !s.Dead))
            Assert.Equal(Truth[slot.Number - 1], slot.Operator?.Operator.Id);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Names_the_squad_on_every_screen(string screenName)
    {
        var screen = SimulatedScreen.Named(screenName);
        var reader = new PartyReader(new TextRecognizer());
        var states = new List<(string Name, PartyState State)>();
        foreach (var (name, frame) in await Frames4K.Value)
            states.Add((name, await reader.ReadAsync(screen.Render(frame))));
        var identifier = await NewIdentifierAsync(states[0].State);

        // The fixtures in turn, as readings of one session (twice over: 30 readings, 15 s).
        PartyState last = states[0].State;
        for (int round = 0; round < 2; round++)
        {
            foreach (var (name, state) in states)
            {
                last = await ReadingAsync(identifier, state);
                AssertNoWrongName(last, $"{screen.Width}x{screen.Height}, {name}");
            }
        }

        // The last fixture has every operator alive.
        Assert.Equal(Truth, last.Slots.Select(s => s.Operator?.Operator.Id));
    }

    private sealed record SquadScreenshot(string File, string[] Slots);

    /// <summary>Fixtures/squads (see its README): nine squads covering all 32 playable operators.</summary>
    private static readonly SquadScreenshot[] Squads = System.Text.Json.JsonSerializer.Deserialize<SquadScreenshot[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("squads", "squads.json"))),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<int, string> SquadsOnScreens()
    {
        var data = new TheoryData<int, string>();
        foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.Qhd, SimulatedScreen.FullHd, SimulatedScreen.LaptopHdPlus })
            for (int squad = 1; squad <= Squads.Length; squad++)
                data.Add(squad, screen.Name);
        return data;
    }

    private async Task NameSquadAsync(int squad, SimulatedScreen screen, IReadOnlyList<PortraitReference>? references, string[] mustName)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("squads", Squads[squad - 1].File)));
        var state = await new PartyReader(new TextRecognizer()).ReadAsync(screen.Render(frame));
        var identifier = await NewIdentifierAsync(state, references);
        string[] truth = Squads[squad - 1].Slots;

        PartyState named = state;
        for (int n = 0; n < SquadIdentifier.MinReadings * 4; n++)
        {
            named = await ReadingAsync(identifier, state);
            foreach (var slot in named.Slots.Where(s => s.Operator is not null && mustName.Contains(truth[s.Number - 1])))
                Assert.True(slot.Operator!.Operator.Id == truth[slot.Number - 1],
                    $"squad {squad} at {screen.Width}x{screen.Height}: slot {slot.Number} named {slot.Operator.Operator.Id}, but it's {truth[slot.Number - 1]}");
        }
        foreach (var slot in named.Slots.Where(s => mustName.Contains(truth[s.Number - 1])))
            Assert.Equal(truth[slot.Number - 1], slot.Operator?.Operator.Id);
    }

    [Theory]
    [MemberData(nameof(SquadsOnScreens))]
    public async Task Every_operator_is_named_from_the_built_in_references_at_every_size(int squad, string screenName) =>
        await NameSquadAsync(squad, SimulatedScreen.Named(screenName), null, Squads[squad - 1].Slots);

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task An_operator_is_named_from_a_reference_made_from_another_screenshot(int squad)
    {
        // References from every squad screenshot but this one, made as the built-in ones are:
        // operators also in another screenshot must still be named (from that one), at 1080p.
        var reader = new PartyReader(new TextRecognizer());
        var references = new List<PortraitReference>();
        for (int other = 1; other <= Squads.Length; other++)
        {
            if (other == squad) continue;
            var state = await reader.ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("squads", Squads[other - 1].File))));
            for (int i = 0; i < 4; i++)
                references.AddRange(PortraitReferences.Describe(Squads[other - 1].Slots[i], state.Slots[i].ScreenPortrait!, [64]));
        }
        string[] elsewhere = Squads[squad - 1].Slots.Where(id => references.Any(r => r.OperatorId == id)).ToArray();
        Assert.NotEmpty(elsewhere);

        await NameSquadAsync(squad, SimulatedScreen.FullHd, references, elsewhere);
    }

    [Fact]
    public async Task A_swapped_operator_is_renamed_without_a_wrong_name_in_between()
    {
        var screen = SimulatedScreen.FullHd;
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg"));
        var reader = new PartyReader(new TextRecognizer());
        var squad = await reader.ReadAsync(screen.Render(frame));
        var swapped = await reader.ReadAsync(screen.Render(SwapPortraits(frame, 0, 2)));
        string[] swappedTruth = [Truth[2], Truth[1], Truth[0], Truth[3]];
        var identifier = await NewIdentifierAsync(squad);

        for (int n = 0; n < SquadIdentifier.MinReadings * 4; n++) await ReadingAsync(identifier, squad);

        // Slots 1 and 3 swap (as after changing the squad): they start over, so never show the
        // old name, and are named again within MinReadings comparisons each.
        PartyState last = swapped;
        for (int n = 0; n < SquadIdentifier.MinReadings * 2; n++)
        {
            last = await ReadingAsync(identifier, swapped);
            foreach (var slot in last.Slots.Where(s => s.Operator is not null))
                Assert.Equal(swappedTruth[slot.Number - 1], slot.Operator!.Operator.Id);
        }
        Assert.Equal(swappedTruth, last.Slots.Select(s => s.Operator?.Operator.Id));
    }

    [Fact]
    public async Task Avatar_descriptions_are_cached_in_the_user_folder_and_a_damaged_cache_is_rebuilt()
    {
        var state = await new PartyReader(new TextRecognizer()).ReadAsync(
            await ImageFile.LoadAsync(CombatFrameTests.Fixture("combat-all-ready.jpg")));
        await NewIdentifierAsync(state);

        string cache = Assert.Single(Directory.GetFiles(Path.Combine(_userFolder, "features")));
        Assert.True(new FileInfo(cache).Length > 34 * 768);

        File.WriteAllText(cache, "{ not json");
        var identifier = await NewIdentifierAsync(state);
        PartyState named = state;
        for (int n = 0; n < SquadIdentifier.MinReadings * 4; n++) named = await ReadingAsync(identifier, state);
        Assert.Equal(Truth, named.Slots.Select(s => s.Operator?.Operator.Id));
    }

    /// <summary>The 4K frame with the portraits of two slots exchanged.</summary>
    private static Frame SwapPortraits(Frame frame, int a, int b)
    {
        var bgra = (byte[])frame.Bgra.Clone();
        var ra = PartyLayout.ForSlot(PartyLayout.Portrait, a);
        var rb = PartyLayout.ForSlot(PartyLayout.Portrait, b);
        for (int y = 0; y < ra.Height; y++)
        {
            int rowA = ((ra.Y + y) * frame.Width + ra.X) * 4, rowB = ((rb.Y + y) * frame.Width + rb.X) * 4;
            Buffer.BlockCopy(frame.Bgra, rowA, bgra, rowB, ra.Width * 4);
            Buffer.BlockCopy(frame.Bgra, rowB, bgra, rowA, ra.Width * 4);
        }
        return new Frame(frame.Width, frame.Height, bgra);
    }
}

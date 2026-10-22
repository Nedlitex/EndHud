using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Si's battle-skill button (<see cref="SkillRing"/>) on the frames of her fight (Fixtures/si,
/// skill-*): the butterfly before her first cast, the swirl and the draining grey ring while it
/// lasts, the butterfly again when it ends. Si is in slot 1 of a squad of four (button 0).
/// </summary>
public class SkillRingTests(ITestOutputHelper output)
{
    private static SkillRingReading Read(Frame frame) => SkillRing.ReadSi(frame, button: 0);

    public static TheoryData<string> Running() =>
        [.. SiFixtures.SkillFrames.Where(l => l.Icon == SkillButtonIcon.Changed && !l.RingHidden).Select(l => l.File)];

    public static TheoryData<string> Ended() => [.. SiFixtures.SkillFrames.Where(l => l.Icon == SkillButtonIcon.Original).Select(l => l.File)];

    [Fact]
    public async Task Before_the_cast_the_icon_is_the_original()
    {
        var reading = Read(await SiFixtures.LoadAsync("skill-12s.jpg"));

        Assert.Equal(SkillButtonIcon.Original, reading.Icon);
        Assert.Null(reading.LeftShare);
    }

    [Theory]
    [MemberData(nameof(Running))]
    public async Task While_it_runs_the_icon_is_changed_and_the_ring_gives_the_time_left(string file)
    {
        var label = SiFixtures.SkillFrames.Single(l => l.File == file);

        var reading = Read(await SiFixtures.LoadAsync(file));

        Assert.Equal(SkillButtonIcon.Changed, reading.Icon);
        Assert.NotNull(reading.LeftShare);
        double expected = 1 - label.DrainedDegrees!.Value / 360;
        output.WriteLine($"{file}: left {reading.LeftShare:F3}, by eye {expected:F3}");
        Assert.InRange(reading.LeftShare!.Value, expected - 0.07, expected + 0.07); // ±1.5 s of 22
    }

    [Theory]
    [MemberData(nameof(Ended))]
    public async Task After_it_ends_the_icon_is_back(string file)
    {
        var reading = Read(await SiFixtures.LoadAsync(file));

        Assert.Equal(SkillButtonIcon.Original, reading.Icon);
        Assert.Null(reading.LeftShare);
    }

    /// <summary>The button fades for a moment (19.5–20 s): the swirl may still be seen, the ring isn't read.</summary>
    [Fact]
    public async Task Ring_covered_reads_unknown_never_a_number()
    {
        foreach (var label in SiFixtures.SkillFrames.Where(l => l.RingHidden))
        {
            var reading = Read(await SiFixtures.LoadAsync(label.File));
            Assert.NotEqual(SkillButtonIcon.Original, reading.Icon);
            Assert.Null(reading.LeftShare);
        }
    }

    private static PartyState Squad(Frame frame, int siSlot, HudLayout layout = HudLayout.KeyboardMouse, int readingsSinceSeen = 0)
    {
        var si = new Operators.OperatorMatch(new Operators.OperatorInfo("Si", "Si", "Si", "Si.png"), 1, true);
        var slots = Enumerable.Range(1, PartyLayout.SlotCount)
            .Select(n => new PartySlot(n, null, 1.0, "", frame) { Operator = n == siSlot ? si : null }).ToList();
        return new PartyState(slots, 7, readingsSinceSeen) { Layout = layout };
    }

    /// <summary>Si's button found from where she is in the squad; nothing for the controller layout or a squad panel out of sight.</summary>
    [Fact]
    public async Task Reads_Sis_button_wherever_the_squad_has_her()
    {
        var frame = await SiFixtures.LoadAsync("skill-24_5s.jpg");

        var reading = Assert.Single(SkillRing.Read(frame, Squad(frame, siSlot: 1)));
        Assert.Equal(("Si", SkillButtonIcon.Changed), (reading.OperatorId, reading.Icon));
        Assert.NotEqual(SkillButtonIcon.Changed, Assert.Single(SkillRing.Read(frame, Squad(frame, siSlot: 2))).Icon); // button 2 is another operator's
        Assert.Empty(SkillRing.Read(frame, Squad(frame, siSlot: 0)));
        Assert.Empty(SkillRing.Read(frame, Squad(frame, siSlot: 1, HudLayout.Controller)));
        Assert.Empty(SkillRing.Read(frame, Squad(frame, siSlot: 1, readingsSinceSeen: 1)));
    }

    [Fact]
    public void The_fixtures_cover_before_during_hidden_and_after()
    {
        Assert.Contains(SiFixtures.SkillFrames, l => l.Icon == SkillButtonIcon.Original && l.File == "skill-12s.jpg");
        Assert.True(Running().Count >= 10);
        Assert.Contains(SiFixtures.SkillFrames, l => l.RingHidden);
        Assert.True(Ended().Count >= 3);
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>On other screens: the icon right or unknown, never the other one; the ring within ±0.1 or not read.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Reads_right_or_nothing_on_other_screens(string name)
    {
        var screen = SimulatedScreen.Named(name);
        int icons = 0, rings = 0, ringFrames = 0;
        foreach (var label in SiFixtures.SkillFrames)
        {
            var reading = Read(screen.Render(await SiFixtures.LoadAsync(label.File)));
            Assert.True(reading.Icon == label.Icon || reading.Icon == SkillButtonIcon.Unknown, $"{label.File}: {reading.Icon}, expected {label.Icon}");
            if (reading.Icon == label.Icon) icons++;
            if (label.DrainedDegrees is { } drained)
            {
                ringFrames++;
                if (reading.LeftShare is { } left)
                {
                    Assert.InRange(left, 1 - drained / 360 - 0.1, 1 - drained / 360 + 0.1);
                    rings++;
                }
            }
            else Assert.Null(reading.LeftShare);
        }
        output.WriteLine($"{screen.Width}x{screen.Height}: icon {icons} of {SiFixtures.SkillFrames.Count()}, ring {rings} of {ringFrames}");
        Assert.True(icons >= 0.8 * SiFixtures.SkillFrames.Count());
        Assert.True(rings >= 0.6 * ringFrames);
    }
}

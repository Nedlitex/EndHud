using System.Text.Json;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Argent Flow's Bolt Rush (<see cref="SkillRing"/>, owner 2026-11-07: "more like Si and Mifu's"): after his combo
/// skill his battle skill becomes Bolt Rush for 20 s, his button showing a horse on gold with Si's draining ring; his
/// own icon comes back when he casts it or the ring runs out. On the frames of his recording (Fixtures/argent-flow,
/// skill-*), him in slot 1 of a squad of four (button 0). The icon masks were built from the frames up to 95.5 s only;
/// the later ones check them.
/// </summary>
public class ArgentFlowSkillTests(ITestOutputHelper output)
{
    /// <param name="Icon">His button's icon, by eye.</param>
    /// <param name="ChangedAt">On horse frames: when the horse first showed in that window (s, by eye at 10 frames a second).</param>
    /// <param name="SecondsLeft">On horse frames: 20 − (the frame's time − <paramref name="ChangedAt"/>).</param>
    internal sealed record Label(string File, SkillButtonIcon Icon, double? ChangedAt, double? SecondsLeft)
    {
        public double Seconds => double.Parse(File[6..^5].Replace('_', '.'), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("argent-flow", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;

    private static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("argent-flow", file)));

    private static SkillRingReading Read(Frame frame) => SkillRing.ReadButton(frame, "Argent_Flow", button: 0);

    /// <summary>The frames whose labels the icon masks weren't built from (see <see cref="SkillRing.ArgentFlowMask"/>).</summary>
    private static bool HeldOut(Label label) => label.Seconds >= 110;

    public static TheoryData<string> Files() => [.. Labels.Select(l => l.File)];

    /// <summary>Each frame: the icon right, or unknown (83 s: a white flash over a pink scene); never the other icon.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task The_icon_is_his_own_or_the_horse_as_labelled(string file)
    {
        var label = Labels.Single(l => l.File == file);
        var reading = Read(await LoadAsync(file));

        Assert.Equal("Argent_Flow", reading.OperatorId);
        Assert.True(reading.Icon == label.Icon || (reading.Icon == SkillButtonIcon.Unknown && file == "skill-83s.jpg"),
            $"{file}: {reading.Icon}, expected {label.Icon}");
        if (label.Icon == SkillButtonIcon.Original) Assert.Null(reading.LeftShare);
    }

    /// <summary>While the horse shows, the ring gives the time left of 20 s: within 2 s of the count from the horse's first frame, or not read.</summary>
    [Fact]
    public async Task While_it_is_Bolt_Rush_the_ring_gives_the_time_left_of_20_seconds()
    {
        int read = 0, horses = 0;
        foreach (var label in Labels.Where(l => l.Icon == SkillButtonIcon.Changed))
        {
            horses++;
            var reading = Read(await LoadAsync(label.File));
            if (reading.LeftShare is not { } left) continue;
            read++;
            double seconds = left * SkillRing.ArgentFlowDuration.TotalSeconds;
            output.WriteLine($"{label.File}: {seconds:F1} s left, by the horse's first frame {label.SecondsLeft:F1} s");
            Assert.InRange(seconds, label.SecondsLeft!.Value - 2, label.SecondsLeft.Value + 2);
        }
        Assert.True(read >= horses - 2, $"the ring read on {read} of {horses} horse frames");
    }

    /// <summary>The masks hold on frames they weren't built from: every held-out frame's icon right.</summary>
    [Fact]
    public async Task The_icons_are_told_on_the_frames_the_masks_did_not_come_from()
    {
        var heldOut = Labels.Where(HeldOut).ToList();
        Assert.Contains(heldOut, l => l.Icon == SkillButtonIcon.Original);
        Assert.Contains(heldOut, l => l.Icon == SkillButtonIcon.Changed);
        foreach (var label in heldOut)
            Assert.Equal(label.Icon, Read(await LoadAsync(label.File)).Icon);
    }

    public static TheoryData<string> Screens() => [SimulatedScreen.Qhd.Name, SimulatedScreen.FullHd.Name, SimulatedScreen.Wuxga.Name];

    /// <summary>On other screens: the icon right or unknown, never the other one, most of them right; the ring within 2.5 s or not read.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Reads_right_or_nothing_on_other_screens(string name)
    {
        var screen = SimulatedScreen.Named(name);
        int right = 0;
        foreach (var label in Labels)
        {
            var reading = Read(screen.Render(await LoadAsync(label.File)));
            Assert.True(reading.Icon == label.Icon || reading.Icon == SkillButtonIcon.Unknown, $"{label.File} at {name}: {reading.Icon}, expected {label.Icon}");
            if (reading.Icon == label.Icon) right++;
            if (reading.LeftShare is { } left && label.SecondsLeft is { } expected)
                Assert.InRange(left * SkillRing.ArgentFlowDuration.TotalSeconds, expected - 2.5, expected + 2.5);
        }
        output.WriteLine($"{name}: {right} of {Labels.Length} icons right");
        Assert.True(right >= Labels.Length * 0.8, $"{name}: {right} of {Labels.Length} icons right");
    }

    private static PartyState Squad(Frame frame, int argentSlot)
    {
        var argent = new OperatorMatch(new OperatorInfo("Argent_Flow", "明河", "Argent Flow", "Argent_Flow.png"), 1, true);
        var slots = Enumerable.Range(1, PartyLayout.SlotCount)
            .Select(n => new PartySlot(n, null, 1.0, "", frame) { Operator = n == argentSlot ? argent : null }).ToList();
        return new PartyState(slots, 7, 0) { Layout = HudLayout.KeyboardMouse };
    }

    /// <summary>
    /// The whole path, as the app runs it: each frame read for the squad (him in slot 1), his timer over the frames in
    /// time order, and the "battle skill running" condition asked of each reading.
    /// </summary>
    [Fact]
    public async Task His_timer_and_the_battle_skill_running_condition_follow_his_button()
    {
        var timers = new SkillTimers();
        var running = new BattleSkillActiveCondition("Argent_Flow", true);
        foreach (var label in Labels.OrderBy(l => l.Seconds))
        {
            var frame = await LoadAsync(label.File);
            var state = Squad(frame, argentSlot: 1);
            state = state with { SkillRings = SkillRing.Read(frame, state) };
            var views = timers.Update(state, TimeSpan.FromSeconds(label.Seconds));
            state = state with { RunningSkills = views };
            bool? told = RotationTracker.Evaluate(running, state, PhaseHistory.None);

            if (label.Icon == SkillButtonIcon.Original)
            {
                Assert.False(views.ContainsKey("Argent_Flow"), $"{label.File}: a timer while his own icon shows");
                Assert.Equal(false, told);
            }
            else if (label.File != "skill-83s.jpg") // 83 s: the icon may be unknown under a flash
            {
                var view = Assert.Contains("Argent_Flow", (IDictionary<string, SkillTimerView>)views);
                Assert.InRange(view.SecondsLeft, label.SecondsLeft!.Value - 2, label.SecondsLeft.Value + 2);
                Assert.Equal(true, told);
            }
        }
    }

    [Fact]
    public void He_can_be_named_in_a_battle_skill_running_condition()
    {
        Assert.Contains("Argent_Flow", SkillRing.Operators);
        Assert.Equal(TimeSpan.FromSeconds(20), SkillRing.DurationOf("Argent_Flow"));
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Argent_Flow", "Antal", "Arcane", "Gilberta"],
            [new RotationPhase("p", "P", [new RotationStep("Argent_Flow", StepAction.BattleSkill)], [new BattleSkillActiveCondition("Argent_Flow", true)])]);
        Assert.DoesNotContain(rotation.Problems(), p => p.Kind == RotationProblemKind.SkillTimeNotRead);
    }
}

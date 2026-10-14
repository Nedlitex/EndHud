using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// When the rotation strip is up (<see cref="StripVisibility"/>): in battle only, through a few
/// readings that miss the battle signs or have the squad panel covered, and gone soon after the
/// battle ends (exploration, menus, loading screens).
/// </summary>
public class StripVisibilityTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    /// <summary>A battle reading: the panel with combo bars and the skill-point bar.</summary>
    private static PartyState Battle(int readingsSinceSeen = 0) => new(
        Enumerable.Range(1, 4).Select(n => new PartySlot(n, null, 1, "", NoPortrait, new ComboReading(1, true))).ToList(), PanelMarks: 8, ReadingsSinceSeen: readingsSinceSeen)
    { SkillPoints = 2 };

    /// <summary>Exploring: the panel with levels, no combo bars or skill points.</summary>
    private static PartyState Exploring() => new(
        Enumerable.Range(1, 4).Select(n => new PartySlot(n, 90, 1, "", NoPortrait)).ToList(), PanelMarks: 8);

    [Fact]
    public void Not_shown_while_exploring()
    {
        var visibility = new StripVisibility();
        for (int i = 0; i < 20; i++) Assert.False(visibility.Update(Exploring()));
    }

    [Fact]
    public void Shown_in_battle_and_through_a_few_readings_without_the_signs()
    {
        var visibility = new StripVisibility();
        Assert.True(visibility.Update(Battle()));
        Assert.True(visibility.Update(Exploring() with { PanelMarks = 2 })); // an effect over the panel for a moment
        Assert.True(visibility.Update(Battle()));
    }

    [Fact]
    public void Shown_while_an_ultimate_animation_covers_the_panel()
    {
        var visibility = new StripVisibility();
        visibility.Update(Battle());
        // The tracker repeats the last reading while the panel is out of sight (ReadingsSinceSeen counts up).
        for (int n = 1; n <= 12; n++) Assert.True(visibility.Update(Battle(readingsSinceSeen: n)), $"reading {n}");
    }

    [Fact]
    public void Gone_soon_after_the_battle_ends()
    {
        var visibility = new StripVisibility();
        visibility.Update(Battle());
        int shown = 0;
        for (int i = 0; i < 40; i++) if (visibility.Update(Exploring())) shown++;
        Assert.InRange(shown, 1, 12); // a couple of seconds at four readings a second, then gone
        Assert.False(visibility.Update(Exploring()));
    }

    [Fact]
    public void Gone_when_the_panel_stays_out_of_sight_menus_and_loading()
    {
        var visibility = new StripVisibility();
        visibility.Update(Battle());
        bool last = true;
        for (int n = 1; n <= 60; n++) last = visibility.Update(Battle(readingsSinceSeen: n));
        Assert.False(last);
    }
}

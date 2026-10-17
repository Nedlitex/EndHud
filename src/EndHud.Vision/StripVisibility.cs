namespace EndHud.Vision;

/// <summary>
/// Whether the rotation strip (and the gauges) are up: in battle only (<see cref="PartyState.InCombat"/>).
/// <list type="bullet">
/// <item>The panel on screen without the battle signs for a moment (an effect over them): stays,
/// for up to <see cref="PresenceMemory.ReadingsToLeave"/> readings.</item>
/// <item>The panel out of sight (the tracker repeats the last reading): stays while that may be an
/// ultimate animation, <see cref="CoveredReadings"/> readings (the longest in the controller full
/// battle recording hid it 2.5 s), and goes then. Waiting two seconds more on top put the gauges
/// over the results page after a battle.</item>
/// </list>
/// </summary>
public sealed class StripVisibility
{
    /// <summary>Readings the panel can be covered while still counting as the battle seen (3 s at four a second).</summary>
    public const int CoveredReadings = 12;

    private readonly PresenceMemory _battle = new();

    public bool Update(PartyState state)
    {
        if (state.ReadingsSinceSeen == 0) return _battle.Update(state.InCombat);
        if (state.InCombat && state.ReadingsSinceSeen <= CoveredReadings) return _battle.Update(true);
        _battle.Forget();
        return false;
    }
}

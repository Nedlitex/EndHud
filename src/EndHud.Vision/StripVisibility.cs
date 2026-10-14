namespace EndHud.Vision;

/// <summary>
/// Whether the rotation strip is up: in battle only (<see cref="PartyState.InCombat"/>). A reading
/// that misses the battle signs (an effect over the panel) or comes while the panel is covered (the
/// tracker repeats the last reading through an ultimate animation, for up to
/// <see cref="CoveredReadings"/>) doesn't take it down; it goes after <see cref="PresenceMemory.ReadingsToLeave"/>
/// readings in a row without a battle seen: a couple of seconds after the battle ends, or once a
/// menu or loading screen has hidden the panel for longer.
/// </summary>
public sealed class StripVisibility
{
    /// <summary>Readings the panel can be covered while still counting as the battle seen (3 s at four a second).</summary>
    public const int CoveredReadings = 12;

    private readonly PresenceMemory _battle = new();

    public bool Update(PartyState state) => _battle.Update(state.InCombat && state.ReadingsSinceSeen <= CoveredReadings);
}

namespace EndHud.Vision;

/// <summary>
/// Carries the team buffs across readings, so one frame that can't be trusted never takes a buff away (or makes one up).
/// Link's icon pulses about once a second, and at the dim end of the pulse over a light floor it matches only just under
/// <see cref="TeamBuffReader.MatchAt"/>; flashes, beams and the HUD jolting hide it for a few frames more. Read one frame at
/// a time, that made the 连击 gauge blink off and on while the buff was there throughout (the owner's test_battle_debug,
/// 84-95 s: 87 of 412 frames). So, per buff:
/// <list type="bullet">
/// <item>Shown: taken at once, with its stacks.</item>
/// <item>Unclear (<see cref="BuffSight.Unclear"/>, or shown with a number that can't be read): the last verdict stays,
/// for up to <see cref="HoldReadings"/> readings in a row; after that a buff that was shown is "shown, stacks unknown"
/// (<see cref="TeamBuff.Stacks"/> null), never gone.</item>
/// <item>Absent: a shown buff is gone only after <see cref="GoneAfterAbsent"/> clearly absent readings with no sighting
/// between (unclear ones neither count nor break the run): one frame misjudged absent under an effect doesn't end it.</item>
/// </list>
/// A buff never seen yet (or gone) stays absent while unclear: the hold carries what was seen, it doesn't make a buff up.
/// Out of combat buffs aren't read: <see cref="Reset"/>.
/// </summary>
public sealed class TeamBuffHold
{
    /// <summary>Unclear readings in a row a verdict is kept through (2 s at four readings a second, as <see cref="PresenceMemory"/>).</summary>
    public const int HoldReadings = PresenceMemory.ReadingsToLeave;

    /// <summary>Clearly absent readings (since it was last seen) that end a shown buff.</summary>
    public const int GoneAfterAbsent = 2;

    private sealed class Held
    {
        public int? Stacks;      // the last stacks read; null: shown, count not read yet
        public int Unclear;      // readings in a row without a clear verdict
        public int Absent;       // clearly absent readings since it was last seen
    }

    private readonly Dictionary<string, Held> _shown = [];

    /// <summary>The buffs to show now, from this reading's sightings (<see cref="TeamBuffReader.Look"/>).</summary>
    public IReadOnlyList<TeamBuff> Track(IReadOnlyList<TeamBuffSighting> sightings) =>
        [.. sightings.Where(s => s.Sight == BuffSight.Shown).Select(s => new TeamBuff(s.Id, s.Stacks))];

    /// <summary>Forget every buff (out of combat: they aren't read there).</summary>
    public void Reset() => _shown.Clear();
}

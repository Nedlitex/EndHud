using EndHud.Vision.Rotations;

namespace EndHud.Vision.Tests;

/// <summary>Battle skills cast, as the step follower is handed them (<see cref="ScreenCastJudge"/>).</summary>
internal static class Casts
{
    /// <summary>Casts of these slots seen for certain (a skill point spent, the slot named by its flash).</summary>
    public static IReadOnlyList<SkillCast> Seen(IEnumerable<int> slots, double atSeconds = 0) =>
        slots.Select(s => new SkillCast(s, TimeSpan.FromSeconds(atSeconds), CastSource.Flash, Paid: true)).ToList();
}

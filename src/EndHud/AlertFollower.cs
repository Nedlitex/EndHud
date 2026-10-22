using EndHud.Vision;
using EndHud.Vision.Alerts;

namespace EndHud;

/// <summary>What the overlay does with the alerts on a reading: pop-ups to show, pop-ups to take down, and whether the boss's statuses must be read.</summary>
internal sealed record AlertFollowing(IReadOnlyList<AlertRule> Show, IReadOnlyList<string> TakeDown, bool ReadBossStatuses);

internal sealed class AlertFollower
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(3);

    public const int MostShown = 3;

    public IReadOnlyList<string> Up => [];

    public AlertFollowing Update(PartyState? state, bool inBattle, bool enabled, IReadOnlyList<AlertRule> rules, TimeSpan now) => new([], [], false);

    public IReadOnlyList<string> Expire(TimeSpan now) => [];
}

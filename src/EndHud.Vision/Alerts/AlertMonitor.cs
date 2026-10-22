using EndHud.Vision.Rotations;

namespace EndHud.Vision.Alerts;

public sealed class AlertMonitor
{
    public IReadOnlyList<AlertRule> Update(PartyState state, IReadOnlyList<AlertRule> rules) => [];

    public bool StillHolds(string ruleId) => false;

    public void EndFight() { }

    public static bool NeedsBossStatuses(IEnumerable<AlertRule> rules) => false;
}

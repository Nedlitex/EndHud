using EndHud.Vision;
using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>What the overlay does with the alerts on a reading.</summary>
/// <param name="Show">Rules whose pop-up goes up now.</param>
/// <param name="TakeDown">Rule ids whose pop-up comes down now.</param>
/// <param name="ReadBossStatuses">A rule asks about the boss's statuses, so they must be read (in battle only).</param>
internal sealed record AlertFollowing(IReadOnlyList<AlertRule> Show, IReadOnlyList<string> TakeDown, bool ReadBossStatuses);

/// <summary>
/// The overlay's alerts, without the drawing (<see cref="OverlayWindow"/> draws what it says). In battle,
/// a rule's pop-up goes up when <see cref="AlertMonitor"/> says it turned true, and comes down after
/// <see cref="ShowFor"/> or on the first reading where it stopped holding, whichever is first. At most
/// <see cref="MostShown"/> at once: the oldest leaves. Leaving battle takes every pop-up down and re-arms
/// every rule. No sound (owner, 2026-10-22).
/// </summary>
internal sealed class AlertFollower
{
    /// <summary>How long a pop-up stays up at most (owner, 2026-10-22: 3 s, less if it stops holding).</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(3);

    public const int MostShown = 3;

    private readonly AlertMonitor _monitor = new();
    private readonly List<(string Id, TimeSpan Since)> _up = [];
    private bool _wasInBattle;

    /// <summary>The rules whose pop-up is up, oldest first.</summary>
    public IReadOnlyList<string> Up => _up.Select(u => u.Id).ToList();

    /// <param name="inBattle">In battle, riding over short gaps (the strip's rule, <see cref="StripVisibility"/>).</param>
    /// <param name="enabled">Alerts on and the squad panel read.</param>
    /// <param name="history">The phases shown and played through (<see cref="RotationFollowing.History"/>), for "phase played".</param>
    /// <param name="now">The overlay's clock.</param>
    public AlertFollowing Update(PartyState? state, bool inBattle, bool enabled, IReadOnlyList<AlertRule> rules, IPhaseHistory history, TimeSpan now)
    {
        if (!inBattle || !enabled)
        {
            // The fight ended (or alerts were turned off): everything comes down, and every rule may pop again next fight.
            if (_wasInBattle || !enabled) _monitor.EndFight();
            _wasInBattle = inBattle;
            var all = Up;
            _up.Clear();
            return new AlertFollowing([], all, false);
        }
        _wasInBattle = true;

        var shown = state is null ? [] : _monitor.Update(state, rules, history);
        var down = new List<string>();
        foreach (var (id, since) in _up.ToList())
        {
            // Over its time, stopped holding, or deleted in the editor.
            if (now - since >= ShowFor || !_monitor.StillHolds(id) || !rules.Any(r => r.Id == id))
            {
                _up.RemoveAll(u => u.Id == id);
                down.Add(id);
            }
        }
        foreach (var rule in shown)
        {
            _up.RemoveAll(u => u.Id == rule.Id);
            _up.Add((rule.Id, now));
        }
        while (_up.Count > MostShown)
        {
            down.Add(_up[0].Id);
            _up.RemoveAt(0);
        }
        return new AlertFollowing(shown, down, AlertMonitor.NeedsBossStatuses(rules));
    }

    /// <summary>Pop-ups whose time is up, between readings (the overlay's timer): taken down.</summary>
    public IReadOnlyList<string> Expire(TimeSpan now)
    {
        var over = _up.Where(u => now - u.Since >= ShowFor).Select(u => u.Id).ToList();
        _up.RemoveAll(u => over.Contains(u.Id));
        return over;
    }
}

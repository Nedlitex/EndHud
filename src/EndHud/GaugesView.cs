using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>
/// What the gauges show for a reading: first the team buffs seen this battle (Link), lit while
/// active, with their stacks (one ring segment each, the count in the corner); then per recognised operator, in slot order, their ultimate's charge and their own
/// counters (Zhuang Fangyi's Sunderblade, Typhoeus's Hunting Arrow and Sign, …). An operator not recognised yet has no
/// gauges: there'd be no avatar or counter to show. A buff shows once it has been seen in the battle,
/// so a squad that never gets it isn't shown an empty circle for it.
/// </summary>
internal static class GaugesView
{
    public static GaugeBoard Build(PartyState state, IReadOnlyList<RememberedCounter> counters, IReadOnlyList<string> buffsSeen,
        IReadOnlyDictionary<string, SkillTimerView> timers,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, Func<string, BitmapSource?> statusIcon) =>
        Build(state, counters, buffsSeen, skillIcon, avatar, statusIcon);

    /// <param name="buffsSeen">Buffs seen so far this battle (the reading's own included), in the order first seen.</param>
    public static GaugeBoard Build(PartyState state, IReadOnlyList<RememberedCounter> counters, IReadOnlyList<string> buffsSeen,
        Func<OperatorInfo, StepAction, BitmapSource?> skillIcon, Func<OperatorInfo, BitmapSource?> avatar, Func<string, BitmapSource?> statusIcon)
    {
        var gauges = new List<Gauge>();
        foreach (string buff in buffsSeen)
        {
            // Lapsed: no count. Active with a count that can't be told now: "?", the ring full.
            var active = state.Buffs.FirstOrDefault(b => b.Id == buff);
            int max = TeamBuffReader.MaxStacks;
            gauges.Add(new Gauge(GaugeKind.Buff, statusIcon(buff), null, StatusNames.Zh(buff) ?? buff,
                active is null ? null : active.Stacks is { } n ? Math.Min(n, max) / (double)max : 1, active is not null)
            {
                FirstOfOperator = gauges.Count == 0,
                Number = active is null ? null : active.Stacks?.ToString() ?? "?",
                Segments = max,
            });
        }
        foreach (var slot in state.Slots)
        {
            if (slot.Operator?.Operator is not { } op) continue;
            var face = avatar(op);
            // A lasting ultimate running (the controlled operator's time bar): its time left, draining.
            // Otherwise the charge. Fill null: not readable this time (an effect over it); drawn empty, without a guess.
            bool running = state.UltimateTimeLeft is not null && state.ActiveSlot == slot.Number;
            gauges.Add(running
                ? new Gauge(GaugeKind.Ultimate, face, skillIcon(op, StepAction.Ultimate), Loc.Get("Gauge.UltimateRunning"),
                    state.UltimateTimeLeft, false) { Running = true, Fallen = slot.Dead, FirstOfOperator = true }
                : new Gauge(GaugeKind.Ultimate, face, skillIcon(op, StepAction.Ultimate), Loc.Get("Gauge.Ultimate"),
                    slot.Ultimate?.Progress, slot.Ultimate?.Ready == true) { Fallen = slot.Dead, FirstOfOperator = true });
            foreach (var counter in counters.Where(c => c.OperatorId == op.Id))
            {
                int max = counter.Counter.Max;
                gauges.Add(new Gauge(GaugeKind.Counter, face, null, counter.Counter.NameZh,
                    counter.Value is { } v ? v / (double)max : null, counter.Value == max)
                {
                    Number = counter.Value?.ToString() ?? "?",
                    Segments = max,
                    Live = counter.Live,
                    Fallen = slot.Dead,
                });
            }
        }
        return new GaugeBoard(gauges);
    }

    /// <summary>Something to drag while placing the gauges (menu open, no battle): one of each kind.</summary>
    public static GaugeBoard Sample() => new(
    [
        new Gauge(GaugeKind.Buff, null, null, Loc.Get("Gauge.Buff"), null, true) { FirstOfOperator = true },
        new Gauge(GaugeKind.Ultimate, null, null, Loc.Get("Gauge.Ultimate"), 0.6, false) { FirstOfOperator = true },
        new Gauge(GaugeKind.Counter, null, null, Loc.Get("Gauge.Counter"), 0.5, false) { Number = "2", Segments = 4 },
    ]);

    /// <summary>
    /// What would change on screen, to redraw only then. Fill is rounded to the arc's resolution so a
    /// charge ticking on redraws a few times a second, not on every tiny change.
    /// </summary>
    public static string Key(GaugeBoard board) => string.Join("|", board.Gauges.Select(x =>
        $"{x.Kind}{x.Label}{x.Avatar?.GetHashCode()}{x.Badge is not null}{(x.Fill is { } f ? Math.Round(f * 50) : -1)}{x.Ready}{x.Running}{x.Number}{x.Live}{x.Fallen}"));
}

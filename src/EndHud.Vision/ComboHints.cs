namespace EndHud.Vision;

/// <summary>How the combo-ready hint behaves.</summary>
public enum ComboHintMode
{
    /// <summary>Show for a few seconds when a combo comes off cooldown.</summary>
    WhenReady,

    /// <summary>Show the whole time a combo is off cooldown in combat.</summary>
    WhileReady,
}

/// <summary>
/// Decides which slots get a combo-ready hint, from one tracked reading after another.
/// <list type="bullet">
/// <item>Only in combat: the combo bar is on screen (out of combat it isn't drawn), the operator
/// hasn't fallen, and the panel was seen in the last <see cref="MaxReadingsSinceSeen"/> readings —
/// so a kept reading carries the hint through a short cutscene, but not into menus.</item>
/// <item>A cooldown that finishes while the panel is out of view (an ultimate cutscene) is hinted
/// as soon as the panel is back.</item>
/// <item>"Came off cooldown" means a ready bar after the bar was seen cooling, however far it had
/// got: a skill that refreshes the cooldown makes it jump straight from, say, 40% to ready, and
/// that counts the same as refilling.</item>
/// <item>"Seen cooling" takes <see cref="CoolingReadingsNeeded"/> readings in a row. A single
/// one is not trusted: as the HUD fades back in after a scene change, the bar is briefly drawn as
/// an empty track, which reads as cooling (seen live at 1440p). Real cooldowns last many seconds;
/// the cost is no hint for a cooldown refreshed within a reading or so of being used.</item>
/// </list>
/// <item>Early (<see cref="Lead"/>): a combo about to come off cooldown is hinted that long before,
/// with the seconds left (<see cref="SecondsLeft"/>), worked out from how fast its bar has been
/// filling over the last <see cref="SpeedWindow"/>.</item>
/// Calling <see cref="Update"/> again with the same reading only re-checks the timing, so a
/// display can refresh on a timer between readings.
/// </summary>
public sealed class ComboHints
{
    public const int MaxReadingsSinceSeen = 1;
    public const int CoolingReadingsNeeded = 2;

    private readonly bool[] _sawCooling = new bool[PartyLayout.SlotCount];
    private readonly int[] _coolingReadings = new int[PartyLayout.SlotCount];
    private PartyState? _lastReading;
    private readonly TimeSpan?[] _readyAt = new TimeSpan?[PartyLayout.SlotCount];
    private readonly List<(TimeSpan At, double Progress)>[] _filling = Enumerable.Range(0, PartyLayout.SlotCount).Select(_ => new List<(TimeSpan, double)>()).ToArray();

    /// <summary>The readings a bar's filling speed is worked out from, and the least time they must span.</summary>
    public static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinSpeedSpan = TimeSpan.FromSeconds(0.75);

    public ComboHintMode Mode { get; set; } = ComboHintMode.WhenReady;

    /// <summary>How long a <see cref="ComboHintMode.WhenReady"/> hint stays up.</summary>
    public TimeSpan ShowFor { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long before a combo comes off cooldown to hint it already; zero: only once ready.</summary>
    public TimeSpan Lead { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Seconds until this slot's (1–4) combo is ready, while it's hinted early; null when it's ready,
    /// or when there's no estimate (the bar's speed isn't known yet).
    /// </summary>
    public double? SecondsLeft(int slotNumber, TimeSpan now)
    {
        if (slotNumber is < 1 or > PartyLayout.SlotCount) return null;
        var filling = _filling[slotNumber - 1];
        if (filling.Count < 2 || filling[^1].At - filling[0].At < MinSpeedSpan) return null;
        double perSecond = (filling[^1].Progress - filling[0].Progress) / (filling[^1].At - filling[0].At).TotalSeconds;
        if (perSecond <= 0) return null;
        return (1 - filling[^1].Progress) / perSecond - (now - filling[^1].At).TotalSeconds;
    }

    private bool Soon(int slot, TimeSpan now) =>
        Lead > TimeSpan.Zero && SecondsLeft(slot + 1, now) is { } left && left > 0 && left <= Lead.TotalSeconds + 1e-6;

    /// <summary>Slot numbers (1–4) that should show a hint now. <paramref name="now"/> is any steady clock.</summary>
    public IReadOnlyList<int> Update(PartyState state, TimeSpan now)
    {
        bool inView = state.PanelVisible && state.ReadingsSinceSeen <= MaxReadingsSinceSeen;
        bool newReading = !ReferenceEquals(state, _lastReading);
        _lastReading = state;
        var show = new List<int>();
        foreach (var slot in state.Slots)
        {
            int i = slot.Number - 1;
            var combo = inView && !slot.Dead ? slot.Combo : null;
            if (!newReading)
            {
                if (combo is { Ready: true } && (Mode == ComboHintMode.WhileReady || IsRecent(i, now))) show.Add(slot.Number);
                else if (combo is { Ready: false } && Soon(i, now)) show.Add(slot.Number);
                continue;
            }
            if (combo is null)
            {
                // Out of combat or fallen: start over, so a combo that is simply ready when the next
                // fight starts doesn't count as having come off cooldown. Merely out of view (an
                // ultimate cutscene, a menu) keeps the state: a cooldown that finished meanwhile
                // is hinted when the panel is back.
                if (slot.Dead || (inView && slot.Level is not null))
                {
                    (_sawCooling[i], _coolingReadings[i], _readyAt[i]) = (false, 0, null);
                    _filling[i].Clear();
                }
                continue;
            }
            if (!combo.Ready)
            {
                _readyAt[i] = null;
                if (++_coolingReadings[i] >= CoolingReadingsNeeded) _sawCooling[i] = true;
                var filling = _filling[i];
                if (filling.Count > 0 && combo.Progress < filling[^1].Progress - 0.05) filling.Clear(); // used again: a new cooldown
                filling.Add((now, combo.Progress));
                filling.RemoveAll(f => now - f.At > SpeedWindow);
                if (Soon(i, now)) show.Add(slot.Number);
                continue;
            }
            _coolingReadings[i] = 0;
            _filling[i].Clear();
            if (_sawCooling[i])
            {
                _sawCooling[i] = false;
                _readyAt[i] = now;
            }
            if (Mode == ComboHintMode.WhileReady || IsRecent(i, now)) show.Add(slot.Number);
        }
        return show;
    }

    private bool IsRecent(int slot, TimeSpan now) => _readyAt[slot] is { } at && now - at < ShowFor;
}

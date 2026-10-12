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

    public ComboHintMode Mode { get; set; } = ComboHintMode.WhenReady;

    /// <summary>How long a <see cref="ComboHintMode.WhenReady"/> hint stays up.</summary>
    public TimeSpan ShowFor { get; set; } = TimeSpan.FromSeconds(3);

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
                continue;
            }
            if (combo is null)
            {
                // Out of combat or fallen: start over, so a combo that is simply ready when the next
                // fight starts doesn't count as having come off cooldown. Merely out of view (an
                // ultimate cutscene, a menu) keeps the state: a cooldown that finished meanwhile
                // is hinted when the panel is back.
                if (slot.Dead || (inView && slot.Level is not null)) (_sawCooling[i], _coolingReadings[i], _readyAt[i]) = (false, 0, null);
                continue;
            }
            if (!combo.Ready)
            {
                _readyAt[i] = null;
                if (++_coolingReadings[i] >= CoolingReadingsNeeded) _sawCooling[i] = true;
                continue;
            }
            _coolingReadings[i] = 0;
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

using EndHud.Vision.Input;

namespace EndHud.Vision.Rotations;

/// <summary>What told whose battle skill a cast was (<see cref="ScreenCastJudge"/>).</summary>
public enum CastSource
{
    /// <summary>The cast flash on that slot's button (<see cref="CastFlashTracker"/>).</summary>
    Flash,

    /// <summary>That slot's button went from available to a countdown across the readings.</summary>
    Cooldown,

    /// <summary>The player's key or pad press for that slot (only with the key reading switch on, <c>ReadSkillPresses</c>).</summary>
    Press,

    /// <summary>An empowered battle skill's flash ending the glow, with no skill point spent (some are free).</summary>
    Empowered,

    /// <summary>Skill points were spent, and nothing told whose cast it was.</summary>
    Unattributed,
}

/// <summary>A battle skill cast.</summary>
/// <param name="Slot">The squad slot (1-4); null when <see cref="CastSource.Unattributed"/>.</param>
/// <param name="At">When: the flash's start, the press, or the reading that saw the points spent.</param>
/// <param name="Paid">Skill points were seen spent for it: certain that a battle skill was cast.</param>
public sealed record SkillCast(int? Slot, TimeSpan At, CastSource Source, bool Paid)
{
    /// <summary>
    /// Sure enough to move the strip past steps not seen done (<see cref="StepFollower"/>): points spent, and the
    /// slot known.
    /// </summary>
    public bool Certain => Paid && Slot is not null;
}

/// <summary>What a reading's judging found: the casts decided, and how each press taken was judged.</summary>
public sealed record CastJudging(IReadOnlyList<SkillCast> Casts, IReadOnlyList<JudgedPress> Presses);

/// <summary>
/// Battle skills cast, from the screen with the player's presses as an assist. Only a battle skill spends skill points,
/// one each, so <b>points dropping by about 1 (0.6 or more) between two readings is a cast</b>; then whose:
/// <list type="number">
/// <item>the cast flash on a slot's button (<see cref="FlashEvent"/>) from 0.5 s before the reading before to 0.25 s
/// after the reading that saw the drop; not on a slot that spent its ultimate then (its button flashes the same), and
/// not on a button that couldn't be read in the reading before (a dimmed one shows the scene through it);</item>
/// <item>else a press of a slot (<see cref="SkillPressJudge"/>, counted) from 0.5 s before to 0.5 s after;</item>
/// <item>else a button that went from available to a countdown;</item>
/// <item>else nobody: <see cref="CastSource.Unattributed"/> (<see cref="StepFollower"/> then marks the current step if
/// it is a battle skill its operator could cast).</item>
/// </list>
/// The flash can come after the reading that saw the drop, so a drop the flash hasn't named yet waits up to 0.6 s.
/// A flash with no drop counts only as an <see cref="FlashKind.EmpoweredCast"/> (the empowered glow ending in a cast;
/// some cost nothing). A press and a screen cast of the same slot within 0.5 s are one cast. A press with no drop
/// within 0.5 s while the points were read is refused (a digit in a chat, a press the game ignored); while they
/// can't be read, a counted press is a cast, though not a certain one.
/// </summary>
public sealed class ScreenCastJudge
{
    /// <summary>Skill points down by this much between readings: a cast (each costs 1; regeneration and gains blur it).</summary>
    internal const double DropAt = 0.6;

    private static readonly TimeSpan FlashBefore = TimeSpan.FromSeconds(0.5), FlashAfter = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan PressWithin = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan WaitForFlash = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(3);

    /// <summary>A flash on a slot, from the readings' flashes (buttons turned into slots).</summary>
    private sealed record Flash(int Slot, TimeSpan At, FlashKind Kind)
    {
        public bool Used { get; set; }
    }

    /// <summary>A press the press judge counted, waiting for a drop to confirm it.</summary>
    private sealed record Press(int Slot, TimeSpan At, bool PointsRead)
    {
        public bool Used { get; set; }
    }

    /// <summary>Points seen spent: the reading before the drop and the one that saw it.</summary>
    private sealed record Drop(PartyState Before, PartyState Now)
    {
        public TimeSpan From => Before.ReadAt ?? TimeSpan.Zero;
        public TimeSpan To => Now.ReadAt ?? TimeSpan.Zero;
    }

    private readonly List<Flash> _flashes = [];
    private readonly List<Press> _presses = [];
    private readonly List<Drop> _drops = [];
    private readonly List<SkillCast> _recent = [];
    private PartyState? _pointsRead;

    /// <summary>A new reading.</summary>
    /// <param name="before">The reading before (presses are judged against it); null for the first.</param>
    /// <param name="presses">Presses made up to <paramref name="now"/>'s time; empty with the key reading off.</param>
    public CastJudging Update(PartyState? before, PartyState now, IReadOnlyList<SkillPress> presses)
    {
        var at = now.ReadAt ?? TimeSpan.Zero;
        var casts = new List<SkillCast>();

        int offset = 4 - Math.Clamp(now.SquadSize, 1, 4); // a squad of fewer than four is drawn from the right
        // Out of battle by the mode switch (exploring, building): a flash there is no battle skill cast, and none
        // told before it is left to count as a free empowered cast.
        if (now.OutOfBattle) _flashes.Clear();
        else foreach (var flash in now.Flashes)
        {
            int slot = flash.Button - offset + 1;
            if (slot is >= 1 and <= 4) _flashes.Add(new Flash(slot, flash.At, flash.Kind));
        }

        var judged = SkillPressJudge.Judge(before, presses);
        bool pointsRead = before?.SkillPoints is not null && now.SkillPoints is not null;
        foreach (var j in judged.Where(j => j.Verdict == PressVerdict.Counted))
            _presses.Add(new Press(j.Press.Slot, j.Press.At, pointsRead));

        // Points spent since the last reading that read them (a cutscene or an effect over the bar in between).
        if (now.SkillPoints is { } points && now.InCombat)
        {
            if (_pointsRead is { SkillPoints: { } was } last && was - points >= DropAt && at - (last.ReadAt ?? at) <= TimeSpan.FromSeconds(1.5))
                for (int n = Math.Max(1, (int)Math.Round(was - points)); n > 0; n--) _drops.Add(new Drop(last, now));
            _pointsRead = now;
        }

        // Whose: oldest drop first.
        foreach (var drop in _drops.ToList())
        {
            if (FlashFor(drop) is { } flash)
            {
                flash.Used = true;
                Add(new SkillCast(flash.Slot, flash.At, CastSource.Flash, Paid: true));
            }
            else if (PressFor(drop) is { } press)
            {
                press.Used = true;
                Add(new SkillCast(press.Slot, press.At, CastSource.Press, Paid: true));
            }
            else if (at - drop.To < WaitForFlash) continue; // the flash may still be told
            else if (CooledDown(drop, now) is { } cooled) Add(new SkillCast(cooled, drop.To, CastSource.Cooldown, Paid: true));
            else Add(new SkillCast(null, drop.To, CastSource.Unattributed, Paid: true));
            _drops.Remove(drop);
        }

        // An empowered cast told without a drop: the free ones.
        foreach (var flash in _flashes.Where(f => f.Kind == FlashKind.EmpoweredCast && !f.Used).ToList())
        {
            flash.Used = true;
            // Its fill already named a drop (a paid empowered cast), or a press did: the same cast.
            if (SameCast(flash.Slot, flash.At) || SpentUltimate(before, now, flash.Slot)) continue;
            Add(new SkillCast(flash.Slot, flash.At, CastSource.Empowered, Paid: false));
        }

        // Presses no drop confirmed: refused where the points were read; a cast of their own where they weren't.
        foreach (var press in _presses.Where(p => !p.Used && at - p.At >= PressWithin).ToList())
        {
            press.Used = true;
            if (!press.PointsRead && !SameCast(press.Slot, press.At)) Add(new SkillCast(press.Slot, press.At, CastSource.Press, Paid: false));
        }

        _flashes.RemoveAll(f => at - f.At > Kept);
        _presses.RemoveAll(p => p.Used && at - p.At > Kept);
        _recent.RemoveAll(c => at - c.At > Kept);
        return new CastJudging(casts.OrderBy(c => c.At).ToList(), judged);

        void Add(SkillCast cast)
        {
            casts.Add(cast);
            _recent.Add(cast);
            // A press of the same slot is the same cast.
            foreach (var p in _presses.Where(p => !p.Used && p.Slot == cast.Slot && (p.At - cast.At).Duration() <= PressWithin)) p.Used = true;
        }
    }

    /// <summary>The fight ended: nothing carries over.</summary>
    public void EndFight()
    {
        _flashes.Clear();
        _presses.Clear();
        _drops.Clear();
        _recent.Clear();
        _pointsRead = null;
    }

    /// <summary>The flash closest to the drop, in its window, on a slot that could have cast.</summary>
    private Flash? FlashFor(Drop drop) =>
        _flashes
            .Where(f => !f.Used && f.Kind is FlashKind.Fill or FlashKind.EmpoweredFill
                && f.At >= drop.From - FlashBefore && f.At <= drop.To + FlashAfter
                && !SpentUltimate(drop.Before, drop.Now, f.Slot)
                && (f.Kind == FlashKind.EmpoweredFill || ButtonRead(drop.Before, f.Slot))
                && !SameCast(f.Slot, f.At))
            .OrderBy(f => (f.At - drop.To).Duration())
            .FirstOrDefault();

    private Press? PressFor(Drop drop) =>
        _presses
            .Where(p => !p.Used && p.At >= drop.From - PressWithin && p.At <= drop.To + PressWithin)
            .OrderBy(p => (p.At - drop.To).Duration())
            .FirstOrDefault();

    /// <summary>A cast of this slot already decided within 0.5 s: the same one, seen twice.</summary>
    private bool SameCast(int slot, TimeSpan at) => _recent.Any(c => c.Slot == slot && (c.At - at).Duration() <= PressWithin);

    /// <summary>The slot's button went from available to a countdown between the reading before the drop and now.</summary>
    private static int? CooledDown(Drop drop, PartyState now)
    {
        for (int i = 0; i < Math.Min(drop.Before.Slots.Count, now.Slots.Count); i++)
            if (drop.Before.Slots[i].BattleSkill == BattleSkillState.Available
                && (drop.Now.Slots.ElementAtOrDefault(i)?.BattleSkill == BattleSkillState.Cooling || now.Slots[i].BattleSkill == BattleSkillState.Cooling))
                return i + 1;
        return null;
    }

    /// <summary>The slot's ultimate was ready before and isn't now: its battle-skill button flashes as it is cast.</summary>
    private static bool SpentUltimate(PartyState? before, PartyState now, int slot) =>
        before?.Slots.ElementAtOrDefault(slot - 1)?.Ultimate?.Ready == true && now.Slots.ElementAtOrDefault(slot - 1)?.Ultimate?.Ready != true;

    /// <summary>The slot's button could be read (available or cooling): a dimmed or covered one shows the scene through it.</summary>
    private static bool ButtonRead(PartyState before, int slot) => before.Slots.ElementAtOrDefault(slot - 1)?.BattleSkill is not null;
}

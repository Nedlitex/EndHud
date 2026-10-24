namespace EndHud.Vision.Rotations;

/// <summary>
/// Which phase of a rotation applies now, from what's on screen. A phase's condition (<c>when</c>)
/// says when it applies: each reading, the first phase in the list whose condition holds is the
/// one shown (the last one, with <see cref="Rotation.LastMatchWins"/>).
/// <list type="bullet">
/// <item>When none holds and every condition could be read, the first phase without a condition
/// is shown: it's the "otherwise" phase. (Before, the phase already shown stayed, so once a phase
/// with a condition had been shown, one without could never come back: a player's 充能轴 I "while
/// Laevatain's ultimate is at most 50%" never gave way to 充能轴 II.)</item>
/// <item>When none holds but some condition can't be read (a cutscene, a reading lost), the phase
/// already shown stays; so does it when every phase has a condition and none holds.</item>
/// <item>At the start, before anything holds, the first phase without a condition is shown, or the
/// first phase when every phase has one.</item>
/// </list>
/// <para>This only picks the phase; it doesn't follow the steps inside it. That is
/// <see cref="StepFollower"/>'s job, from the battle-skill presses when the player has turned on
/// reading their keys (without them the strip shows every step alike).</para>
/// <para>It counts how many times each phase has come onto the strip this fight, for "phase shown"
/// conditions (<see cref="PhaseShownCondition"/>); <see cref="EndFight"/> forgets them.</para>
/// </summary>
public sealed class PhaseSelector(Rotation rotation) : IPhaseHistory
{
    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private readonly AllCondition?[] _conditions = rotation.Phases.Select(p => p.Gated ? new AllCondition(p.When) : null).ToArray();
    private readonly int? _otherwise = Array.FindIndex(rotation.Phases.ToArray(), p => !p.Gated) is var i and >= 0 ? i : null;
    private int? _current;
    private readonly int[] _timesShown = new int[rotation.Phases.Count];

    public Rotation Rotation => rotation;

    /// <summary>Times the phase has come onto the strip this fight (the one on it now included); null for a phase not in the rotation.</summary>
    public int? TimesShown(string phaseId)
    {
        for (int i = 0; i < rotation.Phases.Count; i++)
            if (rotation.Phases[i].Id == phaseId) return _timesShown[i];
        return null;
    }

    /// <summary>The fight ended: the next one starts with nothing shown and every count at 0.</summary>
    public void EndFight()
    {
        _current = null;
        Array.Clear(_timesShown);
    }

    /// <summary>Take a reading; where the strip is now (the first step of the phase that applies).</summary>
    public RotationPosition Update(PartyState state)
    {
        // The conditions see the counts as they were before this reading's switch, so a gate on
        // another phase's count opens on the reading after that phase first shows.
        int? before = _current;
        int? holding = null;
        bool unknown = false;
        for (int i = 0; i < _conditions.Length; i++)
        {
            if (_conditions[i] is not { } condition) continue;
            switch (RotationTracker.Evaluate(condition, state, this))
            {
                case true:
                    holding = i;
                    break;
                case null:
                    unknown = true;
                    break;
            }
            if (holding is not null && !rotation.LastMatchWins) break;
        }

        if (holding is not null) _current = holding;
        else if (!unknown && _otherwise is { } otherwise) _current = otherwise;
        _current ??= _otherwise ?? 0;
        if (_current != before) _timesShown[_current.Value]++; // it came onto the strip
        return new RotationPosition(_played.First(p => p.PhaseIndex == _current), true, []);
    }
}

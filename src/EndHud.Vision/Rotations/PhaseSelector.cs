namespace EndHud.Vision.Rotations;

/// <summary>
/// Which phase of a rotation applies now, from what's on screen. A phase's condition (<c>when</c>)
/// says when it applies: each reading, the first phase in the list whose condition holds is the
/// one shown. When none holds (in between, or something can't be read), the phase already shown
/// stays. At the start, before any condition has held, the first phase without a condition is
/// shown, or the first phase when every phase has one.
/// <para>EndHud can't see which button the player pressed, so this doesn't follow the steps inside
/// a phase (the strip shows them all alike).</para>
/// </summary>
public sealed class PhaseSelector(Rotation rotation)
{
    private readonly IReadOnlyList<PlayedStep> _played = rotation.Played();
    private readonly AllCondition?[] _conditions = rotation.Phases.Select(p => p.Gated ? new AllCondition(p.When) : null).ToArray();
    private int? _current;

    public Rotation Rotation => rotation;

    /// <summary>Take a reading; where the strip is now (the first step of the phase that applies).</summary>
    public RotationPosition Update(PartyState state)
    {
        for (int i = 0; i < _conditions.Length; i++)
        {
            if (_conditions[i] is { } condition && RotationTracker.Evaluate(condition, state) == true)
            {
                _current = i;
                break;
            }
        }
        _current ??= Math.Max(0, Array.FindIndex(_conditions, c => c is null));
        return new RotationPosition(_played.First(p => p.PhaseIndex == _current), true, []);
    }
}

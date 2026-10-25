using System.Windows.Media.Imaging;
using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>Conditions as the strip's badges (<see cref="StripBadge"/>).</summary>
internal static class StripBadges
{
    /// <summary>Where the badges' pictures come from.</summary>
    /// <param name="StepPicture">The picture of the phase's step numbered so (from 1), for "these steps lit".</param>
    internal sealed record Pictures(OperatorLibrary? Library, Func<OperatorInfo, StepAction, BitmapSource?> SkillIcon, Func<OperatorInfo, BitmapSource?> Avatar,
        Func<string, BitmapSource?> StatusIcon, Func<int, BitmapSource?> StepPicture);

    public static IReadOnlyList<StripBadge> Of(IReadOnlyList<StateCondition> conditions, Func<StateCondition, bool?> holds, Pictures pictures) => [];

    public static StripBadge? OwnAbility(RotationStep step, ConditionCheck own, PartyState? state) => null;
}

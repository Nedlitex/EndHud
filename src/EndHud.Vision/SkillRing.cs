namespace EndHud.Vision;

public enum SkillIcon { Original, Changed, Cooling, Unknown }

/// <param name="OperatorId">Whose battle skill (Si).</param>
/// <param name="LeftShare">The grey ring's part not yet drained, 0–1; null when it can't be read.</param>
public sealed record SkillRingReading(string OperatorId, SkillIcon Icon, double? LeftShare);

/// <summary>Battle skills whose button changes while they last. Not read yet.</summary>
public static class SkillRing
{
    public static readonly TimeSpan SiDuration = TimeSpan.FromSeconds(22);

    public static IReadOnlyList<SkillRingReading> Read(Frame frame, PartyState state) => [];

    internal static SkillRingReading ReadSi(Frame frame, int button) => new("Si", SkillIcon.Unknown, null);
}

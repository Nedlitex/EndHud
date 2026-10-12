namespace EndHud.Vision.Operators;

/// <summary>One playable operator in the reference library.</summary>
/// <param name="Id">Stable file-system-safe key, e.g. "Last_Rite".</param>
/// <param name="NameEn">English name, e.g. "Last Rite".</param>
/// <param name="NameZh">Simplified Chinese name as shown in game (from the wiki); English if the wiki has none.</param>
/// <param name="IconFile">Icon file name inside the library's icons folder.</param>
/// <param name="Skills">
/// The operator's skill icons as drawn in the bottom-right HUD (several per kind when the
/// operator has alternate forms). Empty when the wiki has none yet, or for libraries saved
/// before skill icons were collected.
/// </param>
public sealed record OperatorInfo(string Id, string NameEn, string NameZh, string IconFile,
    IReadOnlyList<SkillIcon>? Skills = null)
{
    public IReadOnlyList<SkillIcon> Skills { get; init; } = Skills ?? [];
}

/// <summary>Which of an operator's three active skills an icon belongs to.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SkillKind
{
    /// <summary>Battle skill: the large button in the bottom-right.</summary>
    BattleSkill,

    /// <summary>Combo skill.</summary>
    Combo,

    /// <summary>Ultimate: the small circle above the battle skill button.</summary>
    Ultimate,
}

/// <param name="File">Path inside the library folder, e.g. "skills/Laevatain/Ult-Laevatain.png".</param>
public sealed record SkillIcon(SkillKind Kind, string File);

/// <summary>Result of identifying a squad portrait.</summary>
/// <param name="Score">Similarity in [-1, 1] (normalised cross-correlation) of the best reference.</param>
/// <param name="FromLearned">True when matched against a previously learned in-game portrait, false for a wiki icon.</param>
public sealed record OperatorMatch(OperatorInfo Operator, double Score, bool FromLearned);

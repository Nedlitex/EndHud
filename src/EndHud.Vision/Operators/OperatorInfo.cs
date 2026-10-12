namespace EndHud.Vision.Operators;

/// <summary>One playable operator in the reference library.</summary>
/// <param name="Id">Stable file-system-safe key, e.g. "Last_Rite".</param>
/// <param name="NameEn">English name, e.g. "Last Rite".</param>
/// <param name="NameZh">Simplified Chinese name as shown in game, e.g. "莱万汀"; English if the wiki has none.</param>
/// <param name="IconFile">Icon file name inside the library's icons folder.</param>
public sealed record OperatorInfo(string Id, string NameEn, string NameZh, string IconFile);

/// <summary>Result of identifying a squad portrait.</summary>
/// <param name="Score">Similarity in [-1, 1] (normalised cross-correlation) of the best reference.</param>
/// <param name="FromLearned">True when matched against a previously learned in-game portrait, false for a wiki icon.</param>
public sealed record OperatorMatch(OperatorInfo Operator, double Score, bool FromLearned);

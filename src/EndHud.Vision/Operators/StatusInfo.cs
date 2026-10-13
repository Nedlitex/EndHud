namespace EndHud.Vision.Operators;

/// <summary>
/// A status effect (an enemy debuff such as Heat Infliction or Vulnerable, or a team buff such as
/// Link), by its wiki name, with its icon in the downloaded library's <c>statuses/</c> folder.
/// </summary>
/// <param name="Id">The wiki's English name, e.g. "Heat Infliction"; also the key rotations use.</param>
public sealed record StatusInfo(string Id, string IconFile)
{
    /// <summary>As the game names it in Chinese, when known; otherwise the English name.</summary>
    public string NameZh => StatusNames.Zh(Id) ?? Id;
}

/// <summary>
/// Chinese names of statuses. The wiki only has English names; these come from the community chart
/// "终末地伤害属性特性 Ver1.0" and the owner (see DEVELOPER.md). A status not listed shows in English.
/// </summary>
public static class StatusNames
{
    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["Heat Infliction"] = "灼热附着",
        ["Cryo Infliction"] = "寒冷附着",
        ["Electric Infliction"] = "电磁附着",
        ["Nature Infliction"] = "自然附着",
        ["Heat Burst"] = "灼热爆发",
        ["Cryo Burst"] = "寒冷爆发",
        ["Electric Burst"] = "电磁爆发",
        ["Nature Burst"] = "自然爆发",
        ["Combustion"] = "燃烧",
        ["Electrification"] = "导电",
        ["Solidification"] = "冻结",
        ["Corrosion"] = "腐蚀",
        ["Shatter"] = "碎冰",
        ["Vulnerable"] = "破防",
        ["Breach"] = "碎甲",
        ["Crush"] = "猛击",
        ["Lift"] = "击飞",
        ["Knock Down"] = "倒地",
        ["Link"] = "连击",
    };

    public static string? Zh(string id) => Chinese.GetValueOrDefault(id);
}

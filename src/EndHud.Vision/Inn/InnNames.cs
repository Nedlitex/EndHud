namespace EndHud.Vision.Inn;

/// <summary>Names read by OCR (a dish's, a chef's) matched to the names the data knows.</summary>
internal static class InnNames
{
    // At least this much of the longer name in common, and the next name this much less.
    internal const double MinShare = 0.6, MinLead = 0.2;

    /// <summary>
    /// The key of the name closest to <paramref name="read"/>, letters only (quotes and stray marks
    /// left out): the longest run of letters in order the two share, over the longer's length.
    /// OCR drops or splits a character now and then (白瓜肉汤 for 白瓜肉丸汤, 纟彖 for 缘). Null when
    /// no name is close enough or two are about as close.
    /// </summary>
    public static string? Closest(string read, IEnumerable<(string Key, string Name)> names)
    {
        string bare = Letters(read);
        if (bare.Length < 2) return null;
        var ranked = names
            .Select(n => (n.Key, Share: Letters(n.Name) is { Length: > 0 } name ? Common(bare, name) / (double)Math.Max(bare.Length, name.Length) : 0))
            .OrderByDescending(n => n.Share).ToList();
        if (ranked.Count == 0 || ranked[0].Share < MinShare) return null;
        double next = ranked.Count > 1 ? ranked[1].Share : 0;
        return ranked[0].Share == 1 || ranked[0].Share - next >= MinLead ? ranked[0].Key : null;
    }

    private static string Letters(string s) => new([.. s.Where(char.IsLetter)]);

    /// <summary>The longest common subsequence's length.</summary>
    private static int Common(string a, string b)
    {
        var row = new int[b.Length + 1];
        foreach (char ca in a)
        {
            int diagonal = 0;
            for (int j = 1; j <= b.Length; j++)
            {
                int above = row[j];
                row[j] = ca == b[j - 1] ? diagonal + 1 : Math.Max(row[j], row[j - 1]);
                diagonal = above;
            }
        }
        return row[b.Length];
    }
}

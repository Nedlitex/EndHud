using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>How a line of the inn strip is drawn.</summary>
internal enum InnLineStyle
{
    /// <summary>A kitchen's line, an option.</summary>
    Normal,

    /// <summary>The Full Blaze reminder: the accent colour, bold.</summary>
    Accent,

    /// <summary>Something the helper can't see or do (an unreadable order, what's missing): orange, smaller.</summary>
    Note,

    /// <summary>Starts a card (a gourmand): accent colour, set apart from the lines above.</summary>
    Heading,

    /// <summary>Starts a block within a card (a kitchen's dish types in its setup advice): accent colour, not set apart by a rule.</summary>
    Subheading,

    /// <summary>Secondary text (what was read off a bubble): dimmed.</summary>
    Dim,
}

/// <summary>A piece of a line: words, or a dish's or drink's name with its picture before it.</summary>
/// <param name="PictureId">The dish or drink whose picture goes before the name; null for plain words.</param>
/// <param name="KeepWithNext">A long line wraps, but never between this piece and the next (a marker and its dish, a dish and where it is).</param>
internal sealed record InnSegment(string Text, string? PictureId = null, bool KeepWithNext = false)
{
    public InnSegment Kept() => this with { KeepWithNext = true };
}

internal sealed record InnLine(InnLineStyle Style, IReadOnlyList<InnSegment> Segments)
{
    /// <summary>How far in the line starts, in steps (a row under its heading is 1).</summary>
    public int Indent { get; init; }

    /// <summary>The first segment is a label ("干员："): the rest wrap beside it, never under it.</summary>
    public bool Hanging { get; init; }

    /// <summary>The line's words, pictures left out: what the player reads.</summary>
    public string Text => string.Concat(Segments.Select(s => s.Text));

    public static InnLine Of(InnLineStyle style, params InnSegment[] segments) => new(style, segments);
}

/// <summary>What the inn strip shows: one line after another, top to bottom.</summary>
internal sealed record InnStripModel(IReadOnlyList<InnLine> Lines)
{
    /// <summary>A key that changes when anything drawn changes.</summary>
    public string Key => string.Join("\n", Lines.Select(l => $"{l.Style}:{string.Join("|", l.Segments.Select(s => $"{s.PictureId}={s.Text}"))}"));
}

/// <summary>
/// The inn panel's words during a session: only the gourmands (owner, live 2026-10-20: "remove all hints from the
/// current panel, use the combo instead. the current panel has the requirements of the gourmands"). A card per
/// gourmand: what they asked for in plain words and what on the menu meets it, with where it is. Every other call
/// (the next dish per kitchen, a swap, the last call, Full Blaze) is a banner (<see cref="InnCallView"/>). With no
/// gourmand the panel is empty and put away. What the helper hasn't seen yet (the kitchens, the menu, the drinks) is
/// never said (owner, live 2026-10-20: "do not display '还没看到厨房/菜单…', half of the time it is wrong and it is
/// disturbing"); it's in the diagnostic log. Every string from i18n/zh-CN.xaml.
/// </summary>
internal static class InnStripView
{
    /// <summary>
    /// Options shown per gourmand request: the best and a second choice (the best is also the request's glowing circle,
    /// InnWantsView). Two, not three, since the panel is drawn larger (owner, live 2026-10-20) and the busiest panel, two
    /// gourmands of two requests each, must still fit the window at 1080p (InnStripLayoutTests).
    /// </summary>
    public const int OptionsShown = 2;

    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static InnStripModel Build(InnAdvice advice, Func<string, string> text)
    {
        string T(string key) => text(key);
        string F(string key, params object[] args) => string.Format(text(key), args);

        var lines = new List<InnLine>();
        // Over the open dispatch panel it would cover the dish cards and their mark: put away meanwhile (the gourmands'
        // circles stay, above the panel).
        if (advice.Panel is not null) return new InnStripModel(lines);
        foreach (var note in advice.Gourmands.OrderBy(g => g.Table)) lines.AddRange(GourmandLines(note, advice.ExactKitchens, T, F));
        return new InnStripModel(lines);
    }

    private static InnSegment Dish(Dish dish) => new(dish.Zh, dish.Id);

    private static string KitchenName(int number, Func<string, string> T) => T($"Inn.Kitchen.{number}");

    /// <summary>
    /// A gourmand's card: "gourmand (table 6)", each request in plain words, and what meets it best first
    /// with where it is; for two requests, the order to serve them in. A request not recognised
    /// says so with the text read; one nothing on the menu meets says that.
    /// </summary>
    private static IEnumerable<InnLine> GourmandLines(InnGourmandNote note, IReadOnlySet<int> exactKitchens, Func<string, string> T, Func<string, object[], string> F)
    {
        yield return InnLine.Of(InnLineStyle.Heading, new InnSegment(F("Inn.Gourmand.Title", [note.Table])));
        if (note.Advice is not { } advice)
        {
            yield return InnLine.Of(InnLineStyle.Note, new InnSegment(T("Inn.Gourmand.Unknown")));
            if (note.UnreadText is { Length: > 0 } read) yield return InnLine.Of(InnLineStyle.Dim, new InnSegment(F("Inn.Gourmand.Read", [read])));
            yield break;
        }
        foreach (var request in advice.Requests)
        {
            yield return InnLine.Of(InnLineStyle.Normal, new InnSegment(F("Inn.Gourmand.Wants", [request.Description])));
            if (request.Options.Count == 0)
            {
                // No drink of the menu seen yet: nothing to offer, and "none on the menu" would be wrong; nothing is said.
                if (!note.DrinksUnknown) yield return InnLine.Of(InnLineStyle.Note, new InnSegment(T("Inn.Gourmand.NoneOnMenu")));
                continue;
            }
            var segments = new List<InnSegment> { new InnSegment(T("Inn.Gourmand.Options")).Kept() };
            foreach (var (option, i) in request.Options.Take(OptionsShown).Select((o, i) => (o, i)))
            {
                if (i > 0) segments.Add(new InnSegment(T("Inn.Between")));
                segments.Add(Item(option).Kept());
                segments.Add(new InnSegment(F("Inn.Where.Wrap", [Where(option, exactKitchens, T, F)])));
            }
            yield return new InnLine(InnLineStyle.Normal, segments);
        }
        if (advice.Requests.Count > 1 && advice.Serve is { Count: > 1 } serve)
        {
            var segments = new List<InnSegment> { new InnSegment(T("Inn.Gourmand.Serve")).Kept() };
            for (int i = 0; i < serve.Count; i++)
            {
                if (i > 0) segments.Add(new InnSegment(T("Inn.ReplaceArrow")).Kept());
                segments.Add(Item(serve[i]));
            }
            yield return new InnLine(InnLineStyle.Normal, segments);
        }
    }

    private static InnSegment Item(GourmandOption option) => new(option.Dish?.Zh ?? option.Drink!.Zh, option.Id);

    /// <summary>Where an option is: on the counter, carried, a kitchen and how soon ("about" when that kitchen's queue is an estimate), the drink bar.</summary>
    private static string Where(GourmandOption option, IReadOnlySet<int> exactKitchens, Func<string, string> T, Func<string, object[], string> F)
    {
        int seconds = (int)Math.Ceiling(option.ReadyIn);
        bool exact = option.Kitchen is { } k && exactKitchens.Contains(k);
        string kitchen = option.Kitchen is { } n ? KitchenName(n, T) : "";
        return option.From switch
        {
            GourmandSource.Carried => T("Inn.Where.Carried"),
            GourmandSource.Counter => T("Inn.Where.Counter"),
            GourmandSource.DrinkBar => T("Inn.Where.DrinkBar"),
            GourmandSource.Queued => F(exact ? "Inn.Where.Kitchen" : "Inn.Where.KitchenAbout", [kitchen, seconds]),
            _ => F(exact ? "Inn.Where.ToCook" : "Inn.Where.ToCookAbout", [kitchen, seconds]),
        };
    }

    /// <summary>
    /// What to drag, size and see through while the menu is open out of the inn: a gourmand's card of the inn's own
    /// data (a request and two dishes that meet it), the size a real one is.
    /// </summary>
    public static InnStripModel Sample(InnData? data, Func<string, string> text)
    {
        string T(string key) => text(key);
        string F(string key, params object[] args) => string.Format(text(key), args);
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(T("Inn.Sample"))) };
        if (data is null) return new InnStripModel(lines);
        var example = data.Gourmands.SelectMany(g => g.Requests)
            .Select(r => GourmandCondition.Parse(r.When, data))
            .Select(c => (Condition: c, Dishes: c.ForDrink ? [] : data.Dishes.Where(c.MetBy).Take(2).ToList()))
            .FirstOrDefault(x => x.Dishes.Count > 0);
        if (example.Condition is null) return new InnStripModel(lines);
        lines.Add(InnLine.Of(InnLineStyle.Heading, new InnSegment(F("Inn.Gourmand.Title", 3))));
        lines.Add(InnLine.Of(InnLineStyle.Normal, new InnSegment(F("Inn.Gourmand.Wants", example.Condition.Describe()))));
        var segments = new List<InnSegment> { new InnSegment(T("Inn.Gourmand.Options")).Kept() };
        for (int i = 0; i < example.Dishes.Count; i++)
        {
            if (i > 0) segments.Add(new InnSegment(T("Inn.Between")));
            segments.Add(Dish(example.Dishes[i]).Kept());
            segments.Add(new InnSegment(F("Inn.Where.Wrap", T("Inn.Where.Counter"))));
        }
        lines.Add(new InnLine(InnLineStyle.Normal, segments));
        return new InnStripModel(lines);
    }
}

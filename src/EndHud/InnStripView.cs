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
/// The inn strip's words (plan S7, C3, C4, C13–C17): the advice of one moment of a session, put
/// as the player reads it, every string from i18n/zh-CN.xaml. A vertical list, one line per
/// kitchen with the dishes to queue there in order (ordered ones first, then those to cook ahead,
/// marked), each with its picture; the Full Blaze reminder above the kitchens; then what can't be
/// read, and a card per gourmand: what they asked for in plain words and what on the menu meets it.
/// </summary>
internal static class InnStripView
{
    /// <summary>Options shown per gourmand request: the best few are what the player needs.</summary>
    public const int OptionsShown = 3;

    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    /// <param name="problemKey">Why the inn can only be partly read (<see cref="PartyMonitor.InnProblem"/>), or null.</param>
    public static InnStripModel Build(InnAdvice advice, Func<string, string> text, string? problemKey = null)
    {
        string T(string key) => text(key);
        string F(string key, params object[] args) => string.Format(text(key), args);
        var lines = new List<InnLine>();

        // Nothing but the timer can be read: what to install is the only useful line.
        if (problemKey is not null) return new InnStripModel([InnLine.Of(InnLineStyle.Note, new InnSegment(T(problemKey)))]);

        switch (advice.Blaze)
        {
            case FullBlazeAdvice.StartNow:
                lines.Add(InnLine.Of(InnLineStyle.Accent, new InnSegment(T("Inn.Blaze.StartNow"))));
                break;
            case FullBlazeAdvice.GetReady:
                lines.Add(InnLine.Of(InnLineStyle.Accent, new InnSegment(T("Inn.Blaze.GetReady"))));
                break;
        }

        if (advice.Gap is { } gap)
        {
            lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(T(gap == InnGap.NoKitchens ? "Inn.Gap.NoKitchens" : "Inn.Gap.NoMenu"))));
        }
        else if (advice.Dispatch is { } dispatch)
        {
            foreach (var kitchen in dispatch.Kitchens) lines.AddRange(KitchenLines(kitchen, T, F));
            foreach (var order in dispatch.Waiting)
                lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(T("Inn.Waiting")).Kept(), Dish(order.Dish).Kept(), new InnSegment(F("Inn.ForTable", order.Table))));
            foreach (var order in dispatch.OutOfStock)
                lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(T("Inn.OutOfStock")).Kept(), Dish(order.Dish).Kept(), new InnSegment(F("Inn.ForTable", order.Table))));
        }

        foreach (int table in advice.UnreadableTables.Order())
            lines.Add(InnLine.Of(InnLineStyle.Note, new InnSegment(F("Inn.Unreadable", table))));

        foreach (var note in advice.Gourmands.OrderBy(g => g.Table)) lines.AddRange(GourmandLines(note, advice.ExactKitchens, T, F));

        return new InnStripModel(lines);
    }

    private static InnSegment Dish(Dish dish) => new(dish.Zh, dish.Id);

    private static string KitchenName(int number, Func<string, string> T) => T($"Inn.Kitchen.{number}");

    /// <summary>
    /// A kitchen's line: "kitchen one, queue:" and the dishes in order, those to cook ahead marked;
    /// a dash when there's nothing to queue. A swap replaces the line until the queue changes.
    /// </summary>
    private static IEnumerable<InnLine> KitchenLines(KitchenAdvice kitchen, Func<string, string> T, Func<string, object[], string> F)
    {
        string name = KitchenName(kitchen.Kitchen, T);
        if (kitchen.Replaces.Count > 0)
        {
            foreach (var swap in kitchen.Replaces)
                yield return InnLine.Of(InnLineStyle.Normal,
                    new InnSegment(F("Inn.Replace", [name])).Kept(), new InnSegment(T("Inn.CookAhead")).Kept(), Dish(swap.Remove),
                    new InnSegment(T("Inn.ReplaceArrow")).Kept(), Dish(swap.Put).Kept(), new InnSegment(F("Inn.ForTable", [swap.Table])));
            yield break;
        }
        var segments = new List<InnSegment> { new InnSegment(F("Inn.Queue", [name])).Kept() };
        if (kitchen.Queue.Count == 0) segments.Add(new InnSegment(T("Inn.Nothing")));
        for (int i = 0; i < kitchen.Queue.Count; i++)
        {
            if (i > 0) segments.Add(new InnSegment(T("Inn.Between")));
            if (kitchen.Queue[i].CookAhead) segments.Add(new InnSegment(T("Inn.CookAhead")).Kept());
            segments.Add(Dish(kitchen.Queue[i].Dish));
        }
        yield return new InnLine(InnLineStyle.Normal, segments);
    }

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
                bool forDrink = request.Options.Count == 0 && note.DrinksUnknown;
                yield return InnLine.Of(InnLineStyle.Note, new InnSegment(T(forDrink ? "Inn.Gourmand.NoDrinksSeen" : "Inn.Gourmand.NoneOnMenu")));
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
    /// What to drag, size and see through while the menu is open out of the inn: three kitchens' lines
    /// of the inn's own dishes, the size a real session's strip is.
    /// </summary>
    public static InnStripModel Sample(InnData? data, Func<string, string> text)
    {
        string T(string key) => text(key);
        string F(string key, params object[] args) => string.Format(text(key), args);
        var dishes = data?.Dishes.Take(3).ToList() ?? [];
        var lines = new List<InnLine> { InnLine.Of(InnLineStyle.Heading, new InnSegment(T("Inn.Sample"))) };
        for (int k = 1; k <= KitchenMath.MaxKitchens; k++)
        {
            var segments = new List<InnSegment> { new(F("Inn.Queue", KitchenName(k, T))) };
            if (k - 1 < dishes.Count)
            {
                if (k == KitchenMath.MaxKitchens) segments.Add(new InnSegment(T("Inn.CookAhead")));
                segments.Add(Dish(dishes[k - 1]));
            }
            else segments.Add(new InnSegment(T("Inn.Nothing")));
            lines.Add(new InnLine(InnLineStyle.Normal, segments));
        }
        return new InnStripModel(lines);
    }
}

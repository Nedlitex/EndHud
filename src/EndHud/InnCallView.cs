using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// One call as a banner shows it (<see cref="ComboBanner"/>'s look): a bold title, a caption under it, and the
/// dish's picture where the combo banner has the skill icon.
/// </summary>
/// <param name="Key">Who the banner is (<see cref="InnCall.Key"/>): one with the same key stays up as it is.</param>
/// <param name="Order">Where it goes in the stack: smaller first.</param>
/// <param name="PictureId">The dish whose picture is shown; null for none (Full Blaze).</param>
internal sealed record InnBannerModel(string Key, int Order, string Title, string Caption, string? PictureId);

/// <summary>One row of the banners' stack: a kitchen's row (<paramref name="Banner"/> null while it has no call), or a row for one other banner.</summary>
/// <param name="Row">Who the row is: "kitchen:N", or the banner's own key.</param>
internal sealed record InnBannerRow(string Row, InnBannerModel? Banner)
{
    public bool IsKitchen => Row.StartsWith(KitchenRow, StringComparison.Ordinal);

    public const string KitchenRow = "kitchen:";
}

/// <summary>
/// The banners' rows, kept for the session (owner, 2026-10-21: "the recommendation should stay steady"): each kitchen that
/// has had a banner keeps its row, empty between its calls, so no banner moves when another comes or goes; Full Blaze and
/// the last calls stack below the kitchens, each in its own row while it stands. The overlay draws these rows, and the
/// replay (InnReplayTests) measures them.
/// </summary>
internal sealed class InnBannerRows
{
    private readonly SortedSet<int> _kitchens = [];

    /// <summary>Forgets the kitchens' rows (the session ended).</summary>
    public void Clear() => _kitchens.Clear();

    /// <summary>The rows, top to bottom, for <paramref name="calls"/>; at most <see cref="InnCallView.MostBanners"/>.</summary>
    /// <param name="kitchens">The kitchens that cook this session: each has its row from the start, so none opening later moves another.</param>
    public IReadOnlyList<InnBannerRow> Rows(IReadOnlyList<int> kitchens, IReadOnlyList<InnCall> calls, Func<string, string> text)
    {
        _kitchens.UnionWith(kitchens);
        foreach (var call in calls.Where(c => c.ForKitchen)) _kitchens.Add(call.Kitchen!.Value);
        var rows = new List<InnBannerRow>();
        foreach (int kitchen in _kitchens)
        {
            var call = calls.FirstOrDefault(c => c.ForKitchen && c.Kitchen == kitchen);
            rows.Add(new InnBannerRow($"{InnBannerRow.KitchenRow}{kitchen}", call is null ? null : InnCallView.Of(call, text)));
        }
        // One banner per key (a table given two of a dish at the last call: one banner says it).
        foreach (var banner in calls.Where(c => !c.ForKitchen).Select(c => InnCallView.Of(c, text)).DistinctBy(b => b.Key).OrderBy(b => b.Order))
            rows.Add(new InnBannerRow(banner.Key, banner));
        return [.. rows.Take(InnCallView.MostBanners)];
    }
}

/// <summary>
/// The calls' words (owner, live 2026-10-20: "remove all hints from the current panel, use the combo instead"),
/// every string from i18n/zh-CN.xaml: "厨房二：茄果鳞肉汤", "厨房二 预做：雪绵豆沙", "厨房一 换：雪绵豆沙 → 茄果鳞肉汤",
/// "收尾：肉包子 → 桌3", "开启满堂彩（R）", "满堂彩快满了：先把厨房排满".
/// </summary>
internal static class InnCallView
{
    /// <summary>
    /// The most banners up at once: three kitchens, Full Blaze and three last calls. More last calls wait for one
    /// to be done; the stack stays inside a 1080p window from its default place (InnCallLayoutTests).
    /// </summary>
    public const int MostBanners = 7;

    /// <summary>
    /// A call's banner height and its dish picture's size, at 1080p (scaled with the window): the picture nearly the
    /// banner's height (owner, live 2026-10-20: the combo icon's 44 was "too small, make it very hard for user to
    /// notice"). The fullest stack still fits from its default place at 1080p (InnStripLayoutTests).
    /// </summary>
    public const double BannerHeight = 84, PictureSize = 76;

    /// <param name="text">The i18n lookup (<see cref="Loc.Get"/>; the tests pass zh-CN.xaml's strings).</param>
    public static IReadOnlyList<InnBannerModel> Of(IReadOnlyList<InnCall> calls, Func<string, string> text) =>
        [.. calls.Select(c => Of(c, text)).OrderBy(b => b.Order).Take(MostBanners)];

    public static InnBannerModel Of(InnCall call, Func<string, string> text)
    {
        string F(string key, params object[] args) => string.Format(text(key), args);
        string Kitchen() => text($"Inn.Kitchen.{call.Kitchen}");
        int order = call.Order.Group * 100 + call.Order.Number;
        return call.Kind switch
        {
            InnCallKind.Queue => new(call.Key, order, F("Inn.Call.Queue", Kitchen(), call.Dish!.Zh), F("Inn.Call.ForTable", call.Table!), call.Dish.Id),
            InnCallKind.CookAhead => new(call.Key, order, F("Inn.Call.CookAhead", Kitchen(), call.Dish!.Zh), text("Inn.Call.CookAheadCaption"), call.Dish.Id),
            InnCallKind.Swap => new(call.Key, order, F("Inn.Call.Swap", Kitchen(), call.Remove!.Zh, call.Dish!.Zh), F("Inn.Call.ForTable", call.Table!), call.Dish.Id),
            InnCallKind.LastCall => new(call.Key, order, F("Inn.Call.LastCall", call.Dish!.Zh, call.Table!),
                text(call.Matches ? "Inn.Call.LastCallCaption" : "Inn.Call.LastCallWrong"), call.Dish.Id),
            InnCallKind.BlazeNow => new(call.Key, order, text("Inn.Blaze.StartNow"), text("Inn.Call.BlazeNowCaption"), null),
            _ => new(call.Key, order, text("Inn.Blaze.GetReady"), text("Inn.Call.BlazeSoonCaption"), null),
        };
    }

    /// <summary>A call's banner as the overlay draws it: <see cref="ComboBanner"/>'s look, the dish's picture in the icon slot.</summary>
    /// <param name="picture">A dish's picture by id; null when the package has none.</param>
    public static Border Banner(InnBannerModel model, Func<string, BitmapSource?> picture) =>
        ComboBanner.Create(ComboBanner.Content(null, model.PictureId is { } id ? picture(id) : null, model.Title, model.Caption, iconSize: PictureSize),
            BannerHeight);

    /// <summary>What to drag while the menu is open out of a session: one banner with a dish of the inn's own.</summary>
    public static InnBannerModel Sample(InnData? data, Func<string, string> text) =>
        new("sample", 0, text("Inn.Call.Sample"), text("Inn.Call.SampleCaption"), data?.Dishes.FirstOrDefault()?.Id);
}

using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using EndHud.Vision;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud.Tests;

/// <summary>
/// What the inn tests share: the repository's operator package (the inn's data, dish pictures and
/// roster, as EndHud ships them) unpacked once into the git-ignored .cache, the app's own zh-CN
/// strings, and short ways to write table cards, panels and readings.
/// </summary>
internal static partial class InnTestKit
{
    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln not found above " + AppContext.BaseDirectory);
    }

    private static readonly Lazy<string> PackageFolder = new(() =>
    {
        // Its own folder: the Vision tests unpack theirs into .cache/operator-package, maybe at the same time.
        string folder = Path.Combine(RepositoryRoot(), ".cache", "operator-package-app");
        byte[] package = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "operators", OperatorPackage.FileName));
        if (OperatorPackage.Installed(folder) != OperatorPackage.ReadInfo(package)) OperatorPackage.Install(folder, package);
        return folder;
    });

    /// <summary>The unpacked operator package (reference folder).</summary>
    public static string Folder => PackageFolder.Value;

    private static readonly Lazy<OperatorLibrary> LibraryOnce = new(() =>
        OperatorLibrary.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-app-tests", "inn-" + Guid.NewGuid().ToString("N")), Folder).GetAwaiter().GetResult());

    public static OperatorLibrary Library => LibraryOnce.Value;

    public static InnData Inn => Library.Inn ?? throw new InvalidOperationException("The operator package has no inn data.");

    public static Dish Dish(string id) => Inn.Dishes.Single(d => d.Id == id);

    public static InnDrink Drink(string id) => Inn.Drinks.Single(d => d.Id == id);

    // ---- The app's strings ----

    /// <summary>XML's whitespace, all compiling drops around a string: an ideographic space (Inn.Between) is kept, as in the app.</summary>
    private static readonly char[] XmlWhitespace = [' ', '\t', '\r', '\n'];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Strings = new(() =>
    {
        string xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "EndHud", "i18n", "zh-CN.xaml"));
        return StringEntry().Matches(xaml).ToDictionary(m => m.Groups["key"].Value,
            m => WebUtility.HtmlDecode(m.Groups["preserve"].Success ? m.Groups["value"].Value : m.Groups["value"].Value.Trim(XmlWhitespace)));
    });

    /// <summary>A string of zh-CN.xaml; a missing key fails the test (the player would see the raw key).</summary>
    public static string Text(string key) =>
        Strings.Value.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"i18n/zh-CN.xaml has no \"{key}\"");

    public static string Format(string key, params object[] args) => string.Format(Text(key), args);

    [GeneratedRegex(@"<sys:String x:Key=""(?<key>[^""]+)""(?<preserve> xml:space=""preserve"")?>(?<value>.*?)</sys:String>", RegexOptions.Singleline)]
    private static partial Regex StringEntry();

    // ---- Readings ----

    public static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public static TableCard Empty(int table) => new(table, TableState.Empty, [], []);

    /// <summary>A table's order: one item per id (null: a picture not told apart), none served yet.</summary>
    public static TableCard Order(int table, params string?[] ids) =>
        new(table, TableState.Order, [.. ids.Select(id => new OrderItem(id, Served: false))], [.. ids.Select(_ => false)]);

    public static TableCard Order(int table, params OrderItem[] items) =>
        new(table, TableState.Order, items, [.. items.Select(i => i.Served)]);

    /// <summary>Six cards: the ones given, the rest empty.</summary>
    public static IReadOnlyList<TableCard?> Tables(params TableCard?[] cards)
    {
        var all = Enumerable.Range(1, TableCardReader.Tables).Select(n => (TableCard?)Empty(n)).ToArray();
        foreach (var card in cards.OfType<TableCard>()) all[card.Table - 1] = card;
        return all;
    }

    /// <summary>Six cards exactly as given (null = unreadable).</summary>
    public static IReadOnlyList<TableCard?> TablesAsRead(params TableCard?[] cards) => cards;

    /// <summary>The dispatch panel showing <paramref name="kitchen"/> of <paramref name="kitchens"/>, no chefs named, the menu's dishes and the queue.</summary>
    public static DispatchPanel Panel(int kitchen, int kitchens, IReadOnlyList<string> menu, params (string Id, int? SecondsLeft)[] queue) =>
        new(kitchen, kitchens, [null, null], [null, null], null,
            [.. menu.Select(id => new KitchenDish(id, Dish(id).Zh, Dish(id).CookSeconds))],
            [.. queue.Select(q => new QueueSlot(true, q.Id, q.SecondsLeft)), .. Enumerable.Repeat(new QueueSlot(false, null, null), KitchenQueue.Slots - queue.Length)]);

    public static InnReading Session(double at, double left = 299, IReadOnlyList<TableCard?>? tables = null, FireMeter? fire = null,
        DispatchPanel? panel = null, GourmandBubble? bubble = null) =>
        new(panel is null ? InnScreenKind.Session : InnScreenKind.DispatchPanel, S(at), new SessionClock(S(left), left <= 0),
            tables ?? Tables(), panel is null ? fire : null, panel, bubble);

    /// <summary>The menu screen with these dishes and drinks on the menu, unlimited.</summary>
    public static InnReading MenuScreen(double at, params string[] ids) =>
        new(InnScreenKind.Menu, S(at), Menu: new InnMenu(MenuTab.AllDishes, null, null,
            [.. ids.Select(id => new MenuCard(id, id, OnMenu: true, Unlimited: true, Stock: null))]));

    /// <summary>The kitchens' setup screen: these kitchens staffed (a chef name read, no operator matched).</summary>
    public static InnReading SetupScreen(double at, params int[] staffed) =>
        new(InnScreenKind.Setup, S(at), Setup: new InnSetup("theme", null, null, [], [],
            [.. Enumerable.Range(1, KitchenMath.MaxKitchens).Select(n => new SetupKitchen(n, 2, [null, null], staffed.Contains(n) ? ["chef", null] : [null, null]))]));

    /// <summary>
    /// A judge for the setup planning's tests written before plans on screen stayed (and for fake advice that
    /// isn't worked out for the inputs): a plan shown meanwhile while it fits as before, and any newer plan
    /// that looks different replaces it. The real one is <see cref="InnPlanJudge"/>.
    /// </summary>
    public sealed class ReplacingJudge : IInnPlanJudge
    {
        private static readonly InnPlanJudge Real = new(new InnPlanner(Inn));

        public bool Fits(InnPlanInput input, InnPlanInput planFor, InnSetupAdvice shown, bool chefsAsPlanned) =>
            Real.Fits(input, planFor, shown, chefsAsPlanned: false);

        public bool ClearlyBetter(InnPlanInput input, InnSetupAdvice candidate, InnSetupAdvice shown) => true;

        public InnSetupAdvice Restate(InnPlanInput input, InnSetupAdvice shown) => Real.Restate(input, shown);
    }

    public static readonly IInnPlanJudge Replacing = new ReplacingJudge();

    /// <summary>
    /// A clock for <see cref="InnSetupPlanning"/> that never moves: every plan on screen counts as just shown
    /// (<see cref="InnSetupPlanning.RefinedWithin"/>), so a clearly better one may still replace it however long the test took.
    /// </summary>
    public static TimeSpan Frozen() => TimeSpan.Zero;

    /// <summary>A time long after every reading of a test: the menu's list has been still for long (<see cref="InnSetupPlanning.ScrollSettle"/>).</summary>
    public static readonly TimeSpan Settled = TimeSpan.FromHours(1);

    /// <summary>
    /// A reading clock for sessions whose tests give no gaps: the game read all along, every moment
    /// between two readings a frame of something (so time between inn readings is time elsewhere).
    /// </summary>
    public static TimeSpan ReadAllAlong(TimeSpan now) => now;

    /// <param name="readingTime">Time spent reading the game (<see cref="ReadingClock.At"/>); <see cref="ReadAllAlong"/> when not given.</param>
    public static InnSession NewSession(List<string>? warnings = null, Func<TimeSpan, TimeSpan>? readingTime = null) =>
        new(Inn, Library.Operators, readingTime ?? ReadAllAlong, log: _ => { }, warn: m => warnings?.Add(m));
}

using System.Globalization;
using EndHud.Vision;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud.Cli;

/// <summary>
/// <c>trace-inn</c>: what a recording of the inn shows over time, as text only. Per sampled frame
/// the screen, the session's timer and each table card (its state and the ids of what it ordered);
/// then every order once (table, time, items) and how often each dish and drink was ordered, with
/// the menu as the screens list it (the menu screen's chosen cards, the dispatch panel's dishes) and
/// the theme as the setup screen states it. It never writes an image: it is for measuring how guests
/// choose (which dishes a theme makes them prefer), not for looking at the recording.
/// </summary>
internal static class InnTrace
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task<int> RunAsync(string path, double everySeconds, double from, double? to, double endMargin, string? referenceFolder)
    {
        var text = InnText.TryCreate();
        if (text is null)
        {
            Console.Error.WriteLine("the Simplified Chinese OCR language isn't installed (Windows Settings > Language)");
            return 1;
        }
        string reference = referenceFolder ?? RepositoryPackage();
        var data = InnData.Load(reference);
        if (data is null)
        {
            Console.Error.WriteLine("the reference folder has no inn data");
            return 1;
        }
        var library = await OperatorLibrary.LoadAsync(Path.Combine(Path.GetTempPath(), "endhud-trace-inn-user"), reference);
        var pictures = await InnPictures.LoadAsync(reference, data);
        var tableReader = new TableCardReader(text, pictures);
        var panelReader = new DispatchPanelReader(text, pictures, data, library.Operators);
        var setupReader = new SetupReader(text, data, library.Operators);
        var menuReader = new MenuReader(text, data);

        var video = await VideoFile.OpenAsync(path);
        double end = Math.Min(to ?? double.MaxValue, video.Duration.TotalSeconds - endMargin);
        Console.WriteLine($"# video {video.Width}x{video.Height}, {F(video.Duration.TotalSeconds)} s; sampled {F(from)}-{F(end)} s every {F(everySeconds)} s");
        Console.WriteLine("t,screen,timer," + string.Join(",", Enumerable.Range(1, TableCardReader.Tables).Select(i => $"table{i}")) + ",panel");

        var orders = new OrderTracker();
        var menu = new SortedSet<string>(StringComparer.Ordinal);
        var panelDishes = new SortedSet<string>(StringComparer.Ordinal);
        InnSetup? setup = null;
        for (double t = from; t < end; t += everySeconds)
        {
            var frame = await video.GetFrameAsync(TimeSpan.FromSeconds(t));
            var kind = await InnScreen.DetectAsync(text, frame);
            string timer = "", tables = string.Join(",", Enumerable.Repeat("", TableCardReader.Tables)), panelCell = "";
            switch (kind)
            {
                case InnScreenKind.Session or InnScreenKind.DispatchPanel:
                {
                    var clock = InnScreen.ReadClock(frame);
                    timer = clock is { } c ? c.Left.ToString(@"m\:ss", Invariant) : "";
                    var cards = await tableReader.ReadAsync(frame);
                    tables = string.Join(",", cards.Select(Cell));
                    orders.See(t, clock?.Left, cards);
                    if (kind == InnScreenKind.DispatchPanel && await panelReader.ReadAsync(frame) is { } panel)
                    {
                        var ids = panel.Dishes.Select(d => d.DishId).OfType<string>().ToList();
                        panelDishes.UnionWith(ids);
                        panelCell = $"k{panel.Kitchen}:" + string.Join("+", ids);
                    }
                    break;
                }
                case InnScreenKind.Menu:
                    if (await menuReader.ReadAsync(frame) is { } read)
                        menu.UnionWith(read.Cards.Where(card => card.OnMenu && card.Id is not null).Select(card => card.Id!));
                    break;
                case InnScreenKind.Setup:
                    if (await setupReader.ReadAsync(frame, InnScreenKind.Setup) is { } s && (setup is null || Score(s) > Score(setup))) setup = s;
                    break;
            }
            Console.WriteLine($"{F(t)},{kind?.ToString() ?? ""},{timer},{tables},{panelCell}");
        }

        Console.WriteLine();
        if (setup is not null)
        {
            Console.WriteLine($"# theme: {setup.ThemeName} ({setup.Theme?.Id ?? "unknown"})");
            Console.WriteLine($"# description: {setup.Description ?? ""}");
            Console.WriteLine($"# objective: {setup.Objective ?? ""}");
            Console.WriteLine($"# menu rules: {string.Join(" | ", setup.MenuRules)}");
            Console.WriteLine($"# hot: {string.Join(" ", setup.HotTraits)}");
        }
        Console.WriteLine($"# menu screen, chosen: {string.Join(" ", menu)}");
        Console.WriteLine($"# dispatch panel dishes: {string.Join(" ", panelDishes)}");
        Console.WriteLine();
        Console.WriteLine("order,t,timer,table,items");
        int n = 0;
        foreach (var order in orders.Orders)
            Console.WriteLine($"{++n},{F(order.At)},{order.Timer?.ToString(@"m\:ss", Invariant) ?? ""},{order.Table},{string.Join("+", order.Items.Select(i => i ?? "?"))}");
        Console.WriteLine();
        Console.WriteLine($"# orders: {orders.Orders.Count} (gourmand orders, by category not by dish: {orders.GourmandOrders})");
        var dishIds = data.Dishes.Select(d => d.Id).ToHashSet();
        var counts = orders.Orders.SelectMany(o => o.Items).GroupBy(i => i ?? "?").OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal);
        Console.WriteLine("kind,id,zh,count");
        foreach (var g in counts)
        {
            string zh = data.Dishes.FirstOrDefault(d => d.Id == g.Key)?.Zh ?? data.Drinks.FirstOrDefault(d => d.Id == g.Key)?.Zh ?? "";
            Console.WriteLine($"{(dishIds.Contains(g.Key) ? "dish" : g.Key == "?" ? "unread" : "drink")},{g.Key},{zh},{g.Count()}");
        }
        return 0;

        static int Score(InnSetup s) => s.HotTraits.Count + (s.Theme is null ? 0 : 1) + (s.Description is null ? 0 : 1) + s.MenuRules.Count;
    }

    private static string F(double v) => v.ToString("F1", Invariant);

    private static string Cell(TableCard? card) => card switch
    {
        null => "?",
        { State: TableState.Order } => string.Join("+", card.Items.Select(i => (i.Id ?? "?") + (i.Served ? ":s" : ":w"))),
        _ => card.State.ToString(),
    };

    /// <summary>The repository's operator package, unpacked once into the same git-ignored cache the tests use.</summary>
    private static string RepositoryPackage()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) continue;
            string folder = Path.Combine(dir.FullName, ".cache", "operator-package");
            byte[] package = File.ReadAllBytes(Path.Combine(dir.FullName, "operators", OperatorPackage.FileName));
            if (OperatorPackage.Installed(folder) != OperatorPackage.ReadInfo(package)) OperatorPackage.Install(folder, package);
            return folder;
        }
        throw new DirectoryNotFoundException("EndHud.sln not found above the tool or the working directory; pass --reference <folder>");
    }

    /// <summary>One order as it was first seen, its items filled in as they are read.</summary>
    internal sealed record SeenOrder(double At, TimeSpan? Timer, int Table, string?[] Items);

    /// <summary>
    /// Turns the table cards over time into orders, each once: a table's order starts when its card
    /// shows an order after anything else (empty, ordering, eating, paying), or shows other items
    /// than the order it held. An unreadable card (null) changes nothing; an item read only on a
    /// later frame is filled in then.
    /// </summary>
    internal sealed class OrderTracker
    {
        private readonly SeenOrder?[] _current = new SeenOrder?[TableCardReader.Tables];
        private readonly bool[] _gourmand = new bool[TableCardReader.Tables];

        /// <summary>How soon after an order's first sight more pictures may still appear on its card.</summary>
        private const double GrowSeconds = 3;

        public List<SeenOrder> Orders { get; } = [];

        public int GourmandOrders { get; private set; }

        public void See(double at, TimeSpan? timer, IReadOnlyList<TableCard?> cards)
        {
            for (int i = 0; i < cards.Count && i < _current.Length; i++)
            {
                if (cards[i] is not { } card) continue;
                if (card.State == TableState.GourmandOrder)
                {
                    if (!_gourmand[i]) GourmandOrders++;
                    _gourmand[i] = true;
                    _current[i] = null;
                    continue;
                }
                _gourmand[i] = false;
                if (card.State != TableState.Order)
                {
                    _current[i] = null;
                    continue;
                }
                var items = card.Items.Select(item => item.Id).ToArray();
                if (_current[i] is { } held && held.Items.Length == items.Length
                    && held.Items.Zip(items).All(p => p.First is null || p.Second is null || p.First == p.Second))
                {
                    for (int j = 0; j < items.Length; j++) held.Items[j] ??= items[j];
                    continue;
                }
                // A second picture fading in a moment after the first: the same order, now whole.
                if (_current[i] is { } partial && partial.Items.Length < items.Length && at - partial.At <= GrowSeconds
                    && partial.Items.Zip(items).All(p => p.First is null || p.Second is null || p.First == p.Second))
                {
                    var whole = partial with { Items = [.. items.Select((id, j) => id ?? (j < partial.Items.Length ? partial.Items[j] : null))] };
                    Orders[Orders.IndexOf(partial)] = whole;
                    _current[i] = whole;
                    continue;
                }
                var order = new SeenOrder(at, timer, i + 1, items);
                Orders.Add(order);
                _current[i] = order;
            }
        }
    }
}

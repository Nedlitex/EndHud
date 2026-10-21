using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>What the setup advice can't be worked out without, said to the player instead of a guess.</summary>
internal enum InnSetupGap
{
    /// <summary>The setup screen shows, but its theme couldn't be read (something covers it), and none is remembered.</summary>
    ThemeUnreadable,

    /// <summary>No setup screen seen since the theme list (EndHud started on the menu, say).</summary>
    NoSetup,

    /// <summary>The setup screen was read without its kitchens.</summary>
    NoKitchens,
}

/// <summary>What a piece of setup advice works from: the planner's input, or what's missing.</summary>
/// <param name="Input">Null when anything is missing (<paramref name="Gaps"/>): no plan from half a reading.</param>
/// <param name="Key">Changes when the input does: the plan is worked out again only then.</param>
/// <param name="RulesNotUnderstood">Menu requirement lines (as read) the planner couldn't take in: the plan ignores them, and the advice says so.</param>
internal sealed record InnSetupInputs(IReadOnlyList<InnSetupGap> Gaps, InnPlanInput? Input, string Key, IReadOnlyList<string> RulesNotUnderstood)
{
    /// <summary>
    /// For the menu: no chef is placed in any kitchen yet, so the plan picks the chefs as well as the
    /// menu (and the menu advice says so). Always false for the chefs' advice, which picks them anyway.
    /// </summary>
    public bool ChefsPlanned { get; init; }

    /// <summary>
    /// When the menu screen last showed a card not seen before (on <see cref="InnClock"/>): the player is
    /// scrolling the list while this is recent (<see cref="InnSetupPlanning.ScrollSettle"/>). Null when none was.
    /// </summary>
    public TimeSpan? NewCardAt { get; init; }
}

/// <summary>
/// The setup as the player has chosen it so far (owner, live test 2026-10-19: "remember which theme,
/// cook, menu etc currently picked, UNTIL go back to pick theme page (or exit the inn mode)"), in
/// memory only. The durable-store checklist:
/// <list type="bullet">
/// <item><b>Create:</b> from readings: the setup screen gives the theme (name, rules, hot types, objective) and
/// the kitchens with their places and placed chefs; the chef picker its kitchen; the menu screen each card
/// seen (on the menu or not, its stock), the counters and the menu requirements.</item>
/// <item><b>Read:</b> the setup advice (<see cref="ForSetup"/>, <see cref="ForMenu"/>) and the session's
/// kitchens and menu (<see cref="InnSession"/>). A part never read is a gap, never a guess.</item>
/// <item><b>Correct:</b> a newer clear reading of a part replaces it (a card by its id, the kitchens as
/// a whole, the requirements number by number). A reading that fails changes nothing: a page
/// without the theme never makes the theme unknown.</item>
/// <item><b>Remove:</b> everything at once, on the theme list (a new setup begins) or when no inn screen
/// has shown for <see cref="InnSession.ForgetAfter"/> (the inn was left: a fight, the open world).</item>
/// <item><b>Protect / carry:</b> nothing outlives the inn visit; nothing is written to disk.</item>
/// </list>
/// </summary>
internal sealed class InnSetupMemory(InnData data, IReadOnlyList<OperatorInfo> roster)
{
    private readonly Dictionary<string, MenuCard> _menu = [], _offered = [];
    private MenuRequirements? _requirements;
    private (int Chosen, int Allowed)? _dishCounter, _drinkCounter;
    private bool _setupUnreadable;
    private TimeSpan? _newCardAt; // when the menu screen last showed a card not seen before

    /// <summary>The last clear reading of the setup screen (its kitchens kept from an earlier reading when this one had none); null when none since the theme list.</summary>
    public InnSetup? Setup { get; private set; }

    /// <summary>The dishes and drinks on the menu (the menu screen's red frames), by id.</summary>
    public IReadOnlyDictionary<string, MenuCard> Menu => _menu;

    /// <summary>The kitchen whose chef picker was last read (kitchen one = 1); null when none.</summary>
    public int? PickerKitchen { get; private set; }

    /// <summary>Takes in a reading of a setup screen (the setup screen, a chef picker, the menu screen).</summary>
    public void Take(InnReading reading)
    {
        // The theme is unreadable only while the setup screen showing it is; elsewhere the last clear reading stands.
        if (reading.Screen != InnScreenKind.Setup) _setupUnreadable = false;
        switch (reading.Screen)
        {
            case InnScreenKind.Setup:
                if (reading.Fresh) _setupUnreadable = reading.Setup is null;
                if (reading.Setup is { } setup) Setup = Merge(Setup, setup);
                break;
            case InnScreenKind.ChefPicker:
                // Read on this frame: its kitchen, or none when the title couldn't be read (never the
                // last picker's, whose advice would be another kitchen's). Between reads it stands.
                if (reading.Fresh) PickerKitchen = reading.PickerKitchen;
                break;
            case InnScreenKind.Menu:
                foreach (var read in reading.Menu?.Cards ?? [])
                {
                    if (read.Id is null) continue;
                    if (!_offered.ContainsKey(read.Id)) _newCardAt = reading.At;
                    // A stock left unread this time is the stock read before (it doesn't change on this screen).
                    var card = read is { Unlimited: false, Stock: null } && _offered.TryGetValue(read.Id, out var before)
                        ? read with { Unlimited = before.Unlimited, Stock = before.Stock }
                        : read;
                    _offered[read.Id] = card;
                    if (card.OnMenu) _menu[read.Id] = card;
                    else _menu.Remove(read.Id);
                }
                if (reading.Menu?.Dishes is { } dishes) _dishCounter = dishes;
                if (reading.Menu?.Drinks is { } drinks) _drinkCounter = drinks;
                if (reading.Requirements is { } requirements) _requirements = Merge(_requirements, requirements);
                break;
        }
    }

    /// <summary>Forgets everything (the theme list; the inn left).</summary>
    public void Forget()
    {
        Setup = null;
        _menu.Clear();
        _offered.Clear();
        _requirements = null;
        _dishCounter = _drinkCounter = null;
        _setupUnreadable = false;
        PickerKitchen = null;
        _newCardAt = null;
    }

    /// <summary>
    /// A newer reading of the setup screen over what's remembered (owner, live 2026-10-19: the advice
    /// "refreshes all the time, it does not need to, keep it cached until changed"). OCR misses a
    /// different part of the same screen from frame to frame; a part a reading misses is not a change.
    /// Of the same theme (by its data id, else its name as read), the theme's parts are kept where
    /// the reading has nothing (the objective, the text, the rules, Si's assist) or less (fewer hot
    /// traits found); another theme replaces them all. Kitchens: <see cref="Merge(SetupKitchen?, SetupKitchen)"/>,
    /// and a kitchen whose title the reading missed is kept as it was; a reading without kitchens (the theme
    /// list) keeps the remembered ones. Another theme (as above: by its data id, else its name) is
    /// another setup: its kitchens are as read, nothing of the last setup's chefs carried over
    /// (owner, 2026-10-19: chefs of an earlier setup advised as "set" on empty kitchens).
    /// </summary>
    internal static InnSetup Merge(InnSetup? old, InnSetup read)
    {
        if (old is null) return read;
        bool sameTheme = read.Theme is { } theme && old.Theme is { } before ? theme.Id == before.Id : read.ThemeName == old.ThemeName;
        bool anotherTheme = !sameTheme;
        var kitchens = read.Kitchens is null ? (anotherTheme ? null : old.Kitchens)
            : old.Kitchens is null || anotherTheme ? read.Kitchens
            : [.. read.Kitchens.Select(k => Merge(old.Kitchens.FirstOrDefault(o => o.Number == k.Number), k))
                .Concat(old.Kitchens.Where(o => read.Kitchens.All(k => k.Number != o.Number)))
                .OrderBy(k => k.Number)];
        if (!sameTheme) return read with { Kitchens = kitchens };
        return read with
        {
            ThemeName = old.Theme is not null && read.Theme is null ? old.ThemeName : read.ThemeName,
            Theme = read.Theme ?? old.Theme,
            Objective = read.Objective ?? old.Objective,
            MenuRules = read.MenuRules.Count > 0 ? read.MenuRules : old.MenuRules,
            HotTraits = read.HotTraits.Count >= old.HotTraits.Count ? read.HotTraits : old.HotTraits,
            Kitchens = kitchens,
            // The theme's text doesn't change: a reading that lost its last sentence (what guests prefer) is a miss, not news.
            Description = read.Description is null || (!InnObjective.StatesPreference(read.Description) && InnObjective.StatesPreference(old.Description))
                ? old.Description : read.Description,
            SiChoicePercent = read.SiChoicePercent ?? old.SiChoicePercent,
        };
    }

    /// <summary>
    /// A newer reading of a kitchen over what's remembered of it: its places and how many are taken
    /// when the reading couldn't tell; a chef whose name the reading missed in a place still taken
    /// keeps the name read before (in place order). A place read empty (its grey bar: fewer taken, 0
    /// for a kitchen showing only its "assign operator +" button) is a fact, not a miss: it drops the name. Only a place
    /// that couldn't be read (<see cref="SetupKitchen.ChefsPlaced"/> null) keeps what was read before.
    /// </summary>
    internal static SetupKitchen Merge(SetupKitchen? old, SetupKitchen read)
    {
        if (old is null) return read;
        int? taken = read.ChefsPlaced ?? old.ChefsPlaced;
        var chefs = read.Chefs.ToArray();
        var names = read.ChefNames.ToArray();
        int named = chefs.Count(c => c is not null);
        // The names read before that this reading lacks, for the places still taken but not named.
        var missing = old.Chefs.Select((id, i) => (id, name: i < old.ChefNames.Count ? old.ChefNames[i] : null))
            .Where(o => o.id is not null && !chefs.Contains(o.id)).ToList();
        for (int i = 0; i < chefs.Length && missing.Count > 0 && named < (taken ?? int.MaxValue); i++)
        {
            if (chefs[i] is not null) continue;
            (chefs[i], names[i]) = missing[0];
            missing.RemoveAt(0);
            named++;
        }
        return new SetupKitchen(read.Number, read.ChefSlots ?? old.ChefSlots, chefs, names) { ChefsPlaced = taken };
    }

    /// <summary>A newer reading of the requirements, what it missed taken from the older (the numbers don't change, only their colours).</summary>
    private static MenuRequirements Merge(MenuRequirements? old, MenuRequirements read) => old is null ? read : new MenuRequirements(
        read.Rating ?? old.Rating, read.MinTypes ?? old.MinTypes, read.MinDishes ?? old.MinDishes, read.MinDrinks ?? old.MinDrinks,
        read.DishRules.Count >= old.DishRules.Count ? read.DishRules : old.DishRules, read.Lines);

    /// <summary>
    /// The kitchens as the setup screen showed them, each with its places and the chefs placed there (kept by
    /// the plan): the named ones, then one unnamed place for each place seen taken whose chef wasn't
    /// named. A kitchen's places as read; when they couldn't be told, its taken places (a kitchen
    /// showing neither may be locked: left out, the cautious side).
    /// </summary>
    private List<PlanKitchen> SetupKitchens() =>
    [
        .. (Setup?.Kitchens ?? []).Select(k =>
        {
            var named = k.Chefs.OfType<string>().Select(id => roster.FirstOrDefault(o => o.Id == id)).OfType<OperatorInfo>()
                .DistinctBy(o => o.Id).Take(KitchenMath.MaxChefs).ToList();
            int taken = Math.Max(named.Count, k.ChefsPlaced ?? 0);
            int places = Math.Min(KitchenMath.MaxChefs, k.ChefSlots ?? taken);
            var placed = named.Cast<OperatorInfo?>().Concat(Enumerable.Repeat<OperatorInfo?>(null, Math.Max(0, taken - named.Count))).Take(places).ToList();
            return new PlanKitchen(k.Number, places) { Placed = placed };
        }).Where(k => k.Places > 0).Take(KitchenMath.MaxKitchens),
    ];

    /// <summary>
    /// What the chefs' advice works from (the setup screen's columns, the chef picker): the theme, its hot
    /// types and rules, and the kitchens with their places, the chefs already placed kept. Built in:
    /// every operator in the library who can cook (Si and the Endministrator can't), and every dish
    /// and drink the Inn Rating unlocks, with those a theme's stars unlock (whether the player has
    /// them can't be seen: the advice names substitutes). The menu screen, when seen, refines it:
    /// each card's stock, the rating and the rules as the menu requirements state them.
    /// </summary>
    public InnSetupInputs ForSetup()
    {
        if (Setup is null)
            return _setupUnreadable
                ? new InnSetupInputs([InnSetupGap.ThemeUnreadable], null, "theme-unreadable", [])
                : new InnSetupInputs([InnSetupGap.NoSetup], null, "no-setup", []);
        var kitchens = SetupKitchens();
        if (kitchens.Count == 0) return new InnSetupInputs([InnSetupGap.NoKitchens], null, "no-kitchens", []);
        return Inputs(kitchens, "chefs");
    }

    /// <summary>
    /// What the menu advice works from (the menu screen): as <see cref="ForSetup"/>, but for the chefs actually
    /// placed: only kitchens with a chef, each exactly as it is. With no chef placed anywhere, the
    /// chefs are planned too (<see cref="InnSetupInputs.ChefsPlanned"/>).
    /// </summary>
    public InnSetupInputs ForMenu()
    {
        var setup = ForSetup();
        if (setup.Input is not { } input) return setup;
        var staffed = input.Kitchens.Where(k => k.Placed.Count > 0).Select(k => k with { Places = k.Placed.Count }).ToList();
        return staffed.Count == 0 ? setup with { Key = "menu+" + setup.Key, ChefsPlanned = true } : Inputs(staffed, "menu");
    }

    private InnSetupInputs Inputs(IReadOnlyList<PlanKitchen> kitchens, string purpose)
    {
        var setup = Setup!;
        var r = _requirements;
        int rating = Math.Clamp(r?.Rating ?? setup.Theme?.Rating ?? KitchenMath.MaxRating, 1, KitchenMath.MaxRating);
        var chefs = roster.Where(o => !data.ChefBonuses.NotChefs.Contains(o.Id)).ToList();
        int? Stock(string id) => _offered.TryGetValue(id, out var card) && !card.Unlimited ? card.Stock : null;
        // What the player has: once the menu screen has shown dishes, those it showed (the player's own list,
        // owner: "it never even picked any one that I have"); before, every dish the rating unlocks and those a
        // theme's stars do. Drinks alike, by the drinks seen.
        bool dishesSeen = data.Dishes.Any(d => _offered.ContainsKey(d.Id)), drinksSeen = data.Drinks.Any(d => _offered.ContainsKey(d.Id));
        var dishes = data.Dishes.Where(d => dishesSeen ? _offered.ContainsKey(d.Id) : d.Unlock.Rating is not { } needs || needs <= rating)
            .Select(d => new MenuDish(d, Stock(d.Id))).ToList();
        var drinks = data.Drinks.Where(d => drinksSeen ? _offered.ContainsKey(d.Id) : d.Unlock.Rating is not { } needs || needs <= rating)
            .Select(d => new MenuDrink(d, d.Unlimited ? null : Stock(d.Id))).ToList();

        // The theme's rules (a trait's or a dish type's): as the setup screen states them, and as the menu screen does.
        var setupRules = DishRule.ParseAll(setup.MenuRules, data);
        var dishRules = setupRules.Rules.Concat(r?.DishRules ?? [])
            .DistinctBy(t => $"{t.Count}:{string.Join("/", t.AnyOf)}").ToList();
        var known = MenuRules.OfRating(rating, dishRules);
        int minDishes = r?.MinDishes ?? known?.MinDishes ?? 1, minDrinks = r?.MinDrinks ?? known?.MinDrinks ?? 0, minTypes = r?.MinTypes ?? known?.MinTypes ?? 1;
        // The counters' "N/8" give the most allowed; unread, the rating's (8 of each at Rating 4).
        int maxDishes = _dishCounter?.Allowed ?? known?.MaxDishes ?? MenuRules.MostSeen, maxDrinks = _drinkCounter?.Allowed ?? known?.MaxDrinks ?? MenuRules.MostSeen;
        var rules = new MenuRules(minDishes, Math.Max(minDishes, maxDishes), minDrinks, Math.Max(minDrinks, maxDrinks), minTypes, dishRules);
        // Si's 品质把控 as its line states it; unread, none (the cautious side: no choice-dish chance counted that may not be there).
        var input = new InnPlanInput(InnPlanner.HotTraitsOf(setup.HotTraits, setup.Theme), rating, SiQualityPercent: setup.SiChoicePercent ?? 0,
            InnState.MaxTables, kitchens, chefs, dishes, drinks, rules)
        {
            ChoiceDishTarget = InnObjective.ChoiceDishTarget(setup.Objective),
            ChoiceTipsRaised = InnObjective.RaisesChoiceTips(setup.Description),
            Preferred = InnPlanner.PreferenceOf(setup.Description, setup.HotTraits, setup.Theme, data),
        };
        // Everything the plan depends on and nothing else, each part in a fixed order: the same setup gives the same key.
        string key = string.Join("|", purpose,
            string.Join(",", input.HotTraits.Order(StringComparer.Ordinal)), string.Join(",", input.Preferred.Order(StringComparer.Ordinal)), rating, input.SiQualityPercent, input.ChoiceDishTarget, input.ChoiceTipsRaised,
            string.Join(",", kitchens.Select(k => $"{k.Number}:{k.Places}:{string.Join("+", k.Placed.Select(c => c?.Id ?? "?"))}")),
            string.Join(",", dishes.Select(d => $"{d.Dish.Id}:{d.Stock}").Order()), string.Join(",", drinks.Select(d => $"{d.Drink.Id}:{d.Stock}").Order()),
            $"{rules.MinDishes}-{rules.MaxDishes}/{rules.MinDrinks}-{rules.MaxDrinks}/{rules.MinTypes}",
            string.Join(",", dishRules.Select(t => $"{t.Count}:{string.Join("/", t.AnyOf)}").Order(StringComparer.Ordinal)), string.Join(",", setupRules.NotUnderstood));
        return new InnSetupInputs([], input, key, setupRules.NotUnderstood) { NewCardAt = _newCardAt };
    }
}

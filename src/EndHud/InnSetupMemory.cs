using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>What the setup advice can't be worked out without, said to the player instead of a guess.</summary>
internal enum InnSetupGap
{
    /// <summary>营业设定 shows, but its theme couldn't be read (something covers it), and none is remembered.</summary>
    ThemeUnreadable,

    /// <summary>No 营业设定 seen since the theme list (EndHud started on the menu, say).</summary>
    NoSetup,

    /// <summary>营业设定 was read without its kitchens.</summary>
    NoKitchens,
}

/// <summary>What a piece of setup advice works from: the planner's input, or what's missing.</summary>
/// <param name="Input">Null when anything is missing (<paramref name="Gaps"/>): no plan from half a reading.</param>
/// <param name="Key">Changes when the input does: the plan is worked out again only then.</param>
/// <param name="RulesNotUnderstood">菜单要求 lines (as read) the planner couldn't take in: the plan ignores them, and the advice says so.</param>
internal sealed record InnSetupInputs(IReadOnlyList<InnSetupGap> Gaps, InnPlanInput? Input, string Key, IReadOnlyList<string> RulesNotUnderstood)
{
    /// <summary>
    /// For the menu: no chef is placed in any kitchen yet, so the plan picks the chefs as well as the
    /// menu (and the menu advice says so). Always false for the chefs' advice, which picks them anyway.
    /// </summary>
    public bool ChefsPlanned { get; init; }
}

/// <summary>
/// The setup as the player has chosen it so far (owner, live test 2026-10-19: "remember which theme,
/// cook, menu etc currently picked, UNTIL go back to pick theme page (or exit the inn mode)"), in
/// memory only. The durable-store checklist:
/// <list type="bullet">
/// <item><b>Create:</b> from readings: 营业设定 gives the theme (name, rules, hot types, objective) and
/// the kitchens with their places and placed chefs; the chef picker its kitchen; 菜单设定 each card
/// seen (on the menu or not, its stock), the counters and 菜单要求.</item>
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

    /// <summary>The last clear reading of 营业设定 (its kitchens kept from an earlier reading when this one had none); null when none since the theme list.</summary>
    public InnSetup? Setup { get; private set; }

    /// <summary>The dishes and drinks on the menu (菜单设定's red frames), by id.</summary>
    public IReadOnlyDictionary<string, MenuCard> Menu => _menu;

    /// <summary>The kitchen whose chef picker was last read (厨房一 = 1); null when none.</summary>
    public int? PickerKitchen { get; private set; }

    /// <summary>Takes in a reading of a setup screen (营业设定, a chef picker, 菜单设定).</summary>
    public void Take(InnReading reading)
    {
        // The theme is unreadable only while 营业设定 showing it is; elsewhere the last clear reading stands.
        if (reading.Screen != InnScreenKind.Setup) _setupUnreadable = false;
        switch (reading.Screen)
        {
            case InnScreenKind.Setup:
                if (reading.Fresh) _setupUnreadable = reading.Setup is null;
                if (reading.Setup is { } setup)
                    Setup = setup.Kitchens is null && Setup?.Kitchens is { } kitchens ? setup with { Kitchens = kitchens } : setup;
                break;
            case InnScreenKind.ChefPicker:
                if (reading.PickerKitchen is { } kitchen) PickerKitchen = kitchen;
                break;
            case InnScreenKind.Menu:
                foreach (var card in reading.Menu?.Cards ?? [])
                {
                    if (card.Id is null) continue;
                    _offered[card.Id] = card;
                    if (card.OnMenu) _menu[card.Id] = card;
                    else _menu.Remove(card.Id);
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
    }

    /// <summary>A newer reading of the requirements, what it missed taken from the older (the numbers don't change, only their colours).</summary>
    private static MenuRequirements Merge(MenuRequirements? old, MenuRequirements read) => old is null ? read : new MenuRequirements(
        read.Rating ?? old.Rating, read.MinTypes ?? old.MinTypes, read.MinDishes ?? old.MinDishes, read.MinDrinks ?? old.MinDrinks,
        read.DishRules.Count > 0 ? read.DishRules : old.DishRules, read.Lines);

    /// <summary>
    /// The kitchens as 营业设定 showed them, each with its places and the chefs placed there (kept by
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
    /// What the chefs' advice works from (营业设定's columns, the chef picker): the theme, its hot
    /// types and rules, and the kitchens with their places, the chefs already placed kept. Built in:
    /// every operator in the library who can cook (Si and the Endministrator can't), and every dish
    /// and drink the Inn Rating unlocks, with those a theme's stars unlock (whether the player has
    /// them can't be seen: the advice names substitutes). The menu screen, when seen, refines it:
    /// each card's stock, the rating and the rules as 菜单要求 states them.
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
    /// What the menu advice works from (菜单设定): as <see cref="ForSetup"/>, but for the chefs actually
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
        var dishes = data.Dishes.Where(d => d.Unlock.Rating is not { } needs || needs <= rating || _offered.ContainsKey(d.Id))
            .Select(d => new MenuDish(d, Stock(d.Id))).ToList();
        var drinks = data.Drinks.Where(d => d.Unlock.Rating is not { } needs || needs <= rating || _offered.ContainsKey(d.Id))
            .Select(d => new MenuDrink(d, d.Unlimited ? null : Stock(d.Id))).ToList();

        // The theme's rules (a trait's or a dish type's): as 营业设定 states them, and as the menu screen does.
        var setupRules = DishRule.ParseAll(setup.MenuRules, data);
        var dishRules = setupRules.Rules.Concat(r?.DishRules ?? [])
            .DistinctBy(t => $"{t.Count}:{string.Join("/", t.AnyOf)}").ToList();
        var known = MenuRules.OfRating(rating, dishRules);
        int minDishes = r?.MinDishes ?? known?.MinDishes ?? 1, minDrinks = r?.MinDrinks ?? known?.MinDrinks ?? 0, minTypes = r?.MinTypes ?? known?.MinTypes ?? 1;
        // The counters' "N/8" give the most allowed; unread, the rating's (8 of each at Rating 4).
        int maxDishes = _dishCounter?.Allowed ?? known?.MaxDishes ?? MenuRules.MostSeen, maxDrinks = _drinkCounter?.Allowed ?? known?.MaxDrinks ?? MenuRules.MostSeen;
        var rules = new MenuRules(minDishes, Math.Max(minDishes, maxDishes), minDrinks, Math.Max(minDrinks, maxDrinks), minTypes, dishRules);
        var input = new InnPlanInput(InnPlanner.HotTraitsOf(setup.HotTraits, setup.Theme), rating, SiQualityPercent: 0, InnState.MaxTables,
            kitchens, chefs, dishes, drinks, rules);
        string key = string.Join("|", purpose,
            string.Join(",", input.HotTraits), rating,
            string.Join(",", kitchens.Select(k => $"{k.Number}:{k.Places}:{string.Join("+", k.Placed.Select(c => c?.Id ?? "?"))}")),
            string.Join(",", dishes.Select(d => $"{d.Dish.Id}:{d.Stock}").Order()), string.Join(",", drinks.Select(d => $"{d.Drink.Id}:{d.Stock}").Order()),
            $"{rules.MinDishes}-{rules.MaxDishes}/{rules.MinDrinks}-{rules.MaxDrinks}/{rules.MinTypes}",
            string.Join(",", dishRules.Select(t => $"{t.Count}:{string.Join("/", t.AnyOf)}")), string.Join(",", setupRules.NotUnderstood));
        return new InnSetupInputs([], input, key, setupRules.NotUnderstood);
    }
}

using System.Text.RegularExpressions;
using EndHud.Vision.Operators;

namespace EndHud.Vision.Inn;

/// <summary>A kitchen the session has and how many chef places it has (1 or 2).</summary>
public sealed record PlanKitchen(int Number, int Places)
{
    /// <summary>
    /// The chefs the player has already put in it, kept as they are (owner, 2026-10-19): an operator,
    /// or null for a place seen taken by a chef whose name couldn't be read. None by default: a
    /// kitchen with nothing placed.
    /// </summary>
    public IReadOnlyList<OperatorInfo?> Placed { get; init; } = [];
}

/// <summary>
/// "At least <paramref name="Count"/> dishes with any of these traits, or of any of these types":
/// 至少有2道带有[素食]或[滋补]特性的菜品 (养身心), 至少有1道[汤羹]类菜品 (舌上清流).
/// </summary>
/// <param name="AnyOf">Trait ids (<see cref="InnTrait.Id"/>) or dish types (<see cref="DishType"/> by name, "Soup"), as a theme's preferences name them.</param>
public sealed partial record DishRule(int Count, IReadOnlyList<string> AnyOf)
{
    /// <summary>
    /// The rule a 菜单要求 line states, or null when the line isn't one: "至少有N道带有[A]或[B]特性的菜品"
    /// (traits by their Chinese names in brackets, test_yuanyuan2's 养身心) or "至少有N道[A]类菜品"
    /// (a dish type, the owner's 舌上清流; the type's icon stands in the brackets and may be read as
    /// a stray letter, so a bracket is matched by the type name it contains).
    /// </summary>
    public static DishRule? Parse(string line, InnData data)
    {
        string text = line.Replace(" ", "");
        if (TraitRuleText().Match(text) is { Success: true } traitRule)
        {
            var traits = Brackets().Matches(traitRule.Groups[2].Value)
                .Select(m => data.Traits.FirstOrDefault(t => t.Zh == m.Groups[1].Value)?.Id)
                .ToList();
            if (traits.Count == 0 || traits.Any(t => t is null)) return null;
            return new DishRule(int.Parse(traitRule.Groups[1].Value), [.. traits!]);
        }
        if (TypeRuleText().Match(text) is { Success: true } typeRule)
        {
            var named = Brackets().Matches(typeRule.Groups[2].Value).Select(m => m.Groups[1].Value).ToList();
            var types = named.Select(n => data.DishTypes.Where(t => n.Contains(t.Zh, StringComparison.Ordinal)).Select(t => t.Id.ToString()).ToList()).ToList();
            if (types.Count == 0 || types.Any(t => t.Count != 1)) return null;
            return new DishRule(int.Parse(typeRule.Groups[1].Value), [.. types.Select(t => t[0])]);
        }
        return null;
    }

    /// <summary>Whether <paramref name="dish"/> counts towards the rule: it has one of the traits, or is of one of the types.</summary>
    public bool Matches(Dish dish) => dish.Traits.Any(AnyOf.Contains) || AnyOf.Contains(dish.Type.ToString());

    /// <summary>
    /// The rules in 菜单要求's lines as read (top to bottom), and what of them isn't understood. A rule
    /// may wrap over two lines: the lines are joined and cut where each rule starts ("至少…"). The
    /// Inn Rating's own wordings ("至少需要…") are understood elsewhere (<see cref="MenuRequirementsReader"/>)
    /// and are not rules here.
    /// </summary>
    public static DishRulesRead ParseAll(IReadOnlyList<string> lines, InnData data)
    {
        // One line can state two rules joined by 和 (海陆宴: "至少有1道带有[肉食]特性的菜品和1道带有[水产]
        // 特性的菜品"): the second is a rule of its own, as if it started "至少有".
        string text = SecondRule().Replace(string.Concat(lines.Select(l => l.Replace(" ", ""))), "至少有");
        var rules = new List<DishRule>();
        var notUnderstood = new List<string>();
        foreach (string part in RuleStart().Split(text).Where(p => p.Length > 0))
        {
            if (Parse(part, data) is { } rule) rules.Add(rule);
            else if (!part.StartsWith("至少需要", StringComparison.Ordinal)) notUnderstood.Add(part);
        }
        return new DishRulesRead(rules, notUnderstood);
    }

    [GeneratedRegex(@"至少有?(\d+)道带有(.+?)特性的菜品")]
    private static partial Regex TraitRuleText();

    [GeneratedRegex(@"至少有?(\d+)道(.+?)类的?菜品")]
    private static partial Regex TypeRuleText();

    [GeneratedRegex(@"(?=至少)")]
    private static partial Regex RuleStart();

    /// <summary>"…的菜品和1道带有…": the 和 starting a second rule on the same line.</summary>
    [GeneratedRegex(@"(?<=菜品)和(?=\d+道)")]
    private static partial Regex SecondRule();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex Brackets();
}

/// <summary>菜单要求 as read: the rules understood, and each rule that isn't (as read).</summary>
public sealed record DishRulesRead(IReadOnlyList<DishRule> Rules, IReadOnlyList<string> NotUnderstood);

/// <summary>
/// What the menu must be (菜单要求 and the menu screen's counters "N/8"). The numbers are passed by
/// the caller from what the screens show; a theme's rules are read by <see cref="DishRule.Parse"/>.
/// </summary>
/// <param name="MinTypes">Dish types (主食, 菜肴, 汤羹, 甜品) on the menu at least ("≥3 types" at Rating 4).</param>
public sealed record MenuRules(int MinDishes, int MaxDishes, int MinDrinks, int MaxDrinks, int MinTypes, IReadOnlyList<DishRule> DishRules)
{
    /// <summary>The most dishes, and drinks, a menu has been seen to allow (the menu screen's counters "N/8" at Rating 4).</summary>
    public const int MostSeen = 8;

    /// <summary>
    /// An Inn Rating's own rules, for when the menu screen's 菜单要求 hasn't been read, with the
    /// theme's <paramref name="dishRules"/>. Known only for Rating 4, as test_yuanyuan2's menu screen
    /// states them: at least 3 dish types, 6 dishes and 3 drinks, and 8 of each at most. Null for a
    /// rating not seen yet (never guessed).
    /// </summary>
    public static MenuRules? OfRating(int rating, IReadOnlyList<DishRule> dishRules) =>
        rating == 4 ? new MenuRules(6, MostSeen, 3, MostSeen, 3, dishRules) : null;
}

/// <summary>Everything the planner works from.</summary>
/// <param name="HotTraits">热门特性 as read (trait ids), or the theme's preferences when none were read (<see cref="InnPlanner.HotTraitsOf"/>).</param>
/// <param name="Rating">Inn Rating, 1 to <see cref="KitchenMath.MaxRating"/>.</param>
/// <param name="SiQualityPercent">Si's 品质把控 assist (5 at level II, 0 without it).</param>
/// <param name="Tables">How many tables the inn has (6 at Rating 4 in the recordings).</param>
/// <param name="Kitchens">The kitchens and their chef places.</param>
/// <param name="Chefs">The operators the player has (anyone who can't cook is left out by the planner).</param>
/// <param name="Dishes">The unlocked dishes and their stock.</param>
/// <param name="Drinks">The unlocked drinks and their stock.</param>
public sealed record InnPlanInput(IReadOnlyList<string> HotTraits, int Rating, int SiQualityPercent, int Tables,
    IReadOnlyList<PlanKitchen> Kitchens, IReadOnlyList<OperatorInfo> Chefs, IReadOnlyList<MenuDish> Dishes, IReadOnlyList<MenuDrink> Drinks,
    MenuRules Rules)
{
    /// <summary>
    /// The theme's objective "出售N道[珍馐]菜品" (海陆宴: 6): the choice dishes the session should sell
    /// (<see cref="InnObjective.ChoiceDishTarget"/>). A plan expected to reach it beats one that isn't,
    /// whatever their income. Null: the objective asks for something else, or wasn't read.
    /// </summary>
    public int? ChoiceDishTarget { get; init; }

    /// <summary>The theme says choice dishes earn more tips (海陆宴: "珍馐赏钱加成提升"), <see cref="InnObjective.RaisesChoiceTips"/>.</summary>
    public bool ChoiceTipsRaised { get; init; }

    /// <summary>
    /// What the theme makes guests order more often (its text: "客人更倾向带有[素食]、[滋补]特性的菜品",
    /// <see cref="InnObjective.GuestPreference"/>): trait ids and dish types. A dish with any of them is
    /// ordered <see cref="InnPlanner.PreferenceWeight"/> times as often as another; a drink with one of
    /// the traits likewise among the drinks. Empty: the theme states no preference, guests pick
    /// uniformly. Required: whether guests lean towards some dishes changes the whole plan, so every
    /// caller says it (<see cref="InnPlanner.PreferenceOf"/>).
    /// </summary>
    public required IReadOnlyList<string> Preferred { get; init; }
}

/// <summary>What a theme's objective and text ask of choice dishes (珍馐), as the setup screen states them.</summary>
public static partial class InnObjective
{
    /// <summary>"出售6道[珍馐]菜品" → 6 (the 珍馐 icon between 道 and 珍馐 reads as anything); null for another objective.</summary>
    public static int? ChoiceDishTarget(string? objective)
    {
        if (objective is null) return null;
        var match = ChoiceDishText().Match(objective.Replace(" ", ""));
        return match.Success && int.Parse(match.Groups[1].Value) is var count and > 0 ? count : null;
    }

    /// <summary>Whether a theme's text says choice dishes earn more ("珍馐赏钱加成提升").</summary>
    public static bool RaisesChoiceTips(string? description) => description is not null && description.Replace(" ", "").Contains("珍馐赏钱加成提升", StringComparison.Ordinal);

    /// <summary>
    /// What a theme's text says guests prefer, as trait ids and dish types (<see cref="InnPlanInput.Preferred"/>),
    /// in the order named; null when the text names no preference. Both wordings seen in the game:
    /// "客人更倾向带有[素食]、[滋补]特性的菜品" (养身心, traits in brackets) and "客人更倾向汤羹类菜品"
    /// (舌上清流, a dish type, its icon perhaps in brackets). The sentence may be cut short by the
    /// reading ("…[滋补]特性的"); a name the data doesn't know makes the whole preference unread (never guessed).
    /// </summary>
    public static IReadOnlyList<string>? GuestPreference(string? description, InnData data)
    {
        if (description is null) return null;
        var match = PreferenceText().Match(description.Replace(" ", ""));
        if (!match.Success) return null;
        string named = match.Groups[1].Value;
        var bracketed = PreferenceBrackets().Matches(named).Select(m => m.Groups[1].Value).ToList();
        IEnumerable<(int At, string Id)> Found(IEnumerable<(string Zh, string Id)> names) =>
            names.Select(n => (At: named.IndexOf(n.Zh, StringComparison.Ordinal), n.Id)).Where(f => f.At >= 0);
        var ids = match.Groups[2].Value == "特性"
            ? Found(data.Traits.Where(t => t.Zh is not null).Select(t => (t.Zh!, t.Id)))
            : Found(data.DishTypes.Select(t => (t.Zh, t.Id.ToString())));
        var list = ids.OrderBy(f => f.At).Select(f => f.Id).Distinct().ToList();
        // Every bracket must name something known: "[素食]、[某某]" with one unknown is not a preference read whole.
        if (list.Count == 0 || bracketed.Count > list.Count) return null;
        return list;
    }

    /// <summary>Whether a theme's text has its sentence on what guests prefer ("客人更倾向…"), however much of it was read.</summary>
    public static bool StatesPreference(string? description) => description?.Replace(" ", "").Contains("更倾向", StringComparison.Ordinal) == true;

    [GeneratedRegex(@"出售(\d+)道.{0,3}?珍馐")]
    private static partial Regex ChoiceDishText();

    /// <summary>"客人更倾向" then what (带有 … for traits), up to 特性 or 类.</summary>
    [GeneratedRegex(@"更倾向(?:于)?(?:带有)?(.{1,24}?)(特性|类)")]
    private static partial Regex PreferenceText();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex PreferenceBrackets();
}

/// <summary>How a plan scores under the planner's model.</summary>
/// <param name="Income">Expected income per 5-minute session: prices with the 热门 bonus, and the tips a choice dish (珍馐) is taken to bring (<see cref="InnPlanner.ChoiceDishTipShare"/>).</param>
/// <param name="Orders">Orders served in the session.</param>
/// <param name="KitchenBound">True when the kitchens, not the guests, limit the orders.</param>
/// <param name="ChoiceDishes">Expected choice dishes (珍馐) in the session.</param>
public sealed record InnPlanScore(double Income, double Orders, bool KitchenBound, double ChoiceDishes)
{
    public static readonly InnPlanScore Zero = new(0, 0, false, 0);

    /// <summary>The expected choice dishes reach the theme's objective (<see cref="InnPlanInput.ChoiceDishTarget"/>); true when there is none.</summary>
    public bool MeetsTarget { get; init; } = true;

    /// <summary>Better: reaching the objective, then more income, then (within a cent) more choice dishes.</summary>
    public bool BetterThan(InnPlanScore other) =>
        MeetsTarget != other.MeetsTarget ? MeetsTarget
        : Income > other.Income + InnPlanner.Tolerance || (Math.Abs(Income - other.Income) <= InnPlanner.Tolerance && ChoiceDishes > other.ChoiceDishes + 1e-9);
}

public enum PlanReasonKind
{
    /// <summary>A dish's value per kitchen-second where it cooks fastest (<see cref="PlanReason.Value"/>).</summary>
    DishValue,

    /// <summary>A dish matches this many 热门 traits (<see cref="PlanReason.Value"/>).</summary>
    HotTraits,

    /// <summary>A dish counts towards a trait rule (<see cref="PlanReason.Rule"/>).</summary>
    MeetsRule,

    /// <summary>A rule no menu of the available dishes can meet (<see cref="PlanReason.Rule"/>; -1 the types or counts).</summary>
    RuleUnmet,

    /// <summary>A kitchen left without chefs: none left to put there (it can't cook, <see cref="KitchenMath.Cooks"/>).</summary>
    KitchenUnstaffed,

    /// <summary>The guests limit the orders, not the kitchens.</summary>
    GuestBound,

    /// <summary>A dish or drink whose stock runs out before the session ends (<see cref="PlanReason.Value"/> = orders short).</summary>
    ShortOfStock,

    /// <summary>
    /// The theme's choice-dish objective (<see cref="InnPlanInput.ChoiceDishTarget"/>) isn't expected to be
    /// reached by any plan tried (<see cref="PlanReason.Value"/> = the choice dishes expected).
    /// </summary>
    ChoiceTargetUnreached,
}

/// <summary>Why the plan is what it is, as data (the panel words it).</summary>
/// <param name="Id">The dish or drink, when it's about one.</param>
/// <param name="Kitchen">The kitchen, when it's about one.</param>
/// <param name="Rule">Index into <see cref="MenuRules.DishRules"/>, when it's about a rule.</param>
public sealed record PlanReason(PlanReasonKind Kind, string? Id = null, int? Kitchen = null, int? Rule = null, double Value = 0);

/// <summary>A kitchen of the plan: its number and the chefs to put in it.</summary>
public sealed record PlannedKitchen(int Number, IReadOnlyList<OperatorInfo> Chefs);

/// <summary>The planner's suggestion for the session.</summary>
/// <param name="Opening">What to cook ahead in every kitchen the moment the session starts.</param>
/// <param name="RulesMet">False when no menu of the available dishes meets every rule (the reasons say which).</param>
public sealed record InnPlan(IReadOnlyList<PlannedKitchen> Kitchens, IReadOnlyList<MenuDish> Dishes, IReadOnlyList<MenuDrink> Drinks,
    DispatchAdvice? Opening, InnPlanScore Score, bool RulesMet, IReadOnlyList<PlanReason> Reasons)
{
    /// <summary>The plan played in the simulator (<see cref="InnPlanner.Simulate"/>); null for a plan by the model alone (<see cref="InnPlanner.PlanByModel"/>).</summary>
    public InnSimulatedScore? Simulated { get; init; }
}

/// <summary>A plan played in the simulator, the means over its sessions (<see cref="InnPlanner.Simulate"/>).</summary>
/// <param name="Income">Money earned on the orders served: dishes with the 热门 bonus and their choice-dish tips, and drinks.</param>
/// <param name="Orders">Orders served.</param>
/// <param name="ChoiceDishes">Choice dishes (珍馐) expected among the dishes served.</param>
/// <param name="MeetsTarget">The choice dishes reach the theme's objective (<see cref="InnPlanInput.ChoiceDishTarget"/>); true when there is none.</param>
public sealed record InnSimulatedScore(double Income, double Orders, double ChoiceDishes, bool MeetsTarget)
{
    /// <summary>Better: reaching the objective, then more income, then (within a cent) more choice dishes.</summary>
    public bool BetterThan(InnSimulatedScore other) =>
        MeetsTarget != other.MeetsTarget ? MeetsTarget
        : Income > other.Income + InnPlanner.Tolerance || (Math.Abs(Income - other.Income) <= InnPlanner.Tolerance && ChoiceDishes > other.ChoiceDishes + 1e-9);
}

/// <summary>
/// The pre-session planner (plan S4): which chefs to put in which kitchen and which dishes and
/// drinks to put on the menu, to earn the most in a session under every menu rule.
/// <para>
/// The model (from the measurements of test_yuanyuan1, plan S1): the kitchens, not the guests,
/// are the bottleneck. Guests pick from the menu uniformly, but for the dishes the theme's text says
/// they prefer (<see cref="InnPlanInput.Preferred"/>), picked <see cref="PreferenceWeight"/> times as
/// often (measured, trace-inn); an order is 1 dish + 1 drink
/// 75 %, 2 dishes 20 %, 1 dish 5 %, so 1.2 dishes and 0.75 drinks. Each dish is cooked in the
/// kitchens side by side, each share where it costs least, so that the kitchens finish together
/// (a small linear program, <see cref="Balance"/>); the orders served are what the kitchens cook
/// in the session, or what the tables can bring, whichever is less. A dish is worth its price ×
/// (1 + 10 % per matched 热门 trait); tips aren't modelled (owner), and a dish's choice-dish (珍馐)
/// chance only breaks ties (owner: "only track it is a 珍馐").
/// </para>
/// <para>
/// The search: chefs into kitchens by a beam search over bonus profiles (operators with the same
/// bonuses are one profile), the menu by greedy add and then add/remove/swap until nothing
/// improves; the two alternate until the plan stops improving.
/// </para>
/// </summary>
public sealed class InnPlanner(InnData inn)
{
    /// <summary>Two incomes this close are equal (then the choice-dish chance decides).</summary>
    public const double Tolerance = 0.01;

    /// <summary>The session is 5:00 (recording).</summary>
    public const double SessionSeconds = InnSimulator.SessionSeconds;

    /// <summary>Dishes per order: 1 dish + 1 drink 75 %, 2 dishes 20 %, 1 dish 5 % (20 orders of test_yuanyuan1).</summary>
    public const double DishesPerOrder = 0.75 * 1 + 0.20 * 2 + 0.05 * 1;

    /// <summary>Drinks per order: the 75 % of orders with a drink; never 2 drinks, never a drink alone (test_yuanyuan1).</summary>
    public const double DrinksPerOrder = 0.75;

    /// <summary>A dish or drink is worth 10 % more per matched 热门 trait (the theme screen: 热门特性 +10 % each).</summary>
    public const double HotTraitBonus = 0.10;

    /// <summary>
    /// How many times as often a guest picks a dish the theme says guests prefer (<see cref="InnPlanInput.Preferred"/>)
    /// as one it doesn't. Measured with trace-inn on test_yuanyuan2 (养身心, "客人更倾向带有[素食]、[滋补]特性的菜品"):
    /// 3 of the 4 dishes ordered were among the 3 preferred dishes of the 8 on the menu, whose most likely weight
    /// is 5 (likelihood interval 0.6 to over 100: four dishes can't pin it; re-measure on a longer session of a
    /// theme with a preference). The 热门 chips alone change nothing: on the two 海陆宴 sessions, whose text
    /// names no preference, the hot dishes were ordered 1.2 times as often as the others (0.6 to 2.3, 38 dishes).
    /// </summary>
    public const double PreferenceWeight = 5;

    /// <summary>
    /// What a choice dish (珍馐) is taken to bring in tips, as a share of its value. The game shows
    /// no number and the tips weren't measured (owner: tips aren't worth modelling, only whether a
    /// dish came out 珍馐); half its value again is an assumption, there so a kitchen that turns out
    /// choice dishes more often counts for something beside the speed.
    /// </summary>
    public const double ChoiceDishTipShare = 0.5;

    /// <summary>
    /// <see cref="ChoiceDishTipShare"/> on a theme whose text says choice dishes earn more
    /// (海陆宴: "珍馐赏钱加成提升"); how much more isn't shown: double, an assumption.
    /// </summary>
    public const double RaisedChoiceDishTipShare = 2 * ChoiceDishTipShare;

    /// <summary>
    /// The share of a choice dish's value its tips are taken to bring, for <paramref name="input"/>:
    /// nothing unless the theme asks for choice dishes, in its objective (出售N道珍馐菜品) or its text
    /// (珍馐赏钱加成提升). Owner, 2026-10-19: "only prefer 珍馐 if it is in the bonus or requirement".
    /// Elsewhere the choice chance only breaks ties.
    /// </summary>
    public static double ChoiceTipShare(InnPlanInput input) =>
        input.ChoiceTipsRaised ? RaisedChoiceDishTipShare
        : input.ChoiceDishTarget is not null ? ChoiceDishTipShare
        : 0;

    /// <summary>
    /// The orders the guests can bring in a session, whatever the kitchens: the guests the clock
    /// brings (<see cref="InnGuests.Arrivals"/>, ~25: a guest every 12 s), or what the tables can
    /// turn over if fewer (each table a guest every <see cref="InnGuests.TableTurnSeconds"/> at
    /// best). test_yuanyuan1 had 23 orders: the guests' clock, not the kitchens, set the pace, so a
    /// dish's price counts for more than its speed once the kitchens keep up.
    /// </summary>
    public static double GuestOrders(int tables) => Math.Min(InnGuests.Arrivals, tables * SessionSeconds / InnGuests.TableTurnSeconds);

    /// <summary>Line-ups the beam keeps at each chef place.</summary>
    public const int BeamWidth = 20;

    /// <summary>The best line-ups of a beam search each get their own menu.</summary>
    public const int LineUpsTried = 8;

    /// <summary>Steps of Dinkelbach's method when the beam judges a line-up.</summary>
    private const int DinkelbachSteps = 3;

    /// <summary>Rounds of chefs-then-menu at most.</summary>
    private const int MaxRounds = 4;

    /// <summary>
    /// How hard the model's search works. <see cref="Full"/>: every round and start, the beam's line-ups
    /// judged with Dinkelbach's steps, the plan polished (seconds over the whole roster). <see cref="Quick"/>:
    /// one narrower beam from one starting menu, each line-up judged by that menu alone, the best few given a
    /// menu, no polish (measured 0.3–0.4 s where the full search takes 4–11 s over the whole roster; within
    /// ~5 % of its income under the model): the setup advice's first answer, refined after (<see cref="Plan"/>).
    /// </summary>
    private sealed record Effort(int DinkelbachSteps, int Rounds, bool DearestStart, int LineUpsTried, bool Polish, int BeamWidth)
    {
        public static readonly Effort Full = new(InnPlanner.DinkelbachSteps, MaxRounds, DearestStart: true, InnPlanner.LineUpsTried, Polish: true, InnPlanner.BeamWidth);
        public static readonly Effort Quick = new(DinkelbachSteps: 0, Rounds: 1, DearestStart: false, LineUpsTried: 3, Polish: false, BeamWidth: 8);
    }

    /// <summary>
    /// A cook with no bonus: a kitchen at base speed (a place the beam hasn't filled yet, or one taken
    /// by a chef whose name couldn't be read, <see cref="PlanKitchen.Placed"/>). Its id is empty.
    /// </summary>
    private static readonly OperatorInfo PlainCook = new("", "", "", "");

    public InnData Inn { get; } = inn;

    /// <summary>The traits to value: those read off the screen, else the theme's preferences.</summary>
    public static IReadOnlyList<string> HotTraitsOf(IReadOnlyList<string> read, InnTheme? theme) =>
        read.Count > 0 ? read : theme?.Prefers ?? [];

    /// <summary>
    /// The 热门 traits <paramref name="dish"/> matches: its traits and its type. The screen has only
    /// shown traits as 热门特性 so far; a type counts when a theme's preferences (used when none were
    /// read) name one, an assumption until a type-preferring theme is seen.
    /// </summary>
    public static int HotMatches(IReadOnlyCollection<string> hot, Dish dish) =>
        dish.Traits.Count(hot.Contains) + (hot.Contains(dish.Type.ToString()) ? 1 : 0);

    /// <summary>The 热门 traits <paramref name="drink"/> matches: its traits, and "Drink" for any drink.</summary>
    public static int HotMatches(IReadOnlyCollection<string> hot, InnDrink drink) =>
        drink.Traits.Count(hot.Contains) + (hot.Contains(InnData.Drink) ? 1 : 0);

    /// <summary>
    /// What the theme makes guests prefer (<see cref="InnPlanInput.Preferred"/>): what its text says
    /// (<see cref="InnObjective.GuestPreference"/>); when the text wasn't read, the 热门 chips read off the
    /// screen, else the theme's preferences in the data. A text read that names no preference (海陆宴's
    /// speaks of choice dishes only) gives none: guests pick uniformly.
    /// </summary>
    public static IReadOnlyList<string> PreferenceOf(string? description, IReadOnlyList<string> hotRead, InnTheme? theme, InnData data) =>
        description is null ? HotTraitsOf(hotRead, theme) : InnObjective.GuestPreference(description, data) ?? [];

    /// <summary>Whether guests prefer <paramref name="dish"/>: it has one of the <paramref name="preferred"/> traits, or is of one of the types.</summary>
    public static bool Prefers(IReadOnlyCollection<string> preferred, Dish dish) =>
        dish.Traits.Any(preferred.Contains) || preferred.Contains(dish.Type.ToString());

    /// <summary>Whether guests prefer <paramref name="drink"/> among the drinks: it has one of the <paramref name="preferred"/> traits.</summary>
    public static bool Prefers(IReadOnlyCollection<string> preferred, InnDrink drink) => drink.Traits.Any(preferred.Contains);

    /// <summary>How often a guest picks <paramref name="dish"/> relative to a dish nobody prefers: <see cref="PreferenceWeight"/> or 1.</summary>
    public static double OrderWeight(IReadOnlyCollection<string> preferred, Dish dish) => Prefers(preferred, dish) ? PreferenceWeight : 1;

    public static double OrderWeight(IReadOnlyCollection<string> preferred, InnDrink drink) => Prefers(preferred, drink) ? PreferenceWeight : 1;

    /// <summary>Each weight's share of their sum: the share of the orders each dish (or drink) gets.</summary>
    internal static double[] OrderShares(IReadOnlyList<double> weights)
    {
        double sum = weights.Sum();
        return [.. weights.Select(w => sum > 0 ? w / sum : 0)];
    }

    public static double Value(IReadOnlyCollection<string> hot, Dish dish) => dish.Price * (1 + HotTraitBonus * HotMatches(hot, dish));

    public static double Value(IReadOnlyCollection<string> hot, InnDrink drink) => drink.Price * (1 + HotTraitBonus * HotMatches(hot, drink));

    /// <summary>Whether the menu meets every rule: its size, its drinks, its types and each trait rule; no dish or drink twice.</summary>
    public bool MeetsRules(MenuRules rules, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks) =>
        dishes.Count >= rules.MinDishes && dishes.Count <= rules.MaxDishes
        && drinks.Count >= rules.MinDrinks && drinks.Count <= rules.MaxDrinks
        && dishes.Select(d => d.Dish.Id).Distinct().Count() == dishes.Count
        && drinks.Select(d => d.Drink.Id).Distinct().Count() == drinks.Count
        && dishes.Select(d => d.Dish.Type).Distinct().Count() >= rules.MinTypes
        && rules.DishRules.All(r => dishes.Count(d => r.Matches(d.Dish)) >= r.Count);

    /// <summary>The score of a line-up and menu under the model.</summary>
    public InnPlanScore Evaluate(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks)
    {
        var hot = input.HotTraits.ToHashSet();
        var preferred = input.Preferred.ToHashSet();
        var times = Times(input, kitchens, dishes.Select(d => d.Dish).ToList());
        return Score(input, times, [.. dishes.Select(d => Value(hot, d.Dish))], [.. dishes.Select(d => d.Stock)], [.. dishes.Select(d => OrderWeight(preferred, d.Dish))],
            [.. drinks.Select(d => Value(hot, d.Drink))], [.. drinks.Select(d => d.Stock)], [.. drinks.Select(d => OrderWeight(preferred, d.Drink))]);
    }

    /// <summary>
    /// The kitchen (its number) that cooks the larger share of each of <paramref name="dishes"/> under
    /// the model, the kitchens' loads balanced (<see cref="Shares"/>); on an equal share, the faster
    /// kitchen, then the lower number. A kitchen that cooks yet holds the larger share of no dish (one
    /// dish most guests order, a theme's preferred, spread over the kitchens) takes the dish it cooks
    /// the most of from a kitchen left with another, so every kitchen has a dish to be told. Null for
    /// every dish when no kitchen cooks.
    /// </summary>
    public IReadOnlyList<int?> MainKitchens(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes)
    {
        var cooking = kitchens.Where(Cooks).ToList();
        if (cooking.Count == 0) return [.. dishes.Select(_ => (int?)null)];
        var (seconds, _) = Times(input, cooking, [.. dishes.Select(d => d.Dish)]);
        var preferred = input.Preferred.ToHashSet();
        var shares = Shares(seconds, OrderShares([.. dishes.Select(d => OrderWeight(preferred, d.Dish))]));
        int[] main = [.. Enumerable.Range(0, dishes.Count).Select(d => Enumerable.Range(0, cooking.Count)
            .OrderByDescending(k => Math.Round(shares[k, d], 9)).ThenBy(k => seconds[k, d]).ThenBy(k => cooking[k].Number).First())];
        for (int k = 0; k < cooking.Count; k++)
        {
            if (main.Contains(k)) continue;
            int kitchen = k;
            int taken = Enumerable.Range(0, dishes.Count)
                .Where(d => shares[kitchen, d] > 1e-9 && main.Count(m => m == main[d]) > 1)
                .OrderByDescending(d => Math.Round(shares[kitchen, d], 9)).ThenBy(d => seconds[kitchen, d]).DefaultIfEmpty(-1).First();
            if (taken >= 0) main[taken] = k;
        }
        return [.. main.Select(k => (int?)cooking[k].Number)];
    }

    /// <summary>
    /// Kitchen-seconds one dish of the menu's mix costs with the kitchens cooking side by side, loads
    /// balanced (<see cref="Balance(double[,], double[])"/>, the order mix by the theme's preference):
    /// less is a faster line-up. Infinite when no kitchen cooks.
    /// </summary>
    public double KitchenSecondsPerDish(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes)
    {
        var cooking = kitchens.Where(Cooks).ToList();
        if (cooking.Count == 0 || dishes.Count == 0) return double.PositiveInfinity;
        var (seconds, _) = Times(input, cooking, [.. dishes.Select(d => d.Dish)]);
        var preferred = input.Preferred.ToHashSet();
        return Balance(seconds, OrderShares([.. dishes.Select(d => OrderWeight(preferred, d.Dish))]));
    }

    /// <summary>
    /// The plan for <paramref name="input"/>: the model's search (<see cref="PlanByModel"/>) gives the
    /// best line-ups and, for each, menus from the dearest to the quickest; each is played in the
    /// simulator over <see cref="SimulatedSessions"/> sessions of guests (<see cref="Simulate"/>), and
    /// the one that earns most there is the plan (one reaching the theme's choice-dish objective first).
    /// The model only proposes; the simulator, which seats guests on the measured clock and cooks
    /// every order through the kitchens' queues, decides. Chefs already placed
    /// (<see cref="PlanKitchen.Placed"/>) stay where they are and are put nowhere else; each planned
    /// kitchen lists them first, then the chefs the plan adds in its empty places.
    /// </summary>
    /// <param name="cancel">Stops the work between candidates (the inputs changed: the setup advice wants another plan).</param>
    /// <exception cref="ArgumentException">No kitchens or more than three, two with one number, places outside 1–2, more chefs placed than places, a chef placed twice, or an unknown Inn Rating.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
    public InnPlan Plan(InnPlanInput input, CancellationToken cancel = default)
    {
        var (search, best, lineUps) = SearchByModel(input, Effort.Full, cancel);
        (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnSimulatedScore Simulated)? chosen = null;
        foreach (var (kitchens, menu, drinks) in search.Candidates(best, lineUps))
        {
            cancel.ThrowIfCancellationRequested();
            var simulated = Simulate(input, kitchens, [.. menu.Select(i => search.Dishes[i])], [.. drinks.Select(i => search.Drinks[i])]);
            if (chosen is null || simulated.BetterThan(chosen.Value.Simulated)) chosen = (kitchens, menu, drinks, simulated);
        }
        var (k, m, d, s) = chosen!.Value;
        var plan = Finish(input, search, k, m, d, search.Score(k, m, d)) with { Simulated = s };
        // Whether the objective is reached is the simulator's word, like the rest of the choice.
        var reasons = plan.Reasons.Where(r => r.Kind != PlanReasonKind.ChoiceTargetUnreached).ToList();
        if (!s.MeetsTarget) reasons.Add(new PlanReason(PlanReasonKind.ChoiceTargetUnreached, Value: s.ChoiceDishes));
        return plan with { Reasons = reasons };
    }

    /// <summary>
    /// The plan the model alone gives (no simulation): the search's best line-up and menu by the
    /// model's score. What <see cref="Plan"/> starts from; its own tests check the search against
    /// brute force under the model.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Plan"/>.</exception>
    public InnPlan PlanByModel(InnPlanInput input)
    {
        var (search, best, _) = SearchByModel(input, Effort.Full, CancellationToken.None);
        return Finish(input, search, best.Kitchens, best.Menu, best.Drinks, best.Score);
    }

    /// <summary>
    /// A plan by the model from a shorter search (one beam, no polish; a fraction of a second where
    /// <see cref="PlanByModel"/> takes seconds over the whole roster): the setup advice shows it while
    /// <see cref="Plan"/> works out the better one. Its chefs and menu meet the same rules; it may earn less.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Plan"/>.</exception>
    public InnPlan PlanQuickly(InnPlanInput input)
    {
        var (search, best, _) = SearchByModel(input, Effort.Quick, CancellationToken.None);
        return Finish(input, search, best.Kitchens, best.Menu, best.Drinks, best.Score);
    }

    private (Search Search, (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) Best, IReadOnlyList<Kitchen[]> LineUps) SearchByModel(InnPlanInput input,
        Effort effort, CancellationToken cancel)
    {
        Validate(input);
        var hot = input.HotTraits.ToHashSet();
        var search = new Search(this, input, hot, effort.DinkelbachSteps, effort.BeamWidth, cancel);

        // Start from a menu for kitchens at base speed, then alternate chefs and menu.
        var (menu, drinks) = search.BestMenu(search.Materialize(search.PlainLineUp()), search.InitialDrinks());
        (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score)? best = null;
        var tried = new List<(Kitchen[] Kitchens, InnPlanScore Score)>();
        // The first round also starts from the dearest menu: with guests on a clock a menu of dear dishes
        // and one of quick dishes can both be good, and chefs suited to one are judged poorly by the other.
        var starts = new List<(int[] Menu, int[] Drinks)> { (menu, drinks) };
        if (effort.DearestStart && search.DearestMenu(drinks) is { } dearest) starts.Add((dearest, drinks));
        for (int round = 0; round < effort.Rounds; round++)
        {
            bool improved = false;
            var beam = round == 0 ? starts.SelectMany(s => search.Beam(s.Menu, s.Drinks).Take(effort.LineUpsTried)) : search.Beam(menu, drinks).Take(effort.LineUpsTried);
            foreach (var lineUp in beam)
            {
                cancel.ThrowIfCancellationRequested();
                var kitchens = search.Materialize(lineUp, int.MaxValue);
                var (m, d) = search.BestMenu(kitchens, drinks, menu);
                var score = search.Score(kitchens, m, d);
                tried.Add((kitchens, score));
                if (best is null || score.BetterThan(best.Value.Score))
                {
                    best = (kitchens, m, d, score);
                    improved = true;
                }
            }
            (menu, drinks) = (best!.Value.Menu, best.Value.Drinks);
            if (!improved) break;
        }

        var final = search.FillEmptyPlaces(effort.Polish ? search.Polish(best!.Value) : best!.Value);
        // The runners-up: the best other line-ups the search judged, each once.
        var lineUps = tried.OrderByDescending(t => t.Score, Comparer<InnPlanScore>.Create((a, b) => a.BetterThan(b) ? 1 : b.BetterThan(a) ? -1 : 0))
            .Select(t => t.Kitchens).Prepend(final.Kitchens).DistinctBy(Search.KitchensKey).Take(LineUpsSimulated).ToList();
        return (search, final, lineUps);
    }

    private InnPlan Finish(InnPlanInput input, Search search, Kitchen[] finalKitchens, int[] finalMenu, int[] finalDrinks, InnPlanScore finalScore)
    {
        var hot = input.HotTraits.ToHashSet();
        var dishes = finalMenu.Select(i => search.Dishes[i]).ToList();
        var drinkList = finalDrinks.Select(i => search.Drinks[i]).ToList();
        var cooking = finalKitchens.Where(Cooks).ToList();
        var opening = cooking.Count == 0 || dishes.Count == 0
            ? null
            : new DispatchAdvisor(new InnKitchens(Inn, cooking, input.Rating, input.SiQualityPercent)).Opening(dishes);
        bool rulesMet = MeetsRules(input.Rules, dishes, drinkList);
        return new InnPlan([.. finalKitchens.Select(k => new PlannedKitchen(k.Number, k.Chefs))], dishes, drinkList, opening, finalScore, rulesMet,
            Reasons(input, hot, finalKitchens, dishes, drinkList, finalScore, rulesMet, search));
    }

    // ---- The simulator's judgement ----

    /// <summary>Sessions each candidate plan is played over (the same guests and orders for every plan, so they're compared alike).</summary>
    public const int SimulatedSessions = 6;

    /// <summary>Line-ups whose menus are played in the simulator: the model's best and its runners-up.</summary>
    public const int LineUpsSimulated = 3;

    /// <summary>Menus per line-up played in the simulator, besides the dearest ones (<see cref="Search.Candidates"/>).</summary>
    public const int MenusSimulated = 8;

    /// <summary>Guests a session can bring at most (<see cref="InnGuests.Arrivals"/>, rounded up, and one to spare).</summary>
    private const int MostGuests = (int)InnGuests.Arrivals + 2;

    /// <summary>
    /// The random draws of each simulated session: per guest, what kind of order (1 dish + 1 drink
    /// 75 %, 2 dishes 20 %, 1 dish 5 %, test_yuanyuan1) and which dish and drink (by each one's share
    /// of the orders, <see cref="OrderWeight(IReadOnlyCollection{string}, Dish)"/>), as numbers in [0, 1)
    /// a menu turns into its own dishes. Fixed, so every plan meets the same guests.
    /// </summary>
    private static readonly double[][][] Draws = [.. Enumerable.Range(0, SimulatedSessions).Select(s =>
    {
        var random = new Random(20261019 + s);
        return Enumerable.Range(0, MostGuests).Select(_ => new[] { random.NextDouble(), random.NextDouble(), random.NextDouble(), random.NextDouble() }).ToArray();
    })];

    /// <summary>
    /// A plan played in the simulator (<see cref="InnSimulator.RunGuests"/>, the dispatch advice
    /// followed: <see cref="InnSimulator.Following"/>): the mean over <see cref="SimulatedSessions"/>
    /// sessions of the money earned on the orders served (each dish as ordered at its value with the
    /// 热门 bonus, and its choice-dish tips by the chance of the kitchen that cooked it; each drink at
    /// its value), the orders served and the choice dishes expected.
    /// </summary>
    public InnSimulatedScore Simulate(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks)
    {
        var cooking = kitchens.Where(Cooks).ToList();
        bool noTarget = input.ChoiceDishTarget is null;
        if (cooking.Count == 0 || dishes.Count == 0) return new InnSimulatedScore(0, 0, 0, noTarget);
        var hot = input.HotTraits.ToHashSet();
        var preferred = input.Preferred.ToHashSet();
        double[] dishMix = OrderShares([.. dishes.Select(d => OrderWeight(preferred, d.Dish))]);
        double[] drinkMix = OrderShares([.. drinks.Select(d => OrderWeight(preferred, d.Drink))]);
        double tipShare = ChoiceTipShare(input);
        var inn = new InnKitchens(Inn, cooking, input.Rating, input.SiQualityPercent);
        var guests = new InnGuests(Math.Clamp(input.Tables, 1, InnState.MaxTables));
        double income = 0, orders = 0, choice = 0;
        foreach (var draws in Draws)
        {
            var wanted = draws.Select(u => (IReadOnlyList<Dish>)(u[0] < 0.95
                ? u[0] < 0.75 ? [Pick(dishes, dishMix, u[1]).Dish] : [Pick(dishes, dishMix, u[1]).Dish, Pick(dishes, dishMix, u[2]).Dish]
                : [Pick(dishes, dishMix, u[1]).Dish])).ToList();
            // A drink with the 75 % of orders that bring one, when the menu has drinks.
            var drinkOf = new Dictionary<IReadOnlyList<Dish>, MenuDrink>(ReferenceEqualityComparer.Instance);
            for (int g = 0; g < wanted.Count; g++)
                if (draws[g][0] < 0.75 && drinks.Count > 0) drinkOf[wanted[g]] = Pick(drinks, drinkMix, draws[g][3]);

            var run = new InnSimulator(inn, dishes).RunGuests(guests, wanted, InnSimulator.Following(new DispatchAdvisor(inn)));
            var servedOrders = run.Orders.Where(o => o.ServedAt is not null).Select(o => o.Order).ToHashSet();
            orders += servedOrders.Count;
            foreach (var delivered in run.Delivered.Where(d => d.AsOrdered && servedOrders.Contains(d.Order)))
            {
                double chance = delivered.Kitchen > 0 ? inn.ChoicePercent(delivered.Kitchen, delivered.Dish) / 100.0 : 0;
                income += Value(hot, delivered.Dish) * (1 + chance * tipShare);
                choice += chance;
            }
            var drinksLeft = drinks.ToDictionary(d => d.Drink.Id, d => d.Stock);
            foreach (var order in servedOrders)
            {
                if (!drinkOf.TryGetValue(order.Dishes, out var drink) || drinksLeft[drink.Drink.Id] is <= 0) continue;
                income += Value(hot, drink.Drink);
                if (drinksLeft[drink.Drink.Id] is { } left) drinksLeft[drink.Drink.Id] = left - 1;
            }
        }
        int n = Draws.Length;
        return new InnSimulatedScore(income / n, orders / n, choice / n, noTarget || choice / n >= input.ChoiceDishTarget!.Value - 1e-9);

        // The item whose share of [0, 1) holds u (equal shares: the u × count-th, as guests picking uniformly).
        static T Pick<T>(IReadOnlyList<T> items, double[] shares, double u)
        {
            double edge = 0;
            for (int i = 0; i < items.Count - 1; i++)
                if (u < (edge += shares[i])) return items[i];
            return items[^1];
        }
    }

    private static void Validate(InnPlanInput input)
    {
        if (input.Kitchens.Count is < 1 or > KitchenMath.MaxKitchens)
            throw new ArgumentException($"{input.Kitchens.Count} kitchens; the inn has 1 to {KitchenMath.MaxKitchens}.", nameof(input));
        if (input.Kitchens.Select(k => k.Number).Distinct().Count() != input.Kitchens.Count)
            throw new ArgumentException("Two kitchens have one number.", nameof(input));
        if (input.Kitchens.FirstOrDefault(k => k.Places is < 1 or > KitchenMath.MaxChefs) is { } bad)
            throw new ArgumentException($"Kitchen {bad.Number} has {bad.Places} chef places; a kitchen has 1 to {KitchenMath.MaxChefs}.", nameof(input));
        if (input.Kitchens.FirstOrDefault(k => k.Placed.Count > k.Places) is { } full)
            throw new ArgumentException($"Kitchen {full.Number} has {full.Placed.Count} chefs placed in {full.Places} places.", nameof(input));
        var placed = input.Kitchens.SelectMany(k => k.Placed).OfType<OperatorInfo>().Select(o => o.Id).ToList();
        if (placed.Count != placed.Distinct().Count())
            throw new ArgumentException("A chef is placed in two kitchens.", nameof(input));
        if (input.Rating < 1 || input.Rating > KitchenMath.MaxRating)
            throw new ArgumentException($"Inn Rating {input.Rating}; the model knows 1 to {KitchenMath.MaxRating}.", nameof(input));
    }

    private static bool Cooks(Kitchen kitchen) => KitchenMath.Cooks(kitchen);

    /// <summary>Cook seconds and choice chances (0–1) of each dish in each kitchen that cooks.</summary>
    private (double[,] Seconds, double[,] Choice) Times(InnPlanInput input, IReadOnlyList<Kitchen> kitchens, IReadOnlyList<Dish> dishes)
    {
        var cooking = kitchens.Where(Cooks).ToList();
        var seconds = new double[cooking.Count, dishes.Count];
        var choice = new double[cooking.Count, dishes.Count];
        for (int k = 0; k < cooking.Count; k++)
        {
            for (int d = 0; d < dishes.Count; d++)
            {
                seconds[k, d] = KitchenMath.CookSeconds(Inn, cooking[k], dishes[d], input.Rating);
                choice[k, d] = KitchenMath.ChoiceChance(Inn, cooking[k], dishes[d], input.Rating, input.SiQualityPercent);
            }
        }
        return (seconds, choice);
    }

    /// <summary>The model's score from the cook times, the values and the stock of each dish and drink.</summary>
    /// <param name="dishWeights">How often each dish is picked (<see cref="OrderWeight(IReadOnlyCollection{string}, Dish)"/>); drinks alike.</param>
    private static InnPlanScore Score(InnPlanInput input, (double[,] Seconds, double[,] Choice) times,
        double[] dishValues, int?[] dishStock, double[] dishWeights, double[] drinkValues, int?[] drinkStock, double[] drinkWeights)
    {
        var (seconds, choice) = times;
        int kitchens = seconds.GetLength(0), dishes = seconds.GetLength(1);
        // Nothing served: no objective reached either.
        if (kitchens == 0 || dishes == 0 || input.Tables <= 0) return InnPlanScore.Zero with { MeetsTarget = input.ChoiceDishTarget is null };

        var mix = OrderShares(dishWeights);
        double perDish = Balance(seconds, mix);
        double kitchenOrders = SessionSeconds / (DishesPerOrder * perDish);
        double guestOrders = GuestOrders(input.Tables);
        double orders = Math.Min(kitchenOrders, guestOrders);

        // Each dish gets its share of the dishes ordered (equal ones unless the theme makes guests prefer some), each drink of the drinks.
        double dishesOrdered = orders * DishesPerOrder;
        double income = 0, choiceDishes = 0, tipShare = ChoiceTipShare(input);
        for (int d = 0; d < dishes; d++)
        {
            double served = Math.Min(dishesOrdered * mix[d], dishStock[d] ?? double.MaxValue);
            // The choice chance where the dish cooks fastest (the better chance on a tie).
            int fastest = 0;
            for (int k = 1; k < kitchens; k++)
                if (seconds[k, d] < seconds[fastest, d] || (seconds[k, d] == seconds[fastest, d] && choice[k, d] > choice[fastest, d])) fastest = k;
            income += served * dishValues[d] * (1 + choice[fastest, d] * tipShare);
            choiceDishes += served * choice[fastest, d];
        }
        if (drinkValues.Length > 0)
        {
            var drinkMix = OrderShares(drinkWeights);
            for (int d = 0; d < drinkValues.Length; d++) income += Math.Min(orders * DrinksPerOrder * drinkMix[d], drinkStock[d] ?? double.MaxValue) * drinkValues[d];
        }
        return new InnPlanScore(income, orders, kitchenOrders <= guestOrders, choiceDishes)
        {
            MeetsTarget = input.ChoiceDishTarget is not { } target || choiceDishes >= target - 1e-9,
        };
    }

    /// <summary>
    /// The kitchen-seconds one dish of the menu's mix costs with the kitchens cooking side by side
    /// and their loads balanced: the least, over every way of sharing each dish among the kitchens,
    /// of the busiest kitchen's seconds per dish ordered (each dish an equal share of the orders).
    /// Solved through its dual, max over kitchen weights λ (λ ≥ 0, Σλ = 1) of Σ_d min_k λ_k t_kd / m,
    /// a concave piecewise-linear function whose maximum is at a vertex of the arrangement of the
    /// planes λ_i t_id = λ_j t_jd and λ_k = 0; with at most three kitchens there are few, and every
    /// one is tried.
    /// </summary>
    internal static double Balance(double[,] seconds) => BalanceWeights(seconds, Uniform(seconds.GetLength(1))).PerDish;

    /// <summary>
    /// <see cref="Balance"/> when the dishes aren't ordered alike: dish d is <paramref name="mix"/>[d]
    /// of the dishes ordered (the shares sum to 1; <see cref="OrderShares"/>). The same dual, Σ_d mix_d
    /// min_k λ_k t_kd: the planes where the least kitchen changes don't depend on the shares.
    /// </summary>
    internal static double Balance(double[,] seconds, double[] mix) => BalanceWeights(seconds, mix).PerDish;

    /// <summary>Each of <paramref name="count"/> dishes an equal share.</summary>
    private static double[] Uniform(int count) => [.. Enumerable.Repeat(count == 0 ? 0 : 1.0 / count, count)];

    /// <summary><see cref="Balance"/>, with the kitchen weights λ that reach it.</summary>
    private static (double PerDish, double[] Weights) BalanceWeights(double[,] seconds, double[] mix)
    {
        int kitchens = seconds.GetLength(0), dishes = seconds.GetLength(1);
        double F(ReadOnlySpan<double> lambda)
        {
            double sum = 0;
            for (int d = 0; d < dishes; d++)
            {
                double least = double.MaxValue;
                for (int k = 0; k < kitchens; k++) least = Math.Min(least, lambda[k] * seconds[k, d]);
                sum += mix[d] * least;
            }
            return sum;
        }
        if (kitchens == 1) return (F([1.0]), [1.0]);

        // The planes, as coefficient rows c (c·λ = 0), each once.
        var planes = new List<double[]>();
        var seen = new HashSet<(int, int, double)>();
        for (int k = 0; k < kitchens; k++)
        {
            var c = new double[kitchens];
            c[k] = 1;
            planes.Add(c);
        }
        for (int d = 0; d < dishes; d++)
        {
            for (int i = 0; i < kitchens; i++)
            {
                for (int j = i + 1; j < kitchens; j++)
                {
                    if (!seen.Add((i, j, Math.Round(seconds[i, d] / seconds[j, d], 12)))) continue;
                    var c = new double[kitchens];
                    c[i] = seconds[i, d];
                    c[j] = -seconds[j, d];
                    planes.Add(c);
                }
            }
        }

        double best = 0;
        var weights = new double[kitchens];
        Span<double> lambda = stackalloc double[kitchens];
        void Keep(Span<double> at)
        {
            double value = F(at);
            if (value <= best) return;
            best = value;
            at.CopyTo(weights);
        }
        if (kitchens == 2)
        {
            foreach (var c in planes)
            {
                // c0 λ0 + c1 λ1 = 0, λ0 + λ1 = 1.
                double det = c[0] - c[1];
                if (Math.Abs(det) < 1e-12) continue;
                lambda[0] = -c[1] / det;
                lambda[1] = c[0] / det;
                if (Feasible(lambda)) Keep(lambda);
            }
            return (best, weights);
        }
        for (int p = 0; p < planes.Count; p++)
        {
            for (int q = p + 1; q < planes.Count; q++)
            {
                if (Solve3(planes[p], planes[q], lambda) && Feasible(lambda)) Keep(lambda);
            }
        }
        return (best, weights);
    }

    /// <summary>
    /// How the balanced kitchens share each dish (<see cref="Balance"/>): [kitchen, dish], each dish's
    /// column summing to 1. From the weights λ that reach the balance: a dish is cooked only where its
    /// weighted time λ_k t_kd is least (complementary slackness), and every kitchen is loaded to the
    /// balance. A dish least in one kitchen goes there whole; dishes tied between kitchens take what
    /// load is left, a flow in units of weighted time (a tied dish costs the same weighted time in
    /// each kitchen it's tied in, so the units carry over).
    /// </summary>
    internal static double[,] Shares(double[,] seconds) => Shares(seconds, Uniform(seconds.GetLength(1)));

    /// <summary><see cref="Shares(double[,])"/> for dishes ordered in the shares <paramref name="mix"/> (summing to 1).</summary>
    internal static double[,] Shares(double[,] seconds, double[] mix)
    {
        int kitchens = seconds.GetLength(0), dishes = seconds.GetLength(1);
        var shares = new double[kitchens, dishes];
        if (kitchens == 0 || dishes == 0) return shares;
        var (perDish, weights) = BalanceWeights(seconds, mix);
        if (kitchens == 1 || weights.Any(w => w <= 1e-12))
        {
            // One kitchen, or a degenerate balance: each dish where it cooks fastest.
            for (int d = 0; d < dishes; d++) shares[Fastest(seconds, d), d] = 1;
            return shares;
        }

        var load = new double[kitchens];
        var tied = new List<(int Dish, double Work, int[] Kitchens)>();
        for (int d = 0; d < dishes; d++)
        {
            double least = double.MaxValue;
            for (int k = 0; k < kitchens; k++) least = Math.Min(least, weights[k] * seconds[k, d]);
            int[] where = [.. Enumerable.Range(0, kitchens).Where(k => weights[k] * seconds[k, d] <= least * (1 + 1e-9))];
            if (where.Length == 1)
            {
                shares[where[0], d] = 1;
                load[where[0]] += mix[d] * seconds[where[0], d];
            }
            else tied.Add((d, mix[d] * least, where));
        }
        if (tied.Count == 0) return shares;

        // Max flow: source -> tied dish (its weighted time) -> kitchen it's tied in -> sink (the weighted load left).
        int n = tied.Count, source = 0, sink = n + kitchens + 1;
        var capacity = new double[sink + 1, sink + 1];
        for (int i = 0; i < n; i++)
        {
            capacity[source, 1 + i] = tied[i].Work;
            foreach (int k in tied[i].Kitchens) capacity[1 + i, 1 + n + k] = double.MaxValue;
        }
        for (int k = 0; k < kitchens; k++) capacity[1 + n + k, sink] = Math.Max(0, weights[k] * (perDish - load[k]));
        var flow = MaxFlow(capacity, source, sink);
        for (int i = 0; i < n; i++)
        {
            int d = tied[i].Dish;
            double sent = tied[i].Kitchens.Sum(k => flow[1 + i, 1 + n + k]);
            if (sent <= 1e-12 || mix[d] <= 0) shares[tied[i].Kitchens.MinBy(k => seconds[k, d]), d] = 1;
            else foreach (int k in tied[i].Kitchens) shares[k, d] = flow[1 + i, 1 + n + k] / sent;
        }
        return shares;
    }

    private static int Fastest(double[,] seconds, int dish)
    {
        int fastest = 0;
        for (int k = 1; k < seconds.GetLength(0); k++)
            if (seconds[k, dish] < seconds[fastest, dish]) fastest = k;
        return fastest;
    }

    /// <summary>Edmonds-Karp over a dense capacity matrix (a handful of nodes); returns the flow on each edge.</summary>
    private static double[,] MaxFlow(double[,] capacity, int source, int sink)
    {
        int nodes = capacity.GetLength(0);
        var flow = new double[nodes, nodes];
        double Residual(int u, int v) => capacity[u, v] - flow[u, v] + flow[v, u];
        while (true)
        {
            var parent = Enumerable.Repeat(-1, nodes).ToArray();
            parent[source] = source;
            var queue = new Queue<int>([source]);
            while (queue.Count > 0 && parent[sink] < 0)
            {
                int u = queue.Dequeue();
                for (int v = 0; v < nodes; v++)
                {
                    if (parent[v] >= 0 || Residual(u, v) <= 1e-12) continue;
                    parent[v] = u;
                    queue.Enqueue(v);
                }
            }
            if (parent[sink] < 0) return flow;
            double push = double.MaxValue;
            for (int v = sink; v != source; v = parent[v]) push = Math.Min(push, Residual(parent[v], v));
            for (int v = sink; v != source; v = parent[v])
            {
                int u = parent[v];
                // Undo flow coming back first, then send forward.
                double back = Math.Min(push, flow[v, u]);
                flow[v, u] -= back;
                flow[u, v] += push - back;
            }
        }
    }

    private static bool Feasible(Span<double> lambda)
    {
        for (int k = 0; k < lambda.Length; k++)
        {
            if (lambda[k] < -1e-12) return false;
            lambda[k] = Math.Max(0, lambda[k]);
        }
        return true;
    }

    /// <summary>a·λ = 0, b·λ = 0, λ0 + λ1 + λ2 = 1, by Cramer's rule; false when the planes don't meet in one point.</summary>
    private static bool Solve3(double[] a, double[] b, Span<double> lambda)
    {
        double Det(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
            a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
        double det = Det(a[0], a[1], a[2], b[0], b[1], b[2], 1, 1, 1);
        if (Math.Abs(det) < 1e-9) return false;
        // Right-hand side (0, 0, 1).
        lambda[0] = Det(0, a[1], a[2], 0, b[1], b[2], 1, 1, 1) / det;
        lambda[1] = Det(a[0], 0, a[2], b[0], 0, b[2], 1, 1, 1) / det;
        lambda[2] = Det(a[0], a[1], 0, b[0], b[1], 0, 1, 1, 1) / det;
        return true;
    }

    private IReadOnlyList<PlanReason> Reasons(InnPlanInput input, HashSet<string> hot, IReadOnlyList<Kitchen> kitchens,
        IReadOnlyList<MenuDish> dishes, IReadOnlyList<MenuDrink> drinks, InnPlanScore score, bool rulesMet, Search search)
    {
        var reasons = new List<PlanReason>();
        foreach (var kitchen in kitchens.Where(k => !Cooks(k))) reasons.Add(new PlanReason(PlanReasonKind.KitchenUnstaffed, Kitchen: kitchen.Number));
        if (!score.KitchenBound && score.Orders > 0) reasons.Add(new PlanReason(PlanReasonKind.GuestBound, Value: score.Orders));
        if (!rulesMet) reasons.AddRange(search.UnmetRules().Select(r => new PlanReason(PlanReasonKind.RuleUnmet, Rule: r)));
        if (!score.MeetsTarget) reasons.Add(new PlanReason(PlanReasonKind.ChoiceTargetUnreached, Value: score.ChoiceDishes));

        var cooking = kitchens.Where(Cooks).ToList();
        var preferred = input.Preferred.ToHashSet();
        var dishMix = OrderShares([.. dishes.Select(d => OrderWeight(preferred, d.Dish))]);
        var drinkMix = OrderShares([.. drinks.Select(d => OrderWeight(preferred, d.Drink))]);
        foreach (var (menu, share) in dishes.Zip(dishMix))
        {
            double ordered = score.Orders * DishesPerOrder * share;
            var dish = menu.Dish;
            if (cooking.Count > 0)
            {
                var fastest = cooking.MinBy(k => KitchenMath.CookSeconds(Inn, k, dish, input.Rating))!;
                double perSecond = Value(hot, dish) / KitchenMath.CookSeconds(Inn, fastest, dish, input.Rating);
                reasons.Add(new PlanReason(PlanReasonKind.DishValue, dish.Id, fastest.Number, Value: perSecond));
            }
            if (HotMatches(hot, dish) is var matches and > 0) reasons.Add(new PlanReason(PlanReasonKind.HotTraits, dish.Id, Value: matches));
            for (int r = 0; r < input.Rules.DishRules.Count; r++)
                if (input.Rules.DishRules[r].Matches(dish)) reasons.Add(new PlanReason(PlanReasonKind.MeetsRule, dish.Id, Rule: r));
            if (menu.Stock is { } stock && stock < ordered) reasons.Add(new PlanReason(PlanReasonKind.ShortOfStock, dish.Id, Value: ordered - stock));
        }
        foreach (var (drink, share) in drinks.Zip(drinkMix))
        {
            double eachDrink = score.Orders * DrinksPerOrder * share;
            if (HotMatches(hot, drink.Drink) is var matches and > 0) reasons.Add(new PlanReason(PlanReasonKind.HotTraits, drink.Drink.Id, Value: matches));
            if (drink.Stock is { } stock && stock < eachDrink) reasons.Add(new PlanReason(PlanReasonKind.ShortOfStock, drink.Drink.Id, Value: eachDrink - stock));
        }
        return reasons;
    }

    /// <summary>One planning run: the candidates, the chefs' profiles, and the cached scores.</summary>
    private sealed class Search
    {
        private readonly InnPlanner _planner;
        private readonly InnPlanInput _input;
        private readonly double[] _dishValues, _drinkValues;

        /// <summary>How often guests pick each dish and drink (<see cref="OrderWeight(IReadOnlyCollection{string}, Dish)"/>).</summary>
        private readonly double[] _dishWeights, _drinkWeights;

        /// <summary>Chefs with the same bonuses, as one profile: its bonuses and its operators (in the input's order). Never a placed chef.</summary>
        private readonly List<(string Bonuses, List<OperatorInfo> Operators)> _profiles;

        /// <summary>Per kitchen (the input's order), the chefs already placed there: always its first chefs.</summary>
        private readonly OperatorInfo[][] _placed;

        /// <summary>Places of kitchen <paramref name="k"/> (the input's order) the plan may fill.</summary>
        private int Free(int k) => _input.Kitchens[k].Places - _placed[k].Length;

        /// <summary>Steps of Dinkelbach's method in <see cref="QuickScore"/> (none: a line-up is judged by the beam's menu alone).</summary>
        private readonly int _dinkelbachSteps;

        /// <summary>Line-ups the beam keeps at each chef place.</summary>
        private readonly int _beamWidth;

        /// <summary>Stops the search between chef places and polish rounds.</summary>
        private readonly CancellationToken _cancel;

        public Search(InnPlanner planner, InnPlanInput input, HashSet<string> hot, int dinkelbachSteps, int beamWidth, CancellationToken cancel)
        {
            _cancel = cancel;
            _planner = planner;
            _dinkelbachSteps = dinkelbachSteps;
            _beamWidth = beamWidth;
            _input = input;
            Dishes = [.. input.Dishes.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Dish.Id)];
            Drinks = [.. input.Drinks.Where(d => d.Stock is not <= 0).DistinctBy(d => d.Drink.Id)];
            _dishValues = [.. Dishes.Select(d => Value(hot, d.Dish))];
            _drinkValues = [.. Drinks.Select(d => Value(hot, d.Drink))];
            var preferred = input.Preferred.ToHashSet();
            _dishWeights = [.. Dishes.Select(d => OrderWeight(preferred, d.Dish))];
            _drinkWeights = [.. Drinks.Select(d => OrderWeight(preferred, d.Drink))];
            var inn = planner.Inn;
            // Chefs already placed stay where they are (a place taken by an unnamed chef holds a plain cook) and are put nowhere else.
            _placed = [.. input.Kitchens.Select(k => k.Placed.Select(o => o ?? PlainCook).ToArray())];
            var placed = input.Kitchens.SelectMany(k => k.Placed).OfType<OperatorInfo>().Select(o => o.Id).ToHashSet();
            _profiles = [.. input.Chefs
                .Where(o => !inn.ChefBonuses.NotChefs.Contains(o.Id) && !placed.Contains(o.Id))
                .DistinctBy(o => o.Id)
                .GroupBy(o => string.Join("|", inn.ChefBonusesOf(o).Select(b => $"{b.Kind}:{b.Percent}:{b.DishType}:{b.Flavor}").Order(StringComparer.Ordinal)))
                .Select(g => (g.Key, g.ToList()))];
        }

        /// <summary>The dishes and drinks that can go on the menu (offered, in stock, each once).</summary>
        public IReadOnlyList<MenuDish> Dishes { get; }

        public IReadOnlyList<MenuDrink> Drinks { get; }

        private MenuRules Rules => _input.Rules;

        // ---- Scoring ----

        private readonly Dictionary<string, (double[,] Seconds, double[,] Choice)> _times = [];

        /// <summary>Cook times of every candidate dish in the kitchens, kept per line-up.</summary>
        private (double[,] Seconds, double[,] Choice) TimesOf(IReadOnlyList<Kitchen> kitchens)
        {
            string key = string.Join("/", kitchens.Where(Cooks).Select(k => string.Join("+", k.Chefs.Select(c => c.Id).Order(StringComparer.Ordinal))));
            if (!_times.TryGetValue(key, out var times))
                _times[key] = times = _planner.Times(_input, kitchens, [.. Dishes.Select(d => d.Dish)]);
            return times;
        }

        public InnPlanScore Score(IReadOnlyList<Kitchen> kitchens, IReadOnlyList<int> menu, IReadOnlyList<int> drinks)
        {
            var (allSeconds, allChoice) = TimesOf(kitchens);
            int k = allSeconds.GetLength(0);
            var seconds = new double[k, menu.Count];
            var choice = new double[k, menu.Count];
            for (int i = 0; i < k; i++)
            {
                for (int d = 0; d < menu.Count; d++)
                {
                    seconds[i, d] = allSeconds[i, menu[d]];
                    choice[i, d] = allChoice[i, menu[d]];
                }
            }
            return InnPlanner.Score(_input, (seconds, choice), [.. menu.Select(i => _dishValues[i])], [.. menu.Select(i => Dishes[i].Stock)], [.. menu.Select(i => _dishWeights[i])],
                [.. drinks.Select(i => _drinkValues[i])], [.. drinks.Select(i => Drinks[i].Stock)], [.. drinks.Select(i => _drinkWeights[i])]);
        }

        // ---- Drinks ----

        /// <summary>Drinks dearest first (unlimited stock first on a tie).</summary>
        private IEnumerable<int> DrinksByValue() =>
            Enumerable.Range(0, Drinks.Count).OrderByDescending(i => _drinkValues[i]).ThenBy(i => Drinks[i].Stock is null ? 0 : 1).ThenBy(i => i);

        /// <summary>
        /// The drinks for a dish menu: the dearest few, as many as pay best (guests pick among them, so
        /// a cheaper drink lowers the average; a drink short of stock spreads the orders); at least
        /// one when there is one, since orders bring drinks.
        /// </summary>
        public int[] BestDrinks(IReadOnlyList<Kitchen> kitchens, IReadOnlyList<int> menu)
        {
            var order = DrinksByValue().ToList();
            int least = Math.Max(Rules.MinDrinks, Math.Min(1, order.Count)), most = Math.Min(Rules.MaxDrinks, order.Count);
            int[] best = [.. order.Take(least)];
            InnPlanScore? bestScore = null;
            for (int count = least; count <= most; count++)
            {
                int[] drinks = [.. order.Take(count)];
                var score = Score(kitchens, menu, drinks);
                if (bestScore is null || score.BetterThan(bestScore)) (best, bestScore) = (drinks, score);
            }
            return best;
        }

        /// <summary>The drinks before any dish is chosen: the dearest, as few as allowed (at least one).</summary>
        public int[] InitialDrinks() => [.. DrinksByValue().Take(Math.Min(Drinks.Count, Math.Max(Rules.MinDrinks, 1)))];

        // ---- Menu ----

        private int Types(IEnumerable<int> menu) => menu.Select(i => Dishes[i].Dish.Type).Distinct().Count();

        private bool Has(int dish, DishRule rule) => rule.Matches(Dishes[dish].Dish);

        /// <summary>How far the menu is from its types and trait rules.</summary>
        private int Shortfall(IReadOnlyCollection<int> menu) =>
            Math.Max(0, Rules.MinTypes - Types(menu)) + Rules.DishRules.Sum(r => Math.Max(0, r.Count - menu.Count(i => Has(i, r))));

        private bool Valid(IReadOnlyCollection<int> menu) =>
            menu.Count >= Rules.MinDishes && menu.Count <= Rules.MaxDishes && Shortfall(menu) == 0;

        /// <summary>Rules no menu of the candidates can meet on its own: a trait rule's index, or -1 for the types or counts.</summary>
        public IEnumerable<int> UnmetRules()
        {
            bool countsOrTypes = Dishes.Count < Rules.MinDishes || Types(Enumerable.Range(0, Dishes.Count)) < Rules.MinTypes || Drinks.Count < Rules.MinDrinks;
            var dishRules = Enumerable.Range(0, Rules.DishRules.Count)
                .Where(r => Enumerable.Range(0, Dishes.Count).Count(i => Has(i, Rules.DishRules[r])) < Rules.DishRules[r].Count).ToList();
            // Rules each possible on their own but not together within the menu's size.
            if (!countsOrTypes && dishRules.Count == 0) countsOrTypes = true;
            return countsOrTypes ? dishRules.Prepend(-1) : dishRules;
        }

        /// <summary>
        /// The best menu for these kitchens: built greedily (first what the rules need, then what pays
        /// most) up to each size the rules allow, each improved by adding, removing and swapping a
        /// dish while that pays; then the drinks for it, and the dishes again for those drinks.
        /// </summary>
        /// <param name="seed">A menu to improve as well (the plan's so far), so a change of one dish from it is never missed.</param>
        public (int[] Menu, int[] Drinks) BestMenu(IReadOnlyList<Kitchen> kitchens, int[] drinks, int[]? seed = null)
        {
            var menu = BestDishes(kitchens, drinks, seed);
            var newDrinks = BestDrinks(kitchens, menu);
            if (!newDrinks.SequenceEqual(drinks)) menu = BestDishes(kitchens, newDrinks, menu);
            return (menu, newDrinks);
        }

        /// <summary>
        /// The plans the simulator judges (<see cref="InnPlanner.Plan"/>): for each line-up, the model's
        /// best menu, the <see cref="MenusSimulated"/> best of the menus from the dearest dishes to the
        /// quickest (greedy by value − r × time over every r where the order changes, each size the
        /// rules allow), and the dearest menus of each size whatever the model thinks of them (the
        /// model counts speed; the guests' clock may count price more). Each with its best drinks;
        /// only menus meeting the rules, unless none does.
        /// </summary>
        public IEnumerable<(Kitchen[] Kitchens, int[] Menu, int[] Drinks)> Candidates(
            (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) best, IReadOnlyList<Kitchen[]> lineUps)
        {
            var seen = new HashSet<string>();
            foreach (var kitchens in lineUps)
            {
                var menus = new List<int[]>();
                if (ReferenceEquals(kitchens, best.Kitchens)) menus.Add(best.Menu);
                else menus.Add(BestMenu(kitchens, best.Drinks, best.Menu).Menu);
                if (Weights(kitchens, best.Drinks) is var (worth, together))
                {
                    var score = Scorer(kitchens, best.Drinks);
                    var sweep = Enumerable.Range(0, Dishes.Count).Select(d => worth[d] / together[d]).Append(0).Distinct()
                        .SelectMany(r => BuildAtRatio(worth, together, r)).Where(Valid)
                        .Select(m => m.Order().ToArray()).DistinctBy(m => string.Join(",", m)).ToList();
                    menus.AddRange(sweep.OrderByDescending(m => score(m), Comparer<InnPlanScore>.Create((a, b) => a.BetterThan(b) ? 1 : b.BetterThan(a) ? -1 : 0))
                        .Take(MenusSimulated));
                    menus.AddRange(BuildAtRatio(worth, together, 0).Where(Valid).Select(m => m.Order().ToArray()));
                }
                foreach (var menu in menus.Select(m => m.Order().ToArray()))
                {
                    if (!seen.Add(KitchensKey(kitchens) + "#" + string.Join(",", menu))) continue;
                    yield return (kitchens, menu, BestDrinks(kitchens, menu));
                }
            }
        }

        /// <summary>
        /// The best, for kitchens at base speed, of the menus of the dearest dishes (the rules met
        /// first) at each size allowed; null when none meets the rules or no kitchen cooks.
        /// </summary>
        public int[]? DearestMenu(int[] drinks)
        {
            var kitchens = Materialize(PlainLineUp());
            if (Weights(kitchens, drinks) is not var (worth, together)) return null;
            var score = Scorer(kitchens, drinks);
            return BuildAtRatio(worth, together, 0).Where(Valid).Select(m => m.Order().ToArray())
                .OrderByDescending(m => score(m), Comparer<InnPlanScore>.Create((a, b) => a.BetterThan(b) ? 1 : b.BetterThan(a) ? -1 : 0))
                .FirstOrDefault();
        }

        /// <summary>Scores of menus already worked out, by line-up, drinks and dishes.</summary>
        private readonly Dictionary<(string Kitchens, string Drinks, UInt128 Menu), InnPlanScore> _scores = [];

        public static string KitchensKey(IReadOnlyList<Kitchen> kitchens) =>
            string.Join("/", kitchens.Select(k => string.Join("+", k.Chefs.Select(c => c.Id).Order(StringComparer.Ordinal))));

        /// <summary>The menu's score for the line-up and drinks, worked out once.</summary>
        private Func<IReadOnlyCollection<int>, InnPlanScore> Scorer(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            string kitchensKey = KitchensKey(kitchens), drinksKey = string.Join(",", drinks);
            return menu =>
            {
                if (Dishes.Count > 128) return Score(kitchens, [.. menu], drinks);
                UInt128 mask = 0;
                foreach (int i in menu) mask |= (UInt128)1 << i;
                if (!_scores.TryGetValue((kitchensKey, drinksKey, mask), out var score))
                    _scores[(kitchensKey, drinksKey, mask)] = score = Score(kitchens, [.. menu], drinks);
                return score;
            };
        }

        /// <summary>
        /// Each dish's worth (its value and its share of the drinks an order brings) and its time with
        /// every kitchen cooking it at once; null when no kitchen cooks.
        /// </summary>
        private (double[] Worth, double[] Together)? Weights(IReadOnlyList<Kitchen> kitchens, int[] drinks)
        {
            var (seconds, _) = TimesOf(kitchens);
            int cooking = seconds.GetLength(0);
            if (cooking == 0) return null;
            double drinkShare = drinks.Length == 0 ? 0 : DrinksPerOrder * drinks.Sum(i => _drinkWeights[i] * _drinkValues[i]) / drinks.Sum(i => _drinkWeights[i]) / DishesPerOrder;
            var together = Enumerable.Range(0, Dishes.Count).Select(d => 1 / Enumerable.Range(0, cooking).Sum(k => 1 / seconds[k, d])).ToArray();
            var worth = Enumerable.Range(0, Dishes.Count).Select(d => _dishValues[d] + drinkShare).ToArray();
            return (worth, together);
        }

        /// <summary>
        /// Greedy menus of every allowed size by (worth − r × time) × how often the dish is picked: a dish
        /// guests prefer weighs that much more in the menu's income and its kitchens' time alike.
        /// </summary>
        private IEnumerable<List<int>> BuildAtRatio(double[] worth, double[] together, double r)
        {
            var weight = Enumerable.Range(0, Dishes.Count).Select(d => _dishWeights[d] * (worth[d] - r * together[d])).ToArray();
            for (int size = Math.Max(1, Rules.MinDishes); size <= Math.Min(Rules.MaxDishes, Dishes.Count); size++) yield return BuildBy(size, weight);
        }

        /// <summary>
        /// A quick judge of a line-up for the beam: the better of <paramref name="menu"/> and the menus
        /// one step of Dinkelbach's method gives from it for these kitchens (worth − r × time, r the
        /// menu's own worth per second here), so a line-up that suits other dishes isn't judged only
        /// by the dishes another line-up suits.
        /// </summary>
        public InnPlanScore QuickScore(IReadOnlyList<Kitchen> kitchens, int[] menu, int[] drinks)
        {
            var score = Scorer(kitchens, drinks);
            var best = score(menu);
            if (Weights(kitchens, drinks) is not var (worth, together) || menu.Length == 0) return best;
            // Each step's best menu gives the next step's r, until it settles.
            IReadOnlyList<int> current = menu;
            for (int step = 0; step < _dinkelbachSteps; step++)
            {
                double r = current.Sum(d => _dishWeights[d] * worth[d]) / current.Sum(d => _dishWeights[d] * together[d]);
                IReadOnlyList<int>? next = null;
                foreach (var candidate in BuildAtRatio(worth, together, r))
                {
                    if (!Valid(candidate)) continue;
                    var s = score(candidate);
                    if (s.BetterThan(best)) (best, next) = (s, candidate);
                }
                if (next is null) break;
                current = next;
            }
            return best;
        }

        private int[] BestDishes(IReadOnlyList<Kitchen> kitchens, int[] drinks, int[]? seed)
        {
            var ScoreOf = Scorer(kitchens, drinks);

            // Starting menus: greedy by the score itself, and by a weight per dish for a range of
            // thresholds r: value (with its share of the drinks) − r × its time with every kitchen
            // cooking it. For one kitchen and no rules the best menu is the top of that order for
            // some r (Dinkelbach's method for a best ratio); the swaps below take care of the rest.
            int smallest = Math.Max(1, Rules.MinDishes), largest = Math.Min(Rules.MaxDishes, Dishes.Count);
            var starts = new List<List<int>>();
            for (int size = smallest; size <= largest; size++) starts.Add(Build(size, ScoreOf));
            if (Weights(kitchens, drinks) is var (worth, together))
            {
                foreach (double r in Enumerable.Range(0, Dishes.Count).Select(d => worth[d] / together[d]).Append(0).Distinct())
                    starts.AddRange(BuildAtRatio(worth, together, r));
            }

            // The best few distinct starts (valid ones first) are improved by swaps.
            var chosen = starts
                .DistinctBy(m => string.Join(",", m.Order()))
                .OrderByDescending(m => Valid(m))
                .ThenByDescending(m => m, Comparer<List<int>>.Create((a, b) => ScoreOf(a).BetterThan(ScoreOf(b)) ? 1 : ScoreOf(b).BetterThan(ScoreOf(a)) ? -1 : 0))
                .Take(StartsImproved)
                .ToList();
            if (seed is { Length: > 0 }) chosen.Add([.. seed]);
            List<int>? best = null;
            foreach (var menu in chosen)
            {
                Improve(menu, ScoreOf);
                bool valid = Valid(menu), bestValid = best is not null && Valid(best);
                if (best is null || (valid && !bestValid) || (valid == bestValid && ScoreOf(menu).BetterThan(ScoreOf(best))))
                    best = menu;
            }
            return [.. (best ?? []).Order()];
        }

        /// <summary>Starting menus improved by swaps, per menu search.</summary>
        private const int StartsImproved = 6;

        /// <summary>
        /// First the dishes that bring the menu closest to its rules, then the rest, each time the one
        /// <paramref name="better"/> prefers, up to <paramref name="size"/>.
        /// </summary>
        private List<int> BuildWith(int size, Func<List<int>, int, int, bool> better)
        {
            var menu = new List<int>();
            while (menu.Count < size)
            {
                int pick = -1, pickShortfall = int.MaxValue;
                for (int i = 0; i < Dishes.Count; i++)
                {
                    if (menu.Contains(i)) continue;
                    menu.Add(i);
                    int shortfall = Shortfall(menu);
                    menu.RemoveAt(menu.Count - 1);
                    if (pick < 0 || shortfall < pickShortfall || (shortfall == pickShortfall && better(menu, i, pick)))
                        (pick, pickShortfall) = (i, shortfall);
                }
                if (pick < 0) break;
                menu.Add(pick);
            }
            return menu;
        }

        /// <summary>Greedy by the score of the menu with each dish added.</summary>
        private List<int> Build(int size, Func<IReadOnlyCollection<int>, InnPlanScore> score) =>
            BuildWith(size, (menu, i, pick) => score([.. menu, i]).BetterThan(score([.. menu, pick])));

        /// <summary>Greedy by a weight per dish.</summary>
        private List<int> BuildBy(int size, double[] weight) => BuildWith(size, (_, i, pick) => weight[i] > weight[pick]);

        /// <summary>Add, remove or swap one dish, the best move each time, while the menu stays valid and pays more.</summary>
        private void Improve(List<int> menu, Func<IReadOnlyCollection<int>, InnPlanScore> score)
        {
            if (!Valid(menu)) return;
            while (true)
            {
                var current = score(menu);
                List<int>? bestMove = null;
                var bestScore = current;
                void Try(List<int> candidate)
                {
                    if (!Valid(candidate)) return;
                    var s = score(candidate);
                    if (s.BetterThan(bestScore)) (bestMove, bestScore) = (candidate, s);
                }
                var outside = Enumerable.Range(0, Dishes.Count).Where(i => !menu.Contains(i)).ToList();
                if (menu.Count < Rules.MaxDishes)
                    foreach (int add in outside) Try([.. menu, add]);
                if (menu.Count > Rules.MinDishes)
                    foreach (int remove in menu) Try([.. menu.Where(i => i != remove)]);
                foreach (int remove in menu)
                    foreach (int add in outside) Try([.. menu.Where(i => i != remove), add]);
                if (bestMove is null) return;
                menu.Clear();
                menu.AddRange(bestMove);
            }
        }

        // ---- Chefs ----

        /// <summary>A line-up: per kitchen (in the input's order), the profiles of its chefs; and the chefs of each profile left.</summary>
        public sealed record LineUp(int[][] Kitchens, int[] Left);

        /// <summary>No chef placed yet.</summary>
        private LineUp Empty() => new([.. _input.Kitchens.Select(_ => Array.Empty<int>())], [.. _profiles.Select(p => p.Operators.Count)]);

        /// <summary>As many kitchens as there are chefs, each with a cook of no bonus (the menu's starting point).</summary>
        public LineUp PlainLineUp() => Empty();

        /// <summary>
        /// The line-up's kitchens. A profile's chefs are given out in the input's order. Kitchens not
        /// yet reached (<paramref name="reached"/>) get a cook of no bonus while chefs are left for them,
        /// so a partial line-up is judged as it would at least end up.
        /// </summary>
        public Kitchen[] Materialize(LineUp lineUp, int reached = -1)
        {
            var next = new int[_profiles.Count];
            int spare = lineUp.Left.Sum();
            var kitchens = new Kitchen[_input.Kitchens.Count];
            for (int k = 0; k < kitchens.Length; k++)
            {
                var chefs = new List<OperatorInfo>(_placed[k]);
                foreach (int profile in lineUp.Kitchens[k])
                {
                    var operators = _profiles[profile].Operators;
                    chefs.Add(operators[Math.Min(next[profile]++, operators.Count - 1)]);
                }
                if (chefs.Count == 0 && k > reached && spare > 0)
                {
                    chefs.Add(PlainCook);
                    spare--;
                }
                kitchens[k] = new Kitchen(_input.Kitchens[k].Number, chefs);
            }
            return kitchens;
        }

        /// <summary>
        /// The line-ups, best first: chef places filled one at a time (kitchen by kitchen), each with
        /// any profile that has chefs left or left empty, keeping the beam's width (<see cref="BeamWidth"/> in the full search) best
        /// at each place as judged with <paramref name="menu"/> and <paramref name="drinks"/>.
        /// </summary>
        public IReadOnlyList<LineUp> Beam(int[] menu, int[] drinks)
        {
            var places = _input.Kitchens.SelectMany((_, i) => Enumerable.Repeat(i, Free(i))).ToList();
            var beam = new List<LineUp> { Empty() };
            for (int place = 0; place < places.Count; place++)
            {
                _cancel.ThrowIfCancellationRequested();
                int kitchen = places[place];
                bool lastOfKitchen = place + 1 == places.Count || places[place + 1] != kitchen;
                var next = new Dictionary<string, (LineUp LineUp, InnPlanScore Score, int Chefs)>();
                foreach (var lineUp in beam)
                {
                    foreach (int profile in Enumerable.Range(-1, _profiles.Count + 1))
                    {
                        if (profile >= 0 && lineUp.Left[profile] == 0) continue;
                        var kitchens = lineUp.Kitchens.Select(a => a).ToArray();
                        var left = lineUp.Left;
                        if (profile >= 0)
                        {
                            kitchens[kitchen] = [.. lineUp.Kitchens[kitchen].Append(profile).Order()];
                            left = [.. lineUp.Left];
                            left[profile]--;
                        }
                        var candidate = new LineUp(kitchens, left);
                        string key = Key(candidate, lastOfKitchen ? kitchen : kitchen - 1);
                        if (next.ContainsKey(key)) continue;
                        var score = QuickScore(Materialize(candidate, kitchen), menu, drinks);
                        next[key] = (candidate, score, kitchens.Sum(a => a.Length));
                    }
                }
                beam = [.. next.Values.OrderByDescending(x => x, Comparer<(LineUp LineUp, InnPlanScore Score, int Chefs)>.Create(Compare))
                    .Take(_beamWidth).Select(x => x.LineUp)];
            }
            return beam;

            // Better first: the score, then more chefs placed (a chef never slows a kitchen).
            static int Compare((LineUp LineUp, InnPlanScore Score, int Chefs) a, (LineUp LineUp, InnPlanScore Score, int Chefs) b) =>
                a.Score.BetterThan(b.Score) ? 1 : b.Score.BetterThan(a.Score) ? -1 : a.Chefs.CompareTo(b.Chefs);
        }

        /// <summary>Line-ups alike up to the order of finished kitchens with the same number of places are one.</summary>
        private string Key(LineUp lineUp, int finishedUpTo)
        {
            var parts = new List<string>();
            var finished = Enumerable.Range(0, finishedUpTo + 1)
                .GroupBy(k => $"{Free(k)}/{string.Join("+", _placed[k].Select(c => c.Id))}")
                .SelectMany(g => g.Select(k => $"{g.Key}:{string.Join(",", lineUp.Kitchens[k])}").Order(StringComparer.Ordinal));
            parts.AddRange(finished);
            parts.Add("#");
            parts.AddRange(Enumerable.Range(finishedUpTo + 1, lineUp.Kitchens.Length - finishedUpTo - 1).Select(k => string.Join(",", lineUp.Kitchens[k])));
            return string.Join(";", parts);
        }

        /// <summary>
        /// Places left empty while chefs are left over get the chef that pays most there (a chef never
        /// slows a kitchen; it may add choice-dish chance), and the menu is chosen again for them.
        /// </summary>
        public (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) FillEmptyPlaces((Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) plan)
        {
            var kitchens = plan.Kitchens.ToArray();
            var used = kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
            var spare = _profiles.SelectMany(p => p.Operators).Where(o => !used.Contains(o.Id)).ToList();
            bool changed = false;
            while (spare.Count > 0)
            {
                (int Kitchen, OperatorInfo Chef, InnPlanScore Score)? best = null;
                for (int k = 0; k < kitchens.Length; k++)
                {
                    if (kitchens[k].Chefs.Count >= _input.Kitchens[k].Places) continue;
                    foreach (var chef in spare)
                    {
                        var trial = kitchens.ToArray();
                        trial[k] = kitchens[k] with { Chefs = [.. kitchens[k].Chefs, chef] };
                        var score = Score(trial, plan.Menu, plan.Drinks);
                        if (best is null || score.BetterThan(best.Value.Score)) best = (k, chef, score);
                    }
                }
                if (best is not { } b) break;
                kitchens[b.Kitchen] = kitchens[b.Kitchen] with { Chefs = [.. kitchens[b.Kitchen].Chefs, b.Chef] };
                spare.Remove(b.Chef);
                changed = true;
            }
            if (!changed) return plan;
            // The old menu with the added chefs scores at least what it did; keep whichever menu is better now.
            var kept = Score(kitchens, plan.Menu, plan.Drinks);
            var (menu, drinks) = BestMenu(kitchens, plan.Drinks, plan.Menu);
            var chosen = Score(kitchens, menu, drinks);
            return chosen.BetterThan(kept) ? (kitchens, menu, drinks, chosen) : (kitchens, plan.Menu, plan.Drinks, kept);
        }

        /// <summary>
        /// The last improvement: line-ups one change away from the plan (a chef replaced by one not
        /// placed, taken out, or two chefs of different kitchens swapped), each judged quickly with the
        /// plan's menu (<see cref="QuickScore"/>); the best <see cref="LineUpsTried"/> get a full menu
        /// search, and the best that beats the plan is taken, until none does. This finds a better
        /// chef and dish changed together, which choosing chefs and menu in turn can't.
        /// </summary>
        public (Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) Polish((Kitchen[] Kitchens, int[] Menu, int[] Drinks, InnPlanScore Score) plan)
        {
            for (int round = 0; round < MaxPolishRounds; round++)
            {
                _cancel.ThrowIfCancellationRequested();
                var neighbours = Neighbours(plan.Kitchens)
                    .Select(k => (Kitchens: k, Quick: Better(QuickScore(k, plan.Menu, plan.Drinks), OneDishChanged(k, plan.Menu, plan.Drinks))))
                    .OrderByDescending(x => x.Quick, Comparer<InnPlanScore>.Create((a, b) => a.BetterThan(b) ? 1 : b.BetterThan(a) ? -1 : 0))
                    .Take(LineUpsTried)
                    .ToList();
                var best = plan;
                foreach (var (kitchens, _) in neighbours)
                {
                    var (menu, drinks) = BestMenu(kitchens, plan.Drinks, plan.Menu);
                    var score = Score(kitchens, menu, drinks);
                    if (score.BetterThan(best.Score)) best = (kitchens, menu, drinks, score);
                }
                if (ReferenceEquals(best.Kitchens, plan.Kitchens)) break;
                plan = best;
            }
            return plan;
        }

        private static InnPlanScore Better(InnPlanScore a, InnPlanScore b) => b.BetterThan(a) ? b : a;

        /// <summary>The best score of <paramref name="menu"/> or a valid menu one dish added, removed or swapped from it.</summary>
        private InnPlanScore OneDishChanged(IReadOnlyList<Kitchen> kitchens, int[] menu, int[] drinks)
        {
            var score = Scorer(kitchens, drinks);
            var best = score(menu);
            var outside = Enumerable.Range(0, Dishes.Count).Where(i => !menu.Contains(i)).ToList();
            void Try(List<int> candidate)
            {
                if (Valid(candidate)) best = Better(best, score(candidate));
            }
            if (menu.Length < Rules.MaxDishes)
                foreach (int add in outside) Try([.. menu, add]);
            if (menu.Length > Rules.MinDishes)
                foreach (int remove in menu) Try([.. menu.Where(i => i != remove)]);
            foreach (int remove in menu)
                foreach (int add in outside) Try([.. menu.Where(i => i != remove), add]);
            return best;
        }

        /// <summary>Rounds of <see cref="Polish"/> at most.</summary>
        private const int MaxPolishRounds = 6;

        private IEnumerable<Kitchen[]> Neighbours(Kitchen[] kitchens)
        {
            var used = kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
            // One spare chef per profile: the others of that profile change nothing.
            var spare = _profiles.Select(p => p.Operators.FirstOrDefault(o => !used.Contains(o.Id))).OfType<OperatorInfo>().ToList();
            Kitchen[] With(int k, IEnumerable<OperatorInfo> chefs)
            {
                var copy = kitchens.ToArray();
                copy[k] = kitchens[k] with { Chefs = [.. chefs] };
                return copy;
            }
            for (int k = 0; k < kitchens.Length; k++)
            {
                var chefs = kitchens[k].Chefs;
                // The placed chefs (first) are never taken out or replaced.
                for (int i = _placed[k].Length; i < chefs.Count; i++)
                {
                    yield return With(k, chefs.Where((_, j) => j != i));
                    foreach (var chef in spare) yield return With(k, chefs.Select((c, j) => j == i ? chef : c));
                }
                if (chefs.Count < _input.Kitchens[k].Places)
                    foreach (var chef in spare) yield return With(k, [.. chefs, chef]);
            }
            for (int a = 0; a < kitchens.Length; a++)
            {
                for (int b = a + 1; b < kitchens.Length; b++)
                {
                    for (int i = _placed[a].Length; i < kitchens[a].Chefs.Count; i++)
                    {
                        for (int j = _placed[b].Length; j < kitchens[b].Chefs.Count; j++)
                        {
                            var copy = kitchens.ToArray();
                            copy[a] = kitchens[a] with { Chefs = [.. kitchens[a].Chefs.Select((c, x) => x == i ? kitchens[b].Chefs[j] : c)] };
                            copy[b] = kitchens[b] with { Chefs = [.. kitchens[b].Chefs.Select((c, x) => x == j ? kitchens[a].Chefs[i] : c)] };
                            yield return copy;
                        }
                    }
                }
            }
        }
    }
}

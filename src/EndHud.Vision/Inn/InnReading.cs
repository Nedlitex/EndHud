namespace EndHud.Vision.Inn;

/// <summary>
/// Which of the inn's screens is showing (<see cref="InnScreen.DetectAsync"/>). The drink bar and
/// the results screen aren't told apart yet: no frame of either has been recorded.
/// </summary>
public enum InnScreenKind
{
    /// <summary>营业设定 (and the theme list before it, 主题设定): theme, kitchens and chefs.</summary>
    Setup,

    /// <summary>A kitchen's chef picker (厨房一 … with the operator grid).</summary>
    ChefPicker,

    /// <summary>菜单设定: choosing the dishes and drinks.</summary>
    Menu,

    /// <summary>The session itself: the table cards at the top, the timer at the top left.</summary>
    Session,

    /// <summary>The dispatch panel (派单) over the session: a kitchen's dishes, chefs and queue.</summary>
    DispatchPanel,
}

/// <summary>The session's time left (the gold pill at the top left).</summary>
/// <param name="Closed">The timer reached 0:00: no new guests (those seated are still served).</param>
public sealed record SessionClock(TimeSpan Left, bool Closed);

/// <summary>
/// The Full Blaze meter (满堂彩, 灶中火) under the round button at the right. Below full it shows the
/// fire "N/100"; at full the number goes and the button turns red with "开启满堂彩 ▸▸ [R]".
/// </summary>
/// <param name="Fire">0–100; 100 when <paramref name="Ready"/>; null when the number couldn't be read.</param>
/// <param name="Streak">成功连续上餐: orders served in a row; null when it couldn't be read.</param>
/// <param name="Ready">Full: Full Blaze can be started (R).</param>
public sealed record FireMeter(int? Fire, int? Streak, bool Ready);

/// <summary>What a table card at the top of the session shows.</summary>
public enum TableState
{
    /// <summary>Only the number tab: no guest.</summary>
    Empty,

    /// <summary>Grey card, 点餐中: a guest is choosing.</summary>
    Ordering,

    /// <summary>White card with one round picture per item ordered.</summary>
    Order,

    /// <summary>Grey card, 用餐中: everything served, the guest is eating.</summary>
    Eating,

    /// <summary>Red card, 待结账: waiting for the player to collect payment.</summary>
    Paying,

    /// <summary>Yellow card, 老饕 (a raised hand): a gourmand has come and is speaking.</summary>
    GourmandArrived,

    /// <summary>Yellow card, 点餐中: a gourmand is choosing.</summary>
    GourmandOrdering,

    /// <summary>Yellow card with category icons (what kind of dish or drink) instead of pictures.</summary>
    GourmandOrder,

    /// <summary>Yellow card, 用餐中: a gourmand is eating.</summary>
    GourmandEating,
}

/// <summary>Where an ordered item is, from the badge on its picture.</summary>
public enum ItemProgress
{
    /// <summary>No badge: not started.</summary>
    Waiting,

    /// <summary>A cyan flower: being made (in a kitchen or at the drink bar).</summary>
    Making,

    /// <summary>A green smiley: ready to carry.</summary>
    Ready,
}

/// <summary>One item of a table's order.</summary>
/// <param name="Id">The dish's or drink's id (<see cref="Dish.Id"/>, <see cref="InnDrink.Id"/>); null when its picture couldn't be told apart (never guessed).</param>
public sealed record OrderItem(string? Id, ItemProgress Progress);

/// <summary>A table card at the top of the session.</summary>
/// <param name="Table">1–6, left to right.</param>
/// <param name="Items">For <see cref="TableState.Order"/>: one per picture, left to right; otherwise empty.</param>
/// <param name="Served">One per pip under the number, green = served; empty when there are none.</param>
public sealed record TableCard(int Table, TableState State, IReadOnlyList<OrderItem> Items, IReadOnlyList<bool> Served);

using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// What the inn's session overlays show at one moment, before anything is drawn: the banners' calls, the marks on the
/// open dispatch panel, and the gourmands' circles.
/// </summary>
internal sealed record InnViews(IReadOnlyList<InnCall> Calls, IReadOnlyList<InnPanelMark> Marks, IReadOnlyList<InnWant> Wants)
{
    public static readonly InnViews None = new([], [], []);
}

/// <summary>
/// The overlay's inn state without the drawing (<see cref="OverlayWindow"/> draws what it says): which inn screen the
/// last reading was, the readings handed to the session, and what the session's overlays show for it. The replay of a
/// recording (InnReplayTests) drives this same class, so what it measures is what the overlay does.
/// </summary>
internal sealed class InnDisplay
{
    private readonly InnWantsHold _wants = new();

    /// <summary>The last reading was a session frame (with or without the dispatch panel over it).</summary>
    public bool OnSession { get; private set; }

    /// <summary>The last reading's screen when it was the setup screen, a chef picker or the menu screen.</summary>
    public InnScreenKind? SetupScreen { get; private set; }

    /// <summary>The menu screen's cards in view, as last read while it shows (kept through a read that found none).</summary>
    public IReadOnlyList<MenuCard> MenuCards { get; private set; } = [];

    /// <summary>A chef picker's cards in view, as last read while it shows.</summary>
    public IReadOnlyList<ChefCard> PickerCards { get; private set; } = [];

    /// <summary>A reading of the inn (null: a frame that isn't the inn), handed to <paramref name="session"/>.</summary>
    public void Show(InnSession? session, InnReading? reading, TimeSpan now)
    {
        if (reading is null)
        {
            OnSession = false;
            SetupScreen = null;
            MenuCards = [];
            PickerCards = [];
            session?.Tick(now);
            return;
        }
        session?.Update(reading);
        OnSession = reading.InSession;
        var setupScreen = reading.OnSetupScreens ? reading.Screen : (InnScreenKind?)null;
        // Cards of another screen are gone; a read that found none (or a frame between reads) keeps those last read.
        if (setupScreen != InnScreenKind.Menu) MenuCards = [];
        else if (reading.Menu is { Cards.Count: > 0 } menu) MenuCards = menu.Cards;
        if (setupScreen != InnScreenKind.ChefPicker) PickerCards = [];
        else if (reading.PickerCards is { Count: > 0 } cards) PickerCards = cards;
        SetupScreen = setupScreen;
    }

    /// <summary>
    /// The marks on the setup screens' cards (<see cref="InnSetupMarks"/>): the menu screen's recommended dishes and drinks
    /// (<paramref name="menu"/>'s plan), a chef picker's recommended chefs for its kitchen (<paramref name="chefs"/>' suggestion
    /// for <paramref name="pickerKitchen"/>).
    /// </summary>
    public IReadOnlyList<InnPanelMark> SetupMarks(InnSetupAdvice? chefs, InnSetupAdvice? menu, int? pickerKitchen, Func<string, string> text)
    {
        IReadOnlyCollection<string> recommended = SetupScreen switch
        {
            InnScreenKind.Menu when menu is not null => [.. menu.Plan.Dishes.Select(d => d.Dish.Id), .. menu.Plan.Drinks.Select(d => d.Drink.Id)],
            InnScreenKind.ChefPicker when chefs?.Suggestions.Kitchens.FirstOrDefault(k => k.Number == pickerKitchen) is { } kitchen =>
                [.. kitchen.Chefs.Select(c => c.Id)],
            _ => [],
        };
        return InnSetupMarks.Of(SetupScreen, recommended, MenuCards, PickerCards, text);
    }

    /// <summary>What the session's overlays show now: nothing off a session.</summary>
    /// <param name="callsEnabled">The banners' setting (InnCallsEnabled): the panel's marks go with it.</param>
    /// <param name="text">The i18n lookup.</param>
    public InnViews Views(InnSession? session, TimeSpan now, bool callsEnabled, Func<string, string> text)
    {
        if (!OnSession || session is null || Advice(session, now) is not { } advice)
        {
            if (session is not { Active: true }) _wants.Clear();
            return InnViews.None;
        }
        return new InnViews(
            // The banners, except over the open dispatch panel, where its marks take over.
            advice.BannerCalls,
            // The marks only while the panel is on screen: gone the moment a frame reads it closed.
            callsEnabled ? InnPanelMarks.Of(advice.MarkedPanel, advice.Calls, text) : [],
            // What each gourmand wants, as glowing circles, while their order is open, held steady.
            _wants.Update(advice, text));
    }

    /// <summary>The session's advice now; null (and logged) if working it out failed, so the overlay carries on.</summary>
    private static InnAdvice? Advice(InnSession session, TimeSpan now)
    {
        try
        {
            return session.Advice(now);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            DiagnosticLog.Throttled("inn-advice-" + ex.GetType().Name, TimeSpan.FromSeconds(30), $"inn: working out the advice failed: {ex}", "ERROR");
            return null;
        }
    }
}

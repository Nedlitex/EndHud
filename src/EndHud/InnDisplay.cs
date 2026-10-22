using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// What the inn's session overlays show at one moment, before anything is drawn: the banners' calls, the marks on the
/// open dispatch panel, and the gourmands' requests (their banners' rows).
/// </summary>
internal sealed record InnViews(IReadOnlyList<InnCall> Calls, IReadOnlyList<InnPanelMark> Marks, IReadOnlyList<InnWant> Wants)
{
    public static readonly InnViews None = new([], [], []);

    /// <summary>
    /// The dispatch panel is open, or the last frame read it so: the banners (Full Blaze, the last call) are put away in
    /// place (hidden, not taken down), so the same ones show again when it closes, with no slide-in (owner, 2026-10-21:
    /// "the recommended dish flashes heavily"); the kitchens' circles stand in a row below the panel
    /// (<see cref="Circles"/>), and the gourmands' tiles stay at their fixed spots beside it (<see cref="InnGourmandLayout"/>:
    /// their place never depends on the panel), so nothing but the marks is over it and nothing the player follows
    /// disappears or moves while it's open.
    /// </summary>
    public bool Away { get; init; }

    /// <summary>Where the kitchens' circles are drawn: their row above the tray, or below the dispatch panel while it's open.</summary>
    public InnCirclePlace Circles { get; init; }

    /// <summary>The kitchen whose tab is selected on the open panel (its circle emphasised); null with the panel closed.</summary>
    public int? SelectedKitchen { get; init; }

    /// <summary>A session's views (<see cref="None"/> is not): the banners' rows are kept while this holds (<see cref="InnBannerRows"/>).</summary>
    public bool Session { get; init; }

    /// <summary>The kitchens that cook this session, each with its banner row from the start (<see cref="InnBannerRows"/>).</summary>
    public IReadOnlyList<int> Kitchens { get; init; } = [];
}

/// <summary>
/// The overlay's inn state without the drawing (<see cref="OverlayWindow"/> draws what it says): which inn screen the
/// last reading was, the readings handed to the session, and what the session's overlays show for it. The replay of a
/// recording (InnReplayTests) drives this same class, so what it measures is what the overlay does.
/// </summary>
internal sealed class InnDisplay
{
    private readonly InnWantsHold _wants = new();

    /// <summary>
    /// The kitchen tabs the last fresh frame read on the dispatch panel (null: no panel), when the session doesn't count
    /// the panel open yet (it does after two frames, InnSession.PanelFramesToOpen). Owner, live 2026-10-21 (test_yuanyuan5
    /// at 02:25, 01:20 and 00:09): banners drawn over the open panel. Measured on the two recorded sessions: 51 and 13
    /// frames reading the panel showed them (InnKitchenCircleTests). A frame reading the panel with as many tabs as the
    /// kitchens that cook, and as any panel read before this session, puts everything away at once. One reading fewer tabs
    /// does not: on the 2026-10-20 recording 48 single frames read a one-tab "panel" between frames showing the Full Blaze
    /// meter it would cover (the dark bar behind the theme line), and each would blink everything off and on.
    /// </summary>
    private int? _panelTabs;

    /// <summary>The kitchen the last fresh frame read the panel showing (with <see cref="_panelTabs"/>).</summary>
    private int? _panelKitchen;

    /// <summary>The most kitchen tabs a panel was read with this session (<see cref="_panelTabs"/>), before the last frame.</summary>
    private int _mostTabs, _mostTabsBefore;

    /// <summary>The last reading was a session frame (with or without the dispatch panel over it).</summary>
    public bool OnSession { get; private set; }

    /// <summary>The last reading's screen when it was the setup screen, a chef picker or the menu screen.</summary>
    public InnScreenKind? SetupScreen { get; private set; }

    /// <summary>The advice the last <see cref="Views"/> drew from; null off a session (the replay's trace reads every call from it, banners hidden or not).</summary>
    public InnAdvice? LastAdvice { get; private set; }

    /// <summary>A reading of the inn (null: a frame that isn't the inn), handed to <paramref name="session"/>.</summary>
    public void Show(InnSession? session, InnReading? reading, TimeSpan now)
    {
        if (reading is null)
        {
            OnSession = false;
            SetupScreen = null;
            _panelTabs = _panelKitchen = null;
            session?.Tick(now);
            return;
        }
        // A held reading (a missed frame) says nothing new about the panel.
        if (reading.Fresh)
        {
            _panelTabs = reading.InSession ? reading.Panel?.Kitchens : null;
            _panelKitchen = reading.InSession ? reading.Panel?.Kitchen : null;
            _mostTabsBefore = _mostTabs;
            _mostTabs = Math.Max(_mostTabs, _panelTabs ?? 0);
        }
        session?.Update(reading);
        OnSession = reading.InSession;
        SetupScreen = reading.OnSetupScreens ? reading.Screen : null;
    }

    /// <summary>What the session's overlays show now: nothing off a session.</summary>
    /// <param name="callsEnabled">The banners' setting (InnCallsEnabled): the panel's marks go with it.</param>
    /// <param name="text">The i18n lookup.</param>
    public InnViews Views(InnSession? session, TimeSpan now, bool callsEnabled, Func<string, string> text)
    {
        LastAdvice = null;
        if (!OnSession || session is null || Advice(session, now) is not { } advice)
        {
            if (session is not { Active: true })
            {
                _wants.Clear();
                _mostTabs = _mostTabsBefore = 0;
            }
            return InnViews.None;
        }
        LastAdvice = advice;
        var wants = _wants.Update(advice, text, now);
        bool away = advice.Panel is not null
                    || (_panelTabs is { } tabs && tabs >= _mostTabsBefore && tabs >= (advice.Dispatch?.Kitchens.Count ?? 0));
        return new InnViews(
            // The banners: every call, put away in place over the open dispatch panel, where its marks take over.
            advice.Calls,
            // The marks only while the panel is on screen: gone the moment a frame reads it closed.
            callsEnabled ? InnPanelMarks.Of(advice.MarkedPanel, advice.Calls, advice.Cards, text) : [],
            // What each gourmand wants, while their order is open, held steady; put away in place over the open panel too.
            wants)
        {
            Away = away,
            Session = true,
            // Over the open panel the circles move to the row below it (the panel covers the tray and the row above it), the
            // kitchen whose tab is selected emphasised: the circles, the tabs' marks and the card's mark are one picture.
            Circles = away ? InnCirclePlace.BelowPanel : InnCirclePlace.AboveTray,
            SelectedKitchen = away ? (advice.MarkedPanel ?? advice.Panel)?.Kitchen ?? _panelKitchen : null,
            Kitchens = [.. advice.Dispatch?.Kitchens.Select(k => k.Kitchen) ?? []],
        };
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

using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// What the inn's session overlays show at one moment, before anything is drawn: the banners' calls, the marks on the
/// open dispatch panel, and the gourmands' circles.
/// </summary>
/// <param name="Panel">The gourmands' panel; null when it's put away.</param>
internal sealed record InnViews(IReadOnlyList<InnCall> Calls, IReadOnlyList<InnPanelMark> Marks, IReadOnlyList<InnWant> Wants, InnStripModel? Panel)
{
    public static readonly InnViews None = new([], [], [], null);
}

/// <summary>
/// The overlay's inn state without the drawing (<see cref="OverlayWindow"/> draws what it says): which inn screen the
/// last reading was, the readings handed to the session, and what the session's overlays show for it. The replay of a
/// recording (InnReplayTests) drives this same class, so what it measures is what the overlay does.
/// </summary>
internal sealed class InnDisplay
{
    /// <summary>The last reading was a session frame (with or without the dispatch panel over it).</summary>
    public bool OnSession { get; private set; }

    /// <summary>The last reading's screen when it was the setup screen, a chef picker or the menu screen.</summary>
    public InnScreenKind? SetupScreen { get; private set; }

    /// <summary>A reading of the inn (null: a frame that isn't the inn), handed to <paramref name="session"/>.</summary>
    public void Show(InnSession? session, InnReading? reading, TimeSpan now)
    {
        if (reading is null)
        {
            OnSession = false;
            SetupScreen = null;
            session?.Tick(now);
            return;
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
        if (!OnSession || session is null || Advice(session, now) is not { } advice) return InnViews.None;
        return new InnViews(
            // The banners, except over the open dispatch panel, where its marks take over.
            advice.BannerCalls,
            callsEnabled ? InnPanelMarks.Of(advice.Panel, advice.Calls, text) : [],
            // What each gourmand wants, as glowing circles, while their order is open.
            InnWantsView.Of(advice),
            // The panel: the gourmands only; empty, it's put away.
            InnStripView.Build(advice, text) is { Lines.Count: > 0 } built ? built : null);
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

using EndHud.Vision.Inn;

namespace EndHud;

/// <summary>
/// What the inn's session overlays show at one moment, before anything is drawn: the banners' calls, the marks on the
/// open dispatch panel, and the gourmands' requests (their banners' rows).
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

    /// <summary>The advice the last <see cref="Views"/> drew from; null off a session (the replay's trace reads every call from it, banners hidden or not).</summary>
    public InnAdvice? LastAdvice { get; private set; }

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
        LastAdvice = null;
        if (!OnSession || session is null || Advice(session, now) is not { } advice)
        {
            if (session is not { Active: true }) _wants.Clear();
            return InnViews.None;
        }
        LastAdvice = advice;
        var wants = _wants.Update(advice, text, now);
        return new InnViews(
            // The banners, except over the open dispatch panel, where its marks take over.
            advice.BannerCalls,
            // The marks only while the panel is on screen: gone the moment a frame reads it closed.
            callsEnabled ? InnPanelMarks.Of(advice.MarkedPanel, advice.Calls, text) : [],
            // What each gourmand wants, while their order is open, held steady; put away over the open panel (the banners would cover it).
            advice.Panel is null ? wants : []);
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

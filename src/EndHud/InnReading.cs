using EndHud.Vision;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;

namespace EndHud;

/// <summary>
/// One frame of the inn, as read: which screen, and what that screen shows that the helper uses.
/// A field the screen doesn't have (the table cards on the setup screen) or that couldn't be read
/// is null or empty; nothing is guessed.
/// </summary>
/// <param name="At">When it was read, on a clock that keeps running (not the session's timer).</param>
/// <param name="Clock">The session's time left; null off the session or when unread.</param>
/// <param name="Tables">The six table cards (null where one couldn't be read); empty off the session.</param>
/// <param name="Fire">The Full Blaze meter; null when hidden (the dispatch panel covers it) or unread.</param>
/// <param name="Panel">The dispatch panel, when it's open.</param>
/// <param name="Bubble">A gourmand's speech bubble, read only while a gourmand is at a table.</param>
/// <param name="Setup">The setup screens (theme, kitchens and chefs).</param>
/// <param name="Menu">The menu screen (dishes and drinks on the menu, their stock).</param>
/// <param name="Requirements">The menu screen's 菜单要求 (Inn Rating, the menu's minimums).</param>
/// <param name="Fresh">The screen was read on this frame. The setup screens are read every
/// <see cref="InnFrameReader.SetupEvery"/>; a frame between says only which screen shows, and a
/// null <paramref name="Setup"/> on it isn't a failed reading.</param>
internal sealed record InnReading(InnScreenKind Screen, TimeSpan At, SessionClock? Clock = null,
    IReadOnlyList<TableCard?>? Tables = null, FireMeter? Fire = null, DispatchPanel? Panel = null,
    GourmandBubble? Bubble = null, InnSetup? Setup = null, InnMenu? Menu = null,
    MenuRequirements? Requirements = null, bool Fresh = true)
{
    public IReadOnlyList<TableCard?> TableCards => Tables ?? [];

    /// <summary>On a chef picker: the kitchen it's for (kitchen one = 1), read off its title; null when unread or between reads.</summary>
    public int? PickerKitchen { get; init; }

    /// <summary>A session frame (with or without the dispatch panel over it).</summary>
    public bool InSession => Screen is InnScreenKind.Session or InnScreenKind.DispatchPanel;

    /// <summary>A screen before the session with advice on it: the setup screen, a chef picker, the menu (not the theme list).</summary>
    public bool OnSetupScreens => Screen is InnScreenKind.Setup or InnScreenKind.ChefPicker or InnScreenKind.Menu;
}

/// <summary>The inn's screen readers, one call per thing read: the seam the wire-up's tests replace.</summary>
internal interface IInnScreens
{
    /// <summary>Which inn screen this is (the titles by OCR, the timer by its digits); null when it isn't the inn.</summary>
    Task<InnScreenKind?> DetectAsync(Frame frame);

    /// <summary>The session's time left, by the timer's digits only (no OCR); null when there's no timer.</summary>
    SessionClock? ReadClock(Frame frame);

    Task<IReadOnlyList<TableCard?>> ReadTablesAsync(Frame frame);

    Task<FireMeter?> ReadFireAsync(Frame frame);

    /// <summary>The dispatch panel, or null when it isn't open (told by its tabs' pixels before any OCR).</summary>
    Task<DispatchPanel?> ReadPanelAsync(Frame frame);

    Task<GourmandBubble?> ReadBubbleAsync(Frame frame);

    Task<InnSetup?> ReadSetupAsync(Frame frame);

    /// <summary>The kitchen a chef picker is for, off its title (kitchen one = 1); null when unread.</summary>
    Task<int?> ReadPickerKitchenAsync(Frame frame);

    Task<InnMenu?> ReadMenuAsync(Frame frame);

    /// <summary>The menu screen's 菜单要求 panel.</summary>
    Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame);
}

/// <summary>The real readers (src/EndHud.Vision/Inn), over one OCR engine, the package's inn data and the operator library.</summary>
internal sealed class InnScreens(InnText text, InnPictures pictures, InnData data, OperatorLibrary library) : IInnScreens
{

    private readonly TableCardReader _tables = new(text, pictures);
    private readonly DispatchPanelReader _panel = new(text, pictures, data, library.Operators);
    private readonly BubbleReader _bubble = new(text);
    private readonly SetupReader _setup = new(text, data, library.Operators);
    private readonly MenuReader _menu = new(text, data);
    private readonly MenuRequirementsReader _requirements = new(text, data);

    public Task<InnScreenKind?> DetectAsync(Frame frame) => InnScreen.DetectAsync(text, frame);

    public SessionClock? ReadClock(Frame frame) => InnScreen.ReadClock(frame);

    public Task<IReadOnlyList<TableCard?>> ReadTablesAsync(Frame frame) => _tables.ReadAsync(frame);

    public Task<FireMeter?> ReadFireAsync(Frame frame) => FireMeterReader.ReadAsync(text, frame);

    public Task<DispatchPanel?> ReadPanelAsync(Frame frame) => _panel.ReadAsync(frame);

    public Task<GourmandBubble?> ReadBubbleAsync(Frame frame) => _bubble.ReadAsync(frame);

    public Task<InnSetup?> ReadSetupAsync(Frame frame) => _setup.ReadAsync(frame, InnScreenKind.Setup);

    public Task<int?> ReadPickerKitchenAsync(Frame frame) => InnScreen.ReadPickerKitchenAsync(text, frame);

    public Task<InnMenu?> ReadMenuAsync(Frame frame) => _menu.ReadAsync(frame);

    public Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame) => _requirements.ReadAsync(frame);
}

/// <summary>
/// Without the Simplified Chinese OCR language nothing of the inn can be read but the session's
/// timer (digits by shape): enough to know a session is on screen and say what's missing.
/// </summary>
internal sealed class ClockOnlyScreens : IInnScreens
{
    public Task<InnScreenKind?> DetectAsync(Frame frame) =>
        Task.FromResult(InnScreen.ReadClock(frame) is null ? null : (InnScreenKind?)InnScreenKind.Session);

    public SessionClock? ReadClock(Frame frame) => InnScreen.ReadClock(frame);

    public Task<IReadOnlyList<TableCard?>> ReadTablesAsync(Frame frame) => Task.FromResult<IReadOnlyList<TableCard?>>([]);

    public Task<FireMeter?> ReadFireAsync(Frame frame) => Task.FromResult<FireMeter?>(null);

    public Task<DispatchPanel?> ReadPanelAsync(Frame frame) => Task.FromResult<DispatchPanel?>(null);

    public Task<GourmandBubble?> ReadBubbleAsync(Frame frame) => Task.FromResult<GourmandBubble?>(null);

    public Task<InnSetup?> ReadSetupAsync(Frame frame) => Task.FromResult<InnSetup?>(null);

    public Task<int?> ReadPickerKitchenAsync(Frame frame) => Task.FromResult<int?>(null);

    public Task<InnMenu?> ReadMenuAsync(Frame frame) => Task.FromResult<InnMenu?>(null);

    public Task<MenuRequirements?> ReadMenuRequirementsAsync(Frame frame) => Task.FromResult<MenuRequirements?>(null);
}

/// <summary>
/// Reads one frame of the inn, only what its screen needs, and decides how often to look for the
/// inn at all, so a fight pays nothing for it:
/// <list type="bullet">
/// <item>While the squad panel is on screen (a fight, the open world) the inn isn't looked for:
/// the inn's screens don't show the squad panel.</item>
/// <item>Otherwise it's looked for once every <see cref="LookEvery"/> (one small OCR read of the
/// screen's title), and on every frame once it's been found, and still on every frame for
/// <see cref="PartyMonitor.SessionHold"/> after it was last seen (a frame missed is not leaving).</item>
/// <item>In a session the screen is told by the timer's digits and the dispatch panel's tabs
/// (pixels, no OCR) rather than the titles; a frame with neither is looked at in full again.</item>
/// <item>The setup and menu screens change only when the player clicks or scrolls: read at once when
/// one opens (another than the last seen, a missed frame between not counting), then at most every
/// <see cref="SetupEvery"/>. Of a chef picker only its title is read
/// (which kitchen); the theme list is only told apart (it shows no advice). The gourmand's bubble only
/// while a gourmand is at a table.</item>
/// </list>
/// </summary>
internal sealed class InnFrameReader(IInnScreens screens)
{
    /// <summary>Out of the inn, with no squad panel on screen, how often to look for the inn.</summary>
    public static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the setup and menu screens are read again while they stay open, so the advice follows
    /// a click within about this long (owner, live 2026-10-19: "the page refreshes too slowly", at 2 s).
    /// Measured at 4K (InnSetupCostTests): the setup screen ~300 ms of OCR, the menu ~120 ms, a picker's
    /// title ~1 ms, all on the capture loop's worker, never the UI thread.
    /// </summary>
    public static readonly TimeSpan SetupEvery = TimeSpan.FromSeconds(0.5);

    private InnScreenKind? _last;
    private InnScreenKind? _lastSeen; // the last inn screen found, kept through missed frames
    private TimeSpan? _seenAt;
    private TimeSpan? _lookedAt;
    private TimeSpan? _setupReadAt;

    /// <summary>The last frame read was one of the inn's screens.</summary>
    public bool InInn => _last is not null;

    /// <summary>
    /// Reading resumed after <paramref name="skipped"/> without frames (the game in the background):
    /// the inn screen seen before is held as if no time had passed, so a first frame back that misses
    /// it (the game redrawing as it gets the focus) doesn't send the reader to looking once a second.
    /// </summary>
    public void Resume(TimeSpan skipped)
    {
        if (_seenAt is { } seen) _seenAt = seen + skipped;
    }

    /// <summary>
    /// The frame's inn reading, or null when it isn't the inn (or the inn wasn't looked for on this
    /// frame: see the class). <paramref name="squadPanelSeen"/>: the last squad reading saw the panel.
    /// </summary>
    public async Task<InnReading?> ReadAsync(Frame frame, TimeSpan now, bool squadPanelSeen)
    {
        bool held = !squadPanelSeen && _seenAt is { } seen && now - seen < PartyMonitor.SessionHold;
        if (_last is null && !held)
        {
            if (squadPanelSeen || (_lookedAt is { } at && now - at < LookEvery)) return null;
            _lookedAt = now;
        }

        InnScreenKind? kind = null;
        SessionClock? clock = null;
        DispatchPanel? panel = null;
        if ((_last is InnScreenKind.Session or InnScreenKind.DispatchPanel) && screens.ReadClock(frame) is { } running)
        {
            // Still in the session: the panel's tabs say whether it's open.
            clock = running;
            panel = await screens.ReadPanelAsync(frame);
            kind = panel is null ? InnScreenKind.Session : InnScreenKind.DispatchPanel;
        }
        else
        {
            kind = await screens.DetectAsync(frame);
            if (kind is InnScreenKind.Session or InnScreenKind.DispatchPanel)
            {
                clock = screens.ReadClock(frame);
                if (kind == InnScreenKind.DispatchPanel) panel = await screens.ReadPanelAsync(frame);
            }
        }
        _last = kind;
        if (kind is not { } screen)
        {
            _lookedAt = now;
            return null;
        }
        // A screen just opened is read at once, not after the last screen's wait; the same screen
        // after a missed frame keeps its wait.
        if (kind != _lastSeen) _setupReadAt = null;
        (_lastSeen, _seenAt) = (screen, now);

        switch (screen)
        {
            case InnScreenKind.Session or InnScreenKind.DispatchPanel:
            {
                _setupReadAt = null;
                var tables = await screens.ReadTablesAsync(frame);
                // The meter hides behind the dispatch panel.
                var fire = panel is null ? await screens.ReadFireAsync(frame) : null;
                var bubble = tables.Any(t => t?.State is TableState.GourmandArrived or TableState.GourmandOrdering or TableState.GourmandOrder)
                    ? await screens.ReadBubbleAsync(frame)
                    : null;
                return new InnReading(screen, now, clock, tables, fire, panel, bubble);
            }
            case InnScreenKind.Setup or InnScreenKind.Menu or InnScreenKind.ChefPicker:
            {
                if (_setupReadAt is { } read && now - read < SetupEvery) return new InnReading(screen, now, Fresh: false);
                _setupReadAt = now;
                return screen switch
                {
                    InnScreenKind.Setup => new InnReading(screen, now, Setup: await screens.ReadSetupAsync(frame)),
                    InnScreenKind.Menu => new InnReading(screen, now, Menu: await screens.ReadMenuAsync(frame),
                        Requirements: await screens.ReadMenuRequirementsAsync(frame)),
                    _ => new InnReading(screen, now) { PickerKitchen = await screens.ReadPickerKitchenAsync(frame) },
                };
            }
            default:
                return new InnReading(screen, now);
        }
    }
}

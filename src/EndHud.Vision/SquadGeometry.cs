namespace EndHud.Vision;

/// <summary>
/// Where each slot's parts are in one <see cref="HudLayout"/>, at 4K and anchored bottom-left:
/// each slot's HP bar, and the combo bar, portrait and controlled-operator marker placed from it.
/// </summary>
internal sealed class SquadGeometry
{
    private readonly PixelRect[] _hpBars;
    private readonly PixelRect _combo, _portrait, _marker, _deathMark;

    private SquadGeometry(HudLayout layout, PixelRect[] hpBars, PixelRect combo, PixelRect portrait, PixelRect marker, string[] markerShape, PixelRect deathMark)
    {
        _deathMark = deathMark;
        Layout = layout;
        _hpBars = hpBars;
        _combo = combo;
        _portrait = portrait;
        _marker = marker;
        MarkerShape = markerShape;
    }

    public HudLayout Layout { get; }

    /// <summary>
    /// Level text (out of combat) and battle skill buttons are read in the keyboard-and-mouse
    /// layout only: how the controller layout draws them hasn't been recorded yet.
    /// </summary>
    public bool ReadsLevelsAndSkills => Layout == HudLayout.KeyboardMouse;

    /// <summary>
    /// The slot's ultimate circle; null while LB is held, when the controller draws large rings
    /// instead (not read: the last reading stays, see PartyTracker).
    /// </summary>
    public UltimateCircle? Ultimate(int slot) => Layout switch
    {
        HudLayout.KeyboardMouse => UltimateCircle.Row(slot),
        HudLayout.Controller => UltimateCircle.Diamond(slot),
        _ => null,
    };

    /// <param name="slot">0-based, in the game's slot order.</param>
    public PixelRect HpBar(int slot) => _hpBars[slot];

    public PixelRect ComboBar(int slot) => Place(_combo, slot);
    public PixelRect Portrait(int slot) => Place(_portrait, slot);
    public PixelRect Marker(int slot) => Place(_marker, slot);

    /// <summary>The centre of the fallen operator's mark (⊘ on a hatched square), as a 1×1 rectangle.</summary>
    public PixelRect DeathMark(int slot) => Place(_deathMark, slot);

    /// <summary>
    /// The centre of the ring the controller layout draws in an empty place (a squad of fewer than
    /// four), as a 1x1 rectangle; null for keyboard and mouse, which leaves empty places blank.
    /// </summary>
    public PixelRect? EmptyPlace(int slot) => Layout == HudLayout.KeyboardMouse ? null : Place(new PixelRect(83, -70, 1, 1), slot);

    /// <summary>The controlled-operator marker's white pixels, '#', laid over <see cref="Marker"/>.</summary>
    public string[] MarkerShape { get; }

    /// <summary><paramref name="relative"/> is measured from the slot's HP bar's top-left corner.</summary>
    private PixelRect Place(PixelRect relative, int slot) => relative.Offset(_hpBars[slot].X, _hpBars[slot].Y);

    /// <summary>The layouts with their own squad portrait size (LB held draws the controller's).</summary>
    public static readonly HudLayout[] Layouts = [HudLayout.KeyboardMouse, HudLayout.Controller];

    public static SquadGeometry For(HudLayout layout) => layout switch
    {
        HudLayout.Controller => Controller,
        HudLayout.ControllerSkillsHeld => ControllerSkillsHeld,
        _ => KeyboardMouse,
    };

    /// <summary>Four slots in a row (<see cref="PartyLayout"/>).</summary>
    private static readonly SquadGeometry KeyboardMouse = new(HudLayout.KeyboardMouse,
        [.. Enumerable.Range(0, PartyLayout.SlotCount).Select(i => PartyLayout.ForSlot(PartyLayout.HpBar, i))],
        FromHpBar(PartyLayout.ComboBar), FromHpBar(PartyLayout.Portrait), FromHpBar(ActiveSlot.KeyboardMouseMarkerRect),
        ActiveSlot.KeyboardMouseMarker, FromHpBar(new PixelRect(PartyLayout.DeathMarkCentreX, PartyLayout.DeathMarkCentreY, 1, 1)));

    /// <summary>
    /// A diamond: slot 1 left, 2 top, 3 right, 4 bottom (the D-pad's directions, clockwise from
    /// left; the same order the keyboard-and-mouse row shows, checked in the recording where the
    /// player switches). HP bars are 166×10; the combo bar is the 6 rows 10–5 above the HP bar
    /// (the middle 4 are read). The portrait is drawn at 0.8 of the keyboard-and-mouse size.
    /// </summary>
    private static readonly SquadGeometry Controller = Diamond(HudLayout.Controller, left: 219, top: 1742, right: 542, bottom: 2078);

    /// <summary>LB held: the same diamond, each slot 16 px further out from the middle.</summary>
    private static readonly SquadGeometry ControllerSkillsHeld = Diamond(HudLayout.ControllerSkillsHeld, left: 203, top: 1726, right: 558, bottom: 2094);

    private static SquadGeometry Diamond(HudLayout layout, int left, int top, int right, int bottom) => new(layout,
        [new(left, 1918, 166, 10), new(381, top, 166, 10), new(right, 1918, 166, 10), new(381, bottom, 166, 10)],
        combo: new(0, -9, 166, 4),
        portrait: new(21, -143, 102, 128),
        marker: new(43, -166, 70, 30),
        ActiveSlot.ControllerMarker,
        // The same ⊘ and ring sizes as keyboard and mouse (ring radius 24–30 at 4K), measured on
        // three fallen operators in the second controller recording.
        deathMark: new(83, -77, 1, 1));

    private static PixelRect FromHpBar(PixelRect firstSlot) =>
        firstSlot.Offset(-PartyLayout.HpBar.X, -PartyLayout.HpBar.Y);
}

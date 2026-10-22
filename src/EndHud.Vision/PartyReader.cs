using System.Text;
using System.Text.RegularExpressions;

namespace EndHud.Vision;

/// <param name="Number">Slot number as shown in game, 1–4.</param>
/// <param name="Level">Operator level, or null when it couldn't be read.</param>
/// <param name="Hp">HP bar fill in [0, 1], or null when the bar is off-screen.</param>
/// <param name="LevelText">Raw OCR output, for diagnostics.</param>
/// <param name="Portrait">The operator's head portrait, resampled to the matcher's fixed size.</param>
/// <param name="Combo">Combo cooldown; null outside combat (the bar isn't drawn) or when unreadable.</param>
/// <param name="Ultimate">Ultimate charge from the bottom-right button; null when not on screen.</param>
/// <param name="Dead">The operator has fallen: the death mark (⊘ on a hatched square) replaces the portrait.</param>
/// <param name="Operator">Who it is, once <see cref="Operators.SquadIdentifier"/> has run; null = not recognised.</param>
/// <param name="ScreenPortrait">The portrait at the screen's own resolution, for the character model
/// (<see cref="Operators.PortraitMatcher.Differences"/>); null = use <paramref name="Portrait"/>.</param>
public sealed record PartySlot(int Number, int? Level, double? Hp, string LevelText, Frame Portrait,
    ComboReading? Combo = null, UltimateReading? Ultimate = null, bool Dead = false,
    Operators.OperatorMatch? Operator = null, Frame? ScreenPortrait = null)
{
    /// <summary>
    /// No operator in this place: the squad has fewer than four (see <see cref="PartyState.SquadSize"/>).
    /// Nothing is read or named for it.
    /// </summary>
    public bool Empty { get; init; }

    /// <summary>This slot's battle skill button (bottom right): available or cooling; null when unreadable or fallen.</summary>
    public BattleSkillState? BattleSkill { get; init; }
}

/// <param name="PanelMarks">How many of the eight "-" marks beside the HP bars were found (see <see cref="PanelMarkers"/>).</param>
/// <param name="ReadingsSinceSeen">0 when this reading saw the panel; otherwise how many readings in a row
/// have missed it and this is the last one kept by <see cref="PartyTracker"/>.</param>
public sealed record PartyState(IReadOnlyList<PartySlot> Slots, int PanelMarks = 0, int ReadingsSinceSeen = 0)
{
    /// <summary>The team's skill points (技力), 0–3 with a fraction for the filling segment (see
    /// <see cref="SkillPointBar"/>); null out of combat or when unreadable.</summary>
    public double? SkillPoints { get; init; }

    /// <summary>
    /// The controlled operator's lasting ultimate (Laevatain's, Zhuang Fangyi's): the share of its
    /// time left, 0–1; null when none is running (or it can't be seen). See <see cref="UltimateTimeBar"/>.
    /// </summary>
    public double? UltimateTimeLeft { get; init; }

    /// <summary>How many operators the squad has (1-4): the slots that aren't <see cref="PartySlot.Empty"/>.</summary>
    public int SquadSize => Slots.Count(s => !s.Empty);

    /// <summary>Which HUD the game drew (<see cref="HudLayoutDetector"/>). In the controller layouts
    /// the "-" marks aren't drawn (<see cref="PanelMarks"/> is 0), levels and battle skills
    /// aren't read, and ultimates aren't while LB is held (see SquadGeometry).</summary>
    public HudLayout Layout { get; init; }

    /// <summary>
    /// How wide the squad portraits are on this screen in each layout (keyboard and mouse, then
    /// controller), in pixels: what the naming prepares for before the player switches.
    /// </summary>
    public IReadOnlyList<int> PortraitWidths { get; init; } = [];

    /// <summary>The controlled operator's slot, 1–4 (see <see cref="Vision.ActiveSlot"/>); null when not shown.</summary>
    public int? ActiveSlot { get; init; }

    /// <summary>The controlled operator's own counters (see <see cref="Operators.OperatorCounters"/>),
    /// once the squad is identified; empty when unknown.</summary>
    public IReadOnlyList<Operators.CounterReading> Counters { get; init; } = [];

    /// <summary>Team buffs shown left of the HP bar, by the wiki's names, with their stacks (see
    /// <see cref="TeamBuffReader"/>); empty when none, or out of combat (not read then).</summary>
    public IReadOnlyList<TeamBuff> Buffs { get; init; } = [];

    /// <summary>The boss's statuses recognised under its bars (see <see cref="BossStatusReader"/>);
    /// null when not read this time (it's read less often than the rest).</summary>
    public IReadOnlyList<BossStatusReading>? BossStatuses { get; init; }

    /// <summary>Lasting battle skills' buttons (Si's, see <see cref="SkillRing"/>), read once the squad
    /// is named; empty when none.</summary>
    public IReadOnlyList<SkillRingReading> SkillRings { get; init; } = [];

    /// <summary>
    /// False when the squad panel isn't on screen (menus, map, cutscenes). Mainly the HP-bar
    /// marks, which show in every state; the level text (exploration) and combo bars (combat)
    /// also count. The controller layout has no marks and was seen in combat only: its combo
    /// bars and fallen operators' marks are the sign.
    /// </summary>
    public bool PanelVisible =>
        (PanelMarks >= MinPanelMarks
         || Slots.Any(s => s.Level is not null)
         // An empty combo track is just dark, so also require the HP bar under it; a fallen
         // operator's mark counts too (with a controller, three fallen leave one combo bar).
         || Slots.Count(s => (s.Combo is not null && s.Hp > 0.02) || s.Dead) >= 2
         || SmallSquadSeen)
        && !LooksLikeAnotherScreen;

    /// <summary>
    /// A squad of one or two: too few marks or combo bars for the rules above. Every operator in it
    /// shows an HP bar (or has fallen), the controlled one is among them, and with keyboard and
    /// mouse their marks are there (a dimmed slot can lose one).
    /// </summary>
    private bool SmallSquadSeen =>
        SquadSize is >= 1 and < 3
        && Slots.Where(s => !s.Empty).All(s => s.Dead || s.Hp > EmptyHp)
        && ActiveSlot is { } active && active <= SquadSize
        && (Layout != HudLayout.KeyboardMouse || PanelMarks >= 2 * SquadSize - 1);

    /// <summary>
    /// Every HP bar empty with nobody fallen, and no sign of play (no controlled operator, no
    /// ultimate charging or ready): another screen in the panel's place. The squad select screen
    /// (its tab bar gives 6 of the marks, its strips read as combo bars and an empty skill-point
    /// bar) and ultimate cutscenes look like that; a real squad panel always shows someone's HP, a
    /// fallen operator's mark, or, when a status covers every HP bar (a boss's conductive debuff,
    /// 4.5 s in the recordings), the controlled operator and charging ultimates. Read as the panel,
    /// that screen's floor and legs were named as the squad (four Purrchena in a recording) and kept
    /// the gauges up through the loading screen after it.
    /// </summary>
    private bool LooksLikeAnotherScreen =>
        Slots.Count > 0
        && Slots.Where(s => !s.Empty).All(s => !s.Dead && s.Hp is <= EmptyHp)
        && ActiveSlot is null
        && !Slots.Any(s => s.Ultimate is { } u && (u.Ready || u.Progress > EmptyHp));

    private const double EmptyHp = 0.02;

    /// <summary>
    /// In a battle: the squad panel is on screen with a combo bar and the skill-point bar, which the
    /// game draws in combat only. Not in exploration, menus, login or loading screens.
    /// </summary>
    public bool InCombat => PanelVisible && SkillPoints is not null && Slots.Any(s => s.Combo is not null);

    private const int MinPanelMarks = 6;
}

/// <summary>Reads the squad panel in the bottom-left corner of the game.</summary>
public sealed partial class PartyReader(TextRecognizer ocr)
{
    public async Task<PartyState> ReadAsync(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var squad = SquadGeometry.For(HudLayoutDetector.Detect(frame, ui));
        var slots = new List<PartySlot>(PartyLayout.SlotCount);
        int[] marks = squad.Layout == HudLayout.KeyboardMouse ? PanelMarkers.CountBySlot(frame) : new int[PartyLayout.SlotCount];
        int size = SquadSize(frame, ui, squad, marks);
        for (int i = 0; i < PartyLayout.SlotCount; i++)
        {
            if (i >= size)
            {
                slots.Add(new PartySlot(i + 1, null, null, "", EmptyPortrait) { Empty = true });
                continue;
            }
            double? hp = HpBar.Measure(frame, ui.BottomLeft(squad.HpBar(i)));
            var screenPortrait = frame.Crop(ui.BottomLeft(squad.Portrait(i)));
            var portrait = screenPortrait.Resize(Operators.PortraitMatcher.PortraitWidth, Operators.PortraitMatcher.PortraitHeight);
            // A fallen operator: empty HP bar and the death mark. Their combo and ultimate aren't shown.
            if (hp <= 0.02 && PanelMarkers.IsDeathMark(frame, squad.DeathMark(i)))
            {
                slots.Add(new PartySlot(i + 1, null, 0, "", portrait, Dead: true, ScreenPortrait: screenPortrait));
                continue;
            }

            var combo = ComboBar.Measure(frame, ui.BottomLeft(squad.ComboBar(i)));
            if (!squad.ReadsLevelsAndSkills)
            {
                var ultimate = squad.Ultimate(i) is { } circle ? UltimateGauge.Measure(frame, circle) : null;
                slots.Add(new PartySlot(i + 1, null, hp, "", portrait, combo, ultimate, ScreenPortrait: screenPortrait));
                continue;
            }

            // In combat the combo bar replaces the level text, so there's nothing to OCR.
            var (level, text) = combo is not null ? (null, "") : await ReadLevelAsync(frame, ui, i);
            // The ultimate circles and battle skill buttons of a smaller squad are drawn from the right.
            int button = PartyLayout.SlotCount - size + i;
            slots.Add(new PartySlot(i + 1, level, hp, text, portrait, combo, UltimateGauge.Measure(frame, button),
                ScreenPortrait: screenPortrait) { BattleSkill = BattleSkillButton.Measure(frame, button) });
        }
        // Skill points are drawn in combat only (shown by the combo bars); out of combat the bar's
        // place shows the scenery, which can look like an empty bar. Same place in every layout.
        bool inCombat = slots.Any(s => s.Combo is not null);
        return new PartyState(slots, marks.Sum())
        {
            Layout = squad.Layout,
            PortraitWidths = [.. SquadGeometry.Layouts.Select(layout => ui.BottomLeft(SquadGeometry.For(layout).Portrait(0)).Width)],
            ActiveSlot = Vision.ActiveSlot.Find(frame, ui, squad),
            SkillPoints = inCombat ? SkillPointBar.Measure(frame, ui) : null,
            UltimateTimeLeft = inCombat ? UltimateTimeBar.Measure(frame, ui) : null,
        };
    }

    private static readonly Frame EmptyPortrait = new(Operators.PortraitMatcher.PortraitWidth, Operators.PortraitMatcher.PortraitHeight,
        new byte[Operators.PortraitMatcher.PortraitWidth * Operators.PortraitMatcher.PortraitHeight * 4]);

    /// <summary>
    /// How many operators the squad has: places from the end that are empty. Keyboard and mouse
    /// draw nothing there: no marks, no HP bar, no fallen mark (marks alone aren't enough, as
    /// effects and fading can hide a slot's marks in a full squad; its HP bar still shows).
    /// Controller: the empty ring.
    /// </summary>
    private static int SquadSize(Frame frame, UiScaler ui, SquadGeometry squad, int[] marks)
    {
        int size = PartyLayout.SlotCount;
        while (size > 1 && IsEmptyPlace(size - 1)) size--;
        // With keyboard and mouse an empty place is the absence of everything, which any screen
        // without the squad panel has too: smaller only if slot 1 shows an operator.
        if (size < PartyLayout.SlotCount && squad.Layout == HudLayout.KeyboardMouse && ShowsNothing(0)) return PartyLayout.SlotCount;
        return size;

        bool IsEmptyPlace(int slot) => squad.EmptyPlace(slot) is { } place ? PanelMarkers.IsEmptyPlace(frame, place) : ShowsNothing(slot);

        bool ShowsNothing(int slot) =>
            marks[slot] == 0
            && HpBar.Measure(frame, ui.BottomLeft(squad.HpBar(slot))) is not > 0.02
            && !PanelMarkers.IsDeathMark(frame, squad.DeathMark(slot));
    }

    /// <summary>
    /// Below this HUD scale (smaller than 1536x864, so 1366x768 and 1280x720) the level text is a
    /// handful of pixels and OCR read 0-1 of 15 levels on the simulated screens, so it isn't
    /// attempted: the time is better saved. Bars, circles and death marks are still measured.
    /// </summary>
    public const double MinTextScale = 0.4;

    /// <summary>The game's level cap. A higher reading is a misread ("96" for "90").</summary>
    public const int MaxLevel = 90;

    /// <summary>
    /// OCR the level under slot <paramref name="slot"/>'s portrait. Only two-digit levels up to
    /// <see cref="MaxLevel"/> are accepted: a one-digit result is usually a two-digit level with
    /// a digit lost ("9" for "LV.90"; seen at 4K and on simulated laptop screens), so levels 1-9
    /// are left unknown rather than risk showing 9 for 90. Two attempts must agree: a level
    /// sometimes reads as another two-digit number ("56" for "90" on a 2256x1504 screen).
    /// </summary>
    private Task<(int? Level, string Text)> ReadLevelAsync(Frame frame, UiScaler ui, int slot)
    {
        var region = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelText, slot));
        var glyphRows = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelGlyphRows, slot));
        return ReadAgreedLevelAsync(frame, ui, region, glyphRows);
    }

    /// <summary>The first acceptable level two OCR attempts agree on, or null.</summary>
    private async Task<(int? Level, string Text)> ReadAgreedLevelAsync(Frame frame, UiScaler ui, PixelRect region, PixelRect glyphRows)
    {
        if (ui.Scale < MinTextScale) return (null, "");
        var seen = new List<int>();
        string last = "";
        await foreach (string text in ocr.ReadAttemptsAsync(frame, region, glyphRows, ui.Scale))
        {
            last = text;
            if (ParseLevel(text) is not { } level || level is < 10 or > MaxLevel) continue;
            if (seen.Contains(level)) return (level, text);
            seen.Add(level);
        }
        return (null, last);
    }

    /// <summary>
    /// "LV.90", "Lv. 9O", "LV90", "1 90" → 90. The level is the last number: whatever comes before
    /// it is the small "LV." prefix, which OCR sometimes misreads as a digit.
    /// </summary>
    internal static int? ParseLevel(string text)
    {
        int v = text.LastIndexOfAny(['V', 'v']);
        var matches = Number().Matches(FixDigits(v >= 0 ? text[(v + 1)..] : text));
        if (matches.Count == 0) return null;
        // Levels have at most two digits; a third is background right after the text read as "1".
        string digits = matches[^1].Value;
        if (digits.Length > 2) digits = digits[..2];
        return int.TryParse(digits, out int level) && level >= 1 ? level : null;
    }

    // Letters OCR commonly confuses with digits in this font.
    private static string FixDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                'O' or 'o' or 'D' or 'Q' => '0',
                'I' or 'l' or '|' or 'i' or '!' => '1',
                'Z' or 'z' => '2',
                'S' or 's' => '5',
                'B' => '8',
                _ => c,
            });
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\d{1,4}")]
    private static partial Regex Number();
}

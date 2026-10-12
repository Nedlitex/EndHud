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
public sealed record PartySlot(int Number, int? Level, double? Hp, string LevelText, Frame Portrait,
    ComboReading? Combo = null, UltimateReading? Ultimate = null, bool Dead = false,
    Operators.OperatorMatch? Operator = null);

/// <param name="PanelMarks">How many of the eight "-" marks beside the HP bars were found (see <see cref="PanelMarkers"/>).</param>
/// <param name="ReadingsSinceSeen">0 when this reading saw the panel; otherwise how many readings in a row
/// have missed it and this is the last one kept by <see cref="PartyTracker"/>.</param>
public sealed record PartyState(IReadOnlyList<PartySlot> Slots, int PanelMarks = 0, int ReadingsSinceSeen = 0)
{
    /// <summary>
    /// False when the squad panel isn't on screen (menus, map, cutscenes). Mainly the HP-bar
    /// marks, which show in every state; the level text (exploration) and combo bars (combat)
    /// also count.
    /// </summary>
    public bool PanelVisible =>
        PanelMarks >= MinPanelMarks
        || Slots.Any(s => s.Level is not null)
        // An empty combo track is just dark, so also require the HP bar under it.
        || Slots.Count(s => s.Combo is not null && s.Hp > 0.02) >= 2;

    private const int MinPanelMarks = 6;
}

/// <summary>Reads the squad panel in the bottom-left corner of the game.</summary>
public sealed partial class PartyReader(TextRecognizer ocr)
{
    public async Task<PartyState> ReadAsync(Frame frame)
    {
        var ui = new UiScaler(frame.Width, frame.Height);
        var slots = new List<PartySlot>(PartyLayout.SlotCount);
        for (int i = 0; i < PartyLayout.SlotCount; i++)
        {
            double? hp = HpBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.HpBar, i)));
            var portrait = frame.Crop(ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.Portrait, i)))
                .Resize(Operators.PortraitMatcher.PortraitWidth, Operators.PortraitMatcher.PortraitHeight);

            // A fallen operator: empty HP bar and the death mark. Their combo and ultimate aren't shown.
            if (hp <= 0.02 && PanelMarkers.IsDeathMark(frame, i))
            {
                slots.Add(new PartySlot(i + 1, null, 0, "", portrait, Dead: true));
                continue;
            }

            var combo = ComboBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.ComboBar, i)));
            // In combat the combo bar replaces the level text, so there's nothing to OCR.
            var (level, text) = combo is not null ? (null, "") : await ReadLevelAsync(frame, ui, i);
            slots.Add(new PartySlot(i + 1, level, hp, text, portrait, combo, UltimateGauge.Measure(frame, i)));
        }
        return new PartyState(slots, PanelMarkers.Count(frame));
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

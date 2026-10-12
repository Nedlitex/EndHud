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
public sealed record PartyState(IReadOnlyList<PartySlot> Slots, int? PingMs, string PingText, int PanelMarks = 0,
    int ReadingsSinceSeen = 0)
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

        // The ping is white normally and red when the connection is poor.
        var pingRegion = ui.BottomLeft(PartyLayout.Ping);
        var (ping, pingText) = await ReadNumberAsync(frame, ui, pingRegion, null, TextColour.White, ParsePing, _ => true,
            checkDigits: true, needAgreement: false);
        // Red text needs two attempts to agree instead: the pink "5" loses its corner and reads as 3
        // or 6 on some frames, even at 4K (the red "515ms" in the battle recording read as 316 or
        // 616), which keeps the number of digits; and the digit check miscounts red text (the bar
        // drawn left of it counts as a glyph), so it would only reject correct readings.
        if (ping is null) (ping, pingText) = await ReadNumberAsync(frame, ui, pingRegion, null, TextColour.Red, ParsePing, _ => true,
            checkDigits: false, needAgreement: true);
        return new PartyState(slots, ping, pingText, PanelMarkers.Count(frame));
    }

    /// <summary>
    /// Below this HUD scale (smaller than about 1650x930, so 1600x900, 1366x768 and 1280x720)
    /// glyphs are a handful of pixels: at 1600x900 a quarter of levels and pings were readable
    /// and digits were still dropped ("16" for "165ms"), so levels and ping are not read at all.
    /// Bars, circles and death marks are still measured.
    /// </summary>
    public const double MinTextScale = 0.43;

    /// <summary>The game's level cap. A higher reading is a misread ("96" for "90").</summary>
    public const int MaxLevel = 90;

    /// <summary>
    /// OCR the level under slot <paramref name="slot"/>'s portrait. Only two-digit levels up to
    /// <see cref="MaxLevel"/> are accepted: a one-digit result is usually a two-digit level with
    /// a digit lost ("9" for "LV.90"; seen at 4K and on simulated laptop screens), so levels 1-9
    /// are left unknown rather than risk showing 9 for 90. Two attempts must agree: a level
    /// sometimes reads as another two-digit number ("56" for "90" on a 2256x1504 screen), and
    /// the digit check can't be used, because the portrait behind the text adds glyph-sized blobs.
    /// </summary>
    private Task<(int? Level, string Text)> ReadLevelAsync(Frame frame, UiScaler ui, int slot)
    {
        var region = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelText, slot));
        var glyphRows = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelGlyphRows, slot));
        return ReadNumberAsync(frame, ui, region, glyphRows, TextColour.White, ParseLevel, level => level is >= 10 and <= MaxLevel,
            checkDigits: false, needAgreement: true);
    }

    /// <summary>
    /// The first number <paramref name="parse"/> finds across the OCR attempts that
    /// <paramref name="wanted"/> accepts, or with <paramref name="needAgreement"/>, the first one
    /// two attempts agree on. Null when there is none. With <paramref name="checkDigits"/>, an
    /// attempt only counts if the number has as many digits as the region has full-height glyphs:
    /// OCR sometimes drops or adds a digit the same way on every attempt ("16" or "1660" for
    /// "166ms", seen on 1600x900, 3000x2000 and 5K screens), which agreement can't catch.
    /// </summary>
    private async Task<(int? Value, string Text)> ReadNumberAsync(Frame frame, UiScaler ui, PixelRect region,
        PixelRect? glyphRows, TextColour colour, Func<string, int?> parse, Func<int, bool> wanted, bool checkDigits, bool needAgreement)
    {
        if (ui.Scale < MinTextScale) return (null, "");
        var seen = new List<int>();
        string last = "";
        await foreach (var (text, tallGlyphs) in ocr.ReadAttemptsAsync(frame, region, glyphRows, colour, ui.Scale))
        {
            last = text;
            if (parse(text) is not { } value || !wanted(value)) continue;
            if (checkDigits && value.ToString().Length != tallGlyphs) continue;
            if (!needAgreement || seen.Contains(value)) return (value, text);
            seen.Add(value);
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

    /// <summary>"164ms", "164 ms" → 164.</summary>
    internal static int? ParsePing(string text)
    {
        int m = text.IndexOf('m', StringComparison.OrdinalIgnoreCase);
        var match = Number().Match(FixDigits(m >= 0 ? text[..m] : text));
        // Real pings to the game's servers are tens to hundreds of ms; a single digit is OCR
        // having dropped some ("5ms" for "165ms"), and unknown beats a believable wrong number.
        return match.Success && int.TryParse(match.Value, out int ms) && ms is >= 10 and < 10000 ? ms : null;
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

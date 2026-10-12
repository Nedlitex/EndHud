using System.Text;
using System.Text.RegularExpressions;

namespace EndHud.Vision;

/// <param name="Number">Slot number as shown in game, 1–4.</param>
/// <param name="Level">Operator level, or null when it couldn't be read.</param>
/// <param name="Hp">HP bar fill in [0, 1], or null when the bar is off-screen.</param>
/// <param name="LevelText">Raw OCR output, for diagnostics.</param>
/// <param name="Portrait">The operator's head portrait, resampled to the matcher's fixed size.</param>
/// <param name="Combo">连携 cooldown; null outside combat (the bar isn't drawn) or when unreadable.</param>
/// <param name="Ultimate">终结技 charge from the bottom-right button; null when not on screen.</param>
/// <param name="Dead">The operator has fallen: the death mark (⊘ on a hatched square) replaces the portrait.</param>
/// <param name="Operator">Who it is, once <see cref="Operators.SquadIdentifier"/> has run; null = not recognised.</param>
public sealed record PartySlot(int Number, int? Level, double? Hp, string LevelText, Frame Portrait,
    ComboReading? Combo = null, UltimateReading? Ultimate = null, bool Dead = false,
    Operators.OperatorMatch? Operator = null);

/// <param name="PanelMarks">How many of the eight "-" marks beside the HP bars were found (see <see cref="PanelMarkers"/>).</param>
public sealed record PartyState(IReadOnlyList<PartySlot> Slots, int? PingMs, string PingText, int PanelMarks = 0)
{
    /// <summary>
    /// False when the squad panel isn't on screen (menus, map, cutscenes). Mainly the HP-bar
    /// marks, which show in every state; the level text (exploration) and 连携 bars (combat)
    /// also count.
    /// </summary>
    public bool PanelVisible =>
        PanelMarks >= MinPanelMarks
        || Slots.Any(s => s.Level is not null)
        // An empty 连携 track is just dark, so also require the HP bar under it.
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

            // A fallen operator: empty HP bar and the death mark. Their 连携/终结技 aren't shown.
            if (hp <= 0.02 && PanelMarkers.IsDeathMark(frame, i))
            {
                slots.Add(new PartySlot(i + 1, null, 0, "", portrait, Dead: true));
                continue;
            }

            var combo = ComboBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.ComboBar, i)));
            // In combat the 连携 bar replaces the level text, so there's nothing to OCR.
            string text = combo is not null ? "" : await ReadLevelTextAsync(frame, ui, i);
            slots.Add(new PartySlot(i + 1, ParseLevel(text), hp, text, portrait, combo, UltimateGauge.Measure(frame, i)));
        }

        // The ping is white normally and red when the connection is poor.
        var pingRegion = ui.BottomLeft(PartyLayout.Ping);
        string pingText = await ocr.ReadAsync(frame, pingRegion, t => ParsePing(t) is not null);
        if (ParsePing(pingText) is null)
            pingText = await ocr.ReadAsync(frame, pingRegion, t => ParsePing(t) is not null, colour: TextColour.Red);
        return new PartyState(slots, ParsePing(pingText), pingText, PanelMarkers.Count(frame));
    }

    /// <summary>
    /// OCR the level under slot <paramref name="slot"/>'s portrait. A one-digit result is
    /// often a two-digit level with a digit lost ("LV9" for "LV.90"), so first look for a
    /// two-digit reading across all attempts, and only then settle for one digit — operators
    /// below level 10 do exist.
    /// </summary>
    private async Task<string> ReadLevelTextAsync(Frame frame, UiScaler ui, int slot)
    {
        var region = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelText, slot));
        var glyphRows = ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelGlyphRows, slot));
        string text = await ocr.ReadAsync(frame, region, t => ParseLevel(t) >= 10, glyphRows);
        return ParseLevel(text) >= 10 ? text : await ocr.ReadAsync(frame, region, t => ParseLevel(t) is not null, glyphRows);
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

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
/// <param name="Operator">Who it is, once <see cref="Operators.SquadIdentifier"/> has run; null = not recognised.</param>
public sealed record PartySlot(int Number, int? Level, double? Hp, string LevelText, Frame Portrait,
    ComboReading? Combo = null, UltimateReading? Ultimate = null, Operators.OperatorMatch? Operator = null);

public sealed record PartyState(IReadOnlyList<PartySlot> Slots, int? PingMs, string PingText)
{
    /// <summary>
    /// False when the squad panel isn't on screen (menus, map, cutscenes). Out of combat it shows
    /// level text; in combat the 连携 bars replace the text.
    /// </summary>
    public bool PanelVisible =>
        Slots.Any(s => s.Level is not null)
        // An empty 连携 track is just black, so also require the HP bar under it.
        || Slots.Count(s => s.Combo is not null && s.Hp > 0.02) >= 2;
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
            var combo = ComboBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.ComboBar, i)));
            // In combat the 连携 bar replaces the level text, so there's nothing to OCR.
            string text = combo is not null
                ? ""
                : await ocr.ReadAsync(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelText, i)),
                    t => ParseLevel(t) is not null,
                    ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.LevelGlyphRows, i)));
            double? hp = HpBar.Measure(frame, ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.HpBar, i)));
            var portrait = frame.Crop(ui.BottomLeft(PartyLayout.ForSlot(PartyLayout.Portrait, i)))
                .Resize(Operators.PortraitMatcher.PortraitWidth, Operators.PortraitMatcher.PortraitHeight);
            slots.Add(new PartySlot(i + 1, ParseLevel(text), hp, text, portrait, combo, UltimateGauge.Measure(frame, i)));
        }

        string pingText = await ocr.ReadAsync(frame, ui.BottomLeft(PartyLayout.Ping), t => ParsePing(t) is not null);
        return new PartyState(slots, ParsePing(pingText), pingText);
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
        return match.Success && int.TryParse(match.Value, out int ms) && ms < 10000 ? ms : null;
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

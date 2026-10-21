using System.Text.RegularExpressions;

namespace EndHud.Vision.Inn;

/// <summary>
/// Which inn screen is showing, from text the game always draws in fixed places (two small OCR
/// reads at most): the setup screens' and the menu's titles at the top left ("//营业设定", the
/// theme list's "//主题设定", "// 菜单设定"), a kitchen's chef picker ("厨房一"), and during the session the
/// timer ("04:46") in its gold pill. The pill sits under the "结束经营" button, or in the button's
/// place when the dispatch panel is open; the panel is told by its "干员加成" heading.
/// </summary>
public static partial class InnScreen
{
    // At 4K. The title (and, with the dispatch panel open, the timer) at the top left.
    internal static readonly PixelRect Title = new(90, 45, 330, 80);
    // The timer's digits in its pill under the "结束经营" button (a little higher while the session
    // fades in), or at the top when a panel covers the button. Right of the clock icon.
    internal static readonly PixelRect TimerBelowButton = new(235, 205, 175, 85);
    internal static readonly PixelRect TimerAtTop = new(235, 45, 175, 60);
    // The dispatch panel's "干员加成" heading, over the chefs' bonuses.
    internal static readonly PixelRect DispatchHeading = new(600, 1110, 300, 80);

    /// <summary>The screen showing, or null when it isn't one of the inn's (or it can't be told).</summary>
    public static async Task<InnScreenKind?> DetectAsync(InnText text, Frame frame)
    {
        var layout = new InnLayout(frame);
        string title = await text.ReadAsync(frame, layout.TopLeft(Title), TimerInk, layout.Scale) ?? "";
        // OCR may miss a character of a title ("//菜设定", "//营业设正"): two of them are enough.
        if (title.Contains("营业")) return InnScreenKind.Setup;
        if (title.Contains("主题")) return InnScreenKind.ThemeList;
        if (title.Contains("菜单") || title.Contains("菜设") || title.Contains("单设")) return InnScreenKind.Menu;
        if (KitchenTitle().IsMatch(title)) return InnScreenKind.ChefPicker;
        if (ReadTime(frame, layout, TimerAtTop) is not null)
        {
            // The timer moved to the top: something covers the button. The dispatch panel, or another panel.
            string heading = await text.ReadAsync(frame, layout.Centered(DispatchHeading), InnText.White, layout.Scale) ?? "";
            return heading.Contains("干员") || heading.Contains("加成") ? InnScreenKind.DispatchPanel : InnScreenKind.Session;
        }
        return ReadTime(frame, layout, TimerBelowButton) is not null ? InnScreenKind.Session : null;
    }

    /// <summary>The session's time left, or null when no timer is read (not in a session, or covered).</summary>
    public static SessionClock? ReadClock(Frame frame)
    {
        var layout = new InnLayout(frame);
        foreach (var region in new[] { TimerBelowButton, TimerAtTop })
        {
            if (ReadTime(frame, layout, region) is { } left)
                return new SessionClock(left, left == TimeSpan.Zero);
        }
        return null;
    }

    private static TimeSpan? ReadTime(Frame frame, InnLayout layout, PixelRect region)
    {
        var glyphs = InnDigits.Glyphs(frame, layout.TopLeft(region), TimerInk, layout.Scale);
        var chars = glyphs.Select(InnDigits.Classify).ToList();
        if (chars.Any(c => c is null)) return null;
        string text = new([.. chars.Select(c => c!.Value)]);
        // The colon's dots are a couple of pixels at 1080p and can vanish: four digits with the
        // colon's gap in the middle are the time too.
        if (glyphs.Count == 4 && !text.Contains(':'))
        {
            int Gap(int i) => glyphs[i + 1].Left - glyphs[i].Right;
            if (Gap(1) >= 2 * Math.Max(Gap(0), Gap(2))) text = text[..2] + ":" + text[2..];
        }
        return ParseTime(text);
    }

    /// <summary>"04:46" as a time; null for anything else.</summary>
    internal static TimeSpan? ParseTime(string text)
    {
        var m = Time().Match(text);
        if (!m.Success) return null;
        int minutes = int.Parse(m.Groups[1].Value), seconds = int.Parse(m.Groups[2].Value);
        // A session lasts 5 minutes.
        return minutes <= 5 && seconds < 60 ? new TimeSpan(0, minutes, seconds) : null;
    }

    /// <summary>
    /// The timer's digits and the titles: white (light grey while the HUD fades in), or red when time
    /// is nearly up. Not the pill's gold fill, which is saturated and not red enough.
    /// </summary>
    internal static bool TimerInk(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (min >= 150 && max - min <= 60) return true;
        return r >= 200 && g <= 160 && b <= 170 && r - g >= 70;
    }

    [GeneratedRegex(@"^(\d{2}):(\d{2})$")]
    private static partial Regex Time();

    [GeneratedRegex(@"^\W*厨房([一二三])")]
    private static partial Regex KitchenTitle();
}

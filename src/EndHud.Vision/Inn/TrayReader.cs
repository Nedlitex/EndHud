namespace EndHud.Vision.Inn;

/// <summary>A dish (or drink) the player holds in the tray at the bottom of the session.</summary>
/// <param name="Id">The dish's or drink's id; null when its picture couldn't be told apart (never guessed).</param>
/// <param name="Choice">The slot carries the 珍馐 badge (a yellow circle with a cloche at its top left): a choice dish.</param>
public sealed record HeldDish(string? Id, bool Choice);

/// <summary>
/// The tray of what the player holds (手持), five slots at the bottom middle of the session, left of the 派单 button. A
/// dish picked up off the counter shows in a slot as its picture, with the 珍馐 badge when it came out a choice dish
/// (owner's live session 2026-10-21, test_yuanyuan5: 至臻鳞肉卷 and "勺里游" in hand at 258 s while the advice still sent
/// a 至臻鳞肉卷 for table 4 to a kitchen). Hidden while the dispatch panel is open.
/// <list type="bullet">
/// <item>The slots are see-through panels over the scene, so their background is never one colour: a picture is
/// compared on its solid pixels only (<see cref="InnPictures.Solids"/>), drawn at the size and place the tray draws it,
/// a few pixels either way, leaving out the slot's top corners (the badge at the left, an icon at the right).</item>
/// <item>A slot is empty when its middle is as flat as the panel; the tray is on screen when every slot's sides are the
/// panel's beige (or a selected slot's white).</item>
/// </list>
/// Measured on 4K frames of the owner's recording (InnTrayReaderTests), at 1440p and 1080p by scaling.
/// </summary>
public sealed class TrayReader(InnPictures pictures)
{
    public const int Slots = 5;

    // At 4K, on the centred 16:9 area: each slot's middle (the third at the screen's middle), its size, and the art's
    // size and place (its centre a little right of and below the slot's).
    internal const double MiddleSlotX = 1919.5, SlotStep = 188.75, SlotY = 1933, SlotSize = 176;
    internal const double ArtSize = 158, ArtDx = 1.5, ArtDy = 2;
    // The slot's top corners left out of the comparison: the 珍馐 badge at the left, an icon at the right.
    private const double CornerWidth = 64, CornerHeight = 62;
    // The badge: a yellow circle at the slot's top left.
    private static readonly (double X, double Y, double Size) Badge = (10, 7, 52);
    // Measured: a badge 0.32 yellow and 0.13 black at every size; a slot without one 0 and 0.
    private const double BadgeYellowShare = 0.15, BadgeDarkShare = 0.06;
    // A picture is named when its colour is this close (mean difference per channel over its solid pixels) and the next
    // picture this much further. Measured: see InnTrayReaderTests.
    internal const double MaxDifference = 24, MinLead = 5;
    // An empty slot's middle is flat: its colours spread less than this (mean distance from the mean, largest channel).
    private const double EmptySpread = 9;

    /// <summary>
    /// What each slot holds, left to right (null: empty), or null when the tray isn't on screen (the dispatch panel over
    /// it, the HUD faded).
    /// </summary>
    public IReadOnlyList<HeldDish?>? Read(Frame frame)
    {
        var layout = new InnLayout(frame);
        if (!DispatchButtonShows(frame, layout)) return null;
        var slots = new HeldDish?[Slots];
        for (int k = 0; k < Slots; k++)
        {
            if (!IsPanel(frame, layout, k)) return null;
            if (IsEmpty(frame, layout, k)) continue;
            slots[k] = new HeldDish(Identify(frame, layout, k), HasBadge(frame, layout, k));
        }
        return slots;
    }

    /// <summary>
    /// The 派单 button right of the tray shows: its black bar along the bottom (under the word) with the light picture
    /// right above it. The slots alone are see-through panels, and an empty tray reads like a plain beige floor (the dispatch
    /// panel of test_yuanyuan1 leaves the floor showing where the tray goes).
    /// </summary>
    private static bool DispatchButtonShows(Frame frame, InnLayout layout)
    {
        double Share(PixelRect r, Func<byte, byte, byte, bool> like)
        {
            var area = layout.Centered(r).ClampTo(frame.Width, frame.Height);
            if (area.IsEmpty) return 0;
            int n = 0, yes = 0;
            int step = Math.Max(1, (int)Math.Round(2 * layout.Scale));
            for (int y = area.Y; y < area.Bottom; y += step)
            for (int x = area.X; x < area.Right; x += step)
            {
                n++;
                var (cr, cg, cb) = frame.GetPixel(x, y);
                if (like(cr, cg, cb)) yes++;
            }
            return yes / (double)n;
        }
        return Share(ButtonBar, (r, g, b) => Math.Max(r, Math.Max(g, b)) <= 45) >= 0.8
               && Share(ButtonPicture, (r, g, b) => Math.Min(r, Math.Min(g, b)) >= 170 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 30) >= 0.6;
    }

    // At 4K, on the centred 16:9 area: the 派单 button's black bar (left of its word) and a strip of its light picture above.
    private static readonly PixelRect ButtonBar = new(2440, 1984, 240, 28), ButtonPicture = new(2440, 1945, 120, 18);

    /// <summary>A slot's rectangle in window pixels.</summary>
    internal static PixelRect SlotArea(InnLayout layout, int slot) => layout.Centered(new PixelRect(
        (int)Math.Round(MiddleSlotX + SlotStep * (slot - 2) - SlotSize / 2), (int)Math.Round(SlotY - SlotSize / 2), (int)SlotSize, (int)SlotSize));

    /// <summary>The slot's left and right sides, inside its edge, are the panel's beige or a selected slot's white.</summary>
    private static bool IsPanel(Frame frame, InnLayout layout, int slot)
    {
        var area = SlotArea(layout, slot).ClampTo(frame.Width, frame.Height);
        if (area.Width < 8 || area.Height < 8) return false;
        int inset = Math.Max(2, (int)Math.Round(6 * layout.Scale));
        int like = 0, all = 0;
        foreach (int x in new[] { area.X + inset, area.Right - 1 - inset })
        for (int y = area.Y + area.Height / 3; y < area.Y + area.Height * 5 / 6; y += Math.Max(1, (int)Math.Round(3 * layout.Scale)))
        {
            all++;
            var (r, g, b) = frame.GetPixel(x, y);
            if (Beige(r, g, b)) like++;
        }
        return all > 0 && like >= all * 0.8;
    }

    /// <summary>The tray's panel: a warm light grey (the scene shows through it), or white on the selected slot.</summary>
    internal static bool Beige(byte r, byte g, byte b) =>
        (r >= 130 && r >= g && g >= b && r - b is >= 8 and <= 70) || (Math.Min(r, Math.Min(g, b)) >= 215 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 20);

    private static bool IsEmpty(Frame frame, InnLayout layout, int slot)
    {
        var area = SlotArea(layout, slot);
        int margin = area.Width / 4;
        var middle = new PixelRect(area.X + margin, area.Y + margin, area.Width - 2 * margin, area.Height - 2 * margin).ClampTo(frame.Width, frame.Height);
        return Spread(frame, middle) < EmptySpread;
    }

    private static double Spread(Frame frame, PixelRect r)
    {
        if (r.IsEmpty) return 0;
        double sr = 0, sg = 0, sb = 0;
        int n = r.Width * r.Height;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            sr += cr; sg += cg; sb += cb;
        }
        sr /= n; sg /= n; sb /= n;
        double dr = 0, dg = 0, db = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        for (int x = r.X; x < r.Right; x++)
        {
            var (cr, cg, cb) = frame.GetPixel(x, y);
            dr += Math.Abs(cr - sr); dg += Math.Abs(cg - sg); db += Math.Abs(cb - sb);
        }
        return Math.Max(dr, Math.Max(dg, db)) / n;
    }

    private static bool HasBadge(Frame frame, InnLayout layout, int slot)
    {
        var (yellow, _, dark) = BadgeShares(frame, layout, slot);
        return yellow >= BadgeYellowShare && dark >= BadgeDarkShare;
    }

    /// <summary>
    /// Of the badge's place (the slot's top left): the share of its yellow (the circle's top), its near-white (the circle's
    /// bottom) and its black (the cloche drawn on it).
    /// </summary>
    internal static (double Yellow, double Light, double Dark) BadgeShares(Frame frame, InnLayout layout, int slot)
    {
        var area = SlotArea(layout, slot);
        double s = layout.Scale;
        var box = new PixelRect(area.X + (int)Math.Round(Badge.X * s), area.Y + (int)Math.Round(Badge.Y * s),
            Math.Max(1, (int)Math.Round(Badge.Size * s)), Math.Max(1, (int)Math.Round(Badge.Size * s))).ClampTo(frame.Width, frame.Height);
        if (box.IsEmpty) return (0, 0, 0);
        int yellow = 0, light = 0, dark = 0;
        for (int y = box.Y; y < box.Bottom; y++)
        for (int x = box.X; x < box.Right; x++)
        {
            var (r, g, b) = frame.GetPixel(x, y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (r >= 190 && g >= 190 && b <= 150 && Math.Min(r, g) - b >= 70) yellow++;
            else if (min >= 225 && max - min <= 35) light++;
            else if (max <= 60) dark++;
        }
        double n = box.Width * box.Height;
        return (yellow / n, light / n, dark / n);
    }

    /// <summary>The dish or drink in the slot, or null when no picture is clearly it.</summary>
    internal string? Identify(Frame frame, InnLayout layout, int slot)
    {
        var ranked = Rank(frame, layout, slot);
        if (ranked.Count == 0) return null;
        var (id, best) = ranked[0];
        double next = ranked.Count > 1 ? ranked[1].Difference : double.MaxValue;
        return best <= MaxDifference && next - best >= MinLead ? id : null;
    }

    /// <summary>Every picture's colour difference from the slot over its solid pixels (mean per channel, 0–255), closest first.</summary>
    internal List<(string Id, double Difference)> Rank(Frame frame, InnLayout layout, int slot)
    {
        double s = layout.Scale;
        var area = SlotArea(layout, slot);
        int size = Math.Max(8, (int)Math.Round(ArtSize * s));
        double slotLeft = area.X, slotTop = area.Y;
        int artX = (int)Math.Round(slotLeft + area.Width / 2.0 + ArtDx * s - size / 2.0), artY = (int)Math.Round(slotTop + area.Height / 2.0 + ArtDy * s - size / 2.0);
        double cornerW = CornerWidth * s, cornerH = CornerHeight * s;
        // Every other pixel at 4K (plenty), every one below.
        int step = s >= 0.9 ? 2 : 1;
        var solids = pictures.Solids(size);

        double Difference(InnPictures.Solid p, int shiftX, int shiftY)
        {
            long sum = 0;
            int n = 0;
            for (int y = 0; y < size; y += step)
            for (int x = 0; x < size; x += step)
            {
                int i = y * size + x;
                if (!p.Opaque[i]) continue;
                int fx = artX + shiftX + x, fy = artY + shiftY + y;
                double inSlotX = fx - slotLeft, inSlotY = fy - slotTop;
                if (inSlotY < cornerH && (inSlotX < cornerW || inSlotX > area.Width - cornerW)) continue;
                if (fx < 0 || fy < 0 || fx >= frame.Width || fy >= frame.Height) continue;
                var (r, g, b) = frame.GetPixel(fx, fy);
                sum += Math.Abs(r - p.Rgb[i * 3]) + Math.Abs(g - p.Rgb[i * 3 + 1]) + Math.Abs(b - p.Rgb[i * 3 + 2]);
                n++;
            }
            // A picture with almost nothing solid left to compare says nothing.
            return n < size * size / (4 * step * step) ? double.MaxValue : sum / (3.0 * n);
        }

        int reach = Math.Max(1, (int)Math.Round(3 * s));
        var first = solids.Select(p => (Picture: p, Difference: Difference(p, 0, 0))).OrderBy(p => p.Difference).ToList();
        var refined = first.Take(4).Select(p => (p.Picture.Id, Difference: (
            from sx in Enumerable.Range(-reach, 2 * reach + 1)
            from sy in Enumerable.Range(-reach, 2 * reach + 1)
            select Difference(p.Picture, sx, sy)).Min()));
        return [.. refined.Concat(first.Skip(4).Select(p => (p.Picture.Id, p.Difference))).OrderBy(p => p.Difference)];
    }
}

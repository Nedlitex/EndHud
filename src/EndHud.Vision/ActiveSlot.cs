namespace EndHud.Vision;

/// <summary>
/// Which squad slot is the controlled operator: its portrait has a white chevron above it and a
/// white ring around it. Matched by shape (white hair in another slot is white too, but not this
/// shape).
/// </summary>
public static class ActiveSlot
{
    /// <summary>The marker above slot 1 at 4K (the chevron, and the top of the ring), white pixels as '#'.</summary>
    private static readonly string[] Marker =
    [
        "...........................###......................",
        "..........................#####.....................",
        ".........................#######....................",
        "........................########....................",
        ".......................#########....................",
        "........................########....................",
        ".........................#######....................",
        "..........................#####.....................",
        "..........................###.......................",
        "..........###########..............############.....",
        "..........############............############......",
        "...........############..........############.......",
        "............############.........###########........",
        ".............############.......############........",
        "..............############.....###########..........",
        "................############..###########...........",
        "................########################............",
        ".................######################.............",
        "..................####################..............",
        "...........#......####################..............",
        ".........#######....#################.....######....",
        "..###.##########....################...#############",
        "##################....#############....#############",
        "##############.........##########.......############",
        "##########..............########.............#######",
        "######...................######..................##.",
        "..........................####......................",
        "...........................##.......................",
    ];

    /// <summary>Where <see cref="Marker"/> sits above slot 1 at 4K, anchored bottom-left.</summary>
    private static readonly PixelRect MarkerRect = new(140, 1801, 52, 28);

    // Overlap (intersection over union) of white pixels with the marker: the controlled slot scored
    // 0.84–1.0 on the battle recording, the others 0.34 at most.
    private const double MatchAt = 0.6;
    private const int SearchRadius = 3; // pixels, at the screen's own scale

    /// <summary>The controlled slot (1–4), or null when no slot clearly has the marker (cutscenes, menus).</summary>
    public static int? Find(Frame frame, UiScaler ui)
    {
        int? best = null;
        double bestScore = MatchAt;
        for (int slot = 0; slot < PartyLayout.SlotCount; slot++)
        {
            double score = Score(frame, ui.BottomLeft(PartyLayout.ForSlot(MarkerRect, slot)));
            if (score >= bestScore)
            {
                (best, bestScore) = (slot + 1, score);
            }
        }
        return best;
    }

    private static double Score(Frame frame, PixelRect rect)
    {
        int w = rect.Width, h = rect.Height;
        if (w < 10 || h < 6) return 0;
        // The marker resampled to this screen's size.
        var marker = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            marker[y * w + x] = Marker[y * Marker.Length / h][x * Marker[0].Length / w] == '#';

        double best = 0;
        for (int dy = -SearchRadius; dy <= SearchRadius; dy++)
        for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
        {
            int both = 0, either = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int px = rect.X + x + dx, py = rect.Y + y + dy;
                bool white = false;
                if ((uint)px < (uint)frame.Width && (uint)py < (uint)frame.Height)
                {
                    var (r, g, b) = frame.GetPixel(px, py);
                    white = Math.Min(r, Math.Min(g, b)) > 200;
                }
                bool m = marker[y * w + x];
                if (white && m) both++;
                if (white || m) either++;
            }
            if (either > 0) best = Math.Max(best, both / (double)either);
        }
        return best;
    }
}

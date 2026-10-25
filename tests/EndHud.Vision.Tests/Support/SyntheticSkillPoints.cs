using EndHud.Vision;

namespace EndHud.Vision.Tests;

/// <summary>
/// A skill-point bar drawn at 4K with any number of segments, as the game draws it on the three- and seven-segment
/// screenshots: one row from x 1598 to 2241 (rows 1952-1970), the segments sharing it with a 15 px gap between them;
/// each segment a dark see-through box with a lighter rim, a full one yellow, the filling one white from the left over
/// its inner rows. Behind it, scenery: a dim, uneven ground with some grain, so a gap shows the ground and a box darkens it.
/// This is a model of the two screenshots, not of the game: the real bars stay the proof (SkillPointBarTests).
/// </summary>
internal static class SyntheticSkillPoints
{
    private const int Width = 3840, Height = 2160;
    private const double Left = 1598, Right = 2241, Gap = 15;
    private const int Top = 1952, Bottom = 1970, FillTop = 1957, FillBottom = 1965;

    public static Frame Draw(int segments, double points)
    {
        var bgra = new byte[Width * Height * 4];
        var random = new Random(segments * 1000 + (int)(points * 10));
        for (int y = 1900; y < 2020; y++)
        for (int x = 1500; x < 2340; x++)
        {
            int ground = 70 + (int)(40 * Math.Sin(x / 37.0) * Math.Cos(y / 23.0)) + random.Next(-12, 13);
            Set(bgra, x, y, (byte)ground, (byte)(ground - 4), (byte)(ground - 8));
        }

        double pitch = (Right - Left + Gap) / segments;
        for (int s = 0; s < segments; s++)
        {
            int left = (int)Math.Round(Left + s * pitch), right = (int)Math.Round(Left + s * pitch + pitch - Gap);
            double fill = Math.Clamp(points - s, 0, 1);
            bool full = fill >= 1;
            for (int y = Top; y <= Bottom; y++)
            for (int x = left; x < right; x++)
            {
                bool rim = y < Top + 3 || y > Bottom - 3 || x < left + 3 || x >= right - 3;
                if (full) Set(bgra, x, y, 182, 155, 51);
                else if (rim) Set(bgra, x, y, 150, 148, 146);
                else
                {
                    int i = (y * Width + x) * 4;
                    Set(bgra, x, y, (byte)(bgra[i + 2] * 0.4), (byte)(bgra[i + 1] * 0.4), (byte)(bgra[i] * 0.4));
                }
            }
            int innerLeft = left + 4, innerRight = right - 4;
            int lit = full ? innerRight : innerLeft + (int)Math.Round(fill * (right - 3 - innerLeft));
            for (int y = FillTop; y <= FillBottom; y++)
            for (int x = innerLeft; x < lit; x++)
                if (full) Set(bgra, x, y, 255, 255, 0);
                else Set(bgra, x, y, 245, 245, 245);
        }
        return new Frame(Width, Height, bgra);
    }

    private static void Set(byte[] bgra, int x, int y, byte r, byte g, byte b)
    {
        int i = (y * Width + x) * 4;
        bgra[i] = b;
        bgra[i + 1] = g;
        bgra[i + 2] = r;
        bgra[i + 3] = 255;
    }
}

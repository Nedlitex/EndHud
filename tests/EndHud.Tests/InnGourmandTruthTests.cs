using System.IO;
using EndHud.Vision.Inn;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// Every gourmand of an owner's recording, read off its frames by eye (not by EndHud's readers), against what the gourmands'
/// tiles showed when the recording's readings are replayed through the real session and views (<see cref="InnReplay"/>).
/// Owner, 2026-10-21, test_yuanyuan8 (a feast-mode session, where every guest is a gourmand): "MOST of the gourmand
/// requirement IS NOT recognized".
/// </summary>
public class InnGourmandTruthTests(ITestOutputHelper output)
{
    /// <summary>
    /// A request must be on its table's tile within this many seconds of its bubble first showing it. The reading loop reads
    /// a frame every 0.3 s, a tile goes up once it reads the same for 0.5 s (InnWantsHold.ShowAfter), and the words of a
    /// bubble are read in about a second at 4K: 3 s leaves room for one bubble half-faded or covered on its first frames.
    /// </summary>
    public const double ShownWithin = 3;

    /// <summary>
    /// test_yuanyuan8 (2026-10-21, 3838x2158), 14 gourmands and 28 requests, from frames at 1 s and 0.25 s (t &lt;= 480 s):
    /// each request by its gourmand in the data's feast pool (number, request index), its table (the number post under the
    /// bubble's tail), when its words first showed, and when its icon turned into a face (green served, yellow imperfect,
    /// red failed). The full table with the lines as shown: .tracking/changes/2026-10-21-inn-y8-gourmands-truth.md.
    /// </summary>
    internal static IReadOnlyList<InnReplay.TruthRequest> Y8 { get; } =
    [
        Feast("A1", 1, 184.5, 266, 10, 1), Feast("A2", 1, 184.5, 192, 10, 3),
        Feast("B1", 2, 203.25, 291, 7, 0), Feast("B2", 2, 203.25, 206, 7, 3),
        Feast("C1", 5, 224.25, 231, 11, 0), Feast("C2", 5, 224.25, 305, 11, 4),
        Feast("D1", 4, 247.0, 249, 12, 0), Feast("D2", 4, 247.0, 297, 12, 4),
        Feast("E1", 3, 272.0, 280.5, 13, 2), Feast("E2", 3, 272.0, 323, 13, 4),
        Feast("F1", 6, 299.0, 364, 14, 1), Feast("F2", 6, 299.0, 365, 14, 3),
        Feast("G1", 1, 310.25, 313, 5, 2), Feast("G2", 1, 310.25, 375, 5, 4),
        Feast("H1", 2, 332.5, 418, 10, 1), Feast("H2", 2, 332.5, 392, 10, 3),
        Feast("I1", 4, 359.0, 395, 11, 2), Feast("I2", 4, 359.0, 403, 11, 3),
        Feast("J1", 5, 405.25, 408, 4, 4), Feast("J2", 5, 405.25, 409, 4, 0),
        Feast("K1", 3, 410.0, 411.5, 9, 1), Feast("K2", 3, 410.0, 439, 9, 3),
        Feast("L1", 6, 423.25, 424, 13, 2), Feast("L2", 6, 423.25, 442, 13, 4),
        Feast("M1", 4, 444.5, 445.75, 6, 2), Feast("M2", 4, 444.5, 448, 6, 3),
        Feast("N1", 1, 458.5, 461, 15, 2), Feast("N2", 1, 458.5, 467, 15, 3),
    ];

    private static InnReplay.TruthRequest Feast(string label, int table, double from, double resolved, int gourmand, int request) =>
        new(label, table, from, resolved, Inn.Gourmands.Single(g => g.Pool == "feast" && g.Number == gourmand).Requests[request]);

    /// <summary>A request served or failed sooner than <see cref="ShownWithin"/> after its bubble can't be held to it (the player was quicker).</summary>
    private static bool Judged(InnReplay.TruthRequest t) => t.Resolved - t.From > ShownWithin;

    /// <summary>The outcome of every true request over a replay, one line each, and the counts.</summary>
    internal static (int Judged, int Recognised, int Steady, List<string> Lines) Measure(InnReplay replay, IReadOnlyList<InnReplay.TruthRequest> truth)
    {
        var lines = new List<string>();
        int judged = 0, recognised = 0, steady = 0;
        foreach (var o in replay.AgainstTruth(truth))
        {
            bool counts = Judged(o.Truth);
            bool ok = o.ShownAfter is { } after && after <= ShownWithin;
            bool stays = ok && o.MissingAfterShown <= 1;
            if (counts)
            {
                judged++;
                if (ok) recognised++;
                if (stays) steady++;
            }
            lines.Add($"{o.Truth.Label} table {o.Truth.Table} {o.Truth.From,6:F2}-{o.Truth.Resolved,6:F2}: "
                      + (o.ShownAfter is { } a ? $"shown after {a:F2} s" : "NEVER shown") + $", missing {o.MissingAfterShown:F1} s after, "
                      + $"elsewhere {o.ElsewhereSeconds:F1} s, others on its tile {o.OthersSeconds:F1} s{(counts ? "" : " (resolved too soon to judge)")}"
                      + (counts && !ok ? " MISSED" : counts && !stays ? " NOT KEPT" : ""));
        }
        lines.Add($"judged {judged}: recognised within {ShownWithin} s {recognised}, kept until resolved {steady}");
        // What each table's tile showed over time, each request by its true label where it's one this table's gourmand made
        // then, else by its gourmand in the data (the trace to find a miss's cause by).
        var before = new Dictionary<int, string>();
        foreach (var d in replay.GourmandDraws)
        {
            double at = d.At.TotalSeconds;
            for (int table = 1; table <= TableCardReader.Tables; table++)
            {
                string now = d.Tables.TryGetValue(table, out var shown)
                    ? string.Join(" ", shown.Select(r => truth.FirstOrDefault(t => t.Table == table && t.Request == r && t.From - 1 <= at && at < t.Resolved + 5)?.Label
                                                         ?? "?" + Describe(r)))
                    : "";
                if (before.GetValueOrDefault(table, "") == now) continue;
                lines.Add($"{at,7:F1} table {table}: {(now.Length > 0 ? now : "-")}");
                before[table] = now;
            }
        }
        return (judged, recognised, steady, lines);

        static string Describe(GourmandRequest r) =>
            Inn.Gourmands.Where(g => g.Requests.Contains(r)).Select(g => $"{g.Pool}{g.Number}.{g.Requests.ToList().IndexOf(r)}").FirstOrDefault() ?? "unknown";
    }

    /// <summary>The measurement over a readings file named in ENDHUD_Y8_READINGS (a developer's before/after), printed only.</summary>
    [Fact]
    public void Measure_a_readings_file_against_test_yuanyuan8s_gourmands()
    {
        if (Environment.GetEnvironmentVariable("ENDHUD_Y8_READINGS") is not { Length: > 0 } path || !File.Exists(path)) return;
        var replay = InnReplay.Run(InnReplay.Load(path));
        foreach (string line in Measure(replay, Y8).Lines) output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("ENDHUD_Y8_OUT") is { Length: > 0 } outPath)
            File.WriteAllLines(outPath, Measure(replay, Y8).Lines);
    }
}

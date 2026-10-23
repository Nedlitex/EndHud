using System.Globalization;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Whose lasting ultimate the time bar shows (<see cref="LastingUltimates"/>). Measured
/// (Fixtures/lasting-ultimate-background): the bar is not the controlled operator's; with several
/// running it shows the controlled operator's own if she has one, else one running in the background.
/// The recordings' readings are replayed as the app took them (panel seen, controlled slot, the
/// tracked bar, each slot's ultimate), with the squad's names put in.
/// </summary>
public class LastingUltimatesTests(ITestOutputHelper output)
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartyState Reading(string?[] squad, bool panel, int? controlled, double? bar, string ultimates)
    {
        string[] charge = ultimates.Split('/');
        var slots = Enumerable.Range(1, PartyLayout.SlotCount).Select(n =>
        {
            string? id = n <= squad.Length ? squad[n - 1] : null;
            if (id is null) return new PartySlot(n, null, null, "", NoPortrait) { Empty = true };
            var ultimate = charge[n - 1] switch
            {
                "R" => new UltimateReading(true, 1),
                "-" => null,
                var p => new UltimateReading(false, double.Parse(p, CultureInfo.InvariantCulture)),
            };
            return new PartySlot(n, null, 1, "", NoPortrait, null, ultimate, false,
                new Operators.OperatorMatch(new Operators.OperatorInfo(id, id, id, id + ".png"), 1, true));
        }).ToList();
        return new PartyState(slots, 8, panel ? 0 : 1) { ActiveSlot = controlled, UltimateTimeLeft = bar, SkillPoints = 1 };
    }

    /// <summary>"t panel controlled bar ultimates" per line, every 0.5 s, as trace-skills printed them.</summary>
    private static IEnumerable<(double T, bool Panel, int? Controlled, double? Bar, string Ultimates)> Rows(string table) =>
        table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
        {
            string[] f = line.Split(' ');
            return (double.Parse(f[0], CultureInfo.InvariantCulture), f[1] == "1", f[2] == "-" ? (int?)null : int.Parse(f[2]),
                f[3] == "-" ? (double?)null : double.Parse(f[3], CultureInfo.InvariantCulture), f[4]);
        });

    private string Describe(double t, IReadOnlyDictionary<string, LastingUltimateView> views)
    {
        string shown = string.Join(" ", views.Values.Select(v => $"{v.OperatorId}:{(v.Estimated ? "~" : "")}{v.LeftShare:F2}"));
        output.WriteLine($"{t:F1}: {shown}");
        return shown;
    }

    // test_zhuang_background1.mp4, 34–71 s: Perlica (slot 2) controlled throughout; Zhuang Fangyi
    // (slot 1) casts hers from the background at about 36 s (cutscene 36–38 s).
    private const string ZhuangInTheBackground = """
        34 1 2 - R/R/R/R
        35.5 1 2 - R/R/R/R
        36 0 2 - R/R/R/R
        38 0 2 - R/R/R/R
        38.5 1 2 1.00 0.00/R/R/R
        39 1 2 0.98 0.00/R/R/R
        39.5 1 2 0.96 0.00/R/R/R
        40 1 2 0.94 0.00/R/R/R
        41 1 2 0.90 0.00/R/R/R
        42 1 2 0.88 0.00/R/R/R
        42.5 1 2 0.86 0.00/R/R/R
        43 0 2 0.86 0.00/R/R/R
        46 0 2 0.86 0.00/R/R/R
        46.5 1 - 0.73 0.00/R/R/R
        47 1 2 0.72 0.00/R/R/R
        48 1 2 0.67 0.00/R/R/R
        50 1 2 0.59 0.00/R/R/R
        52 1 2 0.51 0.00/R/R/R
        53 1 2 0.47 0.00/R/R/R
        53.5 0 2 0.47 0.00/R/R/R
        55 1 2 0.45 0.00/R/0.00/R
        57 1 2 0.41 0.00/R/0.00/R
        58 0 2 0.41 0.00/R/0.00/R
        60 0 2 0.39 0.00/0.00/0.00/R
        63 1 2 0.37 0.00/0.00/0.00/0.00
        64 1 2 0.33 0.00/0.10/0.00/0.00
        66 1 2 0.25 0.00/0.10/0.00/0.00
        68 1 2 0.17 0.00/0.18/0.51/0.00
        69.5 1 2 0.11 0.00/0.18/0.51/0.00
        """;

    /// <summary>The owner's report: Zhuang's ultimate with Perlica controlled is shown on Zhuang, never on Perlica.</summary>
    [Fact]
    public void A_lasting_ultimate_cast_from_the_background_is_its_operators_whoever_is_controlled()
    {
        string?[] squad = ["Zhuang_Fangyi", "Perlica", "Arcane", "Liino"];
        var lasting = new LastingUltimates();
        foreach (var (t, panel, controlled, bar, ultimates) in Rows(ZhuangInTheBackground))
        {
            var views = lasting.Update(Reading(squad, panel, controlled, bar, ultimates), TimeSpan.FromSeconds(t));
            Describe(t, views);
            Assert.False(views.ContainsKey("Perlica"), $"{t}: on Perlica");
            if (bar is null) Assert.Empty(views);
            else Assert.Equal(new LastingUltimateView("Zhuang_Fangyi", bar.Value, false), Assert.Single(views).Value);
        }
    }

    // test_zhuang_laevatine_background1.mp4, 36–57.5 s: Zhuang Fangyi (slot 1), Laevatain (slot 2)
    // and an operator not named here (slot 3). Zhuang's runs from 36 s, Laevatain's from about 42 s,
    // the slot-3 operator's (teal) about 44–46 s. By eye (Fixtures/lasting-ultimate-background zl-*):
    // 43.5 red (Laevatain controlled), 44.5 teal (slot 3), 46 gold (Zhuang), 53.5 red, 56 gold (slot 3 controlled).
    private const string ThreeRunning = """
        36 0 1 - 0.00/R/R/-
        36.5 1 1 1.00 0.00/R/R/-
        37 0 1 1.00 0.00/R/R/-
        39 1 1 1.00 0.00/R/R/-
        39.5 0 1 1.00 0.00/R/R/-
        41.5 1 1 1.00 0.00/R/R/-
        42 1 1 1.00 0.00/R/R/-
        42.5 1 1 0.93 0.00/0.00/0.00/-
        43 1 - 0.86 0.00/0.00/0.00/-
        43.5 1 2 0.83 0.00/0.00/0.00/-
        44 1 2 0.80 0.00/0.00/0.00/-
        44.5 1 3 0.54 0.00/0.00/0.00/-
        45 1 3 0.46 0.00/0.00/0.00/-
        45.5 1 3 0.39 0.00/0.00/0.00/-
        46 1 - 0.78 0.00/0.00/0.00/-
        46.5 1 1 0.77 0.00/0.00/0.00/-
        47 1 1 0.75 0.00/0.00/0.00/-
        48 1 1 0.71 0.00/0.00/0.00/-
        49 1 1 0.67 0.00/0.00/0.00/-
        50 1 1 0.63 0.00/0.00/0.00/-
        50.5 1 1 0.63 0.00/0.00/0.00/-
        51 1 1 0.59 0.00/0.00/0.00/-
        52 1 1 0.55 0.00/0.00/0.00/-
        52.5 1 2 - 0.00/0.00/0.00/-
        53 1 2 - 0.00/0.00/0.00/-
        53.5 1 2 0.15 0.00/0.00/0.00/-
        54 1 2 0.12 0.00/0.00/0.00/-
        54.5 1 2 0.09 0.00/0.00/0.00/-
        55 1 2 0.05 0.00/0.00/0.00/-
        55.5 1 2 0.05 0.00/0.00/0.00/-
        56 1 3 0.38 0.00/0.00/0.00/-
        56.5 1 - 0.36 0.00/0.00/0.00/-
        57 1 1 0.34 0.00/0.00/0.00/-
        57.5 1 1 0.32 0.00/0.00/0.00/-
        """;

    /// <summary>
    /// Three running, the player switching between them: each reading of the bar goes to the
    /// operator it belongs to, the teal one (not one of the known lasting ultimates) to no one, and
    /// a running one off the bar is carried on, marked as an estimate, on its own operator.
    /// </summary>
    [Fact]
    public void With_several_running_the_bar_goes_to_whose_it_is_and_the_others_are_carried_on()
    {
        string?[] squad = ["Zhuang_Fangyi", "Laevatain", "Slot3Operator"];
        var lasting = new LastingUltimates();
        var shown = new Dictionary<double, IReadOnlyDictionary<string, LastingUltimateView>>();
        foreach (var (t, panel, controlled, bar, ultimates) in Rows(ThreeRunning))
        {
            var views = lasting.Update(Reading(squad, panel, controlled, bar, ultimates), TimeSpan.FromSeconds(t));
            Describe(t, views);
            Assert.False(views.ContainsKey("Slot3Operator"));
            shown[t] = views;
        }

        void Read(double t, string id) => Assert.True(shown[t].TryGetValue(id, out var v) && !v.Estimated, $"{t}: {id} not read on the bar");
        void NotRead(double t) => Assert.True(shown[t].Values.All(v => v.Estimated), $"{t}: the bar given to someone");

        Read(36.5, "Zhuang_Fangyi");
        Read(42.5, "Zhuang_Fangyi");
        Read(43.5, "Laevatain");      // red, hers: she just cast it (Zhuang's is about the same by now)
        Read(44, "Laevatain");
        NotRead(44.5);                 // teal: the slot-3 operator's
        NotRead(45.5);
        foreach (double t in new[] { 46.5, 48, 50, 52 })
        {
            Read(t, "Zhuang_Fangyi");
            Assert.True(shown[t].TryGetValue("Laevatain", out var laevatain) && laevatain.Estimated, $"{t}: Laevatain's not carried on");
        }
        Assert.Equal(2, shown[52.5].Count); // the tracker dropped the bar on the switch: both carried on
        Assert.True(shown[52.5].Values.All(v => v.Estimated));
        foreach (double t in new[] { 53.5, 54.5 })
        {
            Read(t, "Laevatain");
            Assert.True(shown[t].TryGetValue("Zhuang_Fangyi", out var zhuang) && zhuang.Estimated && Math.Abs(zhuang.LeftShare - 0.47) < 0.1,
                $"{t}: Zhuang's carried on near 0.47 (her own reading at 56 s is 0.38)");
        }
        foreach (double t in new[] { 56, 57, 57.5 }) Read(t, "Zhuang_Fangyi"); // gold, with slot 3 controlled at 56 s
    }

    [Fact]
    public void No_bar_for_a_moment_carries_them_on_and_for_longer_ends_them()
    {
        string?[] squad = ["Zhuang_Fangyi", "Perlica"];
        var lasting = new LastingUltimates();
        lasting.Update(Reading(squad, true, 2, 0.9, "0.00/R"), TimeSpan.FromSeconds(0));
        lasting.Update(Reading(squad, true, 2, 0.8, "0.00/R"), TimeSpan.FromSeconds(1));

        var gap = lasting.Update(Reading(squad, true, 2, null, "0.00/R"), TimeSpan.FromSeconds(2));
        Assert.Equal(new LastingUltimateView("Zhuang_Fangyi", 0.7, true), gap["Zhuang_Fangyi"] with { LeftShare = Math.Round(gap["Zhuang_Fangyi"].LeftShare, 3) });
        Assert.NotEmpty(lasting.Update(Reading(squad, true, 2, null, "0.00/R"), TimeSpan.FromSeconds(3.5)));
        Assert.Empty(lasting.Update(Reading(squad, true, 2, null, "0.00/R"), TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void A_bar_nobody_with_a_lasting_ultimate_could_own_is_shown_for_no_one()
    {
        var views = new LastingUltimates().Update(Reading(["Perlica", "Arcane"], true, 1, 0.8, "0.00/R"), TimeSpan.Zero);

        Assert.Empty(views);
    }

    [Fact]
    public void Two_with_lasting_ultimates_the_one_just_spent_is_the_new_bars()
    {
        string?[] squad = ["Laevatain", "Zhuang_Fangyi", "Perlica"];
        var lasting = new LastingUltimates();
        lasting.Update(Reading(squad, true, 3, null, "R/R/R"), TimeSpan.FromSeconds(0));
        lasting.Update(Reading(squad, true, 3, null, "R/0.00/R"), TimeSpan.FromSeconds(0.5)); // Zhuang's spent, from the background

        var views = lasting.Update(Reading(squad, true, 3, 1.0, "R/0.00/R"), TimeSpan.FromSeconds(3));

        Assert.Equal("Zhuang_Fangyi", Assert.Single(views).Key);
    }

    // ---- The recorded frames, through the reader ----

    internal sealed record FrameLabel(string File, int ControlledSlot, string? Bar, string? Owner);

    private static readonly FrameLabel[] Labels = JsonSerializer.Deserialize<FrameLabel[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("lasting-ultimate-background", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("lasting-ultimate-background", file)));

    private static double Seconds(string file) =>
        double.Parse(file[(file.IndexOf('-') + 1)..^"s.jpg".Length].Replace('_', '.'), CultureInfo.InvariantCulture);

    /// <summary>What the attribution rests on, as read: the bar is there under whoever is controlled, and the controlled slot is the one seen by eye.</summary>
    [Fact]
    public async Task The_bar_is_read_under_whoever_is_controlled()
    {
        var reader = new PartyReader(new TextRecognizer());
        var wrong = new List<string>();
        int unread = 0;
        foreach (var label in Labels)
        {
            var state = await reader.ReadAsync(await LoadAsync(label.File));
            output.WriteLine($"{label.File}: controlled {state.ActiveSlot}, bar {state.UltimateTimeLeft:F2}");
            if (state.ActiveSlot is { } read && read != label.ControlledSlot) wrong.Add($"{label.File}: controlled {read}, by eye {label.ControlledSlot}");
            if (state.ActiveSlot is null) unread++;
            if ((state.UltimateTimeLeft is not null) != (label.Bar is not null)) wrong.Add($"{label.File}: bar {state.UltimateTimeLeft}, by eye {label.Bar ?? "none"}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.True(unread <= 2, $"the controlled slot not read on {unread} frames"); // 2 of 11 measured (a switch, an effect)
    }

    /// <summary>
    /// Each of the Zhuang recording's frames read and named, on its own (as the first reading after
    /// the app starts would be): Zhuang's on every one with a bar, Perlica's never. The frames are
    /// seconds apart, with cutscenes between, so a run across them is the replay's job above.
    /// </summary>
    [Fact]
    public async Task On_the_recorded_frames_Zhuangs_ultimate_is_hers_with_Perlica_controlled()
    {
        var reader = new PartyReader(new TextRecognizer());
        string[] names = ["Zhuang_Fangyi", "Perlica", "Arcane", "Liino"];
        foreach (var label in Labels.Where(l => l.File.StartsWith("zh-", StringComparison.Ordinal)).OrderBy(l => Seconds(l.File)))
        {
            var read = await reader.ReadAsync(await LoadAsync(label.File));
            var state = read with
            {
                Slots = read.Slots.Select(s => s with { Operator = new Operators.OperatorMatch(new Operators.OperatorInfo(names[s.Number - 1], "", "", ""), 1, true) }).ToList(),
            };
            var views = new LastingUltimates().Update(state, TimeSpan.FromSeconds(Seconds(label.File)));
            Describe(Seconds(label.File), views);
            if (label.Owner is null) Assert.Empty(views);
            else Assert.Equal((label.Owner, false), (Assert.Single(views).Key, views[label.Owner].Estimated));
        }
    }
}

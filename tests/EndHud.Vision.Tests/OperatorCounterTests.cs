using System.Text.Json;
using EndHud.Vision;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>Typhoeus's 猎矢 and 启示 (<see cref="OperatorCounters.HuntingArrow"/>, <see cref="OperatorCounters.Sign"/>) on frames labelled by eye (Fixtures/counters).</summary>
public class OperatorCounterTests
{
    /// <param name="Signs">启示; null with <paramref name="SignsUnreadable"/> where it can't be told.</param>
    /// <param name="Sunderblades">Zhuang Fangyi's 青霆剑, on her frames (zhuang-*).</param>
    private sealed record Label(string File, int? HuntingArrows, int? Signs, bool SignsUnreadable, int? Sunderblades);

    private static bool Typhoeus(Label l) => !l.File.StartsWith("zhuang-", StringComparison.Ordinal);

    private static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("counters", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static TheoryData<string> Files() => [.. Labels.Where(Typhoeus).Select(l => l.File)];

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Reads_Typhoeus_hunting_arrows(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("counters", file)));

        var reading = OperatorCounters.Read(frame, "Typhoeus").Single(r => r.CounterId == OperatorCounters.HuntingArrow.Id);

        Assert.Equal(Labels.Single(l => l.File == file).HuntingArrows, reading.Value);
    }

    public static TheoryData<string> SignFiles() => [.. Labels.Where(Typhoeus).Select(l => l.File)];

    public static TheoryData<string> ZhuangFiles() => [.. Labels.Where(l => !Typhoeus(l)).Select(l => l.File)];

    [Theory]
    [MemberData(nameof(ZhuangFiles))]
    public async Task Reads_Zhuang_Fangyis_sunderblades(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("counters", file)));

        var reading = Assert.Single(OperatorCounters.Read(frame, "Zhuang_Fangyi"));

        Assert.Equal(OperatorCounters.Sunderblade.Id, reading.CounterId);
        Assert.Equal(Labels.Single(l => l.File == file).Sunderblades, reading.Value);
    }

    [Fact]
    public void The_sunderblade_fixtures_cover_none_some_all_and_hidden()
    {
        foreach (int n in new[] { 0, 3, 5, 9 }) Assert.Contains(Labels, l => l.Sunderblades == n && !Typhoeus(l));
        Assert.Contains(Labels, l => l.Sunderblades is null && !Typhoeus(l));
    }

    /// <summary>Her grid read on Typhoeus's frames, and the other way round: never a count.</summary>
    [Fact]
    public async Task One_operators_counter_isnt_read_in_anothers_icon()
    {
        foreach (var label in Labels.Where(l => l.Signs is { } s && s > 0).Take(4))
        {
            var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("counters", label.File)));
            Assert.Null(OperatorCounters.Read(frame, "Zhuang_Fangyi").Single().Value);
        }
        foreach (var label in Labels.Where(l => l.Sunderblades is > 0).Take(4))
        {
            var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("counters", label.File)));
            Assert.All(OperatorCounters.Read(frame, "Typhoeus"), r => Assert.Null(r.Value));
        }
    }

    [Theory]
    [MemberData(nameof(SignFiles))]
    public async Task Reads_Typhoeus_signs(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("counters", file)));

        var reading = OperatorCounters.Read(frame, "Typhoeus").Single(r => r.CounterId == OperatorCounters.Sign.Id);

        Assert.Equal(Labels.Single(l => l.File == file).Signs, reading.Value);
    }

    [Fact]
    public void The_sign_fixtures_cover_none_some_and_all()
    {
        foreach (int n in new[] { 0, 1, 4, 7, 8 }) Assert.Contains(Labels, l => l.Signs == n);
    }

    [Fact]
    public void The_fixtures_cover_every_count_and_frames_that_cant_be_told()
    {
        for (int n = 0; n <= OperatorCounters.HuntingArrow.Max; n++) Assert.Contains(Labels, l => l.HuntingArrows == n);
        Assert.True(Labels.Count(l => l.SignsUnreadable) >= 2);
    }

    public static TheoryData<string> OtherSquads() =>
    [
        .. Directory.GetFiles(CombatFrameTests.Fixture("battle-hud"), "*.jpg").Select(f => Path.Combine("battle-hud", Path.GetFileName(f))),
        .. Directory.GetFiles(CombatFrameTests.Fixture(""), "*.jpg").Select(Path.GetFileName)!,
    ];

    /// <summary>Other operators' screens (Laevatain's flower in the same place, exploration, menus): no arrows are made up.</summary>
    [Theory]
    [MemberData(nameof(OtherSquads))]
    public async Task Other_screens_arent_read_as_arrows(string file)
    {
        var frame = await ImageFile.LoadAsync(CombatFrameTests.Fixture(file));

        Assert.All(OperatorCounters.Read(frame, "Typhoeus"), r => Assert.Null(r.Value));
        Assert.All(OperatorCounters.Read(frame, "Zhuang_Fangyi"), r => Assert.Null(r.Value));
    }
}

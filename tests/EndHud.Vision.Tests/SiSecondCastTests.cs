using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace EndHud.Vision.Tests;

/// <summary>
/// Si's battle skill cast a second time while it still lasts (Fixtures/si-timer-second-cast): the
/// icon becomes a crossed-out butterfly and the ring drains on. The skill lasts until the
/// butterfly is back, so the timer must show through the second cast, from the ring where it can
/// be read and as an estimate where the button is hidden.
/// </summary>
public class SiSecondCastTests(ITestOutputHelper output)
{
    /// <param name="Button">Si's battle-skill button, 0 = leftmost of four.</param>
    /// <param name="Icon">By eye: Original, Changed or SecondCast; null where the button is hidden.</param>
    /// <param name="DrainedDegrees">How much of the ring is gone, clockwise from 12 o'clock (360: none left).</param>
    internal sealed record Label(string File, int Button, string? Icon, double? DrainedDegrees, bool Hidden);

    internal static readonly Label[] Labels = JsonSerializer.Deserialize<Label[]>(
        File.ReadAllText(CombatFrameTests.Fixture(Path.Combine("si-timer-second-cast", "labels.json"))),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    internal static Task<Frame> LoadAsync(string file) => ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("si-timer-second-cast", file)));

    internal static double Seconds(string file) =>
        double.Parse(file[(file.IndexOf('-') + 1)..^"s.jpg".Length].Replace('_', '.'), System.Globalization.CultureInfo.InvariantCulture);

    public static TheoryData<string> Clips() => ["lae", "si"];

    /// <summary>
    /// Each clip's frames in order, as a fight shows them: nothing before the first cast; from the
    /// swirl until the butterfly is back a time on every reading where the button is seen, the
    /// second cast included, from the ring where it's read; an estimate where it's hidden (while the
    /// estimate lasts); nothing once the butterfly is back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Clips))]
    public async Task The_timer_runs_from_the_first_cast_until_the_butterfly_is_back(string clip)
    {
        var frames = Labels.Where(l => l.File.StartsWith(clip + "-", StringComparison.Ordinal)).OrderBy(l => Seconds(l.File)).ToList();
        var timer = new SkillTimer(SkillRing.SiDuration);
        bool started = false, ended = false;
        var wrong = new List<string>();
        foreach (var label in frames)
        {
            var reading = SkillRing.ReadSi(await LoadAsync(label.File), label.Button);
            var view = timer.Update(reading, TimeSpan.FromSeconds(Seconds(label.File)));
            output.WriteLine($"{label.File}: {reading.Icon} ring {reading.LeftShare:F3} -> {(view is null ? "nothing" : $"{(view.Estimated ? "~" : "")}{view.SecondsLeft:F1} s")}");
            if (label.Icon == "Changed") started = true;
            if (started && label.Icon == "Original") ended = true;
            bool running = started && !ended;
            if (!running)
            {
                if (view is not null) wrong.Add($"{label.File}: shown though not running");
                continue;
            }
            if (view is null)
            {
                // Hidden past the estimate's end (the ring stood still in the cutscene): nothing, by design.
                if (!label.Hidden) wrong.Add($"{label.File}: running ({label.Icon}) but nothing shown");
                continue;
            }
            if (label.Hidden && !view.Estimated) wrong.Add($"{label.File}: hidden but not marked as an estimate");
            if (label.DrainedDegrees is { } drained && !view.Estimated)
            {
                double expected = SkillRing.SiDuration.TotalSeconds * (1 - drained / 360);
                if (Math.Abs(view.SecondsLeft - expected) > 1.5) wrong.Add($"{label.File}: {view.SecondsLeft:F1} s left, the ring says {expected:F1}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>The crossed-out butterfly is told apart and its ring read, on both clips' frames (Si in slot 3 and in slot 1).</summary>
    [Fact]
    public async Task After_the_second_cast_the_ring_is_still_read()
    {
        var wrong = new List<string>();
        foreach (var label in Labels.Where(l => l.Icon == "SecondCast"))
        {
            var reading = SkillRing.ReadSi(await LoadAsync(label.File), label.Button);
            double expected = 1 - label.DrainedDegrees!.Value / 360;
            output.WriteLine($"{label.File}: {reading.Icon} ring {reading.LeftShare:F3}, by eye {expected:F3}");
            if (reading.Icon.ToString() != "SecondCast") wrong.Add($"{label.File}: icon {reading.Icon}");
            else if (reading.LeftShare is not { } left || Math.Abs(left - expected) > 0.07) wrong.Add($"{label.File}: ring {reading.LeftShare}, by eye {expected:F3}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}

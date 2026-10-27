using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Physical ultimates (Fixtures/physical-ultimate): drawn in a neutral cream, not an element colour.
/// Charging, the arc is cream on the dark disc; ready, the disc is lit grey behind a white icon, in a
/// light ring. A squad of four Physical operators (Mi Fu in slot 1), keyboard and mouse; what each
/// frame shows was read by eye (see the folder's README).
/// </summary>
public class PhysicalUltimateTests
{
    private static Task<Frame> LoadAsync(string file) =>
        ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("physical-ultimate", file)));

    private static UltimateReading Read(Frame frame, int slot) =>
        UltimateGauge.Measure(frame, slot) ?? throw new Xunit.Sdk.XunitException($"slot {slot + 1}: no reading");

    [Fact]
    public async Task Three_ready_and_one_about_a_third_charged()
    {
        var frame = await LoadAsync("mifu-60s.png");

        for (int slot = 1; slot < 4; slot++)
            Assert.True(Read(frame, slot).Ready, $"slot {slot + 1} should read ready");
        var charging = Read(frame, 0);
        Assert.False(charging.Ready);
        Assert.InRange(charging.Progress, 0.28, 0.42);
    }

    [Fact]
    public async Task Nearly_full_charge()
    {
        var reading = Read(await LoadAsync("mifu-103s.png"), 0);

        Assert.False(reading.Ready);
        Assert.InRange(reading.Progress, 0.85, 1.0);
    }

    [Fact]
    public async Task Ready_right_after_filling()
    {
        var frame = await LoadAsync("mifu-114_3s.png");

        Assert.True(Read(frame, 0).Ready, "slot 1 should read ready");
        Assert.True(Read(frame, 3).Ready, "slot 4 should read ready");
    }

    [Fact]
    public async Task Empty_again_after_the_cast()
    {
        var frame = await LoadAsync("mifu-120s.png");

        var emptied = Read(frame, 0);
        Assert.False(emptied.Ready);
        Assert.InRange(emptied.Progress, 0, 0.05);
        Assert.True(Read(frame, 3).Ready, "slot 4 should read ready");
    }

    [Fact]
    public async Task A_white_out_is_not_a_full_charge()
    {
        // The screen washed out to light grey: a centre of ~196 must not read as a full cream arc.
        var reading = UltimateGauge.Measure(await LoadAsync("mifu-66s.png"), 0);

        Assert.False(reading?.Ready ?? false, "slot 1 read as ready");
        Assert.True(reading is null || reading.Progress <= 0.9, $"slot 1 read as {reading?.Progress:F2} charged");
    }

    // A second recording (rms-): Mi Fu in slot 4 next to coloured ultimates, over a light floor.

    [Fact]
    public async Task A_light_effect_over_a_charging_circle_is_not_ready()
    {
        // A pale cyan figure (another operator's ultimate) over the corner: it lights the disc and
        // the ring round it, and leaves a little white where the icon would be.
        var reading = UltimateGauge.Measure(await LoadAsync("rms-94s.png"), 3);

        Assert.False(reading?.Ready ?? false, "slot 4 read as ready");
    }

    [Fact]
    public async Task Ready_next_to_coloured_circles_charging()
    {
        var frame = await LoadAsync("rms-107_5s.png");

        Assert.True(Read(frame, 3).Ready, "slot 4 should read ready");
        var cyan = Read(frame, 1);
        Assert.False(cyan.Ready);
        Assert.InRange(cyan.Progress, 0.22, 0.38);
        var red = Read(frame, 2);
        Assert.False(red.Ready);
        Assert.InRange(red.Progress, 0.42, 0.60);
    }

    [Fact]
    public async Task Charging_over_a_light_floor()
    {
        var reading = Read(await LoadAsync("rms-140s.png"), 3);

        Assert.False(reading.Ready);
        Assert.InRange(reading.Progress, 0.10, 0.25);
    }
}

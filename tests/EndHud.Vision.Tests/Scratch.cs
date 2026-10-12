using EndHud.Vision;
using Xunit;
using Xunit.Abstractions;
namespace EndHud.Vision.Tests;
public class Scratch(ITestOutputHelper o)
{
    [Fact]
    public async Task Pings()
    {
        var reader = new PartyReader(new TextRecognizer());
        foreach (var name in new[] { "combat-combos-refilling.jpg", "exploration-scenery-behind-ultimate.jpg", "ultimate-charging-two-hurt.jpg", "battle-dimmed-squad-heal.jpg" })
        {
            var f = await ImageFile.LoadAsync(CombatFrameTests.Fixture(name));
            foreach (var screen in new[] { SimulatedScreen.Uhd, SimulatedScreen.SurfaceLarge, SimulatedScreen.FiveK, SimulatedScreen.Surface, SimulatedScreen.OddWindow })
                o.WriteLine($"{name} {screen.Width}x{screen.Height}: ping={(await reader.ReadAsync(screen.Render(f))).PingMs}");
        }
    }
}

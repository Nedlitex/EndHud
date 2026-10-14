using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>Something on screen isn't gone after one reading that misses it, only after 2 s of them.</summary>
public class PresenceMemoryTests
{
    [Fact]
    public void Not_there_until_it_is_seen()
    {
        Assert.False(new PresenceMemory().Update(false));
    }

    [Fact]
    public void A_few_readings_without_it_dont_make_it_gone()
    {
        var memory = new PresenceMemory();
        Assert.True(memory.Update(true));
        for (int i = 0; i < PresenceMemory.ReadingsToLeave - 1; i++) Assert.True(memory.Update(false), $"reading {i + 1}");
        Assert.True(memory.Update(true));
    }

    [Fact]
    public void Two_seconds_without_it_make_it_gone()
    {
        var memory = new PresenceMemory();
        memory.Update(true);
        for (int i = 0; i < PresenceMemory.ReadingsToLeave - 1; i++) memory.Update(false);

        Assert.False(memory.Update(false));
        Assert.False(memory.Update(false));
    }
}

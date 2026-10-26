using System.Windows;
using EndHud.SkillInput;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Battle-skill key reading is not offered in this build (<see cref="SkillKeyReading.Offered"/>; owner, 2026-10-25:
/// "hide that for now - we want to minimize risk that user is blamed by anticheat"). A player whose config.json
/// still says <c>ReadSkillPresses = true</c> gets no menu rows and no reader: no key held as a hotkey, no pad
/// opened, no XInput polled. The rotation strip follows the screen alone.
/// </summary>
public class SkillKeyReadingNotOfferedTests
{
    private static HudConfig Consented() => new() { ReadSkillPresses = true, RotationEnabled = true, PartyOcrEnabled = true };

    [Fact]
    public void Battle_skill_key_reading_is_not_offered_in_this_build() => Assert.False(SkillKeyReading.Offered);

    [Fact]
    public void A_saved_consent_does_not_make_the_reader_listen_even_with_the_game_in_front()
    {
        Assert.False(OverlayWindow.SkillInputListens(Consented(), gameInFront: true));
    }

    [Fact]
    public void A_saved_consent_gives_the_strip_no_presses()
    {
        using var reader = new SkillInputReader(readWindows: false);
        Assert.Null(OverlayWindow.PressesFor(Consented(), reader));
    }

    [Fact]
    public void A_saved_consent_is_kept_in_the_settings_so_offering_it_again_restores_it()
    {
        Assert.True(ConfigStore.Parse("""{ "ReadSkillPresses": true }""").ReadSkillPresses);
    }

    [Fact]
    public void The_diagnostics_say_it_is_not_offered()
    {
        var lines = OverlayWindow.SkillReadingLines(SkillKeyReading.Offered, consented: true,
            describe: () => throw new InvalidOperationException("the reader isn't asked"), new int[4, 6]).ToList();

        Assert.Equal(["battle-skill key reading: not offered in this build"], lines);
    }
}

/// <summary>
/// The real overlay window, built with a config that consents to key reading: the key-reading card is hidden and
/// a refresh with the game in front starts nothing. The keys are F13+ (as in <see cref="SkillInputReaderTests"/>):
/// if the reader started, Windows would refuse this test the same keys.
/// </summary>
[Trait("Category", "Desktop")] // asks Windows' input which keys are held: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public sealed class SkillKeyReadingHiddenWindowTests
{
    /// <summary>F13-F16 as stored key codes (set-1 scan codes).</summary>
    private static readonly int[] Keys = [0x64, 0x65, 0x66, 0x67];

    [Fact]
    public void A_consenting_config_gets_no_key_reading_card_and_no_reader()
    {
        Assert.True(SkillInputReaderTests.KeyTurn.WaitOne(TimeSpan.FromMinutes(2)), "another test run kept the skill-key tests' turn for 2 minutes");
        try
        {
            uint[] vks = [.. Keys.Select(SkillInputNative.VirtualKey)];
            Assert.All(SkillInputReaderTests.Free(vks), Assert.True); // nobody holds them before

            var (card, keysFreeWhileInFront, status, hotkeysHeld) = OnSta(() =>
            {
                var config = new HudConfig { ReadSkillPresses = true, RotationEnabled = true, PartyOcrEnabled = true, SkillKeys = [.. Keys] };
                var window = new OverlayWindow(HudOptions.Parse([]), config);
                try
                {
                    window.UpdateSkillInput(gameInFront: true);
                    window.UpdateSkillInput(gameInFront: true); // a second refresh: still nothing
                    return (window.SkillInputCard.Visibility, SkillInputReaderTests.Free(vks), window.SkillInput.Describe(), window.SkillInput.HeldHotkeys.Count);
                }
                finally
                {
                    window.SkillInput.Dispose();
                }
            });

            Assert.All(keysFreeWhileInFront, Assert.True);
            Assert.Equal("off", status);
            Assert.Equal(0, hotkeysHeld);
            Assert.Equal(Visibility.Collapsed, card);
        }
        finally
        {
            SkillInputReaderTests.KeyTurn.Release();
        }
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }
}

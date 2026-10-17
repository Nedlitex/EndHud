using Microsoft.Win32;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// "Start EndHud with Windows": the Run value, Task Manager's off switch, and a moved portable copy.
/// In a throwaway key under HKCU\Software\EndHud-tests, never the real Run key.
/// </summary>
public class AutoStartTests : IDisposable
{
    private readonly string _root = @"Software\EndHud-tests\" + Guid.NewGuid().ToString("N");
    private string Run => _root + @"\Run";
    private string Approved => _root + @"\StartupApproved\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        // The shared parent, once no other test is using it.
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\EndHud-tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            try { Registry.CurrentUser.DeleteSubKey(@"Software\EndHud-tests", throwOnMissingSubKey: false); }
            catch (InvalidOperationException) { } // another test created a child meanwhile
        }
    }

    private AutoStart For(string exe) => new(Run, Approved, exe);

    private string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Run);
        return key?.GetValue(AutoStart.ValueName) as string;
    }

    private void MarkInTaskManager(byte state)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Approved);
        key.SetValue(AutoStart.ValueName, new byte[] { state, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }

    [Fact]
    public void Off_until_turned_on_then_starts_this_exe_quietly()
    {
        var start = For(@"D:\Games\EndHud\EndHud.exe");
        Assert.False(start.Enabled);

        start.Set(true);

        Assert.True(start.Enabled);
        Assert.Equal("\"D:\\Games\\EndHud\\EndHud.exe\" --autostart", RunValue());

        start.Set(false);

        Assert.False(start.Enabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void Turned_off_in_task_manager_counts_as_off_and_turning_it_on_clears_that()
    {
        var start = For(@"D:\EndHud.exe");
        start.Set(true);

        MarkInTaskManager(3); // disabled
        Assert.False(start.Enabled);
        MarkInTaskManager(2); // enabled
        Assert.True(start.Enabled);

        MarkInTaskManager(3);
        start.Set(true);
        Assert.True(start.Enabled);
    }

    [Fact]
    public void A_moved_copy_points_the_entry_at_itself_and_never_turns_it_on()
    {
        For(@"D:\Old\EndHud.exe").Refresh();
        Assert.Null(RunValue()); // off stays off

        For(@"D:\Old\EndHud.exe").Set(true);
        For(@"E:\New\EndHud.exe").Refresh();

        Assert.Equal("\"E:\\New\\EndHud.exe\" --autostart", RunValue());
    }
}

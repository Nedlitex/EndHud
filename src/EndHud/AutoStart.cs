using Microsoft.Win32;

namespace EndHud;

/// <summary>
/// "Start EndHud with Windows" (off unless the player turns it on): a value named EndHud under the
/// current user's Run key, starting this exe with <see cref="Flag"/>, which skips the "EndHud is
/// running" balloon. The registry is the truth, so the menu shows what Windows will actually do:
/// <list type="bullet">
/// <item>Turned off in Task Manager's Startup tab (StartupApproved\Run, first byte odd) counts as off;
/// turning it on in EndHud turns it back on there too.</item>
/// <item>A portable copy that was moved repoints the value to itself when it starts
/// (<see cref="Refresh"/>), so the value never points at a file that's gone.</item>
/// <item>Uninstalling the MSI removes the value (installer/EndHud.wxs).</item>
/// </list>
/// </summary>
internal sealed class AutoStart(string runKey, string approvedKey, string exe)
{
    public const string ValueName = "EndHud";
    public const string Flag = "--autostart";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>For this EndHud.exe, in the real Run key.</summary>
    public static AutoStart ForThisExe() => new(RunKey, ApprovedKey, Environment.ProcessPath ?? "EndHud.exe");

    /// <summary>The command the Run value holds for this exe.</summary>
    public string Command => $"\"{exe}\" {Flag}";

    /// <summary>Whether Windows will start EndHud at sign-in.</summary>
    public bool Enabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(runKey);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(approvedKey);
            // Task Manager's "Disabled": an odd first byte (02 = enabled, 03 = disabled).
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || state[0] % 2 == 0;
        }
    }

    public void Set(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(runKey))
        {
            if (enabled) run.SetValue(ValueName, Command, RegistryValueKind.String);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // Either way, drop a "disabled in Task Manager" mark: on means on.
        using var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        DiagnosticLog.Info(enabled ? $"start with Windows: on ({Command})" : "start with Windows: off");
    }

    /// <summary>When it's on but for another path (a moved portable copy), point it at this exe.</summary>
    public void Refresh()
    {
        using var run = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (run?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
        {
            run.SetValue(ValueName, Command, RegistryValueKind.String);
            DiagnosticLog.Info($"start with Windows: now {Command} (was {current})");
        }
    }
}

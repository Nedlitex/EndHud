using System.Runtime.InteropServices;
using System.Text;

namespace EndHud;

/// <summary>What an installer (MSI) says it is: read from its Property table with Windows Installer (msi.dll), never run.</summary>
/// <param name="UpgradeCode">The product family it upgrades; EndHud's never changes (installer/EndHud.wxs).</param>
internal sealed record MsiIdentity(string? UpgradeCode, string? ProductName, string? ProductVersion)
{
    /// <summary>EndHud's installer for <paramref name="version"/>: EndHud's upgrade code, named EndHud, that version.</summary>
    public bool IsEndHud(Version version) =>
        string.Equals(UpgradeCode, MsiPackage.EndHudUpgradeCode, StringComparison.OrdinalIgnoreCase)
        && ProductName == "EndHud"
        && Version.TryParse(ProductVersion, out var v) && v.Major == version.Major && v.Minor == version.Minor && v.Build == version.Build;
}

/// <summary>
/// Reads an installer's identity before the updater runs it (<see cref="Updater"/>): a file that passed the SHA-256
/// check but is some other product's installer, or another version, is not installed. Windows Installer opens the
/// file read-only; nothing in it runs.
/// </summary>
internal static class MsiPackage
{
    /// <summary>installer/EndHud.wxs: <c>UpgradeCode</c>. Never changes.</summary>
    public const string EndHudUpgradeCode = "{DE14780C-8881-4E6D-A8A7-7227CF00E8EE}";

    /// <summary>The installer's upgrade code, product name and version; each null when the file isn't an installer or lacks it.</summary>
    public static MsiIdentity Read(string path)
    {
        if (MsiOpenDatabaseW(path, IntPtr.Zero /* MSIDBOPEN_READONLY */, out var database) != 0) return new MsiIdentity(null, null, null);
        try
        {
            return new MsiIdentity(Property(database, "UpgradeCode"), Property(database, "ProductName"), Property(database, "ProductVersion"));
        }
        finally
        {
            MsiCloseHandle(database);
        }
    }

    private static string? Property(IntPtr database, string name)
    {
        // The name is one of the three constants above, never outside input.
        if (MsiDatabaseOpenViewW(database, $"SELECT `Value` FROM `Property` WHERE `Property`='{name}'", out var view) != 0) return null;
        try
        {
            if (MsiViewExecute(view, IntPtr.Zero) != 0 || MsiViewFetch(view, out var record) != 0) return null;
            try
            {
                uint size = 0;
                var empty = new StringBuilder(1);
                const uint MoreData = 234;
                if (MsiRecordGetStringW(record, 1, empty, ref size) is not (0 or MoreData)) return null;
                var value = new StringBuilder((int)++size);
                return MsiRecordGetStringW(record, 1, value, ref size) == 0 ? value.ToString() : null;
            }
            finally
            {
                MsiCloseHandle(record);
            }
        }
        finally
        {
            MsiViewClose(view);
            MsiCloseHandle(view);
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string databasePath, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewClose(IntPtr view);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint size);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);
}

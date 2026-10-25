using System.IO;
using System.Runtime.InteropServices;

namespace EndHud.Tests;

/// <summary>
/// A minimal Windows Installer database with only a Property table: enough for <see cref="MsiPackage"/> to read
/// what an installer says it is. It installs nothing (and is never run by the tests).
/// </summary>
internal static class TestMsi
{
    public static byte[] Create(string folder, string? upgradeCode = MsiPackage.EndHudUpgradeCode, string productName = "EndHud", string productVersion = "1.4.0")
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".msi");
        Check(MsiOpenDatabaseW(path, (IntPtr)3 /* MSIDBOPEN_CREATE */, out var database));
        try
        {
            Execute(database, "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL PRIMARY KEY `Property`)");
            if (upgradeCode is not null) Execute(database, $"INSERT INTO `Property` (`Property`, `Value`) VALUES ('UpgradeCode', '{upgradeCode}')");
            Execute(database, $"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductName', '{productName}')");
            Execute(database, $"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductVersion', '{productVersion}')");
            Check(MsiDatabaseCommit(database));
        }
        finally
        {
            MsiCloseHandle(database);
        }
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    private static void Execute(IntPtr database, string sql)
    {
        Check(MsiDatabaseOpenViewW(database, sql, out var view));
        try { Check(MsiViewExecute(view, IntPtr.Zero)); }
        finally
        {
            MsiViewClose(view);
            MsiCloseHandle(view);
        }
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"msi.dll error {result}");
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string databasePath, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewClose(IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiDatabaseCommit(IntPtr database);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);
}

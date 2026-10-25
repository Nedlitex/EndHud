using System.IO;

namespace EndHud;

/// <summary>
/// Every folder and file EndHud keeps the player's data in, derived from one root: <c>%APPDATA%\EndHud</c>
/// (settings, logs, rotations, alerts, the operator library with the learned portraits and the inn's data),
/// plus the update's downloads in <c>%TEMP%\EndHud-update</c>. Nothing else in the app builds these paths.
/// <para>
/// Tests move both into a folder of their own (<see cref="UseRoot"/>), so a test run never writes into the
/// player's real data or log. Deliberately not an environment variable or a command-line option: nothing outside
/// this process can point EndHud at another folder.
/// </para>
/// </summary>
internal static class AppFolders
{
    private static readonly object Lock = new();
    private static string _data = RealData;
    private static string _temp = Path.GetTempPath();

    /// <summary>The player's own data folder: <c>%APPDATA%\EndHud</c>.</summary>
    public static string RealData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EndHud");

    /// <summary>Where the player's data is: <see cref="RealData"/>, except in tests.</summary>
    public static string Data { get { lock (Lock) return _data; } }

    /// <summary>Where EndHud's temporary folders go: the user's %TEMP%, except in tests.</summary>
    public static string Temp { get { lock (Lock) return _temp; } }

    public static string ConfigFile => Path.Combine(Data, "config.json");

    public static string Logs => Path.Combine(Data, "logs");

    public static string Rotations => Path.Combine(Data, "rotations");

    public static string AlertsFile => Path.Combine(Data, "alerts.json");

    /// <summary>The operator library: the operator package (reference/), the learned portraits and calibration.</summary>
    public static string Operators => Path.Combine(Data, "operators");

    /// <summary>The update's downloads and install script (<see cref="Updater.WorkFolder"/>).</summary>
    public static string UpdateWork => Path.Combine(Temp, "EndHud-update");

    /// <summary>
    /// Tests only: keep the data in <paramref name="root"/>\data and the temporary folders in
    /// <paramref name="root"/>\temp, for every path above from now on.
    /// </summary>
    internal static void UseRoot(string root)
    {
        string full = Path.GetFullPath(root);
        lock (Lock)
        {
            _data = Path.Combine(full, "data");
            _temp = Path.Combine(full, "temp");
        }
    }
}

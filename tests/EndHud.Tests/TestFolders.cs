using System.Runtime.CompilerServices;
using EndHud.Vision.Tests;

namespace EndHud.Tests;

/// <summary>
/// Before any test of this assembly runs: the app's data (settings, logs, rotations, alerts, operator library,
/// update downloads) goes to this run's own folder (<see cref="TestRoot"/>), never the player's real
/// <c>%APPDATA%\EndHud</c>. Held by <see cref="TestDataFolderTests"/>.
/// </summary>
internal static class TestFolders
{
#pragma warning disable CA2255 // a module initializer in a test assembly is exactly what this is for
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseTestRoot() => AppFolders.UseRoot(TestRoot.Folder);
}

using System.Runtime.CompilerServices;

namespace EndHud.Vision.Tests;

/// <summary>
/// Before any test of this assembly runs: this run's own folder (<see cref="TestRoot"/>) is created and past
/// runs' folders older than a day are deleted. EndHud.Vision itself never picks a data folder (every store and
/// library is handed its folder, held by <see cref="TestDataFolderTests"/>), so there is nothing else to redirect.
/// </summary>
internal static class TestFolders
{
#pragma warning disable CA2255 // a module initializer in a test assembly is exactly what this is for
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseTestRoot() => _ = TestRoot.Folder;
}

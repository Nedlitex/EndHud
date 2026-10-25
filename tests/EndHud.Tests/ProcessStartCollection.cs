using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Test classes that start processes, or hold an update's files locked with handles a started process inherits
/// (<see cref="Updater"/>: the MSI and its script are locked inheritably, for the script that runs them): one class
/// at a time. Side by side, a process one test starts (PowerShell, cmd.exe) inherits another test's lock and keeps
/// that file locked after the test has let go of it: seen as an update that "couldn't replace" its file.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProcessStartCollection
{
    public const string Name = "Started processes and inheritable update locks, one class at a time";
}

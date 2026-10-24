using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>The 30 fps recording's tests (<see cref="Recordings.ThirtyFps"/>): never side by side with any other test.</summary>
[CollectionDefinition(Recordings.ThirtyFps, DisableParallelization = true)]
public sealed class ThirtyFpsCollection;

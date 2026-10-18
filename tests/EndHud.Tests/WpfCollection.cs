using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Test classes that create WPF objects, each on an STA thread of its own: they run one class at a
/// time, never side by side. WPF's first use isn't safe from several threads at once: on a CI
/// runner the strip's test was inside ItemsControl's static constructor (building ContentPresenter)
/// waiting for the shared XAML schema lock, while the panel's test held that lock waiting for
/// ContentPresenter's static constructor. Neither moved again, and the run was stopped after
/// 10 minutes (the hang dump of run 39087821690).
/// </summary>
[CollectionDefinition(Name)]
public sealed class WpfCollection
{
    public const string Name = "WPF, one class at a time";
}

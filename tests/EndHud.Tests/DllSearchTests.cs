using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// No DLL lying next to EndHud.exe (a portable copy sits in Downloads) and no DOTNET_STARTUP_HOOKS setting can
/// put code into EndHud: the Windows DLLs EndHud calls (hid.dll, xinput1_4.dll, dwmapi.dll, d3d11.dll, dxgi.dll
/// are not KnownDLLs) load from System32 only, Windows searches only System32 for anything loaded by name
/// (<see cref="Program"/>), and .NET startup hooks are off. Read from the built assemblies and their runtime
/// settings; the published exe is checked by release.yml's self-test.
/// </summary>
public class DllSearchTests
{
    private static readonly Assembly[] Ours = [typeof(App).Assembly, typeof(Vision.Rotations.Rotation).Assembly];

    [Fact]
    public void EndHud_and_its_vision_library_load_the_DLLs_they_call_from_System32_only()
    {
        foreach (var assembly in Ours)
        {
            var paths = assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
            Assert.True(paths is not null, $"{assembly.GetName().Name}: no DefaultDllImportSearchPaths");
            Assert.Equal(DllImportSearchPath.System32, paths.Paths);
        }
    }

    [Fact]
    public void Every_DLL_called_is_a_Windows_DLL_in_System32_and_none_asks_for_another_place()
    {
        var calls = Ours.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => (Method: m, Import: m.GetCustomAttribute<DllImportAttribute>()))
            .Where(c => c.Import is not null)
            .ToList();

        Assert.True(calls.Count > 50, $"{calls.Count} DllImports found"); // the check looked at the real ones
        foreach (var (method, import) in calls)
        {
            string dll = import!.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? import.Value : import.Value + ".dll";
            Assert.True(File.Exists(Path.Combine(Environment.SystemDirectory, dll)), $"{method.DeclaringType}.{method.Name}: {dll} is not in System32");
            Assert.Null(method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>());
        }
    }

    [Fact]
    public void Dotnet_startup_hooks_are_off()
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EndHud.runtimeconfig.json")))!;
        Assert.False(config["runtimeOptions"]?["configProperties"]?["System.StartupHookProvider.IsSupported"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public void EndHud_starts_in_its_own_Main_which_restricts_the_DLL_search_first()
    {
        Assert.Equal(typeof(Program), typeof(App).Assembly.EntryPoint?.DeclaringType);
    }
}

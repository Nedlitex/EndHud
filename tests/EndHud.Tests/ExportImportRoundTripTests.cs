using System.IO;
using System.Windows;
using System.Windows.Controls;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using EndHud.Vision.Tests;
using Xunit;
using Rotation = EndHud.Vision.Rotations.Rotation;

namespace EndHud.Tests;

/// <summary>
/// GitHub issue 6 (2026-10-27), a player's report: "exporting the JSON and importing it again gives an error".
/// The editor exported condition steps and attacks by whoever is controlled, then refused them on import as operators
/// missing from the library. Here everything the files can hold (<see cref="EverythingRotation"/>, generated from the
/// condition catalogue so a new kind is covered without a change) and every recommended rotation go out through the
/// editors' own export (what the buttons write) and into a fresh editor and library through its own import, as a file
/// and as a share code, and must come back the same.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class ExportImportRoundTripTests : IDisposable
{
    private const string Everything = "(everything)";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("EndHud.sln");
    }

    /// <summary>The rotation that holds everything, and every file in rotations/ (its subfolders too).</summary>
    public static TheoryData<string> Rotations()
    {
        var data = new TheoryData<string> { Everything };
        foreach (string file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "rotations"), "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(RepositoryRoot(), file));
        // Every rotation file of the frozen backward-compatibility corpus (every format, from 1), enumerated: a file
        // added for a new format is picked up without a change here.
        foreach (string name in CompatCorpus.RotationFiles()) data.Add(CompatSource + name);
        return data;
    }

    /// <summary>A file of the frozen corpus (tests/EndHud.Vision.Tests/Compat), named after this.</summary>
    private const string CompatSource = "compat:";

    /// <summary>A corpus file's reviewed output (its .expected.json); null for any other source.</summary>
    private static string? CompatExpected(string source) =>
        source.StartsWith(CompatSource, StringComparison.Ordinal) ? CompatCorpus.Text(CompatCorpus.ExpectedOf(source[CompatSource.Length..])) : null;

    private static Rotation Load(string source) =>
        source == Everything ? EverythingRotation.Build()
        : source.StartsWith(CompatSource, StringComparison.Ordinal) ? RotationFormat.FromJson(CompatCorpus.Text(source[CompatSource.Length..]))
        : RotationFormat.FromJson(File.ReadAllText(Path.Combine(RepositoryRoot(), source)));

    private static string Said(Window editor) => ((TextBlock)editor.FindName("StatusText")).Text + ((TextBlock)editor.FindName("ProblemsText")).Text;

    /// <summary>
    /// A rotation opened in the editor and exported (what "export to file" writes, and "copy share code" copies), then
    /// imported in another editor with an empty library: imported, and the same rotation, to the last character of its file.
    /// </summary>
    [Theory]
    [MemberData(nameof(Rotations))]
    public void A_rotation_exported_from_the_editor_imports_into_another_unchanged(string source)
    {
        var original = Load(source);
        // First: one the editor would save. Otherwise the editor refuses to export it, and nothing below is about export.
        Assert.Empty(original.Problems());
        var from = new RotationLibrary(Path.Combine(_folder, "from"));
        from.Own.Save(original);

        string? json = null, code = null;
        string said = "";
        OnRotationEditor(from, _ => { }, editor =>
        {
            json = editor.ExportJson();
            code = editor.ExportCode();
            said = Said(editor);
        }, original.Squad);

        Assert.True(json is not null && code is not null, $"the editor refused to export: {said}");
        Assert.Equal(RotationFormat.ToJson(original), json);
        // A corpus file: what goes out is also what was reviewed when it was frozen (a field the reader drops is caught here too).
        if (CompatExpected(source) is { } expected) Assert.Equal(expected, json.ReplaceLineEndings("\n"));
        foreach (var (how, import) in new (string, Action<RotationEditor>)[] { ("file", e => e.ImportJson(json!)), ("code", e => e.ImportCode(code!)) })
        {
            var into = new RotationLibrary(Path.Combine(_folder, how));
            Rotation? changed = null;
            string status = "";
            OnRotationEditor(into, rotation => changed = rotation, editor =>
            {
                import(editor);
                status = Said(editor);
            }, original.Squad);

            Assert.True(status == "Editor.Imported", $"as a {how}: {status}");
            var imported = Assert.Single(into.Own.Load().Rotations);
            Assert.Equal(json, RotationFormat.ToJson(imported));
            Assert.Equal(json, RotationFormat.ToJson(changed!)); // and that is the one the overlay is told about
        }
    }

    /// <summary>The alert rules that hold everything, and every alerts file of the frozen corpus (every format, from 1), enumerated.</summary>
    public static TheoryData<string> AlertSources()
    {
        var data = new TheoryData<string> { Everything };
        foreach (string name in CompatCorpus.AlertFiles()) data.Add(CompatSource + name);
        return data;
    }

    /// <summary>
    /// Alert rules (one per kind offered in alerts, rules tied to a squad, as many as are kept) exported from the alert
    /// editor (what "export to file" writes and "copy all as share code" copies), then imported in another editor with no
    /// rules: all of them added, none skipped, and the same rules in the same order. Only their ids differ: an import
    /// gives every rule a new one (it never replaces a rule).
    /// </summary>
    [Theory]
    [MemberData(nameof(AlertSources))]
    public void Alert_rules_exported_from_the_editor_import_into_another_unchanged(string source)
    {
        var rules = source == Everything ? EverythingRotation.AlertRules() : AlertFormat.FromJson(CompatCorpus.Text(source[CompatSource.Length..]));
        Assert.All(rules, r => Assert.Empty(r.Problems()));
        var from = new AlertStore(Path.Combine(_folder, "from", "alerts.json"));
        from.Save(rules);

        string? json = null, code = null;
        string said = "";
        OnAlertEditor(from, editor =>
        {
            json = editor.ExportJson();
            code = editor.ExportCode();
            said = Said(editor);
        });

        Assert.True(json is not null && code is not null, $"the editor refused to export: {said}");
        Assert.Equal(AlertFormat.ToJson(rules), json);
        if (CompatExpected(source) is { } expected) Assert.Equal(expected, json.ReplaceLineEndings("\n"));
        static AlertRule WithoutId(AlertRule rule) => rule with { Id = "" };
        foreach (var (how, import) in new (string, Action<AlertEditor>)[] { ("file", e => e.ImportJson(json!)), ("code", e => e.ImportCode(code!)) })
        {
            var into = new AlertStore(Path.Combine(_folder, how, "alerts.json"));
            string status = "";
            OnAlertEditor(into, editor =>
            {
                import(editor);
                status = Said(editor);
            });

            Assert.True(status == "Alerts.Imported", $"as a {how}: {status}"); // nothing said about duplicates or the limit
            var imported = into.Load().Rules;
            Assert.Equal(rules.Count, imported.Count);
            Assert.Equal(AlertFormat.ToJson(rules.Select(WithoutId)), AlertFormat.ToJson(imported.Select(WithoutId)));
            Assert.Equal(imported.Count, imported.Select(r => r.Id).Distinct().Count());
            Assert.DoesNotContain(imported, r => rules.Any(o => o.Id == r.Id));
        }
    }

    /// <summary>An operator library with these operators, and the statuses the samples name, without any art: enough for the pickers.</summary>
    private OperatorLibrary Library(IEnumerable<string> operators)
    {
        string reference = Path.Combine(_folder, "reference");
        Directory.CreateDirectory(Path.Combine(reference, "icons"));
        Directory.CreateDirectory(Path.Combine(reference, "statuses"));
        var ids = operators.Distinct().ToList();
        File.WriteAllText(Path.Combine(reference, "operators.json"), System.Text.Json.JsonSerializer.Serialize(ids.Select(id => new OperatorInfo(id, id, id, id + ".png"))));
        byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        foreach (string id in ids) File.WriteAllBytes(Path.Combine(reference, "icons", id + ".png"), pixel);
        string[] statuses = ["Heat Infliction", "Cryo Infliction", "Corrosion", "Vulnerable", "Combustion"];
        File.WriteAllText(Path.Combine(reference, "statuses.json"), System.Text.Json.JsonSerializer.Serialize(statuses.Select(id => new StatusInfo(id, id + ".png"))));
        foreach (string id in statuses) File.WriteAllBytes(Path.Combine(reference, "statuses", id + ".png"), pixel);
        // Decoded here, not on the editor's thread (the image decoder fails on it).
        return OperatorLibrary.LoadAsync(Path.Combine(_folder, "user"), reference).GetAwaiter().GetResult();
    }

    private void OnRotationEditor(RotationLibrary rotations, Action<Rotation?> changed, Action<RotationEditor> act, IEnumerable<string> squad)
    {
        var operators = Library(squad);
        OnStaThread(() => act(new RotationEditor(rotations, operators, [null, null, null, null], Vision.HudLayout.KeyboardMouse, null,
            () => Task.FromResult<RotationSyncResult?>(null), changed)));
    }

    private void OnAlertEditor(AlertStore store, Action<AlertEditor> act)
    {
        var operators = Library(EverythingRotation.Squad);
        OnStaThread(() => act(new AlertEditor(store, operators, () => [], [], () => { })));
    }

    private static void OnStaThread(Action act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                act();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}

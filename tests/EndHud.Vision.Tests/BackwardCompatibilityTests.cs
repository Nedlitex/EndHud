using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Owner, 2026-10-27: "do we have a test to ensure all the rotation files supports are backward compatible?" A frozen
/// corpus (<see cref="CompatCorpus"/>, tests/EndHud.Vision.Tests/Compat/README.md) holds a rotation file of every format
/// from 1 to <see cref="Rotation.CurrentFormat"/> and an alerts file of every format from 1 to
/// <see cref="AlertFile.CurrentFormat"/>: the published rotations as the releases carried them where one exists, else
/// generated once from everything that format can hold. Every one must still load, with the same meaning (written back
/// exactly as its .expected.json, reviewed by eye when it was added), without a problem, and re-save in a format the
/// build that wrote it can read. A new format turns this red until its file is added; a frozen file changed turns it red.
/// A file stamped newer than this build is refused: RotationFormatTests and AlertStoreTests check that already.
/// The editors' own export and import of every corpus file are in EndHud.Tests (ExportImportRoundTripTests).
/// </summary>
public class BackwardCompatibilityTests
{
    public static TheoryData<string> RotationFiles() => Data(CompatCorpus.RotationFiles());

    public static TheoryData<string> AlertFiles() => Data(CompatCorpus.AlertFiles());

    private static TheoryData<string> Data(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (string name in names) data.Add(name);
        return data;
    }

    private const string Readme = CompatCorpus.FolderInRepository + "/README.md";

    /// <summary>The rotation file loads, and writes back exactly what was reviewed when it was frozen: nothing dropped, nothing changed.</summary>
    [Theory]
    [MemberData(nameof(RotationFiles))]
    public void A_frozen_rotation_file_loads_with_the_same_meaning(string name)
    {
        var rotation = RotationFormat.FromJson(CompatCorpus.Text(name));

        Assert.Equal(CompatCorpus.Text(CompatCorpus.ExpectedOf(name)), RotationFormat.ToJson(rotation).ReplaceLineEndings("\n"));
    }

    /// <summary>The alerts file loads, and writes back exactly what was reviewed when it was frozen.</summary>
    [Theory]
    [MemberData(nameof(AlertFiles))]
    public void A_frozen_alerts_file_loads_with_the_same_meaning(string name)
    {
        var rules = AlertFormat.FromJson(CompatCorpus.Text(name));

        Assert.Equal(CompatCorpus.Text(CompatCorpus.ExpectedOf(name)), AlertFormat.ToJson(rules).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Loaded, it has no problem: the editor opens it and the overlay follows it as it always did. (Every corpus file was
    /// without problems when frozen, the published ones too: one that gains a problem is an old file a new check refuses.)
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationFiles))]
    public void A_frozen_rotation_file_has_no_problems(string name)
    {
        Assert.Empty(RotationFormat.FromJson(CompatCorpus.Text(name)).Problems());
    }

    [Theory]
    [MemberData(nameof(AlertFiles))]
    public void A_frozen_alerts_file_has_no_problems(string name)
    {
        Assert.All(AlertFormat.FromJson(CompatCorpus.Text(name)), rule => Assert.Empty(rule.Problems()));
    }

    /// <summary>
    /// Saved again, it is still readable by the build that wrote it: the lowest format that holds it is at most its own.
    /// The exception is format 1 (a fixed sequence of phases): it is converted on reading to phases played in order,
    /// which is format 2, and no build ever wrote format 1 after format 2 existed, so a re-saved one is format 2 exactly.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationFiles))]
    public void A_frozen_rotation_file_saved_again_is_readable_by_the_build_that_wrote_it(string name)
    {
        int format = CompatCorpus.FormatInFile(name);
        var rotation = RotationFormat.FromJson(CompatCorpus.Text(name));
        int needed = Rotation.FormatNeeded(rotation.Phases, rotation.LastMatchWins);

        if (format == 1)
        {
            Assert.Equal(2, needed);
            Assert.Equal(2, rotation.Format);
        }
        else
        {
            Assert.True(needed <= format, $"{name}: format {format}, but saving it again needs format {needed}");
            Assert.Equal(format, rotation.Format);
        }
    }

    /// <summary>Saved again, an alerts file is written in a format at most its own (alerts are always written in the lowest that holds them).</summary>
    [Theory]
    [MemberData(nameof(AlertFiles))]
    public void A_frozen_alerts_file_saved_again_is_readable_by_the_build_that_wrote_it(string name)
    {
        int format = CompatCorpus.FormatInFile(name);
        int needed = AlertFile.FormatNeeded(AlertFormat.FromJson(CompatCorpus.Text(name)));

        Assert.True(needed <= format, $"{name}: format {format}, but saving it again needs format {needed}");
    }

    /// <summary>Each file is named for the format it is stamped with, and has its expected output beside it.</summary>
    [Fact]
    public void Every_frozen_file_is_named_for_its_format_and_has_its_expected_output()
    {
        foreach (string name in CompatCorpus.RotationFiles().Concat(CompatCorpus.AlertFiles()))
        {
            Assert.True(CompatCorpus.FormatInName(name) is { } n && n == CompatCorpus.FormatInFile(name),
                $"{name} is stamped format {CompatCorpus.FormatInFile(name)}: name it rotation-format-NN[-what].json or alerts-format-NN[-what].json with NN its format (see {Readme})");
            Assert.True(File.Exists(Path.Combine(CompatCorpus.Folder, CompatCorpus.ExpectedOf(name))),
                $"{name} has no {CompatCorpus.ExpectedOf(name)} beside it (see {Readme})");
        }
    }

    /// <summary>
    /// A frozen file for every format there is. Adding a format (raising <see cref="Rotation.CurrentFormat"/> or
    /// <see cref="AlertFile.CurrentFormat"/>) turns this red until its file is added; the message says what to add, and a
    /// candidate is written to review (the everything rotation or alert rules, which hold the newest format).
    /// </summary>
    [Fact]
    public void Every_format_has_a_frozen_file()
    {
        var missing = new List<string>();
        missing.AddRange(Missing(CompatCorpus.RotationFiles(), CompatCorpus.RotationPrefix, Rotation.CurrentFormat, "Rotation.CurrentFormat",
            () => RotationFormat.ToJson(EverythingRotation.Build()), json => RotationFormat.ToJson(RotationFormat.FromJson(json))));
        missing.AddRange(Missing(CompatCorpus.AlertFiles(), CompatCorpus.AlertPrefix, AlertFile.CurrentFormat, "AlertFile.CurrentFormat",
            () => AlertFormat.ToJson(EverythingRotation.AlertRules()), json => AlertFormat.ToJson(AlertFormat.FromJson(json))));

        Assert.True(missing.Count == 0, string.Join("\n\n", missing));
    }

    private static IEnumerable<string> Missing(IReadOnlyList<string> files, string prefix, int current, string constant, Func<string> newest, Func<string, string> reload)
    {
        var have = files.Select(CompatCorpus.FormatInName).OfType<int>().ToHashSet();
        for (int n = 1; n <= current; n++)
        {
            if (have.Contains(n)) continue;
            string name = $"{prefix}{n:00}.json";
            if (n < current)
            {
                yield return $"No frozen file for format {n} ({CompatCorpus.FolderInRepository}/{name}). Frozen files are never removed: "
                    + $"restore it from git (git checkout HEAD -- {CompatCorpus.FolderInRepository}/{name} and its .expected.json). See {Readme}.";
                continue;
            }
            // The newest format: write a candidate to review, never into the corpus itself.
            string folder = Path.Combine(TestRoot.Folder, "compat-candidates");
            Directory.CreateDirectory(folder);
            string json = newest();
            File.WriteAllText(Path.Combine(folder, name), json);
            File.WriteAllText(Path.Combine(folder, CompatCorpus.ExpectedOf(name)), reload(json));
            yield return $"{constant} is {n}, and {CompatCorpus.FolderInRepository} has no {name}. Add the format's frozen file: "
                + $"a candidate holding everything format {n} can hold is in {folder} ({name} and {CompatCorpus.ExpectedOf(name)}). "
                + $"Review it, copy both into {CompatCorpus.FolderInRepository}, add their two lines to {CompatCorpus.ManifestName} "
                + $"(the manifest test prints them), and note where it came from in the README. See {Readme}, \"Adding a new format\".";
        }
    }

    /// <summary>
    /// The frozen files are byte for byte what was committed (line ends aside): a file edited, removed or added without its
    /// line in MANIFEST.sha256 fails here. Frozen files are never changed, only added.
    /// </summary>
    [Fact]
    public void The_frozen_files_are_unchanged()
    {
        var manifest = CompatCorpus.Manifest();
        var files = CompatCorpus.FrozenFiles();
        var wrong = new List<string>();
        foreach (string name in files)
        {
            string hash = CompatCorpus.Hash(Path.Combine(CompatCorpus.Folder, name));
            if (!manifest.TryGetValue(name, out string? frozen))
                wrong.Add($"{name} is new: if it is a frozen file being added, add this line to {CompatCorpus.ManifestName}:\n{hash}  {name}");
            else if (frozen != hash)
                wrong.Add($"{name} was changed. Frozen files must never be changed, only added: restore it "
                    + $"(git checkout HEAD -- {CompatCorpus.FolderInRepository}/{name}). If loading an old file now gives something else, "
                    + "that is the bug this corpus exists to catch; fix the loader, not the file.");
        }
        foreach (string name in manifest.Keys.Where(name => !files.Contains(name)))
            wrong.Add($"{name} was removed. Frozen files must never be removed, only added: restore it (git checkout HEAD -- {CompatCorpus.FolderInRepository}/{name}).");

        Assert.True(wrong.Count == 0, string.Join("\n\n", wrong) + $"\n\nSee {Readme}.");
    }
}

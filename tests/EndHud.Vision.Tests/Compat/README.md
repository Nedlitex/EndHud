# Backward-compatibility corpus (frozen)

Rotation and alert files of **every format EndHud has ever written**, kept exactly as they were written, so that
every new build is checked against them: a player's old rotation or alerts file must still load, with the same
meaning.

For each file `X.json` there is `X.expected.json`: what loading `X.json` today writes back
(`RotationFormat.ToJson(RotationFormat.FromJson(...))`, or `AlertFormat.ToJson(AlertFormat.FromJson(...))` for alerts).
Each expected file was read by eye when it was added, to confirm that nothing in the input was dropped or changed in
meaning; only defaults may appear (for example `"note": null`, `"withPrevious": false`, `"expect": []`,
`"minStacks": 1`).

## The rule: never edit, only add

Every `.json` file here is frozen. `MANIFEST.sha256` holds the SHA-256 of each one (Windows line endings are read
as Unix ones, so a checkout's line endings do not matter), and a test fails if a file is changed, removed, or added
without its line.

- **Never change or remove a file here**, and never change a line of `MANIFEST.sha256`. If an old file no longer
  loads the way its `.expected.json` says, the loader has a bug: fix the loader, not the file. A change in what an
  old file means is a decision for the owner, not something to make a test pass.
- **Only add.** A new file needs its `.expected.json` and its two lines in `MANIFEST.sha256`.

## What the tests check

`BackwardCompatibilityTests` (in `tests/EndHud.Vision.Tests`), for every file:

1. it loads, and writes back exactly its `.expected.json`;
2. it has no problems (the editor opens it and the overlay follows it, as before);
3. saved again, it needs no newer format than its own, so the build that wrote it can still read it. Format 1 is
   the exception: a fixed sequence of phases is converted on reading into phases played in order, which is
   format 2 (no build wrote format 1 once format 2 existed);
4. it is named for the format it is stamped with, and has its `.expected.json`;
5. there is a file for every format from 1 to `Rotation.CurrentFormat` (rotations) and from 1 to
   `AlertFile.CurrentFormat` (alerts) (`Every_format_has_a_frozen_file`);
6. the files match `MANIFEST.sha256` (`The_frozen_files_are_unchanged`).

`ExportImportRoundTripTests` (in `tests/EndHud.Tests`) also opens every file in the rotation or alert editor,
exports it as a file and as a share code, imports it into a fresh editor, and checks it comes back the same.

A file stamped with a format newer than the build is refused as "newer": `RotationFormatTests` and
`AlertStoreTests` check that.

## Adding a new format

When `Rotation.CurrentFormat` or `AlertFile.CurrentFormat` goes up, `Every_format_has_a_frozen_file` fails until
the new format has its file. Its message names the file to add, and it writes a candidate for you to review: the
"everything" rotation or alert rules (`tests/EndHud.Vision.Tests/Support/EverythingRotation.cs`, which holds every
condition kind and every step action, and is checked to be in the newest format), with its expected output.

1. Run the vision tests' `BackwardCompatibilityTests`. Take `rotation-format-NN.json` (or `alerts-format-NN.json`)
   and its `.expected.json` from the folder the failure message names.
2. Read the candidate: it must use the new format's feature.
3. Copy both files here.
4. Run the tests again: `The_frozen_files_are_unchanged` prints the two lines to add to `MANIFEST.sha256`. Add them.
5. Add a row to the table below.

When a real file of a format is published (a recommended rotation in `rotations/`), it may be added too, under
`rotation-format-NN-<what>.json`, from the release that carried it (`git show <tag>:rotations/<file>`).

## Where each file came from

**Authentic** files are published recommended rotations, byte for byte as the repository held them (the git blob
is the same). **Generated** files were written once, on 2026-10-27, by the build of that day (format 14 rotations,
format 8 alerts), from the "everything" rotation and alert rules restricted to what format N can hold: every condition
kind whose format is at most N (`ConditionCatalogue`), every step action whose format is at most N
(`StepActions.FormatNeeded`), priorities from 6, finish conditions from 10, condition steps in a group from 12,
"the last phase that holds wins" from 3, alert rules tied to a squad from 8; then stamped N (the lowest format that
holds each is N). Generated files show today's writer's layout; the authentic ones show how old writers laid
files out.

No file here comes from a player or from anyone's computer: only from this repository's history.

| File | Format | Kind | Source |
|---|---|---|---|
| `rotation-format-01.json` | 1 | authentic | `rotations/laevatain-standard.json` at commit 3c5c849 (2026-10-13, published on main through the recommended-rotation sync; removed in f69b262). No release tag carries a format-1 file. A sequence playing one phase twice. |
| `rotation-format-01-laevatain-charge.json` | 1 | authentic | `rotations/laevatain-arcane-ardelia-camille.json` at commit 068643a (2026-10-13, published on main; converted to format 2 in a59b5f6). |
| `rotation-format-02.json` | 2 | authentic | `rotations/typhoeus-arcane-perlica-liino.json` at tag v0.2.0 (unchanged through v0.7.3). |
| `rotation-format-02-laevatain.json` | 2 | authentic | `rotations/laevatain-arcane-ardelia-camille.json` at tag v0.2.0 (unchanged through v0.7.3). |
| `rotation-format-02-zhuang-fangyi.json` | 2 | authentic | `rotations/zhuang-fangyi-liino-arcane-perlica.json` at tag v0.2.1 (unchanged through v0.7.3). |
| `rotation-format-02-generated.json` | 2 | generated | everything format 2 can hold. |
| `rotation-format-03.json` … `rotation-format-08.json` | 3–8 | generated | everything each format can hold. |
| `rotation-format-09.json` | 9 | authentic | `rotations/next-release/si-last-rite-arcane-tangtang.json` at tag v0.7.0 (`rotations/` from v0.7.1, unchanged through v0.7.3). |
| `rotation-format-09-generated.json` | 9 | generated | everything format 9 can hold. |
| `rotation-format-10.json`, `rotation-format-11.json` | 10, 11 | generated | everything each format can hold. |
| `rotation-format-12.json` | 12 | authentic | `rotations/next-release/camille-laevatain-si-arcane.json` at tag v0.7.0 (`rotations/` from v0.7.1, unchanged through v0.7.3). |
| `rotation-format-12-generated.json` | 12 | generated | everything format 12 can hold. |
| `rotation-format-13.json`, `rotation-format-14.json` | 13, 14 | generated | everything each format can hold. |
| `alerts-format-01.json` … `alerts-format-08.json` | 1–8 | generated | everything each alerts format can hold. No alerts file was ever published, so none is authentic. |

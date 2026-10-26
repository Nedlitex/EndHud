# Recommended rotations

Every `<id>.json` here is published to all EndHud players: EndHud checks this folder on `main`
daily (with its update check) and downloads new or changed files, so a rotation is added or
fixed by committing a file, without a new release.

- Make the rotation in EndHud's rotation editor, then "export file".
- Give it a short id of lowercase letters, digits and dashes (e.g. `<squad>-standard`); set
  that as the file's `"id"` and name the file `<id>.json`.
- Changing the file updates it for everyone. Deleting it removes it for everyone.
- Players can't edit a published rotation in place: their changes are saved as their own copy,
  and this one keeps updating. A player who deletes one doesn't get it back until they press
  "restore recommended rotations" in the editor.

| File | Squad | Source |
|---|---|---|
| `laevatain-arcane-ardelia-camille.json` | 莱万汀, 诀 (李织烟), 艾尔黛拉 (小羊), 卡缪 | a Bilibili guide's rotation charts (充能轴 I and II, the guide's 一阶段 and 二阶段); its 重击 steps are left out, since EndHud tracks only battle skills, combos and ultimates |
| `si-last-rite-arcane-tangtang.json` | 祀, 别礼, 诀 (李织烟), 汤汤 | the owner's rotation charts (充能轴 I, 充能轴 II, 爆发轴 IV), phases approved by the owner on 2026-10-24; format 9 (read from v0.7.0) |
| `camille-laevatain-si-arcane.json` | 卡缪, 莱万汀, 祀, 诀 (李织烟) | the owner's rotation charts (充能轴 I, 充能轴 II, 爆发轴 III) and the owner's notes of 2026-10-25 (技力, 熔火 = 4, 强化战技 with 连击 ≥ 1, the burst's ultimate order); format 12 (read from v0.7.0) |

`dotnet test` checks every file here: it must be readable by this build, its id must match its file
name, and every operator in it must exist. It must also be readable by the **released** EndHud (format 13
or less since v0.7.0), since every installed copy downloads it; older builds skip a file newer than they read.

## Waiting for a release: `next-release/`

A rotation that needs a newer format than released builds read waits in [`next-release/`](next-release).
The sync lists only this folder's own files, never a subfolder, so nobody downloads it yet. `dotnet test`
checks it is valid for the current build (readable, id matching, no problems, saved in the format it
needs, operators known). Once a release that reads its format is out, raise the limit in
`Published_rotations_stay_readable_by_released_builds` to that format and move the file up here.

Nothing is waiting now: the two that waited here were published with v0.7.0.

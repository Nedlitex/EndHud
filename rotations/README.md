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
| `laevatain-arcane-ardelia-camille.json` | 莱万汀, 诀 (李织烟), 艾尔黛拉 (小羊), 卡缪 | a Bilibili guide's rotation charts (充能轴 一阶段 / 二阶段); its 重击 steps are left out, since EndHud tracks only battle skills, combos and ultimates |

`dotnet test` checks every file here: it must be readable by this build, its id must match its file
name, and every operator in it must exist.

# EndHud: developer guide

How EndHud works, and how to build, test and release it. The user guide, in Chinese, is
[README.md](README.md).

**An external HUD overlay for *Arknights: Endfield*.** EndHud runs as its own Windows tray app
and draws extra information over the game. Its main feature is a **combo-ready hint**: a banner
telling you when an operator's combo skill comes off cooldown.

It gets everything by **looking at the screen**, the same way OBS's window capture does. It
never opens, reads, writes or injects into the game process, never hooks the game, and never
sends input to it.

> ⚠️ Using any third-party tool with an online game is at your own risk. The game uses the ACE
> anti-cheat, and Gryphline's terms may restrict third-party software. EndHud is designed to
> stay outside the game process, but that is not a guarantee of anything.

---

## What it shows

### Combo-ready hint

In combat, when an operator's combo skill comes off cooldown, a banner slides in showing that
operator's avatar and combo skill icon (both from the downloaded operator library), their name and
"combo ready". It's styled after the game's own combo banner, slides in from the left with its gold
edge flaring, slides out when it ends, and sits left of the screen's centre by default. An operator
EndHud hasn't recognised gets a banner with just their slot number.

- **Two modes** (menu → combo-ready hint section): show for a few seconds when the cooldown
  finishes (2, 3 or 5 s; default 3 s), or show for as long as the combo skill stays ready.
- **Move it**: open the menu (Ctrl+Shift+M) and drag the banner. "Reset position" puts it back.
  The spot is saved relative to the game window, so it survives resizing.
- A cooldown refreshed early by a skill (the bar jumps straight to ready) counts the same as one
  that refilled. A cooldown that finishes during an ultimate cutscene is hinted when the HUD
  comes back.
- No hint out of combat, for a fallen operator, or for a combo skill that is simply ready when a
  fight starts.

### Rotation strip (排轴)

A rotation is the order a squad's skills are used in: battle skills, combos and ultimates (basic
attacks aren't tracked). In combat, a strip at the top centre of the screen shows the rotation for
the recognised squad, styled after community rotation charts, and highlights the step to use now.

- **One per squad, any slot order.** The strip shows the rotation chosen for those four operators
  in the editor (or the first one made for them).
- **Built from phases.** Reusable phases (e.g. 充能轴, 爆发轴) are played in a sequence (e.g.
  充能轴 ×2, then 爆发轴 ×1) that loops.
- **No start or reset.** Each step can say what the game should look like when it's that step's
  turn (its *expected state*). EndHud picks the step that matches the screen best, then moves on
  when it sees the step's skill used. See [Rotations](#rotations).
- **When to press it.** The current step lights up when its expected state holds and its skill is
  available; otherwise a line under it says what's missing ("技力 1.4/2", "敌人缺少：灼热附着").
- **Move it**: open the menu and drag the strip; "reset position" puts it back.
- **Recommended rotations** come from the repository's [`rotations/`](rotations) folder and are
  kept up to date daily, without a release (see [Recommended rotations](#recommended-rotations)).
- **Edit and share** in the rotation editor (menu → 排轴 → 编辑排轴…, or the tray): phases,
  steps, expected states picked from lists, a live preview, and import/export as a file or a share
  code (`EHR1:` + compressed JSON, short enough for a chat message).

### Debug panel

Everything EndHud reads, for checking it (the "debug panel" checkbox in the menu, the tray, or
Ctrl+Shift+D). Off at every start: it shows until EndHud quits, and isn't saved. It lists, per squad slot:

```
ENDHUD
Tracking: <game window title>
Squad
<operator>  level 90  HP 83%
    combo ready        ultimate charging 44%
<operator>  level 90  HP 100%
    combo cooling 37%  ultimate ready
<operator>  fallen
…
```

(Shown in Chinese, like the rest of the interface.)

| Field | Where it's read from | How |
|---|---|---|
| Operator name | squad portraits, bottom-left | compared with every operator's wiki avatar by a character recognition model (see [Recognising operators](#recognising-operators)) |
| Level | "LV.90" under each portrait (outside combat) | Windows' built-in OCR |
| HP | HP bar under each portrait | how much of the bar is coloured |
| Combo skill | thin bar above each HP bar (in combat) | grey refill → "cooling NN%", white → "ready" |
| Ultimate | small circle above each battle skill button, bottom-right | coloured arc growing clockwise from 12 o'clock → "charging NN%"; full colour + icon → "ready" |
| Fallen | the "⊘" on a hatched square that replaces a fallen operator's portrait | ring shape + empty HP bar; stays fallen until clearly revived |

The whole interface is in Chinese. Strings live in [`src/EndHud/i18n/zh-CN.xaml`](src/EndHud/i18n/zh-CN.xaml).

---

## Requirements

- Windows 10 2004 or later, or Windows 11 (EndHud uses Windows.Graphics.Capture and Windows OCR).
- The game in **borderless windowed** mode. In exclusive fullscreen nothing can draw on top of it.
- An installed Windows OCR language: English or Chinese. Every Chinese or English Windows has one.
- To build: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build and run

```powershell
dotnet build -c Release
dotnet run --project src/EndHud -c Release
```

Or produce a single `EndHud.exe` with everything inside it (the operator library is still downloaded on first start):

```powershell
dotnet publish src/EndHud -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

That needs the .NET 8 Desktop Runtime on the machine. Add `--self-contained true` to bundle the
runtime too, which makes a larger exe.

## Releases

Push a version tag and GitHub builds a release:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

[`.github/workflows/release.yml`](.github/workflows/release.yml) runs the tests, builds the two
files below, and checks them on a clean Windows machine before publishing anything: it installs
the MSI, runs the installed EndHud's self-test (download the operators from the wiki, name the
squad in a test screenshot), does the same with the portable exe, and uninstalls, checking nothing
is left behind. On another clean machine it installs an old EndHud (0.0.1, built from the same
code) and lets it update itself to the new release, as "update now" does, from a stand-in for
GitHub serving the new files: the installed copy (then exactly one EndHud, at the new version, is
registered with Windows, and its self-test passes) and the portable exe. Release versions must
therefore be above 0.0.1. Only then are the files attached to a new release on the repository's Releases
page:

- `EndHud-1.2.0.msi`: the installer. It installs for the current user (no administrator prompt),
  adds a Start menu shortcut, and upgrades an older version in place. See
  [`installer/README.md`](installer/README.md).
- `EndHud-1.2.0-portable.exe`: the same program as one file, to run without installing.

Both include .NET, so nothing else needs installing. Tags must look like `v1.2.0`. "Run workflow"
on the Actions tab does a dry run: it builds and checks both files and attaches them (and the
self-test logs) to the run without creating a release.

The self-test is part of EndHud itself and can be run on any machine; it never touches your own
EndHud data:

```powershell
./installer/self-test.ps1 -Exe "$env:LOCALAPPDATA\Programs\EndHud\EndHud.exe" -Folder "$env:TEMP\endhud-self-test" `
    -Screenshot tests/EndHud.Vision.Tests/Fixtures/exploration-levels-visible.png -Squad Laevatain,Camille,Arcane,Ardelia
```

It runs `EndHud.exe --self-test <folder> [--screenshot <image> --expect <ids>]` (no window), which
downloads the operator library into `<folder>` as on first start, loads it, and names the squad in
the screenshot; the steps are logged to `<folder>\self-test.log`, and the exit code is 0 when all
passed. The files aren't code-signed yet, so Windows SmartScreen warns about an
unrecognised app the first time.

---

## Using it

EndHud starts in the **system tray**, and the HUD appears whenever the game window has focus.

| Hotkey | Action |
|---|---|
| **Ctrl+Shift+M** | Open/close the menu: settings, and correcting operator names |
| **Ctrl+Shift+H** | Show/hide the HUD |
| **Ctrl+Shift+D** | Show/hide the debug panel |
| **Ctrl+Shift+Q** | Quit |
| **Esc** | Close the menu |

Right-click the tray icon for: status, show HUD, open menu, auto-show on/off, debug panel on/off,
**check for updates**, open the config folder, and quit.

### Updates

- **A newer EndHud.** [`UpdateService`](src/EndHud/UpdateService.cs) asks GitHub for the
  repository's latest published release (`/releases/latest`: drafts and pre-releases don't count)
  a minute after start and then daily, when `CheckForUpdates` is on (the default; a menu checkbox).
  Development builds (version 0.0.0, a plain `dotnet build`) don't check. GitHub answers "not
  found" while the repository is private, which EndHud reports as "can't check". A manual check
  reuses an answer under a minute old (GitHub allows 60 unauthenticated requests an hour).
- **Updating in one click.** A newer version shows a notification ("click to update now") and an
  "update now to x.y.z" tray item, plus one for the release notes. Nothing changes until the player
  clicks; then [`Updater`](src/EndHud/Updater.cs):
  1. downloads the release's file for this install and checks its SHA-256 against the one GitHub
     reports for it (the `digest` of each release asset); a mismatch is deleted, never run, and a
     file without a checksum isn't downloaded at all. HTTPS only.
  2. **Installed with the MSI** (in `%LocalAppData%\Programs\EndHud`): EndHud exits and a hidden
     script runs the new MSI quietly (per-user, no administrator prompt), then starts EndHud
     again, which says "updated to x.y.z". Windows' installed-apps list stays right. If the
     installer fails, the old version starts again and says so (the installer's log is kept in
     `%TEMP%\EndHud-update`).
  3. **Portable exe:** renames itself to `EndHud.exe.old` (Windows allows renaming a running
     program), moves the new exe into its place and starts it; the next start deletes the .old
     file. In a folder EndHud can't write to, it says so and nothing changes.

  Settings, learned portraits and the operator library are in `%APPDATA%\EndHud`, which neither
  touches. On any failure before the hand-over nothing has changed and the notification links to
  the releases page.
- **New operators.** The weekly background refresh announces operators that weren't there before.
- **Check for updates** (tray) does both at once and shows the result in a notification. It asks
  the wiki at most every 15 minutes; within that it reports the last result.

### Settings

`%APPDATA%\EndHud\config.json` is created on first start. The menu changes it for you; if you
edit it by hand, restart EndHud. Delete the file to reset.

| Key | Default | Meaning |
|---|---|---|
| `CheckForUpdates` | `true` | Check GitHub a minute after start and then daily: for a newer EndHud (a notification; nothing is installed) and for changed recommended rotations (downloaded). |
| `AutoShowOnGameFocus` | `true` | Show the HUD when the game has focus and hide it otherwise. When `false`, the HUD only shows after Ctrl+Shift+H. |
| `PartyOcrEnabled` | `true` | Read the squad panel (names, HP, skills, levels). |
| `OcrIntervalMs` | `250` | How often the screen is read, in milliseconds (minimum 250). Rotations need it fast: skill points are spent and refilled quickly. |
| `SettingsVersion` | current | The file's version, to update old defaults the player never chose: a file from before version 2 with `OcrIntervalMs` 1000 (the old default) gets 250. |
| `Language` | `zh-CN` | UI language: picks `i18n/<Language>.xaml`. |
| `ComboHintEnabled` | `true` | Show the combo-ready hint (needs `PartyOcrEnabled`). |
| `ComboHintMode` | `WhenReady` | `WhenReady`: for `ComboHintSeconds` after the cooldown finishes. `WhileReady`: until the combo skill is used. |
| `ComboHintSeconds` | `3` | How long a `WhenReady` hint stays up. |
| `ComboHintX`, `ComboHintY` | `0.2`, `0.36` | Where the hint sits: its top-left corner as a fraction of the game window's width and height. |
| `RotationEnabled` | `true` | Show the rotation strip when the recognised squad has a rotation. |
| `RotationX`, `RotationY` | `0.3`, `0.13` | Where the strip sits (top-left corner, as a fraction of the game window). |
| `ActiveRotations` | `{}` | The rotation chosen for each squad: squad key (the four operator ids, sorted, joined by `\|`) → rotation id. Set by saving in the editor. |

### Command-line options

| Option | Purpose |
|---|---|
| `--target "<title text>"` | Track any window whose title contains this text instead of the game. Useful to try EndHud without the game, for example over a video of gameplay. |
| `--hide-from-capture` | Keep the HUD out of screenshots and recordings. |
| `--self-test <folder>` | Check this build without a window (see [Releases](#releases)). |
| `--update-now [--from <api>]` | Update in place if a newer release is out, without a window or restarting EndHud ("update now" for scripts). Exit code 0 started, 3 up to date, 1 failed; log in `%TEMP%\EndHud-update\update-now.log`. `--from` asks a stand-in for GitHub (plain HTTP only on this computer). |

EndHud finds the game as a Unity window whose title contains "Endfield" or the game's Chinese title.

---

## Recognising operators

The game shows no operator names in the squad panel, so EndHud identifies each portrait.

1. **References, downloaded.** Every operator's avatar, their battle skill, combo and ultimate
   icons and Chinese name come from [endfield.wiki.gg](https://endfield.wiki.gg/wiki/Operator).
   They're the game's art, so **EndHud ships none of it**: the first start downloads them (about
   1 MB) into your user data, for the MSI and the portable exe alike. A copy older than 7 days is
   refreshed in the background, which picks up new operators; the tray's **check for updates**
   refreshes it now. Without internet on the first start, slots show "slot n" and the download is
   retried (after 1, 5, 15 and then every 30 minutes); the menu says so.
2. **Recognised out of the box.** The squad panel shows the game's 3D model; the wiki avatar is a
   drawing. Comparing their pixels doesn't work (it got 0 of 60 slots right on the test frames),
   so EndHud uses a model trained to tell anime characters apart across different art: CCIP,
   compiled in (38 MB, not game art; see [tools/ccip-model](tools/ccip-model/README.md) and
   [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). In
   [`SquadIdentifier`](src/EndHud.Vision/Operators/SquadIdentifier.cs):
   - Each portrait is compared with two references per operator, and the closer one counts:
     - **The operator's in-game portrait**, built in
       ([`PortraitReferences`](src/EndHud.Vision/Operators/PortraitReferences.cs)): the model's
       description of each operator's squad portrait, made from 4K screenshots of nine squads
       covering all 32 operators ([the squad fixtures](tests/EndHud.Vision.Tests/Fixtures/squads/README.md)),
       at the common portrait sizes. Numbers only (1.3 MB); no image is shipped. Same art as the
       screen, so the most reliable.
     - **The wiki avatar**, so an operator with no in-game reference yet (a new one) is still
       recognised. To add one: put a screenshot with them in the squad into the squad fixtures,
       name them in `squads.json`, and run `endhud-cli build-portrait-references` (see that folder's README).
   - Each wiki avatar is shrunk to the portrait's size on your screen before comparing, so both
     are equally sharp. The model describes the avatars once per screen size (about 8 s, in the
     background, on first use) and caches that in `features/`.
   - One frame can mislead (the HUD dims, the low-HP tint, a small screen), so each slot keeps its
     last 6 comparisons and is named after the operator closest over all of them, once it has 3.
     A slot shows "slot n" until then: a few seconds after the squad panel first appears.
   - A portrait unlike the slot's last one is another operator (squad changed): the slot starts
     over rather than mixing the two.
   - The model takes about 0.2 s per portrait on two CPU threads, so each reading runs it at most
     once (for the slot that needs it most), and not at all once every slot is settled, unless a
     portrait changes.

   On the recordings (held out: none of their frames made the references), every slot is named
   right, with no wrong name at any reading, on all 16 simulated screens from 1280×720 to 5K;
   also with the wiki avatars alone, as a new operator would be.
3. **Correcting it.** If a name is ever wrong, the menu (Ctrl+Shift+M) has a dropdown per slot,
   sorted by most likely operator first. Picking a name saves that in-game portrait, and from then
   on it's recognised from that portrait first. EndHud never learns from its own answers, so a
   wrong one can't make itself permanent.
4. **Correcting and clearing.** Picking a different name later removes the wrong learned
   portraits and saves the right one. "Forget all learned portraits" clears everything learned.

Your own data lives in `%APPDATA%\EndHud\operators\`:

| Path | Contents |
|---|---|
| `reference/` | the roster, avatars and skill icons downloaded from the wiki (`wiki-sha1.json` records the wiki's checksums, so unchanged files aren't downloaded again) |
| `learned/<operator>/*.png` | portraits learned from your picks |
| `features/*.json` | the avatars as the recognition model describes them, per screen size (a cache: safe to delete) |

How a download stays polite to the wiki, which volunteers run
([`WikiPoliteness`](src/EndHud.Vision/Operators/WikiPoliteness.cs)):

- One request at a time from the whole process, with a pause between them: 1 s between API calls,
  0.1 s between images (served from a CDN). A full first download is about 10 API calls and 133
  images, so about 25 s; a weekly refresh downloads only images that changed.
- API calls send `maxlag=5`: when the wiki's database is behind, it answers "busy" instead of doing
  the work, and EndHud waits (Retry-After, up to 60 s) and retries, giving up after three tries
  until the next scheduled check. Busy HTTP answers (429, 5xx) are treated the same.
- How often: on first start, then weekly in the background, plus manual checks at most every 15
  minutes. Requests name EndHud and its repository in the User-Agent.

How a download stays safe ([`OperatorReference`](src/EndHud.Vision/Operators/OperatorReference.cs),
[`WikiOperatorSource`](src/EndHud.Vision/Operators/WikiOperatorSource.cs)):

- New operators need nothing: the roster is whatever the wiki's Operator page lists.
- Requests are retried when the wiki is busy or the connection drops (see above).
- An image that fails, or isn't a valid image, keeps the previous copy's file; the rest still updates.
- The new copy is downloaded beside the current one and swapped in with two renames; an
  interrupted swap is undone on the next start. If the wiki lists far fewer operators than the
  current copy (under 80%), the current copy is kept: operators don't leave the game.

Si and Argent Flow have no skill icons on the wiki yet; they're recognised by portrait only.

---

## Rotations

### The file

A rotation is a JSON file in `%APPDATA%\EndHud\rotations\<id>.json`
([`Rotation`](src/EndHud.Vision/Rotations/Rotation.cs),
[`RotationFormat`](src/EndHud.Vision/Rotations/RotationStore.cs)):

```json
{
  "format": 1,
  "id": "6f1c…",
  "name": "标准轴",
  "squad": ["Laevatain", "Camille", "Arcane", "Ardelia"],
  "phases": [
    {
      "id": "charge",
      "name": "充能轴",
      "steps": [
        { "operatorId": "Ardelia", "action": "BattleSkill",
          "expect": [ { "kind": "skillPoints", "is": "AtLeast", "value": 2 } ] },
        { "operatorId": "Camille", "action": "Combo" },
        { "operatorId": "Arcane", "action": "Combo", "withPrevious": true },
        { "operatorId": "Laevatain", "action": "BattleSkill", "label": "战技·意", "note": "飞天",
          "expect": [ { "kind": "bossStatus", "statusId": "Heat Infliction", "present": true },
                      { "kind": "counter", "operatorId": "Laevatain", "counterId": "melting-flame", "is": "AtMost", "value": 3 } ] }
      ]
    },
    { "id": "burst", "name": "爆发轴", "steps": [ { "operatorId": "Laevatain", "action": "Ultimate" } ] }
  ],
  "sequence": [ { "phaseId": "charge", "times": 2 }, { "phaseId": "burst", "times": 1 } ]
}
```

- `squad`: operator ids from the operator library, in any slot order.
- `action`: `BattleSkill`, `Combo` or `Ultimate`. The skill being available (a skill point for a
  battle skill, the combo or ultimate ready) is implied; `expect` lists anything else.
- `withPrevious`: used right after the previous step, drawn overlapping it.
- `expect` conditions (`kind`):

  | `kind` | Fields | Holds when |
  |---|---|---|
  | `skillPoints` | `is` (`AtLeast`, `AtMost`, `Exactly`), `value` (0–3, halves allowed) | the skill point bar compares so |
  | `combo` | `operatorId`, `ready` | that operator's combo is ready (`true`) or cooling (`false`) |
  | `ultimate` | `operatorId`, `ready` | that operator's ultimate is ready or charging |
  | `counter` | `operatorId`, `counterId`, `is`, `value` | an operator's own resource compares so (see below) |
  | `bossStatus` | `statusId` (the wiki's English name), `present`, `minStacks` | the enemy shows (or doesn't show) that status |
  | `controlling` | `operatorId` | that operator is the one being controlled |

- `format`: a file from a newer EndHud is refused with a message instead of being misread.

The store ([`RotationStore`](src/EndHud.Vision/Rotations/RotationStore.cs)) covers every way an
entry changes:

| | How |
|---|---|
| Create | the editor's "new" or "copy" (a new id), or an import |
| Read | every `*.json` in the folder; a file that can't be read is listed in the editor with why, never dropped silently |
| Correct | edit and save in the editor: the file is rewritten under the same id |
| Remove | the editor's "delete" (asks first): the file is deleted |
| Import | never overwrites: when the id exists the player picks replace or keep both (a copy under a new id) |

### Recommended rotations

Every `<id>.json` in [`rotations/`](rotations) on `main` is published to every player
([`RotationSync`](src/EndHud.Vision/Rotations/RotationSync.cs),
[`PublishedRotationService`](src/EndHud/PublishedRotationService.cs)). Adding, fixing or deleting
one is a commit; no release is needed. EndHud checks a minute after start and then daily (when
`CheckForUpdates` is on), with the tray's "check for updates", and when the player presses
"恢复推荐排轴" in the editor.

- One request lists the folder through GitHub's contents API, which gives each file's git blob
  SHA-1. Only new or changed files are downloaded, and each must match its SHA, be readable by
  this build and carry the id in its file name, or the previous copy is kept.
- A file deleted from the repository is deleted locally, but only after a complete listing; an
  unreachable GitHub, a 404 or an unexpected answer changes nothing.
- Local copies live in `%APPDATA%\EndHud\rotations\published`, apart from the player's own
  rotations, which the sync never writes.

How a published rotation changes ([`RotationLibrary`](src/EndHud.Vision/Rotations/RotationLibrary.cs)):

| | How |
|---|---|
| Create | only the sync, from the repository |
| Read | listed after the player's own, marked 【推荐】 in the editor; an own rotation with the same id wins |
| Correct | the publisher edits the file; the next sync replaces the local copy. A player's edits are saved as their own copy under a new id, and the original keeps updating |
| Remove | the publisher deletes the file. A player's delete hides it: the file goes and its id is kept in `published\hidden.txt`, so it isn't downloaded again until "恢复推荐排轴" |
| Import | a share code or file of a published rotation becomes the player's own copy |

`dotnet test` checks every file in `rotations/`: readable, id matching the file name, no
problems, and every operator in the operator library.

### What's read for it

All in combat, from the same capture as the squad panel. Positions are measured on 4K and scaled
like everything else.

| Reading | Where | How |
|---|---|---|
| Skill points ([`SkillPointBar`](src/EndHud.Vision/SkillPointBar.cs)) | the segmented bar above the battle skill buttons, bottom centre | yellow segments are full; the white fill of the next one gives the fraction (e.g. 1.4) |
| Controlled operator ([`ActiveSlot`](src/EndHud.Vision/ActiveSlot.cs)) | the chevron over the controlled operator's portrait | shape match against the chevron |
| Battle skill ([`BattleSkillButton`](src/EndHud.Vision/BattleSkillButton.cs)) | each battle skill button, bottom right | a dark disc with white digits is cooling; the white icon is available |
| Operator counters ([`OperatorCounters`](src/EndHud.Vision/Operators/OperatorCounters.cs)) | the controlled operator's resource, bottom centre | Laevatain's 熔火 (0–4): lit petals of the flower; pink petals mean full |
| Elemental infliction ([`InflictionRing`](src/EndHud.Vision/InflictionRing.cs)) | the ring at the right end of the boss's HP bar, top centre | present when the white stack segment on top of the ring shows; the element from the colour of the icon inside (heat, cryo, electric, nature) |
| Enemy statuses ([`BossStatusReader`](src/EndHud.Vision/BossStatusReader.cs)) | the icon row under the boss's bars, top centre | each square is matched against the wiki's status icons; read at most once a second and only when the current rotation asks about statuses (about 70 ms a read at 4K) |

Status icons come from the wiki's glossary module along with the operators (`statuses.json` in the
operator library), so new statuses need no release. Chinese names are in
[`StatusNames`](src/EndHud.Vision/Operators/StatusInfo.cs).

### Finding the current step

[`RotationTracker`](src/EndHud.Vision/Rotations/RotationTracker.cs), once per reading:

1. For every step, each check is *holding*, *not holding* or *unknown*: the step's own skill
   available; its expected state; and whether the step before it was seen used recently (within
   the last 40 readings, about 10 s). A value that isn't on screen is unknown, never a guess.
2. Each step's score is (holding + 0.5) / (known + 1), so one holding check doesn't outrank three.
3. On the first reading the best-scoring step becomes current. After that the strip moves on only
   when it sees the current step's skill used (steps drawn together may be used in either order),
   or when another step beats the current one by 0.34 for two readings in a row (the player went
   off-script). After the last step it loops to the first.

The current step is "ready" (lit) when all its known checks hold.

## Tests

```powershell
git lfs install   # once: the gameplay recordings are stored with Git LFS
dotnet test -c Release
```

All tests run from what's in the repository. They also run on every push to `main`, on pull
requests and before every release, on a GitHub-hosted Windows runner
([`.github/workflows/tests.yml`](.github/workflows/tests.yml)). The two real-capture tests are the
exception: hosted runners can't capture windows, so they only run on a developer machine
(`dotnet test` includes them; CI skips `Category=Desktop`).

- **Fixture frames** ([`tests/EndHud.Vision.Tests/Fixtures`](tests/EndHud.Vision.Tests/Fixtures)):
  a 4K exploration screenshot and frames from gameplay recordings, UID blacked out. They cover
  level, HP, combo and ultimate readings in exploration, combat, low HP, a dimmed squad, a heal,
  and with operators fallen.
- **Recognition out of the box** (nothing taught): every recording fixture (with the built-in
  references and with the wiki avatars alone), and a session of readings on every simulated
  screen, must name the whole squad without ever showing a wrong name; all nine squad
  screenshots (32 operators) at 4K, 1440p, 1080p and 1600×900; operators in two screenshots named
  from the other one's reference; a swapped operator is renamed without showing the old name; a
  damaged feature cache is rebuilt.
- **Learning:** correcting and clearing.
- **Operator library:** the downloaded roster and icons, learning on top of them, and that the
  build contains no game art. Downloading and updating are tested against a stand-in for the wiki
  (new operators, changed and unchanged images, busy or broken wiki, broken images, interrupted
  swaps), so those tests need no network.

  The other recognition tests use the real roster from `.cache/operator-library` (git-ignored: it's
  game art). A fresh clone downloads it the first time the tests run (about 10 s); CI keeps it in its
  cache, refreshed weekly.
- **Real capture:** a stand-in game window (shows a full-screen window for a few seconds).
- **Gameplay recordings** ([`tests/EndHud.Vision.Tests/Recordings`](tests/EndHud.Vision.Tests/Recordings),
  Git LFS, HUD areas only): whole fights replayed, checking that values change the way the game
  showed them. Examples:
  - combo cooldowns refill steadily;
  - the ultimate charge only grows;
  - an ultimate is never read in the wrong state on any frame;
  - out of combat, no combo bar is ever reported;
  - through a full boss battle, the panel is followed and operators fall one by one and stay fallen;
  - once read, the panel and ultimates never go blank: unreadable moments keep the last value;
  - the combo-ready hint fires exactly when each cooldown finishes, including during cutscenes, and
    never for an operator who falls mid-cooldown.

  See that folder's README for what each recording contains.
- **Combat HUD for rotations** ([`Fixtures/battle-hud`](tests/EndHud.Vision.Tests/Fixtures/battle-hud),
  labels in `labels.json`): skill points, the controlled operator, battle skill buttons, 熔火 and
  enemy statuses on labelled 4K battle frames (UID blacked out), including a cutscene that must
  read as nothing.
- **Elemental infliction** ([`Fixtures/infliction`](tests/EndHud.Vision.Tests/Fixtures/infliction)):
  heat and nature rings, and frames with no ring (a white background, a cutscene, blue effects
  filling the ring's place), at 4K and on simulated 1440p, 1080p and 16:10 screens.
- **Recommended rotations:** synced against a stand-in for GitHub (new, unchanged, corrected and
  removed files; a SHA or id that doesn't match; a 404, an unexpected answer or an unreachable
  GitHub changing nothing; the player's own rotations never touched; deleted ones staying deleted
  until restored); the editor saving a recommended rotation as the player's copy and hiding a
  deleted one; and every file in `rotations/` being valid.
- **Rotations:** the file format and share codes (round trip, damaged codes, newer formats,
  imports never overwriting); the tracker (picking a step with no reset, moving only on seen use,
  steps used together, going off-script, looping, nothing on screen); the editor's working copy;
  the strip's layout; every UI string translated, including keys built at run time.

- **Other screen sizes**, simulated from the 4K fixtures and recordings (see
  [`SimulatedScreen`](tests/EndHud.Vision.Tests/SimulatedScreen.cs)): desktop monitors (5K,
  2560×1440, 1920×1080, 1920×1200, 21:9 and 32:9 ultrawides), laptop panels (1280×720, 1366×768,
  1600×900, 2560×1600, 2880×1800, and 3:2 at 2256×1504 and 3000×2000) and the game in a 1700×956
  window. At every size, each value read must match the 4K reading (unreadable is allowed, wrong
  is not), and enough values must be read. The combo-ready hint must fire at the same moments on a
  1080p, 720p, 1366×768, 21:9, 16:10 and 3:2 screen.
- **Laptop vs desktop display scaling** ([`tests/EndHud.Tests`](tests/EndHud.Tests)): the
  combo-ready banner's size and position for game windows from 1280×720 to 5K at 100–200% display
  scaling, and a real capture of the game in an ordinary window with a title bar, checking that
  only its client area is read.

  16:9 sizes are the 4K frames scaled down, which is close to a real smaller screen. The other
  aspect ratios follow the layout EndHud assumes (the HUD fits 16:9 and each element keeps its
  distance from its own corner), so they test EndHud's handling of that layout, not that the game
  uses it. A real screenshot at 16:10 or 21:9 would confirm it.

## Developer tool

`endhud-cli` (in `src/EndHud.Cli`) runs the same readers outside the app:

| Command | Purpose |
|---|---|
| `analyze <screenshot>` `[--debug-dir <dir>]` | Read the squad panel from a screenshot, plus the combat readings rotations use (skill points, controlled slot, battle skills). `--debug-dir` saves the exact images given to OCR. |
| `analyze-video <video>` `[--every <s>]` | HP, combo cooldown and ultimate charge per slot over a recording. |
| `capture <window title text> <out.png>` | Grab one frame from a window (e.g. the game) through the HUD's capture path. |
| `identify <screenshot> --library <dir>` | Show each slot's learned match and closest wiki avatars (model differences, lower = closer). |
| `learn <screenshot> --library <dir> --slots A,B,C,D` | Teach a library the squad in a screenshot (operator ids, slot order). |
| `build-portrait-references <squads.json> <out.json>` | Rebuild EndHud's built-in in-game portrait references from labelled 4K squad screenshots. |
| `fetch-operators <dir>` | Download or update the roster, avatars and skill icons from the wiki into `<dir>/reference`, as the app does (`identify` and `learn` use the same layout). |


---

## Project layout

```
src/EndHud/              the tray app: overlay window (debug panel, combo-ready hint, rotation strip), rotation editor, menu, tray icon, config, i18n
src/EndHud.Vision/       everything that reads the screen
  Capture/               Windows.Graphics.Capture of a single window
  Operators/             operator library, recognition (character model), learning, operator counters, status icons
  Rotations/             rotation format, store, recommended-rotation sync, and the tracker that finds the current step
  Assets/ccip/           the character recognition model (Git LFS; see tools/ccip-model)
  Assets/operator-portraits.json  in-game portrait references (the model's numbers; built from test fixtures)
  PartyReader.cs         squad panel: level, HP, combo, portraits
  UltimateGauge.cs       ultimate circles
  PartyTracker.cs        carries readings over (deaths, values that are briefly unreadable)
  ComboHints.cs          when to show the combo-ready hint
  SkillPointBar.cs, ActiveSlot.cs, BattleSkillButton.cs, BossStatusReader.cs   combat readings for rotations
src/EndHud.Cli/          endhud-cli developer tool
tests/EndHud.Vision.Tests/ tests of everything that reads the screen (fixtures, recordings, screen sizes)
tests/EndHud.Tests/      tests of the tray app (banner layout at any display scaling, windowed capture)
.github/workflows/       CI: tests on every push (tests.yml); releases from version tags (release.yml)
installer/               the MSI installer (WiX)
tools/EndHud.ReadmeMedia/ renders the README's animated pictures from a recording
rotations/               recommended rotations, published to every player from main
```

Screen positions are measured on 3840×2160 and scaled to the game window's size, each element
keeping its distance from its corner of the window. What works at each size, tested on simulated
screens (see [Tests](#tests)); "read" is the share of levels read on the test frames:

| Game window | HP, combo, ultimate, fallen | Level (debug panel only) |
|---|---|---|
| 4K, 5K, 1440p class (2560×1440, 3440×1440, 2560×1600, 5120×1440), 3000×2000 | all | 80–100% read |
| 1080p class (1920×1080, 1920×1200, 2560×1080), 2880×1800, 2256×1504, 1600×900 | all | 40–53% read |
| 1366×768, 1280×720 | all (HP within about 8%) | not read |

A level that isn't read keeps its last value; one that might be wrong is not shown: two OCR
attempts must agree, and only levels 10 to 90 (the game's cap) are accepted. Levels 1–9 are
shown as unknown, because a "9" is usually "90" with a digit lost. EndHud doesn't read the ping.

Display scaling (125–200% on most laptops) doesn't change what's read: the game draws in real
pixels and EndHud captures them. The combo-ready banner is sized from the game window's real
pixels, so it looks the same on a laptop at 150% as on a desktop at 100%.

## Adding a language

Copy `src/EndHud/i18n/zh-CN.xaml` to, for example, `en-US.xaml`, translate the values (keep `{0}`,
`{1}`), rebuild, and set `"Language": "en-US"` in `config.json`.

## Known limits

- The ultimate charge has only been observed up to about 46%; no recording yet shows one filling completely.
- When something can't be read (cutscenes, HUD fades, skill effects covering an indicator), the HUD
  keeps showing the last value it read. So a value can be a few seconds old,
  for example a combo skill used at the moment its bar was hidden still shows "ready" until the bar is read again.
  After the squad panel leaves the screen (menus, the map) the last squad reading also stays up.
- Everything is tuned on 4K recordings; other sizes are tested on frames scaled from them, not on
  real recordings at those sizes. 16:10 and 21:9 assume the HUD is laid out as described above.
- At 1366×768 and smaller, levels aren't read, and out of combat the squad panel can't always be
  recognised (its edge marks are under two pixels wide). Both only affect the debug panel.
- Levels 1–9 are shown as unknown (see above).
- The combo-ready hint needs two "cooling" readings in a row before it trusts that a combo skill was used,
  because the bar is briefly drawn as an empty track while the HUD fades in. A cooldown
  refreshed within about two seconds of being used therefore gets no hint.
- Hints and the rotation strip follow the screen reading, four times a second by default
  (`OcrIntervalMs`), so they can lag the game by up to a quarter of a second.
- Rotations: only Laevatain's 熔火 is read as an operator counter so far (Typhoeus and Zhuang Fangyi
  have counters too; they need combat screenshots). Stack counts aren't read (the status row's
  badges, or the infliction ring's segments), so the editor doesn't offer them, and a shared
  rotation asking for 2+ stacks treats that check as unknown. Infliction rings have been seen at one
  and two stacks, and only for heat and nature; cryo and electric are told apart by colour from the
  wiki's icons but not yet seen in a recording. A few boss icons aren't on the
  wiki's glossary and aren't recognised.

## Game art

EndHud ships no game art: operator avatars and skill icons are downloaded from the community wiki
onto the user's machine, and the build contains none (a test checks). The repository's test
fixtures, recordings and README pictures are frames of gameplay, kept for testing and
documentation. Earlier commits did contain the wiki art; clean the history before making the
repository public.

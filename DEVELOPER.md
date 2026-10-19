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
EndHud has no operator library for gets a banner with just their slot number; while the squad is
still being recognised (the first second or so), a hint waits for the name instead.

- **Two modes** (menu → combo-ready hint section): show for a few seconds when the cooldown
  finishes (2, 3 or 5 s; default 3 s), or show for as long as the combo skill stays ready.
- **Early, counting down** (提前提示; off, 0.5, 1, 2 or 3 s; default 1 s): the banner appears that
  long before the cooldown finishes, saying "0.5 秒后就绪", then "连携就绪". The time left comes from
  how fast the bar filled over the last 3 s ([`ComboHints`](src/EndHud.Vision/ComboHints.cs)); once
  up, it stays until the combo is ready, never counts back up, and says "即将就绪" below 0.1 s. A
  combo counting down is drawn muted (grey-blue edge, pale-blue text); turning ready, the edge
  flares gold and the banner pops.
- **Move it**: open the menu (Ctrl+Shift+M) and drag the banner. "Reset position" puts it back.
  The spot is saved relative to the game window, so it survives resizing.
- A cooldown refreshed early by a skill (the bar jumps straight to ready) counts the same as one
  that refilled. A cooldown that finishes during an ultimate cutscene is hinted when the HUD
  comes back.
- No hint out of combat, for a fallen operator, or for a combo skill that is simply ready when a
  fight starts.

### Rotation strip (排轴)

A rotation is the order a squad's skills are used in: battle skills, combos and ultimates (basic
attacks aren't tracked). In battle, a strip at the top centre shows the
phase of the recognised squad's rotation that fits the game right now, styled after community
rotation charts.

- **One per squad, any slot order.** The strip shows the rotation chosen for those four operators
  in the editor (or the first one made for them).
- **Phases chosen by the game's state.** A rotation is a list of phases (e.g. 充能轴 I, 充能轴 II,
  爆发轴), each with a condition for when it applies ("4 arrows and no ultimate ready"). The strip
  shows the first phase whose condition holds; in between, the one it showed. See
  [Rotations](#rotations).
- **Every step alike.** EndHud can't see which button was pressed, so it doesn't mark a current
  step: the strip shows the phase's steps in order, with their notes.
- **Shown in battle only** ([`StripVisibility`](src/EndHud.Vision/StripVisibility.cs)). A reading that
  misses the battle signs, or the panel covered by an ultimate animation (up to 3 s), doesn't take
  it down; it goes 2 s after the battle signs are gone (exploration, menus, loading). The gauges
  follow the same rule. A screen in the squad panel's place where every HP bar is empty, nobody has
  fallen, nobody is controlled and no ultimate charges isn't the panel (`PartyState.PanelVisible`):
  the squad select screen's tab bar gives 6 of the 8 marks and its strips read as combo bars, and
  read as the panel it started a "battle", named its floor as the squad and kept the gauges up
  through the loading screen after it (Fixtures/panel-or-not/squad-select-screen.jpg). A boss's status covering
  every HP bar in a real battle still counts: the controlled operator and charging ultimates show
  (Fixtures/panel-or-not/battle-hp-bars-covered.jpg).
- **Looks like the guides' charts:** a see-through grey band that fades out at both ends, no
  backdrop box, the phase name large on the left, a line through the icons. Centred at the top by
  default, and it stays centred as its width changes.
- **Move it**: open the menu and drag the strip; "reset position" puts it back.
- **Recommended rotations** come from the repository's [`rotations/`](rotations) folder and are
  kept up to date daily, without a release (see [Recommended rotations](#recommended-rotations)).
- **Edit and share** in the rotation editor (menu → 排轴 → 编辑排轴…, or the tray): phases and
  their conditions picked from lists, steps, a live preview, and import/export as a file or a share
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

When there's no reading, the panel says why instead of staying on "recognising the squad panel":
no squad panel on screen, no OCR language, the game window sending no picture (`Party.NoFrames`),
an empty game area in the capture (`Party.WindowEmpty`), reading failing to start (`Party.StartFailed`),
a capture error (`Party.CaptureFailed`), or one step of a reading taking over 15 s (`Party.Stuck`).

### Diagnostic log and export

`%APPDATA%\EndHud\logs\endhud.log` ([`DiagnosticLog`](src/EndHud/DiagnosticLog.cs)) records the PC
(Windows version, processors, screens, OCR languages) at start, and then, rather than every reading,
what changes or goes wrong: the game window followed (rect, frame, client area, DPI), capture
starting and its frame size, the squad panel appearing and disappearing, the operator library
loading, the squad named, slow naming (over 2 s), a reading step hanging (watchdog, 15 s), and
every exception, including unhandled ones. Once per game window, before the first frame, the
graphics ([`GraphicsInfo`](src/EndHud.Vision/Capture/GraphicsInfo.cs)): each adapter with its driver
version and memory, the screens it drives (position, HDR on or off), which screen the game is on,
and which adapter the capture device uses. A capture that sends no picture logs its counts and,
while a copy runs, its step and time (get texture, copy on the GPU, map, read); a copied frame,
its size, pixel format and copy time. When the squad panel is missed for 2 s in a row, what the
reading saw instead: the picture's brightness and share of black (all black: the game's picture
can't be captured), panel marks, controlled slot, skill points, and each slot's HP, level, combo and
ultimate. Repeats are written once per 30 s with a count. It rolls
over at 1 MB and keeps three files; the user's profile folder is written as `%USERPROFILE%`.

"Export diagnostics" (tray, or the menu's General card) writes `EndHud-diagnostics-<time>.zip` to the
desktop ([`DiagnosticExport`](src/EndHud/DiagnosticExport.cs)): `summary.txt` (version, PC, the game
window, what reading is doing, the last reading per slot, the operator library on disk), `logs/`,
`config.json` and `game.png`, the last game picture read with the UID area (bottom left, at 4K
x 170–560, 15–95 px above the bottom) blacked out, and `recent/`: the pictures read over the last
~12 s (one every 2 s, six, at most 960 px wide, UID blacked out) with `readings.txt` saying what
was read from each. The summary also lists the graphics. Learned portraits aren't included.

**Network.** Every GitHub request (EndHud updates, recommended rotations, the operator package)
goes through [`GitHubHttp`](src/EndHud/GitHubHttp.cs), which logs the host and path, the status and
time, or the failure with its whole chain of causes down to the socket error (a reset connection
reads differently from a refused certificate). A failed check is retried after 30 minutes instead of
the usual 6 hours (package) or a day (EndHud, rotations): players' logs show GitHub answering one
minute and failing the SSL handshake six minutes later.

**Copying captured frames.** Each frame Windows.Graphics.Capture delivers is a GPU texture;
[`StagingCopy`](src/EndHud.Vision/Capture/StagingCopy.cs) copies it into a CPU-readable staging
texture on the capture's own D3D11 device and reads it, synchronously in the frame callback (the
usual method, as in Microsoft's capture sample). EndHud 0.4.3 and earlier used
`SoftwareBitmap.CreateCopyFromSurfaceAsync`, waited on inside the callback: on Windows 10 22H2 PCs
that never returned. A 0.4.3 player's log showed the capture counts "1 frames arrived, 0 copied,
1 copying now" for minutes, so the HUD read nothing ("no picture from the game window"). The log's
no-picture line carries those counts (frames arrived, resizes, copied, copies failed or running).

**Reading the game off the screen** (fallback). After two silent captures in a row (2 s each)
`PartyMonitor` asks to keep the overlay out of screen captures (`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`)
and, if Windows agrees, copies the game's client area off the screen
([`ScreenCapture`](src/EndHud.Vision/Capture/ScreenCapture.cs), GDI BitBlt); from then on the HUD is
also missing from the player's screenshots and recordings. Windows 10 refuses that for the overlay,
a see-through (layered) window, with error 8; then it's asked once per game window and the screen
isn't read (it would read EndHud's own HUD). The screen shows whatever is in front, so it reads only
while the game or the overlay has the focus (`Party.GameCovered` otherwise) and the whole game area
is on screen (`Party.OffScreen`). A new game window tries window capture again.

Squad naming runs beside the readings (`PartyMonitor`): a reading waits at most 150 ms for it, then
shows the last names found, so a slow character model never holds up the HUD.

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
git lfs install   # once: the character recognition model is stored with Git LFS
git clone https://github.com/Nedlitex/EndHud.git
```

A clone downloads about 120 MB (history and the 38 MB model). `git clone --depth 1` takes only
the latest commit: about 60 MB.

The test data (screenshots and gameplay recordings, about 220 MB) is a separate repository,
[EndHud-test-data](https://github.com/Nedlitex/EndHud-test-data), included as the submodule
`tests/data`. A plain clone leaves it out; building and running EndHud don't need it. To run the
vision tests, fetch it (or clone with `--recurse-submodules`):

```powershell
git submodule update --init
```

```powershell
dotnet build -c Release
dotnet run --project src/EndHud -c Release
```

Or produce a single `EndHud.exe` with everything inside it (the operator library included):

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
the MSI, runs the installed EndHud's self-test (unpack the built-in operator package, name the
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
    -Screenshot tests/data/Fixtures/exploration-levels-visible.png -Squad Laevatain,Camille,Arcane,Ardelia
```

It runs `EndHud.exe --self-test <folder> [--screenshot <image> --expect <ids>]` (no window), which
unpacks the built-in operator package into `<folder>` as on first start, loads it, and names the squad in
the screenshot; the steps are logged to `<folder>\self-test.log`, and the exit code is 0 when all
passed. The files aren't code-signed yet, so Windows SmartScreen warns about an
unrecognised app the first time.

---

## Using it

EndHud starts in the **system tray**, and the HUD appears whenever the game window has focus.

| Hotkey | Action |
|---|---|
| **Ctrl+Shift+M** or **Ctrl+F9** | Open/close the menu: settings, and correcting operator names |
| **Ctrl+Shift+H** or **Ctrl+F10** | Show/hide the HUD |
| **Ctrl+Shift+D** or **Ctrl+F11** | Show/hide the debug panel |
| **Ctrl+Shift+Q** | Quit |

Not plain F9-F12: the game uses F9 and F10 itself, and a key EndHud registers never reaches the
game. Quit has no F-key, so it can't happen by accident mid-fight. A key another program already
holds is logged ("hotkey Ctrl+F10 is taken by another program"); the other key for it still works.
| **Esc** | Close the menu |

Right-click the tray icon for: status, show HUD, open menu, auto-show on/off, debug panel on/off,
**check for updates**, open the config folder, and quit.

### Updates

- **A newer EndHud.** [`UpdateService`](src/EndHud/UpdateService.cs) asks GitHub for the
  repository's latest published release (`/releases/latest`: drafts and pre-releases don't count)
  a few seconds after start and then daily, when `CheckForUpdates` is on (the default; a menu checkbox).
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
- **Installed by itself** (`AutoInstallUpdates`, on by default): a newer version is installed as
  above without a click, but only while the game isn't running; one found while playing waits until
  the game is closed ([`AutoInstaller`](src/EndHud/AutoInstaller.cs)). Each version is tried once
  per run; a failure is notified and waits for the next start.
- **New operators.** A newer operator package from the repository (see
  [Recognising operators](#recognising-operators)) is installed in the background and its new
  operators announced.
- **Check for updates** (tray) does all of it at once and shows the result in a notification. It
  asks GitHub for the operator package at most every 15 minutes; within that it reports the last
  result.

### Settings

`%APPDATA%\EndHud\config.json` is created on first start. The menu changes it for you; if you
edit it by hand, restart EndHud. Delete the file to reset.

**Start with Windows** (the menu's General card, off by default) isn't in `config.json`: it's the
value `EndHud` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, holding
`"<path to EndHud.exe>" --autostart` ([`AutoStart`](src/EndHud/AutoStart.cs)); `--autostart`
skips the "EndHud is running" balloon. The menu reads the registry, so turning EndHud off in Task
Manager's Startup apps (StartupApproved\Run) shows as off there; turning it on in EndHud clears that.
A moved portable copy points the value at itself when it starts (release builds only). Uninstalling
the MSI removes the value and the Task Manager mark; an upgrade keeps them (both checked in
release.yml).

| Key | Default | Meaning |
|---|---|---|
| `CheckForUpdates` | `true` | Check GitHub a few seconds after start and then daily: for a newer EndHud, for changed recommended rotations and for a newer operator package (both downloaded). |
| `AutoInstallUpdates` | `true` | Install a newer EndHud by itself while the game isn't running (needs `CheckForUpdates`). Off: a notification only. |
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
| `RotationCenterX`, `RotationY` | `0.5`, `0.13` | Where the strip sits, as a fraction of the game window: the middle of its width (it stays centred as it changes width) and its top. |
| `RotationScale` | `1` | The strip's size on top of following the game window, 0.5–2 (the menu's 大小 slider; a value outside is brought back into range). |
| `GaugesEnabled` | `true` | Show the gauges in battle: team buffs (Link with its stacks, once seen in the battle), and each recognised operator's ultimate charge and own counters, as circles with the operator's avatar in the middle. While a lasting ultimate runs (Laevatain's, Zhuang Fangyi's), the controlled operator's ultimate circle shows its time left instead: violet, glowing, draining anticlockwise, labelled 持续中. |
| `GaugesScale` | `1` | The gauges' size on top of following the game window (the menu's slider): 0.5–2; a value outside that is clamped when the file is read. |
| `GaugesCenterX`, `GaugesBottomY` | `0.5`, `0.885` | Where the gauges sit, as a fraction of the game window: the middle of their width and their bottom edge (so a bigger size grows upwards): bottom middle, just above the game's HP and skill-point bars. |
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

1. **References, built in.** Every operator's avatar, their battle skill, combo and ultimate
   icons, Chinese name and the enemy status icons come from
   [endfield.wiki.gg](https://endfield.wiki.gg/wiki/Operator) (CC BY-SA 4.0; the images are the
   game's art), as one package: [`operators/operator-library.zip`](operators) (about 1.2 MB,
   [`OperatorPackage`](src/EndHud.Vision/Operators/OperatorPackage.cs)). It's built into EndHud,
   so a first start needs no network (the wiki was slow or unreachable for many players in
   mainland China). New operators come without a release:
   - A weekly workflow ([`operator-package.yml`](.github/workflows/operator-package.yml)) rebuilds
     the package from the wiki and the inn data
     (`endhud-cli build-operator-package operators/operator-library.zip --inn inn`;
     a change to `inn/` on `main` runs it too) and commits it only if the content changed: the package is reproducible (entries sorted, fixed
     timestamps, its date kept when nothing changed).
   - While EndHud runs (and `CheckForUpdates` is on), it looks at the repository's `operators/`
     folder every 6 hours, as for recommended rotations
     ([`OperatorPackageSync`](src/EndHud.Vision/Operators/OperatorPackageSync.cs)): downloaded only
     when its git blob SHA-1 changed, which it must match; installed only if newer than the one in
     use (built in or downloaded before, whichever is newer). The tray's **check for updates** asks
     now.
   - A package that isn't one, has a file outside its folder, or lists under 80% of the current
     roster is refused and the current copy kept. Installing swaps the folder with two renames (an
     interrupted swap is undone on the next start); learned portraits are never touched.
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
       covering all 32 operators ([the squad fixtures](tests/data/Fixtures/squads/README.md)),
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
| `reference/` | the operator package unpacked: roster, avatars, skill icons, statuses (`package.json` says when its content last changed) |
| `package-seen.sha` | the last package listed on GitHub, so an unchanged one isn't downloaded again |
| `learned/<operator>/*.png` | portraits learned from your picks |
| `features/*.json` | the avatars as the recognition model describes them, per screen size (a cache: safe to delete) |

Players never contact the wiki: only the package build (the weekly workflow, or a developer
running `endhud-cli build-operator-package`) does. How that stays polite to the wiki, which
volunteers run ([`WikiPoliteness`](src/EndHud.Vision/Operators/WikiPoliteness.cs)):

- One request at a time from the whole process, with a pause between them: 1 s between API calls,
  0.1 s between images (served from a CDN). A full first download is about 10 API calls and 133
  images, so about 25 s; a weekly refresh downloads only images that changed.
- API calls send `maxlag=5`: when the wiki's database is behind, it answers "busy" instead of doing
  the work, and EndHud waits (Retry-After, up to 60 s) and retries, giving up after three tries
  until the next scheduled check. Busy HTTP answers (429, 5xx) are treated the same.
- How often: once a week, from the workflow. Requests name EndHud and its repository in the
  User-Agent.

How the package build stays safe ([`OperatorReference`](src/EndHud.Vision/Operators/OperatorReference.cs),
[`WikiOperatorSource`](src/EndHud.Vision/Operators/WikiOperatorSource.cs)):

- New operators need nothing: the roster is whatever the wiki's Operator page lists.
- Requests are retried when the wiki is busy or the connection drops (see above).
- An image that fails, or isn't a valid image, keeps the previous copy's file; the rest still updates.
- The new copy is downloaded beside the current one and swapped in with two renames; an
  interrupted swap is undone on the next start. If the wiki lists far fewer operators than the
  current copy (under 80%), the current copy is kept: operators don't leave the game.

Si and Argent Flow have no skill icons on the wiki yet; they're recognised by portrait only.

---

## The inn (缘缘庄)

The data for the inn of Yuanyuan Manor ("Yuanyuan's Blazing Stoves", from version 1.6) travels
in the operator package, so it's built in and updated like the operators (see above):

- **Each operator's element and two hobbies** come from the wiki with the roster (the infobox's
  `element`, `hb1` and `hb2`; [`OperatorInfo`](src/EndHud.Vision/Operators/OperatorInfo.cs)).
- **The dishes (with their pictures), drinks, traits, ingredients, themes, chef rules and gourmands** are kept by hand in
  [`inn/inn-data.json`](inn/inn-data.json) (the wiki has no dish pages yet; sources and how to
  change it in [inn/README.md](inn/README.md)), read by
  [`InnData`](src/EndHud.Vision/Inn/InnData.cs). The package build refuses the file if it has
  problems, and players' EndHud refuses a package whose inn data is broken, missing or much
  shorter than the current copy's, keeping the current copy.

A chef helps their kitchen through each hobby (+10% cooking speed for one dish type, or +5% chance
of a choice dish, 珍馐) and their element (+5% speed and choice chance for one flavor, from Inn
Rating 3), and bonuses of the same kind add up (`InnData.ChefBonusesOf`). The game's own kitchen
screen agrees: Akekuri and Snowshine together show 主食 +40%, 鲜 +5% and 辣 +5%. The cook times the game shows
for a kitchen are `base ÷ (1 + bonus)` rounded up (赛希 and 伊冯: 金丝小贝 35 s → 27 s, 雪绵豆沙 25 s →
20 s, 白瓜肉丸汤 40 s → 37 s). Names not yet seen
in the game (seven theme names) stay in English until they are; they're never
guessed.

Gourmands (老饕) ask for a kind of dish or drink rather than a dish. Their requests, and what meets
each, come from a players' table (sources in [inn/README.md](inn/README.md)), with each condition
written as players write it ([GourmandCondition](src/EndHud.Vision/Inn/GourmandCondition.cs): dish
types, [traits], <ingredients>, 非, &, 或). What a gourmand says is matched to a request by its
words, not its letters (`InnData.MatchSaid`): the table's sentences were typed by players, OCR
slips, and a bubble wraps onto lines read one at a time. Each two-character piece of the line
counts by how rare it is among all requests, so the word that tells two of a gourmand's requests
apart decides. Tested on the lines OCR read off the owner's recording (two gourmands, both found).

### Reading the inn's screens

The readers in [`src/EndHud.Vision/Inn/`](src/EndHud.Vision/Inn) return data records and draw
nothing; each returns null (or a null field) for what it can't read clearly, never a guess. They
are not wired into the app yet. Chinese text is read with the Windows OCR engine for Simplified
Chinese ([`InnText`](src/EndHud.Vision/Inn/InnText.cs): `InnText.TryCreate()` is null without that
language). Measured on the owner's two recordings (`tests/data/Fixtures/inn`, what each frame
shows in its README), at 4K and scaled to 1440p and 1080p (`SimulatedScreen`); the setup screens,
the menu and the dispatch panel are placed on the centred 16:9 area, the session's HUD by its
corners, and other window shapes are EndHud's assumption, not checked.

| Reader | Reads | On the frames |
|---|---|---|
| `InnScreen.DetectAsync` | setup (营业设定, 主题设定), chef picker, menu, session, dispatch panel (titles by OCR, the timer by its digits) | all 20 frames, at every size (the session's first frame, fading in, at 4K only) |
| `InnScreen.ReadClock` | time left, closed at 00:00 | every session frame |
| `FireMeterReader` | fire "N/100", streak, ready (red button, 开启满堂彩) | all, at every size; hidden behind the dispatch panel |
| `TableCardReader` | each table's state and, for an order, its dishes and drinks by picture, being made / ready, the pips | 56/56 items and 90/90 cards at 4K, 1440p and 1080p |
| `DispatchPanelReader` | kitchen shown, chefs, 珍馐 chance, each dish's cook time, the queue (dish, time left) | 114–115 of 115 values per size, none wrong |
| `BubbleReader` | a gourmand's speech bubble, its sentences matched with `InnData.MatchSaid` | the one recorded bubble, both requests, at every size |
| `SetupReader` | theme, objective, menu rules, hot traits, kitchens with their chef places and chefs | all at 4K; objective and rules slip at 1440p |
| `MenuReader` | tab, cards in view and whether each is on the menu, stock ∞, the counters | tab and cards at every size; stock and counters partly |

How the hard parts work:

- **Numbers** (timer, fire, streak, cook times, counters) are read by their glyphs' shapes
  ([`InnDigits`](src/EndHud.Vision/Inn/InnDigits.cs)), not OCR: Windows OCR misread 04:25 as
  "04•.26" without saying so. The shapes are averaged from the frames at 4K; a glyph not clearly
  one of them makes the number unreadable.
- **Ordered dishes and drinks** ([`TableCardReader`](src/EndHud.Vision/Inn/TableCardReader.cs)):
  each picture's grey disc is found by fitting a circle to its edge (a new order's card is drawn
  larger for a moment), and every dish and drink picture is drawn the way the card draws it (103
  px at 4K over the disc's grey) and compared in colour over the whole disc but the badge's corner.
  A picture is named when it's within 20 (mean difference per channel) and 5 ahead of the next;
  on the frames right pictures score 3–12 and the next ones are ≥ 7.6 behind.
- **OCR'd names** are matched to the data by the longest run of letters they share
  ([`InnNames`](src/EndHud.Vision/Inn/InnNames.cs)), and read at a few sizes (and once in the
  region's own greys, which a small window needs) until one in the data comes out.
- **One OCR engine, one recognition at a time**: two callers sharing it read garbage now and then.

Not built yet: identifying the chef picker's cards (they show no names; the plan matches portraits
and bonus icons), the drink bar and the results screen (no frame recorded), a one-item order's
picture place (no frame of one).

## Rotations

### The file

A rotation is a JSON file in `%APPDATA%\EndHud\rotations\<id>.json`
([`Rotation`](src/EndHud.Vision/Rotations/Rotation.cs),
[`RotationFormat`](src/EndHud.Vision/Rotations/RotationStore.cs)):

```json
{
  "format": 2,
  "id": "6f1c…",
  "name": "示例排轴",
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
        { "operatorId": "Laevatain", "action": "BattleSkill", "note": "熔火未满",
          "expect": [ { "kind": "bossStatus", "statusId": "Heat Infliction", "present": true },
                      { "kind": "counter", "operatorId": "Laevatain", "counterId": "melting-flame", "is": "AtMost", "value": 3 } ] }
      ]
    },
    {
      "id": "charge-2",
      "name": "充能轴",
      "steps": [
        { "operatorId": "Camille", "action": "BattleSkill" },
        { "operatorId": "Arcane", "action": "Combo" }
      ]
    },
    {
      "id": "burst",
      "name": "爆发轴",
      "when": [
        { "kind": "any", "of": [
          { "kind": "ultimate", "operatorId": "Laevatain", "ready": true },
          { "kind": "all", "of": [
            { "kind": "skillPoints", "is": "AtLeast", "value": 3 },
            { "kind": "counter", "operatorId": "Laevatain", "counterId": "melting-flame", "is": "Exactly", "value": 4 } ] } ] }
      ],
      "steps": [ { "operatorId": "Laevatain", "action": "Ultimate" } ]
    }
  ]
}
```

- `squad`: operator ids from the operator library, in any slot order.
- `phases`, in order. `when` says when a phase applies: each reading, the first phase whose `when`
  holds is shown (the last, with `lastMatchWins`). When none holds, the first phase without `when`
  is shown: it's the "otherwise" phase. While something can't be read, or when every phase has a
  `when`, the phase already shown stays. See [Choosing the phase](#choosing-the-phase).
- `lastMatchWins` (optional, format 3): `true` shows the last phase whose `when` holds instead of
  the first.
- `action`: `BattleSkill`, `Combo` or `Ultimate`. The skill being available (a skill point for a
  battle skill, the combo or ultimate ready) is implied; `expect` lists anything else.
- `withPrevious`: used right after the previous step of the same phase, drawn overlapping it.
- `label` (optional): replaces the action's name above the icon, for a variant the game names
  differently (e.g. "战技·意"). `note` (optional): a short hint under the icon.
- `expect` (a step) and `when` (a phase) are lists that must all hold. Each item (`kind`):

  | `kind` | Fields | Holds when |
  |---|---|---|
  | `skillPoints` | `is` (`AtLeast`, `AtMost`, `Exactly`), `value` (0–3, halves allowed) | the skill point bar compares so |
  | `combo` | `operatorId`, `ready` | that operator's combo is ready (`true`) or cooling (`false`) |
  | `comboCharge` | `operatorId`, `is`, `percent` (0–100) | that operator's combo cooldown bar has refilled so far (ready is 100) |
  | `battleSkill` | `operatorId`, `ready` | that operator's battle skill button is available (`true`) or cooling (its countdown seconds aren't read) |
  | `ultimate` | `operatorId`, `ready` | that operator's ultimate is ready or charging |
  | `ultimateCharge` | `operatorId`, `is`, `percent` (0–100) | that operator's ultimate is charged so far (ready is 100; at most 0 is just used) |
  | `ultimateActive` | `operatorId`, `active` (format 3) | that operator's lasting ultimate is running (`true`) or not: the time bar above the skill points, which is the controlled operator's; unknown while the skill points can't be read |
  | `counter` | `operatorId`, `counterId`, `is`, `value` | an operator's own resource compares so (see below) |
  | `bossStatus` | `statusId` (the wiki's English name), `present`, `minStacks` | the enemy shows (or doesn't show) that status |
  | `controlling` | `operatorId` | that operator is the one being controlled |
  | `all` | `of`: a list of conditions | every one holds (and) |
  | `any` | `of`: a list of conditions | at least one holds (or) |
  | `not` | `of`: one condition (format 3) | it doesn't hold; unknown stays unknown |

  `all` and `any` nest to any depth. Something the screen doesn't show right now (a cutscene, an
  operator's counter while someone else is controlled) is *unknown*, not false: `all` is false if a
  part is false, else unknown if a part is unknown; `any` is true if a part is true, else unknown if
  a part is unknown. A phase's `when` that is unknown doesn't switch to it.

- `format`: 3 when it uses `not`, `ultimateActive` or `lastMatchWins`, else 2: a rotation is saved in the lowest
  format that holds it, so one using nothing new stays readable by builds from before format 3. A
  file from a newer EndHud is refused with a message instead of being misread (builds from before
  format 2 or 3 say so about such a file or share code). A format 1 file (a fixed
  `sequence` of phases with repeat counts) is read as those phases in that order, repeats copied,
  none with `when`: it plays exactly as before.

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
one is a commit; no release is needed. EndHud checks a few seconds after start and then daily (when
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

From the same capture as the squad panel. Positions are measured on 4K and scaled
like everything else.

| Reading | Where | How |
|---|---|---|
| Skill points ([`SkillPointBar`](src/EndHud.Vision/SkillPointBar.cs)) | the segmented bar above the battle skill buttons, bottom centre | yellow segments are full; the white fill of the next one gives the fraction (e.g. 1.4) |
| Lasting ultimate ([`UltimateTimeBar`](src/EndHud.Vision/UltimateTimeBar.cs)) | the thin line right above the skill points, while Laevatain's or Zhuang Fangyi's ultimate runs | one colour from the left over a grey track; its length is the time left. Taken as the bar only with that shape and a line's edges (bright sky there read as a full bar before). Kept through gaps of up to 4 s (PartyTracker). |
| Controlled operator ([`ActiveSlot`](src/EndHud.Vision/ActiveSlot.cs)) | the chevron over the controlled operator's portrait | shape match against the chevron |
| Battle skill ([`BattleSkillButton`](src/EndHud.Vision/BattleSkillButton.cs)) | each battle skill button, bottom right | a dark disc with white digits is cooling; the white icon is available |
| Operator counters ([`OperatorCounters`](src/EndHud.Vision/Operators/OperatorCounters.cs)) | the controlled operator's resource, bottom centre | Laevatain's 熔火 (0–4): lit petals of the flower; pink petals mean full. Typhoeus's 猎矢 (`hunting-arrow`, 0–4): bars lit cyan (with a white streak over them when full); and 启示 (`sign`, 0–8): dots lit white, filling from the bottom row. Unknown when a bar or dot is neither lit nor grey, or the lit ones aren't in their fill order (the HUD moving). Zhuang Fangyi's 青霆剑 (`sunderblade`, 0–9): cells of a diamond grid lit cyan |
| Elemental infliction ([`InflictionRing`](src/EndHud.Vision/InflictionRing.cs)) | the ring at the right end of the boss's HP bar, top centre | present when the white stack segment on top of the ring shows; the element from the colour of the icon inside (heat, cryo, electric, nature) |
| Enemy statuses ([`BossStatusReader`](src/EndHud.Vision/BossStatusReader.cs)) | the icon row under the boss's bars, top centre | each square is matched against the wiki's status icons; read at most once a second and only when the current rotation asks about statuses (about 70 ms a read at 4K) |

Status icons come from the wiki's glossary module along with the operators (`statuses.json` in the
operator library), so new statuses need no release. Chinese names are in
[`StatusNames`](src/EndHud.Vision/Operators/StatusInfo.cs).

### Choosing the phase

[`PhaseSelector`](src/EndHud.Vision/Rotations/PhaseSelector.cs), once per reading: the first phase in
the list whose `when` holds becomes the one shown (the last, with `lastMatchWins`; unknown counts
as not holding). When none holds and every `when` could be read, the first phase without `when` is
shown (the "otherwise" phase); while one can't be read, or when every phase has a `when`, the
phase shown stays; before any has held, the first phase without `when` (or the first phase).
Until 0.5.1 the phase shown always stayed when none held, so a phase without `when` never came
back once another had held (a player's 充能轴 I "Laevatain's ultimate at most 50%" never gave way
to 充能轴 II).
Conditions are evaluated by `RotationTracker.Evaluate`.

[`RotationTracker`](src/EndHud.Vision/Rotations/RotationTracker.cs) also follows the steps inside a
phase from skills it sees used, as below. The app doesn't use that part while the strip shows no
current step (it couldn't tell reliably which button was pressed):

1. **Entry conditions first.** Every `when` is checked; the value last *seen* is remembered
   (unknown readings change nothing). A condition that turns true (it wasn't seen true before)
   moves the position to its phase's first step right away. If several turn true together, the
   first in the list wins. A fight that starts with one already true starts in that phase.
2. For every step, each check is *holding*, *not holding* or *unknown*: the step's own skill
   available; its expected state; and whether the step before it was seen used recently (within
   the last 40 readings, about 10 s). The step before is the previous one in its phase, or for a
   phase's first step, the last step of the phase the flow comes from (none for a phase entered by
   its condition).
3. Each step's score is (holding + 0.5) / (known + 1), so one holding check doesn't outrank three.
4. On the first reading the best-scoring step of the flow becomes current. After that the strip
   moves on only when it sees the current step's skill used (steps drawn together may be used in
   either order). After a phase's last step comes the next phase of the flow.
5. It jumps elsewhere if another step beats the current one by 0.34 for two readings in a row
   (the player went off-script), but only within the current phase or to a phase of the flow, and
   never out of a phase entered by its condition (that one ends when it's played, or when another
   condition turns true).

The current step is "ready" (lit) when all its known checks hold.

## Tests

```powershell
git submodule update --init   # once: the test data (tests/data); see Build and run
dotnet test -c Release
```

All tests run from what's in the repository and its test data submodule; without the submodule,
`dotnet test` stops with a message saying how to fetch it. Changing test data is a commit in
EndHud-test-data, then the new `tests/data` pointer committed here with the tests that use it. They also run on every push to `main`, on pull
requests and before every release, on a GitHub-hosted Windows runner
([`.github/workflows/tests.yml`](.github/workflows/tests.yml)). The two real-capture tests are the
exception: hosted runners can't capture windows, so they only run on a developer machine
(`dotnet test` includes them; CI skips `Category=Desktop`).

- **Fixture frames** ([`tests/data/Fixtures`](tests/data/Fixtures)):
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
- **Operator library:** the operator package (built in, equal to the repository's; same content,
  same bytes; only a newer one replaces the installed one; broken, unsafe or shrunken packages
  refused; learned portraits kept; updating from a stand-in for GitHub), learning on top of it. The
  tests unpack the repository's package, so they need no network. The package build's wiki
  download is tested against a stand-in for the wiki (new operators, changed and unchanged images,
  busy or broken wiki, broken images, interrupted swaps).

  The other recognition tests use the real roster from `.cache/operator-library` (git-ignored: it's
  game art). A fresh clone downloads it the first time the tests run (about 10 s); CI keeps it in its
  cache, refreshed weekly.
- **Real capture:** a stand-in game window (shows a full-screen window for a few seconds).
- **Gameplay recordings** ([`tests/data/Recordings`](tests/data/Recordings),
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
- **Combat HUD for rotations** ([`Fixtures/battle-hud`](tests/data/Fixtures/battle-hud),
  labels in `labels.json`): skill points, the controlled operator, battle skill buttons, 熔火 and
  enemy statuses on labelled 4K battle frames (UID blacked out), including a cutscene that must
  read as nothing.
- **Operator counters** ([`Fixtures/counters`](tests/data/Fixtures/counters)):
  Typhoeus's 猎矢 (0–4) and 启示 (0–8) on 4K frames of the owner's fight, including the glow over
  full arrows, the fill flash and the HUD sliding away; all but the middle of the HUD band blacked out.
- **Faded portraits** ([`Fixtures/faded-squad`](tests/data/Fixtures/faded-squad)):
  readings four a second through a fight where the game draws portraits see-through; the squad
  must stay named.
- **Controller layout** ([`Fixtures/controller`](tests/data/Fixtures/controller)):
  the layout recognised (controller, LB held, keyboard and mouse in the same fight); HP, combos,
  controlled operator and names read from the diamond, also on simulated 1440p, 1080p, 900p and
  16:10; no panel out of combat, in photo mode or with the target wheel up; and no screenshot
  in the rest of the test data ever taken for a controller layout.
- **Elemental infliction** ([`Fixtures/infliction`](tests/data/Fixtures/infliction)):
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

## Python tools

The app is .NET only. A few developer tools are Python; they all share one virtual environment
at the repository root, set up from [`requirements.txt`](requirements.txt):

```powershell
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt
```

Run them with `.venv/Scripts/python`, never the system Python. When a tool needs another
package, add it to `requirements.txt` with an exact version.

| Tool | Purpose |
|---|---|
| [`tools/guide-video/fetch.py`](tools/guide-video) | Download a rotation guide video (Bilibili, YouTube, …) into `.cache/guide-videos/` with its source and contact sheets. |
| [`tools/ccip-model/prepare.py`](tools/ccip-model) | Rebuild the character recognition model from the original. |

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
  Inn/                   the inn: its data (InnData), gourmands, and the readers of its screens
  Assets/ccip/           the character recognition model (Git LFS; see tools/ccip-model)
  Assets/operator-portraits.json  in-game portrait references (the model's numbers; built from test fixtures)
  PartyReader.cs         squad panel: level, HP, combo, portraits
  HudLayout.cs, SquadGeometry.cs   keyboard-and-mouse or controller layout, and where the squad is in each
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
tools/guide-video/       downloads rotation guide videos (Python, see requirements.txt)
operators/               the operator package EndHud ships and updates from (rebuilt by operator-package.yml)
inn/                     the inn's data, kept by hand; travels in the operator package
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

### Squads of fewer than four

A squad can have one to four operators. With keyboard and mouse the squad panel is drawn from the
left: an empty place has no marks, no HP bar and no fallen mark (marks alone can be hidden by
effects in a full squad; PartyReader.SquadSize), and the ultimate circles
and battle skill buttons from the right, so operator k of n uses button 4 - n + k. With a controller
the diamond keeps its places and draws an empty one as a large faint ring with a slash
(PanelMarkers.IsEmptyPlace). Empty places are PartySlot.Empty: nothing is read, named, carried over
or shown for them. A squad of one or two counts as on screen when every operator in it shows an HP
bar (or has fallen) and the controlled one is among them (Fixtures/small-squads).

The scenery shows through an empty place and can pass for an operator now and then (a road stripe
for a "-" mark, blue water for a full HP bar), and an effect can hide a real one. A wrong size
moves every ultimate circle to the wrong operator, which reads as an ultimate just used. So
[`PartyTracker`](src/EndHud.Vision/PartyTracker.cs) takes a new size only once it repeats: 12
readings in a row (3 s) for a larger squad, 4 (1 s) for a smaller one, holding the last reading
as not seen meanwhile. In the smaller-squad recording scenery made a squad of two look larger for
up to 10 readings in a row; hidden marks made a full squad look smaller for under 4.

### Controller layout

With a controller the game draws another HUD, and switches back and forth with the last device
used, mid-fight too. [`HudLayoutDetector`](src/EndHud.Vision/HudLayout.cs) tells them apart on
every reading by the white "LB" letters in the middle of the squad diamond and the skill diamond,
and the D-pad that replaces the squad's letters while LB is held. Where each slot's parts are in
each layout is in [`SquadGeometry`](src/EndHud.Vision/SquadGeometry.cs):

| | Keyboard and mouse | Controller | Controller, LB held |
|---|---|---|---|
| Squad | a row of four, bottom left | a diamond: slot 1 left, 2 top, 3 right, 4 bottom | the diamond, each slot 16 px further out |
| HP bar (4K) | 166×12 | 166×10 | 166×10 |
| Combo bar | 10–5 rows above the HP bar in both | | |
| Portrait | 128×160 | 102×128 (0.8 of the size) | as controller |
| "-" marks beside the HP bars | drawn (the panel's sign) | not drawn: the combo bars are the sign | not drawn |

Read in the controller layouts: HP, combo cooldowns, the controlled operator, the squad's names,
skill points, fallen operators and ultimate charge (all checked on the recordings, Fixtures/controller).
The ultimates are the small dots at the points of the skill diamond
([`UltimateCircle.Diamond`](src/EndHud.Vision/UltimateGauge.cs)): charging, a pale element-colour
arc from 12 o'clock round a dark dot; ready, a plain element-colour dot in a light ring. While LB
is held they become large rings around the icons, which aren't read: a nearly full charging ring
looks much like a ready one there, and holds last a second or two, so the last reading stays.
Operator counters, buffs and the boss's statuses are read where they are with keyboard and mouse
(bottom and top centre, which the controller layout doesn't move), but the recordings have none
of them to confirm. Not read yet: levels (the squad isn't drawn out of combat there) and battle
skill cooldowns.

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
- With a controller, levels and battle skill cooldowns aren't read yet, and ultimates aren't while
  LB is held (see [Controller layout](#controller-layout)), so rotation steps that wait on a
  battle skill can't tell when it's ready.
- When something can't be read (cutscenes, HUD fades, skill effects covering an indicator), the HUD
  keeps showing the last value it read. So a value can be a few seconds old,
  for example a combo skill used at the moment its bar was hidden still shows "ready" until the bar is read again.
  After the squad panel leaves the screen (menus, the map) the last squad reading also stays up.
- Everything is tuned on 4K recordings; other sizes are tested on frames scaled from them, not on
  real recordings at those sizes. 16:10 and 21:9 assume the HUD is laid out as described above.
- At 1366×768 and smaller, levels aren't read, and out of combat the squad panel can't always be
  recognised (its edge marks are under two pixels wide). Both only affect the debug panel.
- Levels 1–9 are shown as unknown (see above).
- Link's stack count is read by comparing the badge's digit with the game's own 2, 4 and 5 (from
  the status badges). No 3 has been seen in any recording, so a 3 is shown as "?" (not read)
  rather than guessed. Operator counters and Link are read only while the squad panel is in sight:
  over an ultimate's cutscene the last values stay.
- The combo-ready hint needs two "cooling" readings in a row before it trusts that a combo skill was used,
  because the bar is briefly drawn as an empty track while the HUD fades in. A cooldown
  refreshed within about two seconds of being used therefore gets no hint.
- The rotation strip counts a combo skill as used when its bar goes from full to under 30%. A bar
  just under full can dip below "ready" for a moment (0.91–0.99 after, in the recordings), while
  every use read 0–6% after it.
- A battle skill is counted as used when that operator's button turns into a countdown. In the
  recordings so far the button did so in some fights and not in others (where only the shared
  skill points dropped, which doesn't say whose skill it was), and with a controller the buttons
  aren't read: there a battle skill step isn't seen being done, and the strip stays on it until
  the screen clearly matches another step (RotationTracker's jump).
- Hints and the rotation strip follow the screen reading, four times a second by default
  (`OcrIntervalMs`), so they can lag the game by up to a quarter of a second.
- Rotations: Laevatain's 熔火, Typhoeus's 猎矢 and 启示, and Zhuang Fangyi's 青霆剑 are read as
  operator counters so far; counters are read for the controlled operator only. A battle
  skill's cooldown is read as available or cooling, not its seconds. Stack counts aren't read (the status row's
  badges, or the infliction ring's segments), so the editor doesn't offer them, and a shared
  rotation asking for 2+ stacks treats that check as unknown. Infliction rings have been seen at one
  and two stacks, and only for heat and nature; cryo and electric are told apart by colour from the
  wiki's icons but not yet seen in a recording. A few boss icons aren't on the
  wiki's glossary and aren't recognised.

## Game art

EndHud carries the game's art in one place: the operator package
(`operators/operator-library.zip`: avatars, skill icons, status icons from the community wiki,
CC BY-SA 4.0), built in so a first start needs no network. A test checks the build carries exactly
that and the recognition model. The repository's test fixtures, recordings and README pictures are
frames of gameplay, kept for testing and documentation.

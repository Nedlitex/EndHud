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

A rotation is the order a squad's skills are used in: battle skills, combos and ultimates, plus
basic- and heavy-attack steps that are only drawn (EndHud can't see either). In battle, a strip at the top centre shows the
phase of the recognised squad's rotation that fits the game right now, styled after community
rotation charts.

- **One per squad, any slot order.** The strip shows the rotation chosen for those four operators
  in the editor (or the first one made for them).
- **Phases chosen by the game's state.** A rotation is a list of phases (e.g. 充能轴 I, 充能轴 II,
  爆发轴), each with a condition for when it applies ("4 arrows and no ultimate ready"). The strip
  shows the first phase whose condition holds; in between, the one it showed. See
  [Rotations](#rotations).
- **The current step, when the player allows reading their battle-skill keys.** With the menu's
  "read my battle-skill keys" switch (rotation card; `ReadSkillPresses`, off until the player turns
  it on) the strip marks where the player is in the phase: finished steps fade, the current one has
  two chevrons, cyan when it can be used now, white when not yet, with the reason under the strip
  (skill points short, the battle skill cooling down). With the switch off, nothing is read and
  every step looks the same: the phase's steps in order, with their notes. See
  [Battle-skill key reading](#battle-skill-key-reading).
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
  code (`EHR1:` + compressed JSON, short enough for a chat message). Skill points are picked from
  0.0 to 3.0 by 0.1 and charge percentages from 0 to 100 by 1 (with a hint that the bars are read
  to about ±4 %); a value from a file that isn't on the list is kept as it is (before, editing its
  row saved it as 0.5). 复制轴段 copies the selected phase (its steps and entry condition, new id,
  named "X（副本）") right after it. The condition pickers are one control
  ([`ConditionEditor`](src/EndHud/ConditionEditor.cs)) shared with the alert editor.
  The phase and step lists (and the alert editor's rule list) hold `ListBoxItem` entries: a
  `ComboBoxItem` in a `ListBox` takes only a right click (its left button is kept for a parent
  drop-down that isn't there), which is how picking a step needed a right click until 2026-10-24.

### Combat alerts (战斗提示)

The player writes rules ("when 技力 is at least 1.8, say 可以放战技了"; "when the enemy has 寒冷附着
at least 3 stacks …"), global for every fight and squad, and EndHud pops each up in battle when
it turns true. See [Alerts](#alerts) for the file, the firing rule and the editor.

- **Where:** the combo hint's mirror image (owner, 2026-10-22). The pop-ups' right edge is at the
  window's width less the combo hints' left edge, at the combo hints' height, so they follow the
  combo hint when it's dragged; they're never dragged themselves. A circle on the right holds the
  rule's subject, picked from its first condition (inside a group or "not", the first part): the
  skill points (three slanted yellow segments), a boss status's icon, the operator's avatar for a
  skill, charge, counter or "controlling" condition, otherwise "!". A slanted text panel slides
  out of it leftwards, towards the screen centre, with an orange edge (the combo banners' is
  gold); the text wraps to two lines at most and ends in an ellipsis past that
  ([`AlertPopUp`](src/EndHud/AlertPopUp.cs)).
- **How long:** 3 s, or less if the rule stops holding first; at most three at once (the oldest
  goes); all down on leaving battle. No sound.
- **Words:** a rule with no text shows its conditions in words from the `Alert.Describe.*`
  strings, e.g. "技力 ≥ 1.8", "寒冷附着 ≥ 3 层". These are the one wording every condition has
  ([`ConditionWords`](src/EndHud/ConditionWords.cs)): the strip's reason under a waiting step is
  the same statement as a need, `Rotation.Reason.Need` "需要：{0}", or `NeedNow`
  "需要：{0}（当前 {1}）" when the value it compares is read now (skill points, a charge's or
  cooldown's %, a counter found by operator and counter, a status's stacks or level, a phase's
  showings or plays). An unmet `all` lists the needs of its parts that don't hold. A lasting skill
  that stopped is 已结束 everywhere. The icon rule is shared too
  ([`ConditionIcon`](src/EndHud/ConditionIcon.cs)): the alert pop-up and the condition step use it.
- **Menu and tray:** a 战斗提示 card (on/off, the number of rules, 编辑战斗提示…; while the menu is
  open a sample pop-up shows where alerts appear) and a tray item 编辑战斗提示. Editing works with
  the pop-ups off.

### Gauges for Si

With Si in the squad the gauge row has two more circles, both with her avatar:

- **伤害层数** (her battle skill's damage stacks): the number on the green badge left of the HP
  bar while she's controlled, and on the same badge drawn small on her squad portrait while
  she isn't, like every other counter (see [Counters on the portraits](#counters-on-the-portraits))
  ([`OperatorCounters.SiStacks`](src/EndHud.Vision/Operators/OperatorCounters.cs), digits by
  [`BadgeNumber`](src/EndHud.Vision/BadgeNumber.cs)). The game sets no maximum (owner,
  2026-10-22), so the counter is uncapped (`CounterDefinition.Max` is null, `Uncapped`): drawn as a
  large number on a faded avatar, no segments, no "full" glow; "?" before it's read.
- **战技持续** (her battle skill's time left): from her first cast, her battle-skill button's
  butterfly turns into a swirl and a grey ring inside the button drains clockwise over about 22 s
  ([`SkillRing`](src/EndHud.Vision/SkillRing.cs)). Cast again while it lasts, the icon becomes
  a crossed-out butterfly and the ring drains on from where it was; the skill ends only when the
  butterfly is back. It has no circle of its own (owner, 2026-10-23: one circle, the running
  ultimates' purple countdown, no seconds): her stacks' circle carries it, a glowing purple ring
  draining anticlockwise round the number (`Gauge.Running` on the uncapped counter); seen before
  her stacks are read, that circle shows "?". See [the estimate rule](#sis-battle-skill-timer).

### Counters on the portraits

In combat the game draws each operator's counter twice over: beside the HP bar for the controlled
operator, and small (about 0.7 the size) at the lower left of the squad portrait for each of the
others. EndHud reads both ([`OperatorCounters.ReadSquad`](src/EndHud.Vision/Operators/OperatorCounters.cs):
the controlled operator's beside the HP bar, the others' through
[`PortraitCounters`](src/EndHud.Vision/Operators/PortraitCounters.cs) with the same readers), so a
switched-out operator's counter on the gauge row stays live. It's dimmed (the last value seen,
not live: [`CounterMemory`](src/EndHud.Vision/Operators/CounterMemory.cs)) only when a reading
can't tell it: an effect over it, the panel out of sight, or the controller layout.

- **When:** in combat with keyboard and mouse only. Out of combat the game draws no counter on a
  portrait (the level text is there; a fight's end dims the portrait, which read as Typhoeus's
  empty bars and dots), so nothing is read; the combo bars are the sign of combat here. None
  while no slot is clearly controlled. The controller layout's portraits aren't measured (no
  recording uses it), so with a controller only the controlled operator's counter is live.
- **Where:** each counter's place on a portrait (`PortraitCounters.Placements`) was swept over x, y
  and size until every labelled frame of the owner's four recordings read right, and set in the
  middle of that room (the room is in the comments).
- **Is it drawn?** On a portrait the counter sits on art of any colour, and on a portrait with no
  counter grey hair read as Typhoeus's empty bars and Zhuang Fangyi's empty cells. So Typhoeus's
  icon and Zhuang's grid count only when their dark outline shows round them; Si's badge has
  checks of its own. Laevatain's flower has no outline that tells it from a portrait (its edge was
  as clear on portraits without one), so it's read only on Laevatain's own portrait, where it was
  drawn in every in-combat frame recorded: if the game ever leaves it out there, a value could be
  read from the art.
- **Si's badge gone is 0** (owner, 2026-10-23: "When Si's badge disappears from her portrait, is
  that 0?" "yes"). In combat the game drops the badge from her portrait at times (before her first
  stacks, and when they run out). EndHud reads 0 then only when it can see the corner clearly
  (`PortraitCounters.SiBadgeAbsent`): no badge green, and her own art (the grey hair bun and the pale
  strand right of it) shows where the badge would be. That's 22 cells of the badge area, each within
  40 of her bare art's colour on every channel. The clear no-badge frames measured 3.6–20.2 and
  badges of every number 127 or more. An effect or flash over the corner, or the portrait tinted
  red, dimmed or faded see-through, measured 62.1 or more, so those stay unknown. It's read in full
  combat only (combo bars and skill points): as a fight ends the skill points go first while the
  level text fades in under the portrait (10.3, too faint to tell from light scenery).
- **Measured** on 51 labelled frames ([`Fixtures/portrait-counters`](tests/data/Fixtures/portrait-counters)):
  Si's stacks in slots 1 and 3, Laevatain's 熔火 in slot 2, Typhoeus's 猎矢 and 启示 and Zhuang
  Fangyi's 青霆剑 in slot 1. Every 0.5 s over the four recordings, against the counts seen by eye:
  417 right, 101 unknown (88 of them where no counter is drawn), 0 wrong. With no badge read as 0
  (2026-10-23), Si's portrait counter every 0.5 s over her two recordings (`endhud-cli trace-skills`,
  never the last 5 s): 82 right and 80 unknown before, 152 right and 10 unknown after, 0 wrong. The
  10 are the frames that can't be told (tinted, dimmed, faded, the fight ending), and no other
  counter's reading changed. On test_battle_new1 Si is controlled throughout, so her portrait
  counter isn't read there.

### Lasting ultimates

The time bar right above the skill points ([`UltimateTimeBar`](src/EndHud.Vision/UltimateTimeBar.cs))
is **not** the controlled operator's. Measured (Fixtures/lasting-ultimate-background): it shows the
controlled operator's own lasting ultimate when she has one running, otherwise one running for an
operator in the background (Zhuang Fangyi's, cast from the background, ran under Perlica's HP for
30 s). [`LastingUltimates`](src/EndHud.Vision/LastingUltimates.cs) tells whose it is, across
readings, and carries on the ones running off the bar:

- **By its colour** (`LastingUltimates.OwnerByColour`): the bar is in its operator's colour, read
  as a hue. Laevatain red (hue 3–8), Zhuang Fangyi gold (47–51; a dimmed gold bar is still 49–50),
  Yvonne teal (171–176; named by the owner), measured every 0.25 s over the two Zhuang recordings
  and on the fixtures at 4K and ten simulated 16:9-class screens; a bar within 15 degrees of one of
  them is hers, if she's in the squad (12 degrees of room or more on each; red and gold, the
  nearest two, are 44 apart). A bar of another colour, or of an operator not in the squad, is
  shown for no one: no other lasting ultimate has been measured.
- **Yvonne's**, on test_zhuang_laevatine_background1.mp4: her ultimate's cutscene 39.25–41 s, her
  bar full at 41.25 s and 0.57 at 44.25 s, about 0.14 a second (0.15 while she was switched out
  between, as while controlled), so about 7 s in all, over by about 48 s; at 56 s, with her
  controlled again, the bar is Zhuang's. Her bar is on screen only while she's controlled, so it
  hasn't been seen under another operator; it drained on while she was switched out, and is
  carried on then like the others.
- **When its colour isn't read** (a reading without it): by inference, the controlled operator's
  if hers runs or she just spent hers, else the running one nearest to where it would be by now,
  else the one just spent. This picked wrong on the recording (a teal bar given to Laevatain at
  41.25 s, Yvonne's) before the colour was read.
- **Carried on:** a running one off the bar (another's shown, or the bar hidden a moment) goes on
  at the rate it drained, shown "≈"; its time stands still while the panel is out of sight (the
  game pauses it in a cutscene). No bar for 2 s with the panel in sight ends them all.
- Its own operator's ultimate circle shows it, whoever is controlled (`GaugesView`). The overlay
  works them out once per reading, before anything reads the state, and puts them on
  `PartyState.RunningUltimates`: the gauges, the rotation strip and the alerts all use that, so
  "ultimate active" holds while Zhuang's runs in the background.

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

"Export diagnostics" (tray, or the menu's General card) comes in two sizes, each its own button and
tray item. The mini one writes `EndHud-diagnostics-mini-<time>.zip` to the
desktop ([`DiagnosticExport`](src/EndHud/DiagnosticExport.cs)): `summary.txt` (version, PC, the game
window, what reading is doing, the last reading per slot, the operator library on disk),
`logs/endhud.log`, `config.json`, `hud.jpg` (the bottom corners of the last game picture read, squad
and skill buttons side by side, with the UID area, bottom left at 4K x 200–500, 28–60 px above the
bottom, blacked out) and `recent/readings.txt`, what was read over the last ~12 s. Players upload it
to sites that take small files (a 19 KB zip was too large for one), so it stays under 20 KB
(`MaxZipBytes`): the text gets about 11 KB, the log cut to its end until it fits (all three files
joined, oldest first; a player's 250 KB log kept its last 80 KB), and the picture takes the largest
size that still fits, or is left out. The full one, `EndHud-diagnostics-full-<time>.zip` (several
MB, for GitHub or a file share), has the same summary, settings and `hud.jpg`, plus every log file
whole, `game.png` (the last picture read, full size) and `recent/`: the pictures read over the last
~12 s (one every 2 s, six, at most 960 px wide), with `readings.txt` naming what was read from each;
the UID is blacked out in every picture. JPEGs are written with WPF's encoder: WinRT's takes its
quality in a property set whose factory belongs to the thread that first made one, and failed once
that thread had ended. The summary also lists the graphics. Learned portraits aren't included.

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

**Fast release, only when there's no time for the normal one:** Actions tab → "Fast release" →
Run workflow, with the version (1.2.0), on the branch to release.
[`.github/workflows/fast-release.yml`](.github/workflows/fast-release.yml) builds the same two
files and publishes them straight away, tagging the commit `v1.2.0`; it skips the tests and the
clean-machine install, self-test and update checks below. Every other release goes through the
normal path. It builds the branch as GitHub has it: push first, and check the push went through.
Its only check is the version (three numbers, above 0.0.1, `v` allowed), typed into the run and
handed to the script as an environment variable; `FastReleaseWorkflowTests` parses the workflow and
runs that step's script.

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
| `CloseWithGame` | `false` | Quit EndHud once the game window, seen earlier in this run, has been gone 10 s ([`GameExitWatch`](src/EndHud/GameExitWatch.cs); the wait rides out the window being made again when switching fullscreen). Off by default (owner). |
| `AutoShowOnGameFocus` | `true` | Show the HUD when the game has focus and hide it otherwise. When `false`, the HUD only shows after Ctrl+Shift+H. |
| `PartyOcrEnabled` | `true` | Read the squad panel (names, HP, skills, levels). |
| `OcrIntervalMs` | `250` | How often the screen is read, in milliseconds (minimum 250). Rotations need it fast: skill points are spent and refilled quickly. |
| `SettingsVersion` | current | The file's version, to update old defaults the player never chose (the first start writes the whole file): before version 2 an `OcrIntervalMs` of 1000 gets 250; before 3 a `GaugesScale` of 50% gets 100%; before 5 the inn banners' old default places get the new one; before 6 an `InnStripOpacity` of 100% gets 85%. |
| `Language` | `zh-CN` | UI language: picks `i18n/<Language>.xaml`. |
| `ComboHintEnabled` | `true` | Show the combo-ready hint (needs `PartyOcrEnabled`). |
| `ComboHintMode` | `WhenReady` | `WhenReady`: for `ComboHintSeconds` after the cooldown finishes. `WhileReady`: until the combo skill is used. |
| `ComboHintSeconds` | `3` | How long a `WhenReady` hint stays up. |
| `ComboHintX`, `ComboHintY` | `0.2`, `0.36` | Where the hint sits: its top-left corner as a fraction of the game window's width and height. The alert pop-ups mirror it (right edge at 1 − `ComboHintX`, same `ComboHintY`). |
| `AlertsEnabled` | `true` | Pop up the player's alert rules (`alerts.json`) in battle (needs `PartyOcrEnabled`). An operational switch over a feature that ships on: the rules list decides whether anything shows. A `config.json` from before it reads as on. |
| `RotationEnabled` | `true` | Show the rotation strip when the recognised squad has a rotation. |
| `RotationCenterX`, `RotationY` | `0.5`, `0.13` | Where the strip sits, as a fraction of the game window: the middle of its width (it stays centred as it changes width) and its top. |
| `RotationScale` | `1` | The strip's size on top of following the game window, 0.5–2 (the menu's 大小 slider; a value outside is brought back into range). |
| `GaugesEnabled` | `true` | Show the gauges in battle: team buffs (Link with its stacks, once seen in the battle), and each recognised operator's ultimate charge and own counters, as circles with the operator's avatar in the middle. While a lasting ultimate runs (Laevatain's, Zhuang Fangyi's, Yvonne's), its own operator's ultimate circle shows its time left instead, whoever is controlled: violet, glowing, draining anticlockwise, labelled 持续中 ("≈持续中" while it's carried on off the bar); see [Lasting ultimates](#lasting-ultimates). Counters are live for the whole squad in combat with keyboard and mouse (the switched-out operators' are read on their portraits). With Si in the squad: her damage stacks as a plain number, and while her battle skill lasts its time left (grey-white, "≈" when estimated); see [Gauges for Si](#gauges-for-si). |
| `GaugesScale` | `1` | The gauges' size on top of following the game window (the menu's slider): 0.5–2; a value outside that is clamped when the file is read. |
| `GaugesCenterX`, `GaugesBottomY` | `0.5`, `0.885` | Where the gauges sit, as a fraction of the game window: the middle of their width and their bottom edge (so a bigger size grows upwards): bottom middle, just above the game's HP and skill-point bars. |
| `ReadSkillPresses` | `false` | Read the battle-skill keys and controller chords so the rotation strip can mark the current step. The player's consent: with it off no input is read at all (Windows isn't even asked for it). Not a rollout flag; see [Battle-skill key reading](#battle-skill-key-reading). |
| `SkillKeys` | `[2, 3, 4, 5]` | The key that casts each slot's battle skill, slot 1 first, as the key's scan code (plus 256 for an extended key): the defaults are the top-row 1, 2, 3, 4. Chosen in the menu by pressing the key. A list that isn't four different real keys goes back to the defaults when the file is read. |
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
     timestamps, its date kept when nothing changed). With `--keep-roster` it asks the wiki nothing and puts
     only the inn's data into the package already there (an inn-only edit, or the wiki down).
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

### The session's advice

The session's advice is worked out from a model of the kitchens, with no screen reading of its
own. [`KitchenMath`](src/EndHud.Vision/Inn/KitchenMath.cs) gives a dish's cook time and choice
(珍馐) chance in a kitchen: the Inn Rating's base (2/4/6/8 %) + the chefs' choice bonuses + Si's
品质把控 + the flavor bonuses, which reproduces both recorded 派单 panels (18 %, every time listed).
A kitchen queues 3 dishes, one cooking ([`KitchenQueue`](src/EndHud.Vision/Inn/KitchenQueue.cs));
the counter holds 8 (the simulator's model; the advice never gates on it, below). [`DispatchAdvisor`](src/EndHud.Vision/Inn/DispatchAdvisor.cs)
sends each ordered dish not already carried, queued, or out of a kitchen for it to the kitchen that has it out soonest
(ties: the better choice chance); cooks ahead (a dish matching the theme first, `InnState.ThemeItems`: the theme's hot
items and its text's preference, as the setup's plan counts them, two matches before one; then the dish cooked ahead
least so far; then one whose type the kitchen's chefs speed up, `KitchenMath.SpeedsType`; then the slowest. Owner,
2026-10-21, test_yuanyuan6 at 201 s: "the first kitchen has all the bonus for 主食, but the precook suggest a 甜点, which
completely wasted the bonus". Until then the theme and the kitchen weren't asked, and "the slowest" put first the dishes a
kitchen ISN'T set up for: 金丝小贝, a 35 s dessert, in a kitchen speeding 主食 by 40 %, in a 主食/汤羹 theme; on the replay
kitchen 1 was told 金丝小贝 from 197.7 s to 221.7 s, now 界龙肉卷. In the simulator every average wait went down, recorded
kitchens 29.1/29.6/32.7 s → 28.8/26.0/27.4 s acting at once/every 10 s/every 20 s, live ones 40.1/40.7/40.5 → 38.8/37.9/39.4
s, while the idle kitchen-seconds went up 107 → 209, the dishes cooked ahead being quicker ones; never without stock,
leaving one slot free for orders, with no counter cap); fills every free
slot at the session's start and when Full Blaze is near. **No counter gate** (owner, 2026-10-21: "it should just not
gate on how full it is, it offers little value"): EndHud only estimates the counter, never reads it, and on
test_yuanyuan4 the estimate went "full" (9 dishes on a counter of 8) from about 250 s, which stopped all cooking ahead
and, with dishes taken to be there that weren't, covered real orders: no order call from 251.6 s to 350.4 s while
tables waited up to 65 s. A dish not used gives its ingredients back, so cooking ahead is never a waste. So a dish
cooked ahead on the counter covers no order and cooking ahead has no cap; what's left of the counter: a dish that came
out of a kitchen for an order (it was that order's dish in the queue) still covers that order until it's served
(`InnState.OutForOrders`; without it, an order whose dish had just come out was sent to a kitchen again and every
kitchen's call reshuffled, test_yuanyuan3 362.4 s), the last call hands out what's on the counter, and cooking ahead
prefers dishes there are fewer of. Kept: 3 slots a kitchen, all but one slot in a kitchen nothing was ordered for,
nothing cooked ahead in the last 40 s of serving, no advice for a kitchen with no chef. Every dish, ordered or cooked ahead, goes to
the kitchen where it comes out first (owner, 2026-10-19): the seconds left on the dish that kitchen
is cooking, plus the dishes waiting behind it, plus this dish's cook time there; there's no
"idle kitchen first" rule, an idle kitchen wins only when that makes it the earliest. It also and suggests taking a dish cooked
ahead out for an order only when that wins by more than 5 s (a cooking dish only when barely
started, since its progress is taken as lost), and never a dish the player queued because a call said so while it still
comes out before serving ends (`InnState.QueuedAsCalled`: a dish put in any kitchen's queue while a call names it;
test_yuanyuan5 at 04:16: "厨房二 预做：界龙肉卷" followed, then 10 s later "先拿掉界龙肉卷，再排这个" for a new order; at 00:44
福寿全, called for kitchen 2 and queued in kitchen 1, called out of kitchen 1; the advice doesn't undo its own word). A dish in the player's hand
(the tray, `InnState.Carried`) covers an order of it first. A kitchen with no chef cannot cook (owner): one rule,
`KitchenMath.Cooks`, which the planner, the advisor and the session all go by; such a kitchen gets no
advice and isn't listed (a kitchen whose chefs weren't seen, `Kitchen.ChefsUnknown`, is taken to
cook, at base speed). The end of a session (owner): after the 5:00 timer reaches 00:00 the inn
keeps serving the seated guests for 18 s (`InnState.ServingAfterCloseSeconds`), and every end rule
counts against that serving time (`InnState.ServingSecondsLeft`, a required field: timer + 18 s
before closing; after, 18 s from when the session first read 00:00, since the timer stays at 00:00).
Nothing is cooked ahead in the last 40 s of serving (`NoCookAheadSeconds`), nor ever a dish that
wouldn't come out before serving ends. The last call is per table and can come before 00:00: a table
waiting for a dish whose own dish can no longer come out in time (its queued dish's time, or the
best kitchen's queue + cook time, or no stock) is handed a dish left on the counter or carried, its
own dish if there, else a wrong one ("a wrong dish still earns some money"), oldest table first; a
table whose dish can still make it waits for it. A table waiting only for a drink never gets a dish
(it wouldn't be served; drinks aren't outstanding dishes). Once closed, the dishes on the counter are
also listed for the tables that ordered them. [`FullBlazeAdvisor`](src/EndHud.Vision/Inn/FullBlaze.cs)
says when to start Full Blaze (满堂彩); whether it's running is read off the meter, whose button turns into a red circle
counting down what's left (`FireMeter.BlazeLeft`), and until that's read inferred from the ready button going. Until
2026-10-21 the countdown wasn't read: through the whole of Full Blaze the meter read as nothing, the tracker never saw the
ready button go, and "开启满堂彩（R）" stayed up to the end of the session (test_yuanyuan5 from 333 s; also visible on
test_yuanyuan3's session-hud-faded frame). A ready label read while the countdown shows (the game's line "灶火正旺，满堂生彩!"
stands where the label was) changes nothing.
[`GourmandAdvisor`](src/EndHud.Vision/Inn/GourmandAdvisor.cs) ranks what meets a gourmand's
request and, for two requests, the order to serve them in. [`InnSimulator`](src/EndHud.Vision/Inn/InnSimulator.cs)
replays an order stream through all this, serving 18 s after 5:00 like the game; on the recorded
order times (served to the end, as they run past 5:00), following the advice
halves the average wait against sending each dish to the first kitchen with room. What isn't known
is a named constant saying so (a removed dish's progress and stock, the replace margin, what a full
counter does to a kitchen).

### Planning a session

[`InnPlanner`](src/EndHud.Vision/Inn/InnPlanner.cs) picks the chefs for each kitchen and the menu's
dishes and drinks for a 5-minute session, from the chefs and dishes the player has (Si and the
Endministrator never placed; once the menu screen has shown cards, only the dishes and drinks it
showed). The order of what counts: every menu rule on screen first; then the theme's matching dishes;
then the money. A dish matches when one of its traits or its dish type is among the theme's hot items
or in its text's guest preference (`InnPlanner.ThemeItems`); the owner (2026-10-20): "matching dishes
will reward a lot more at the end, so always prefer the matching ones". So a menu carries every
matching dish it can hold (a dish matching two items before one matching one), and a non-matching dish
only where the rules need one (at least 6 dishes, 3 types); drinks likewise (non-matching drinks only
for the minimum; when every drink matches, as with drinks hot, the drinks are chosen by income as
before). This is `ThemeMatch`, compared before the objective and the income in both the model's and
the simulator's scores (`InnPlanScore.Match`, `InnSimulatedScore.Match`), so every step of the search,
the quick plan included, keeps to it. With the menu settled, the chefs are whoever cooks those dishes
best. Guests come on a clock, not as fast as the
kitchens cook: test_yuanyuan1's first orders came 12 s apart with tables free, about 25 guests a
session (`InnGuests`). Guests never leave, however long they wait (owner, 2026-10-20): the simulator's
guests wait for every dish. An order is 1.2 dishes and 0.75 drinks. Guests pick uniformly from the menu
except for what the theme's text says they prefer ("客人更倾向带有[素食]、[滋补]特性的菜品", 养身心;
"客人更倾向汤羹类菜品", 舌上清流: `InnObjective.GuestPreference`, into `InnPlanInput.Preferred`): a dish
with a preferred trait or of a preferred type is picked `PreferenceWeight` (5) times as often, in the
model's kitchen balance and income and in the simulator's order draws alike. The weight was measured
with `trace-inn` on test_yuanyuan2 (养身心: 3 of 4 dishes ordered among the 3 preferred of 8; the most
likely weight, but four dishes give an interval of 0.6 to over 100, so re-measure on a longer
session). The 热门 chips alone don't shift orders (two 海陆宴 sessions, whose text names no
preference: hot dishes 1.2× as often, 0.6–2.3, 38 dishes), so the preference is the text's; only
before the text is read do the chips (else the theme's data) stand in (`InnPlanner.PreferenceOf`), and a
later reading that lost the text's last sentence keeps the remembered text (`InnSetupMemory.Merge`).
The preference weight sets the order mix (and so the chefs: the preferred dishes are most of the
orders, so chefs speeding their types and flavours win) but no longer which dishes go on: matching
dishes go on whatever they cost the kitchens. A dish is worth its price × (1 + 10 % per matched 热门 trait), plus the tips a 珍馐 dish is
taken to bring (an unmeasured share, doubled when the theme's text says 珍馐 tips are raised:
`ChoiceDishTipShare`). A theme's "出售N道珍馐菜品" is a target that ranks next after the theme match, before the income. The model
(each dish shared among the kitchens so they finish together, orders capped by the guests' clock
and the tables) drives a beam search over bonus profiles and a greedy-then-swap menu search, also
started from the dearest menu; tests compare it with brute force on small cases (`PlanByModel`).
The model only proposes: the best line-ups, each with menus from the dearest to the quickest, are
played in the simulator (`InnSimulator.RunGuests`: guests seated on the clock, the dispatch advice
followed) over six sessions of the same guests, and the plan is the one that earns most there
(`InnPlan.Simulated`). The plan carries the opening precook. A kitchen with no chef doesn't cook
(`KitchenMath.Cooks`). Measured (Release, InnSetupCostTests): 5–12 s for three empty kitchens of two
over the whole roster, almost all of it the model's beam search (the simulator adds ~1.5 s); under
1 s for the menu of placed chefs. So the setup advice shows a quicker answer first:
`PlanQuickly` runs the same search with less effort (one narrower beam from one starting menu, each
line-up judged by that menu alone, no polish): 0.3–0.4 s, within ~5 % of the full search's income
under the model, every rule still met. `Plan` takes a cancellation token, checked at each beam place,
each polish round and each simulated candidate, so a plan for inputs that have since changed stops
within ~0.1 s.

Chefs the player has already put in (`PlanKitchen.Placed`, null for a place seen taken whose chef
wasn't named: a cook of no bonus) stay first in their kitchen, are put nowhere else and are never
swapped out; the beam fills only the empty places. With every place taken only the menu is planned.

[`InnSuggester`](src/EndHud.Vision/Inn/InnSuggestions.cs) turns a plan into what the setup advice
shows per kitchen, with substitutes for a player who lacks a chef, dish or drink the plan names.
Each menu dish goes under the kitchen that cooks the larger share of it (`InnPlanner.MainKitchens`):
the balance is solved through its dual (kitchen weights), and `InnPlanner.Shares` recovers the split
from them, a dish only where its weighted cook time is least and dishes tied between kitchens shared
by a small max-flow over the load left; the tests check the busiest kitchen's load equals the
balance on random cases (and for an order mix leaning on preferred dishes). A kitchen that cooks yet
holds the larger share of no dish (a preferred dish most guests order, spread over the kitchens)
takes the dish it cooks most of from a kitchen left with another, so every panel says what to cook.
Chef substitutes are ranked by what each adds in that kitchen (the plan's income with them in a
place less with a no-bonus cook there, the better place), then, on an equal income, by the
kitchen-seconds per dish they save (`InnPlanner.KitchenSecondsPerDish`): when the guests' clock,
not the kitchens, sets the orders a faster chef earns nothing more under the model, and ranking by
income alone listed no substitute at all; dish substitutes matching the theme first (the most
matches first: a matching dish's stand-in is another matching dish), then by value per second cooked
in that kitchen (price with the 热门 bonus over its cook time there), of the kitchen's own dish types
first within each; drink substitutes matching first, then by value. Substitutes are handed out across
the kitchens best first, at most 3 a row and each to one kitchen only, never a planned chef, menu
dish or drink; a kitchen left with none takes the best listed elsewhere. A kitchen with no planned
chef is unused and gets nothing. A kitchen's chefs to recommend are only those for its empty places
(`KitchenSuggestion.Placed` counts the kept ones); a full kitchen (`Full`) gets no chefs and no chef
substitutes, and a placed chef is never a substitute anywhere.

A theme's menu rule (`DishRule`) asks for at least N dishes with any of some traits
("至少有2道带有[素食]或[滋补]特性的菜品", 养身心) or of any of some dish types
("至少有1道[汤羹]类菜品", 舌上清流; the type's icon stands in the brackets, so a bracket is matched by
the type name it contains). `DishRule.ParseAll` joins 菜单要求's lines (a rule may wrap) and cuts
them at each "至少"; what isn't understood is kept as read and logged, the plan is made without it and the menu
advice says so in one plain sentence (`Inn.Setup.RuleNotUnderstood`), never with OCR's reading of it (owner, live
2026-10-21, test_yuanyuan8: the panel showed "看不懂菜单要求：至少有1道@13小时39分钟0流行趋势", "what is the @13小时 crap").
"且[主食]类、[菜肴]类、[甜品]类、[汤羹]类菜品，每种至少有1道" (the gourmand theme 食有老饕, after "至少需要8道菜品和6种饮品") is
N dishes of each type listed, one rule per type (`DishRule.EachTypeAtLeast`); the setup screen's "至少需要N道菜品和M种饮品"
counts before the menu screen is read (`InnSetupMemory`), so the plan isn't made for the rating's 6 and 3. That theme's
rules sit above a 流行趋势 bar (a chip, and a refresh countdown at its right): the setup reader's rules section and the menu
screen's requirement lines end at that heading (`SetupReader`, `MenuRequirementsReader.Requirements`); before, the countdown
and the bar's title were read as the rule's end, the rule went not understood and the plan had no 菜肴. The 热门 chips are traits, dish types (汤羹) or 饮品 (any drink),
matched longest name first (`SetupReader.HotIds`: a 甜品 chip is the dessert type, not also the
trait 甜).

### Reading the inn's screens

The readers in [`src/EndHud.Vision/Inn/`](src/EndHud.Vision/Inn) return data records and draw
nothing; each returns null (or a null field) for what it can't read clearly, never a guess. The
app runs them in its reading loop (see "The inn helper in the app" below). Chinese text is read with the Windows OCR engine for Simplified
Chinese ([`InnText`](src/EndHud.Vision/Inn/InnText.cs): `InnText.TryCreate()` is null without that
language). Measured on the owner's two recordings (`tests/data/Fixtures/inn`, what each frame
shows in its README), at 4K and scaled to 1440p and 1080p (`SimulatedScreen`); the setup screens,
the menu and the dispatch panel are placed on the centred 16:9 area, the session's HUD by its
corners, and other window shapes are EndHud's assumption, not checked.

| Reader | Reads | On the frames |
|---|---|---|
| `InnScreen.DetectAsync` | the setup screen, the theme list (its own kind: it starts a new setup), chef picker, menu, session, dispatch panel (titles by OCR, the timer by its digits) | all 20 frames, at every size (the session's first frame, fading in, at 4K only) |
| `InnScreen.ReadPickerKitchenAsync` | which kitchen a chef picker is for, off its title (kitchen one = 1) | chef-picker, at every size |
| `InnScreen.ReadClock` | time left, closed at 00:00, and whether it was read in its pill under 结束经营 (`BelowButton`: the panel open puts it at the top) | every session frame; where, on 6 frames at every size |
| `FireMeterReader` | fire "N/100", streak, ready (red button, 开启满堂彩), Full Blaze running (the red circle's countdown, `BlazeLeft`) | all, at every size; hidden behind the dispatch panel; Full Blaze on 2 frames at every size |
| `TrayReader` | the tray of what the player holds (five slots at the bottom middle): each slot's dish or drink by picture, the 珍馐 badge, empty; on screen only with the 派单 button beside it | 12 frames of four sessions (and 4 without it) at 4K, 1440p and 1080p: every slot right, right pictures 6.4–12.9 against ≥ 31.6 for the next; the HUD fading in reads "not told apart", never a guess; ~19 ms a frame holding five dishes at 4K (Release), under 1 ms empty |
| `TableCardReader` | each table's state and, for an order, its dishes and drinks by picture and whether each is served (its pip green, or the blue auto-serve spinner on it); for a gourmand's order, whether each request is resolved (its pip mint green served, yellow-green imperfect, red failed: since 2026-10-21; before, only the order cards' lime green counted and no gourmand request ever read resolved) | 56/56 items and 90/90 cards at 4K, 1440p and 1080p; gourmands' pips on 5 cards of two test_yuanyuan8 frames at every size |
| `DispatchPanelReader` | whether the panel is open (a dark tab AND the first dish card's light face: 10 of 10 points on every panel frame, at most 3 on a session frame), kitchen shown, chefs, choice-dish chance, each dish's cook time and the card it's on (`KitchenDish.Card`; where each card and tab is: `CardArea`, `TabArea`), the queue (dish, time left) | 114–115 of 115 values per size, none wrong; every dish named on its card, each card's place its outline (3 frames, every size); no panel on 10 session frames with it closed, at every size |
| `BubbleReader` | every gourmand's speech bubble on screen (each bubble-sized patch of cream, the largest first: the feast mode shows up to three at once), its rows (a row the engine returns as two lines joined again), the icon before each request (plates for a dish, a bottle for a drink): where each request starts, and whether it is resolved (the icon turned into a face: green, yellow or red). The requests: the lines cut at the icons, each part matched with `InnData.MatchSaid`; without icons (a bubble cut by the screen's edge) the lines as one request or cut once into two; last, for lines OCR ran together, every run of up to 6 pieces cut at sentence ends and commas. A request said exactly matches however short (the feast pool's tenth gourmand's drink is three letters). The text's left edge is measured in character heights, since the bubble's size follows the camera | three recorded bubbles (the owner's soup-and-drink gourmand on three frames), both requests and only them, at every size; six test_yuanyuan8 frames (one or two bubbles, wrapped, faces, two soups) at every size, each request and its state (a far bubble may lose a request to an OCR slip, never read one wrong); one behind the session's stats box reads only what shows; every pair of a gourmand's requests in the data, on their own lines or wrapped at ten letters, both found |
| `SetupReader` | theme, objective, menu rules (down to the next heading: 加分项, 热门, 流行趋势), hot traits, dish types and 饮品, kitchens with their chef places, how many hold a chef (the bar under each: red or grey) and the chefs | all at 4K; objective and rules slip at 1440p; setup-gourmand-theme's rules whole and understood at every size |
| `MenuReader` | tab, cards in view and whether each is on the menu, stock ∞, the counters | tab and cards at every size; stock and counters partly |
| `MenuRequirementsReader` | the menu screen's 菜单要求: Inn Rating ("4级客栈要求"), types, dishes and drinks at least, the theme's trait or dish-type rule, down to the 流行趋势 heading | all five on both menu frames, at every size; menu-gourmand-theme (专属菜单要求: 8 dishes, 6 drinks, one of each type) at every size |

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
  on the frames right pictures score 3–12 and the next ones are ≥ 7.6 behind. An item is served
  when its pip under the number is green or the blue spinner (a ring of cyan dots, the game serving
  it on its own) sits on the disc's lower right (owner, 2026-10-19). The green smiley that can show
  there means nothing useful and isn't read.
- **OCR'd names** are matched to the data by the longest run of letters they share
  ([`InnNames`](src/EndHud.Vision/Inn/InnNames.cs)), and read at a few sizes (and once in the
  region's own greys, which a small window needs) until one in the data comes out.
- **One OCR engine, one recognition at a time**: two callers sharing it read garbage now and then.
- **The menu's requirements** ([`MenuRequirementsReader`](src/EndHud.Vision/Inn/MenuRequirementsReader.cs)):
  a met line turns light grey, so the ink takes that grey too; two OCR slips seen on the frames are
  put right ("品" as "口口", a line's start lost under the yellow tick). Only the wordings seen at
  Inn Rating 4 are understood.
- **The dispatch panel open or not**: its tabs' pixels alone read the dark bar behind the session's theme line as a
  panel of kitchen 1 of 1 on 13 of 30 closed-panel frames (owner, live 2026-10-21: the panel's "queue this" mark over
  the dining room); the first dish card's light face must show too (a menu always has a dish).
- **A theme's rule that wraps** loses its last character now and then (the last character of "dishes", the tea-break theme's
  dessert half, 2026-10-21): a rule is read without it (`DishRule`).
- Of the chef picker only its title is read, for the kitchen it's for; its grid isn't (the "recommended" card marks
  went on 2026-10-21, and with them the picker's grid reader and the menu cards' places; the text panels stay).

Not built yet: the drink bar and the results screen (no frame recorded), a one-item order's
picture place (no frame of one), Si's assists on 营业设定 (the planner takes 品质把控 as 0; it only
breaks ties).

### The inn helper in the app

The reading loop ([`PartyMonitor`](src/EndHud/PartyMonitor.cs)) looks for the inn before the squad
panel ([`InnFrameReader`](src/EndHud/InnReading.cs)) and, on a frame of the inn, raises
`InnUpdated(InnReading)` instead of reading the squad panel (the squad tracker is told "no panel
seen", as it was before). Keeping a fight free of it: while the squad panel is on screen the inn
is never looked for; with neither on screen it's looked for once a second (one small OCR read of
the title); in a session the screen is told by the timer's digits and the dispatch panel's tabs
(pixels, no OCR); the setup and menu screens are read at once when one opens, then at most every
0.5 s (`InnFrameReader.SetupEvery`; measured at 4K: the setup screen ~300 ms of OCR, the menu ~120 ms;
of a chef picker its title, ~1 ms; of the theme list nothing), and the gourmand's bubble only
while a gourmand is at a table. Without the Simplified Chinese OCR language only the timer is
read (`ClockOnlyScreens`); the first inn screen seen then puts what to install in a tray balloon and the
log, once per run (`OverlayWindow.InnProblemFound`), never in a panel over the game (owner, live
2026-10-20: lines over the game about what can't be seen are "disturbing"). The readers sit behind
`IInnScreens`, which the wire-up tests replace.

Every inn screen survives a missed frame (owner, 2026-10-19: the strip flashed as detection missed
single frames while the dispatch panel opened, the HUD faded or the camera moved; and on the setup
screens the log showed "inn: left" then the screen found again ~1.2 s later, the advice put away
meanwhile): a frame with no inn screen keeps the last screen (a held reading, `Fresh` false) and its
strip or advice for up to 8 s (`PartyMonitor.SessionHold`), logged once per streak. Meanwhile the inn
is looked for on every frame (not once a second), and the same screen found again keeps its reading
cadence. The inn is left at once when the squad panel shows or the inn has closed and its 18 s of
serving are over, else after 8 s with no inn screen.

**Alt-tab** (owner, live 2026-10-20: the overlay's reading is off whenever another window is in front, and the
owner alt-tabs every 5–30 s). Time the game spends in the background is never time spent away from the inn:
[`ReadingClock`](src/EndHud/ReadingClock.cs) (`PartyMonitor.Reading`) runs only between frames read (a stall
counts at most 5 s) and stands still while the reading is off. `InnSession`'s 30 s forget and 15 s session end
are measured in it, so the kitchens set before a 27 s alt-tab are still known on the menu screen (the bug: "还没看到
厨房" right after setting every kitchen); 30 s of other screens read (a fight, the open world) still forgets. The
first frame back reports the time skipped, and the inn's holds pick up as if none had passed
(`InnFrameReader.Resume`, the missed-frame streak): a first frame that misses the inn screen is held and the
screen found on the next frame, not a second later. The window capture is kept through the pause and let go only
when the window is gone, minimised, another window or its client area changed size; `SetTarget` wakes the loop
at once when the reading turns back on, and the kept capture must deliver a new picture within 0.5 s
(`PartyMonitor.ResumeWait`; frames during the pause were dropped, so the last copied one may be stale) or it is
built again. "reading on/off" is logged once per change; "capture started" only when a capture is really built.
Measured over a stand-in window with the real loop and readers (InnResumeLatencyTests, 1080p menu screen, 5
resumes): "reading on" to the first fresh menu reading, median 623–791 ms (max 1244 ms, 6 captures built) before,
415 ms (max 444 ms, 1 capture) after. The overlay shows the remembered advice the moment it's back in front; this
is the time to a new reading of the screen.

[`InnSession`](src/EndHud/InnSession.cs) (on the UI thread, memory only) keeps what the advice needs:
before a session and into it, the setup as chosen so far ([`InnSetupMemory`](src/EndHud/InnSetupMemory.cs),
below); during it, each kitchen's chefs and queue (read off the dispatch
panel when it shows that kitchen, otherwise moved on by cook times since: an estimate, and a dish
turning served on a table card takes it off the counter or out of the estimate at once), the orders
(oldest first, served items left out), dishes on the counter (estimated: each dish out of a kitchen is out for the next
order of it still waiting for one, else cooked ahead; one out for an order turns cooked ahead if its order goes), the
dishes in the player's hand (the tray, read on every frame without the panel and taken once two readings agree; a tray
with a picture not told apart says nothing): a dish coming into the hand leaves the counter estimate (or a kitchen the
estimate still has it cooking in), one served from the hand leaves the counter alone (the card and the tray read a frame
apart either way), one put down with no table read served within `InnSession.HandSettlesAfter` (3 s) is back on the
counter (the game's 放回出餐台); drinks in hand aren't dishes. Until 2026-10-21 the hand wasn't read and `InnState.Carried`
was always empty: test_yuanyuan5 at 02:12, 至臻鳞肉卷 in hand and the call "厨房三：至臻鳞肉卷 / 桌4 点的". Then the
timer (0 s left once closed, for the end of the session's advice), the fire meter with
`FullBlazeTracker`, and each gourmand's request. A gourmand's request is kept from the moment it's read
until their order is fulfilled (owner, live 2026-10-20: "if you cannot see but the order is not fulfilled,
just keep showing"): at once when their card reads them eating, or when it reads another state on every
reading for 3 s (`InnSession.GourmandGoneAfter`), or when the session ends; a card not read changes nothing.
The bug it fixes, measured on that session's frames: while the camera moves the HUD fades, and on the faded
frame every table card reads empty (session-hud-faded.jpg), which dropped the request at once.
A bubble doesn't say whose it is, so its words and the cards decide (`InnSession.TakeBubble`; owner, live 2026-10-21: table 4's
gourmand asked for a soup without seed oil and a drink without alcohol, and the circles showed a sour dessert and a soup
without seafood for 46 s, another gourmand's words). Every bubble on the frame is taken, and each gourmand's words in one on
their own (two bubbles side by side can be found as one patch): the table holding most of these words, or the only one holding
words of the same gourmand, keeps them, read once more (every reading is kept, and the one the most readings agree with
stands: an OCR slip that names another of the gourmand's requests now and then changes nothing); words of a gourmand served
within 30 s (`GourmandEchoFor`) are their bubble seen again, nobody's new request; otherwise the newest table whose
gourmand has **ordered** (their card turned to the order's icons: `TakeGourmandCards`, which a HUD fade never resets) and
has no words yet takes them. A gourmand's words show the moment the player takes their order, so a gourmand only arrived
or choosing has said nothing. A request is resolved once its card's pip (two readings in a row) or its
bubble's icon says so, and is no longer asked; a gourmand with nothing left goes; a new gourmand arriving at a table forgets
the last one's words; a HUD fade (every card read empty) says nothing about any table.

**test_yuanyuan8** (owner, 2026-10-21: "MOST of the gourmand requirement IS NOT recognized"; the feast mode, every
guest a gourmand of the feast pool): 14 gourmands and 28 requests, read off the frames by eye (table by the number post
under each bubble; resolved when its card's pip turned, read by a fixed-pixel script), held in `InnGourmandTruthTests.Y8` and measured against the tiles
replayed from the recording's readings (`InnReplay.AgainstTruth`: shown on its own table within 3 s of the bubble, and
kept until resolved; a request resolved, by its card's pip, within 3 s of its bubble isn't judged: 9 of 28). Before (main's
readers and session, c4ad20f): 12 of 19 shown within 3 s, 7 kept. Missed: words given to a table whose gourmand was still
choosing or only arrived, and kept there (D2, F1, F2, K2; J2, whose card hadn't turned yet, went to a table that had lost its
words in a fade); a drink said in three letters never matched (A2, H2). Not kept: HUD fades of 3.6–3.9 s forgot the words
(H1, I1, I2, L2); a misread line (another of the gourmand's requests) replaced them (G2). After (the recording's readings
through the new readers, `Fixtures/inn-replay/test-yuanyuan8-readings.jsonl.gz`): 19 of 19 shown within 3 s (0.65–1.3 s
after the bubble) and kept until resolved, none on another table; 27 of the 28 requests shown at all (the 28th was failed
1 s after it was said). `InnGourmandTruthTests.Over_test_yuanyuan8_…` holds it. On test_yuanyuan3–7 the gourmands' tiles are
as before but two: test_yuanyuan5's table 3 no longer shows another table's half-read words as "not recognised" for 9 s
before its own, and test_yuanyuan3's table 4 goes 1.8 s sooner, when both pips read resolved. The labels are the truth table's. Without the setup screens, the kitchens and chefs
come from the dispatch panel, and the Inn Rating from the cook times it shows. A kitchen the setup
screen showed with every place empty is left out (it cannot cook) unless the dispatch panel has since
named its chefs; one whose chefs were never seen is advised at base speed. A session ends after
15 s without a session frame, on a setup, menu or theme list screen, or when the timer jumps up. `Advice(now)`
runs `DispatchAdvisor` (every slot filled before the first order and from Full Blaze's "get
ready"), `FullBlazeAdvisor` and `GourmandAdvisor`; an order with a picture not told apart is left
out (logged once per table and session, not shown), and a missing kitchen or menu is logged once per session,
never guessed and never shown (owner, live 2026-10-20: "do not display" the not-seen-yet lines, "half of the
time it is wrong and it is disturbing").

**What is simulated, and what puts it right** (owner, 2026-10-21: "it should be simulated from the last known state whenever
possible and refreshed when see the latest state, so we can always try to get a more accurate recommendation"). Every
piece of state the advice relies on that isn't read on each frame is run on from the last state known, and a reading
that contradicts it puts it right; each correction is logged ("inn: corrected …") and counted by kind
(`InnSession.Corrections`, at the head of every replay's trace), so drift is visible. Counted over the five replays
(test_yuanyuan3 / 4 / 5 / 6 / 7):

| State | Run on by | Put right by | Corrections |
|---|---|---|---|
| Each kitchen's queue (dishes, seconds left) | cook times (`KitchenQueue.Tick`) | the dispatch panel showing that kitchen (`InnSession.Align`): a dish the estimate still has is out only when the estimate had it out within `OutEarlyWithinSeconds` (10 s; more while Full Blaze runs, its faster kitchens not modelled); a queue that doesn't line up (a dish taken out, a slot misread) once read so for `RemovalSettlesAfter` (1 s); a dish read served or picked up that the counter didn't have comes out of a queue | queue 3 / 1 / 3 / 1 / 7 |
| The counter (`CounterEstimate`: each dish, when it came out, whether it is out for an order) | dishes out of the queues | a table card reading a dish served; the tray reading it picked up or put down (back on the counter 3 s later, or to a gourmand, below); the panel's queue (above) | |
| — a dish out for an order still waiting | covers that order | the order's card still reading it unserved `CoverTrustedFor` (45 s) after it came out: taken as not there, the order called again (served at a median 10.5 s, 90 % within 43 s over the replays) | cover 0 / 0 / 0 / 0 / 0 |
| — how many | | the counter holds 8: more is a contradiction, the oldest dish cooked ahead goes first | counter 0 / 1 / 0 / 0 / 0 |
| — a dish leaving the hand with no guest's card read served | back on the counter after `HandSettlesAfter` (3 s); meanwhile still in hand | a gourmand waiting (arrived, ordered, or read eating since): it went to them, their card shows no pips | gourmand 0 / 0 / 6 / 5 / 5 |
| The timer | run on from its last reading | a reading more than a second off (it shows whole seconds), or closing | timer 0 everywhere |
| Stock (`MenuDish.Stock`) | the menu screen's stock less the dishes cooked out | nothing yet: the dispatch panel's cards show each dish's stock left, not read (a gap) | |

Read on every frame, not estimated: the orders (held through the HUD's fades, above), the tray, the panel, the fire meter.
Until 2026-10-21 nothing took a phantom off the counter: on test_yuanyuan7 the estimate held up to 20 dishes on a counter
of 8 (before → after, the largest estimate on each replay: 7 / 9 / 11 / 7 / 20 → 7 / 8 / 4 / 3 / 5). Two leaks made them:
`Align` reading a queue with a dish taken out of its middle as every dish out and the rest queued anew (y7 157.5 s:
[immortal-galore-of-fortune, eight-treasure-rice, pearly-fruit-punch] read as [immortal-galore-of-fortune 9 s, pearly-fruit-punch] put three dishes on the counter and cooked immortal-galore-of-fortune out twice; five times on
y7), and every dish served to a gourmand taken as put back. The phantoms covered real orders: table 6's immortal-galore-of-fortune had no call
from 227.7 s to 361.5 s (02:28 → 00:15), and tables 4 and 5 waited on white-fin-broth while the advice told the player to take a
soup out of kitchen 2 (310 s, owner: "stuck behind a lot of soups but the kitchen recommendation is not even picking that up").

The dispatch advice is worked out afresh on every frame, so on its own it flips with every queue time read a
second off (owner, live 2026-10-20: the suggestions "keep changing during the session").
[`InnCallHold`](src/EndHud/InnCalls.cs) turns it into `InnAdvice.Calls`, held steady: one call per kitchen (the
next dish for an order, a dish to cook ahead, or a swap). The calls on screen are handed back to the advice
(`InnCallHold.Kept` → `DispatchAdvisor.Advise(state, fillEverySlot, kept)`, owner 2026-10-21: "the recommendation
should stay steady"), which plans around them: an order shown in a kitchen stays there while it is possible there
(still waiting for a kitchen, in stock, a free slot there, out there before serving ends), however much sooner another
kitchen has it since; a dish to cook ahead shown in a kitchen goes there first, while that kitchen has a free slot
and nothing ordered for it, the dish is in stock and out in time (leaving a slot free for orders decides only a
new dish to cook ahead); a swap shown is its order kept in its kitchen too (test_yuanyuan4 206.1 s: the advice sent a
swap's order to kitchen 1 instead, taking kitchen 1's dish to cook ahead away while the swap kept claiming the order, so
the cyan circle went for 3.6 s with nothing done). So earliest-finish (owner decisions 13, 19) decides only when a call is first made. A call
stays until it is done (its dish went into that kitchen's queue: a queued + cooked-out count per kitchen and dish, put
right by every dispatch-panel reading; or its order no longer waits for a kitchen), impossible (the kitchen full; for
an order, the advice no longer sending it there; for a dish to cook ahead, the advice no longer cooking it ahead there;
the swap and its order gone), or, for a dish to cook ahead only, an order comes to its kitchen. Nothing else replaces
a call: an order shown is never moved to another kitchen or replaced by another order while it is possible, and a dish
to cook ahead never gives way to another dish to cook ahead (until 2026-10-21 an order moved when another kitchen had it
out `KeepWithinSeconds`, 8 s, sooner: with the analyser's blanket excuses gone, 4 jumps on test_yuanyuan3 and 2 on
test_yuanyuan4, all of them that move). A call impossible for a moment stays `GraceSeconds` (1.5 s) first, still
claiming its order (so it isn't called in a second kitchen meanwhile), a dish to cook ahead `CookAheadGraceSeconds`
(10 s: what drops it can come back a moment later). One order is called in one kitchen; a kitchen with no call, or
showing only a dish to cook ahead, takes an order not yet shown when it has it out no more than
`InnCallHold.ShowElsewhereWithinSeconds` (8 s) after the advice's kitchen, so a new order shows at once. The cost: a new
order the advice puts behind another order shown, with every other kitchen showing an order too, waits for a call
until one is done (test_yuanyuan3, 379.2 s: 3.3 s). Full
Blaze and the last call come straight from the advice. Measured (InnCallTests, 10 s of jittery readings): the first
suggestion per kitchen changed 19 times when the advice was worked out afresh, 0 times since it keeps the calls on
screen; the calls 0 times.

**One dish in one circle** (owner, 2026-10-21, test_yuanyuan7 at 00:46: "the recommended queue will be duplicated in
multiple kitchens": eight-treasure-rice for table 3's order in kitchen 1 and to cook ahead in kitchen 3). A dish to cook ahead is never
one another kitchen's circle shows: it gives way to an order or swap of that dish, or to the same dish cooked ahead shown
before it, and its kitchen takes the next dish its advice cooks ahead that no circle shows, or shows nothing
(`InnCallHold.DishShown`). Two orders of one dish for two tables stay, each naming its table. **A swap** stays only while the
advice keeps its order in that kitchen (until 2026-10-21 it stayed while any kitchen wanted the order: y7 341.5 s, the order
moved to kitchen 1 and kitchen 2's swap went on claiming it); it is kept as a swap while the dish it takes out is there to
take out (`KeptCall.Remove`, `DispatchAdvisor`'s `TrySwap`; before, the next frame's advice made it a plain order queued
behind that dish), and shown as a plain order once the advice makes it one (y7 from 261 s: "take out boundary-lung-roll first" (the swap circle) stood 100 s over
table 6's immortal-galore-of-fortune after the boundary-lung-roll was out). The last call's wrong dish for a table is chosen by the dish, not by its place
among what's left, so one dish leaving the hand no longer reshuffles every table (y7 375.3 s).

**Swaps can be turned off** (owner, live 2026-10-21, test_yuanyuan8: the swap circles are "really too fast for user to follow, I
think this can be a check box feature, default on"). The switch 建议换菜 (`HudConfig.InnSwapsEnabled`, on) is handed to the
session when it is made and whenever it changes (`InnSession.SuggestSwaps` → `DispatchAdvisor.SuggestSwaps`). Off, the advice
never suggests taking a dish out: no swap circle and no "先拿掉…" mark; an order goes to a kitchen with room, or waits, and a
swap on screen is kept as a plain order in its kitchen. Over the five replays (y3–y7), call-steps showing a swap: on
6 / 178 / 370 / 0 / 684, off 0 everywhere, no take-out mark, orders still called (`InnReplayTests.With_swaps_off_nothing_ever_says_to_take_a_dish_out`).

The calls are banners (owner, live 2026-10-20: "remove all hints from the current panel, use the combo instead.
the current panel has the requirements of the gourmands"): [`InnCallView`](src/EndHud/InnCallView.cs) words them
(zh-CN, `Inn.Call.*`: "厨房二：茄果鳞肉汤" with "桌3 点的", "厨房二 预做：雪绵豆沙", "厨房一 换：雪绵豆沙 → 茄果鳞肉汤",
"收尾：肉包子 → 桌3", "开启满堂彩（R）", "满堂彩快满了：先把厨房排满") and
[`OverlayWindow.InnCalls`](src/EndHud/OverlayWindow.InnCalls.cs) shows each as a [`ComboBanner`](src/EndHud/ComboBanner.cs)
(the combo-ready hint's look, slide-in, gold edge flare, slide-out) with the dish's picture in the icon slot, up
while the call stands. The inn's banners are 84 units tall with the dish's picture at 76 (76 px at 1080p, 152 at
4K; the combo icon's 44 was "too small, make it very hard for user to notice", owner 2026-10-20;
`InnCallView.BannerHeight`, `PictureSize`, `InnCallView.Banner`). Stacked in rows ([`InnBannerRows`](src/EndHud/InnCallView.cs),
drawn by [`InnBannerStack`](src/EndHud/InnBannerStack.cs)): each kitchen that cooks has its row from the session's first
frame, kept to its end (empty between its calls), its banner changing sliding out and in within the row; then Full
Blaze and the last calls by table, below the kitchens, each opening and closing its own row. So no banner moves when
another comes or goes (2026-10-21; before, Full Blaze came first and every banner below one that went glided up). A
banner for the same call is the same element, never drawn or slid in again. At most 7 (`InnCallView.MostBanners`), so
the fullest stack fits from its default place at 1080p, 1440p and 4K.

**The kitchens' calls as circles** (owner, live 2026-10-21, test_yuanyuan5: "the notification of each kitchen is very
hard to read. try another UI: show the recommended dish for each kitchen with a big circle (like buff) that is placed in
the middle of the screen above where the currently held dishes are"). Since then the overlay draws only Full Blaze and
the last calls as banners (the rows above, kitchens' rows filtered out; `InnBannerRows` still makes them, and the replay
measures them as before). Each kitchen's call is a circle in the gauges' buff look
([`InnKitchenCircleView`](src/EndHud/InnKitchenCircles.cs)): the dish's picture inside a ring, and beside it on the
right the kitchen with a pill of what it asks for, the dish's name and one line of why. The kinds read apart by the ring and the pill: an order gold and glowing
(`Inn.Circle.Order` 点单, "桌4 点的"), cooking ahead cyan, dashed, not glowing (`Inn.Circle.CookAhead` 预做,
`Inn.Circle.CookAheadReason`), a swap orange-red and glowing (`Inn.Circle.Swap` 换菜, `Inn.Circle.SwapReason`
"先拿掉X · 桌5"). One 180-unit slot per kitchen: kitchen 1 left, 2 middle, 3 right, a kitchen with no call
leaving its column empty, so no circle moves sideways ([`InnCircleRow`](src/EndHud/InnKitchenCircles.cs): the same element
while the call stands, a new call fading the old one out and popping in, in the same column). The calls are
`InnCallHold`'s as before; only the look changed. Placed by
[`InnKitchenCircleLayout`](src/EndHud/InnKitchenCircles.cs) at one fixed place (below). Drawn by
[`OverlayWindow.InnCircles`](src/EndHud/OverlayWindow.InnCircles.cs); three sample circles, one of each kind, show at that
place while the menu is open. The style and size are the owner's (2026-10-22: "USE the original circle style", the
words beside the ring as v0.6.0 drew them; "the bottom circle default size should be around 60%", then the words too):
at 1080p the ring 82 and the picture 67, the kitchen, the dish and the reason 13, 13 and 12 units, the slot 180 and the
row 82 tall (60 % of v0.6.0's 136, 112, 22/22/20, 300). A reason longer than the 92-unit words column ends in an ellipsis,
as it did at v0.6.0's size.

**Important things in the middle of the screen, at fixed places** (owner, 2026-10-22: "do not change the circle
location, keep them at fixed location, like gourmand banners. user is very hard to trace things that are flying
around"; "the gormand table is too small and too at the side, make it more closer to the center so human can focus";
"important things should be at the central view port, so human can focus, things on the corner is really impossible
for human to notice"). The rule for every inn overlay: one place each, the same with the dispatch panel open or closed
and whatever else is up, as near the middle of the screen as the game allows; never at an edge or a corner. Where the
middle meets the open dispatch panel, an overlay may stand over the panel's chefs' portraits and bonus column (nothing
the player needs while dispatching), never over its dish cards, kitchen tabs, queue or close button; never over the
table cards, the timer, the tray and 派单, or the Full Blaze meter. Measured on the 4K frames as fractions of the
window's centred 16:9 area (the tests' `InnScreenAreas`): the panel x 0.109–0.891, y 0.194–0.856; its dish cards from x
0.3827, y 0.29–0.80; its tabs to x 0.67; its queue from y 0.697; its left column (portraits y 0.307–0.467, bonus y
0.518–0.653) x 0.112–0.38; with it closed the top-left block down to y 0.487 (x to 0.285), the table cards to y 0.132
and the game's last-call bar to x 0.6194, y 0.167. So:
Where they are now is the owner's own layout (2026-10-22, after trying the middle: "that must stay at the middle bottom,
above the dishes in hands"; "the gourmand location IS TOO close to center! should be roughly at where I draw"; "the left
3 banner for gourmand should be table 1-3, right should be 4-6"):
- **The kitchens' circles** (`InnKitchenCircleLayout`): one row centred across the screen, its bottom at y 0.845, right
  above the held-dishes tray (its slots start at 0.853): 540 × 82 at 1080p, x 0.359–0.641, y 0.769–0.845. The same with
  the dispatch panel open: the row then covers the lower dish cards' bottom line and the queue strip's right end (the
  owner kept the style over a clearer place), and the selected tab's kitchen gets a gold frame around its slot, in
  place. A half see-through dark backing under the row, only while a circle is up (`InnCircleRow`; an empty row draws
  nothing, dcaebc9). No drag (an old config with `InnCircleX/Y` still loads). InnCirclesFixedTests: one place per
  kitchen, panel open or closed, one to three circles, 1080p/1440p/4K; centred; above the tray; clear of the HUD and
  of the gourmands' spots.
- **The gourmands' tiles** (`InnGourmandLayout.Centers`, fractions of the centred 16:9 area): tables 1, 2, 3 down the
  left, centred at x 0.175, y 0.36, 0.60, 0.825; tables 4 and 6 down the right at x 0.835, y 0.36 and 0.795; table 5 at
  y 0.55, ending at x 0.85, clear of the Full Blaze count. Shown with the panel open too, never put away: the left ones
  then over the chef column, the right ones over the right dish-card column's name ends and prices (accepted for the
  owner's spots). On session-two-dishes.jpg table 6's tile reaches the last 0.6 % of the green bar right of 派单 and
  table 5's the left third of the round smiley gauge (成功连续上餐) beside the Full Blaze count (InnSixGourmandsTests measures
  the first). Half see-through backgrounds, solid pictures and words.
- **The banners** (开启满堂彩, the last call): by default top-left at (0.25, 0.36), the combo hint's height just right of
  table 1's tile (`HudConfig.DefaultInnCallX/Y`; settings version 5 moves the old defaults, (0.2, 0.36) until version 3
  and (0.43, 0.52) in version 4), still draggable, put away while the panel is open.
The player's size makes the circles and the tiles smaller, never larger than their place. InnKitchenCircleTests renders
three circles and one, three and six gourmands over session-two-dishes.jpg and dispatch-panel-queue.jpg into
`.tracking/changes/2026-10-22-test-debt-*.png` (local only) for a person to look at.

**The open dispatch panel** (owner, live 2026-10-20: "is it possible to ALSO have a suggestion marker by the dish
itself on the right side?"; the banners covered the panel's chef column). While the panel counts as open (below), or
the last fresh frame read it with all its kitchen tabs (as many as the kitchens that cook and as any panel read this
session: `InnDisplay`; owner, live 2026-10-21: banners over the open panel at 02:25, 01:20 and 00:09 of test_yuanyuan5,
on the replays 10 and 12 such frames before, 0 since; a frame reading fewer tabs is the old one-frame misreading, 48 on
test_yuanyuan3, and hiding on it would blink everything), the banners (Full Blaze, the last call) are hidden in place
(`InnViews.Away`; shown again as they were when it closes, with no slide-in: until 2026-10-21 every opening took every
banner down and every closing slid them all in again with the gold flare, which the owner saw as "flashes heavily"),
the gourmands' tiles and the kitchens' circles stay where they always are (above),
the selected tab's kitchen framed. Until 2026-10-21 the circles were hidden over the panel (owner, test_yuanyuan6 at
6:10: "the kitchen shows 有菜要排, BUT there is no circle under"), until 2026-10-22 they moved to a row below it. And
[`InnPanelMarks`](src/EndHud/InnPanelMarks.cs) takes over: the call for the kitchen the panel shows is a glowing
gold outline just outside its dish's card with a label in the card's blank lower right (`Inn.Mark.Queue`
"queue this (table N)", `Inn.Mark.CookAhead`, `Inn.Mark.Swap` "take out X, then queue this"), only on a card read as
that dish (on this frame, or on an earlier one in any kitchen when this frame read the card as no dish: the menu doesn't
change in a session and a dish's card is the same in every kitchen, measured on all four recorded sessions;
`InnSession.PanelShown`, `InnAdvice.Cards`), never a guess; every other kitchen whose call's dish has a known card gets
`Inn.Mark.Tab` ("a dish to queue") on its tab, so the tab always opens on a marked card (owner, test_yuanyuan6: "the
kitchen shows 有菜要排, but when we go to the kitchen, there is no recommendation"; until 2026-10-21 cards were remembered
per kitchen, so a kitchen opened for the first time showed no mark until its own cards were read); a call whose card isn't
known shows only as its circle. The tabs, the card's mark and the circles are one picture: all three are
the same `InnAdvice.Calls` of the same moment. Placed by the reader's card and tab places on the window's centred
16:9 area (`OverlayWindow.InnMarks`); they follow the game's layout, so nothing is dragged or kept in config. Full
Blaze and the last call wait for the panel to close. The marks go the moment the panel is seen closed
(`InnAdvice.MarkedPanel`; owner, live 2026-10-21: "the recommendation leaks after the kitchen is closed"): a fresh frame
since the panel was last read that shows something the open panel covers or moves takes them away at once: the Full
Blaze meter read, the timer read in its pill under 结束经营 (`SessionClock.BelowButton`), or the tray read; while the
banners stay down for the panel's 1.5 s hold; a frame merely missing the panel (its tabs misread while open, the timer
still at the top) keeps them. Until 2026-10-21 only the meter counted, and it fades in last after the panel closes (and
through Full Blaze wasn't read at all): "预做这个" stayed over the dining room for about a second (test_yuanyuan5 252.6 s,
session-panel-just-closed.jpg). The leak itself was the panel read where there was none (above, "Reading the inn's screens"): a
closed panel's frame read as kitchen 1, its cards remembered from the last real reading standing in.

**Held steady: nothing flashes** (owner, live 2026-10-20: "it is still flashing, the display of both banner and the
circle should just stay, not flash all the time ... also the suggestion keeps flashing too"). Found by measurement,
not guessed: [`InnReplay`](tests/EndHud.Tests/InnReplay.cs) feeds a recording's readings (taken through the real
readers and the reading loop's holds, a frame every 0.3 s as the live loop reads at 4K) through the real session and
views (`InnDisplay`, which the overlay draws) on a fake clock with the overlay's half-second tick, and logs as text
every banner, mark and circle appearing or going with the input that changed. On the owner's session
(test_yuanyuan3.mp4, 0–430 s; its readings are the text fixture `tests/EndHud.Tests/Fixtures/inn-replay`, no
picture), before: banners 60.8 changes a minute (69 hidden and back within a second, 37 up under half a second),
marks 35.4, circles 2.9, the gourmand panel 24.7. The causes and what holds each:

| Cause (measured) | Hold |
|---|---|
| The screen read Session for a single frame inside an open panel 48 times (and for 2–5 frames a dozen more), and the panel read open for one frame 26 times; 142 of 166 banner removals were the panel read as opened | The panel counts as open after `InnSession.PanelFramesToOpen` (2) fresh frames read it, and as closed only once no fresh frame has read it for `PanelClosedAfter` (1.5 s); meanwhile the last reading of it stands |
| An order's card read Empty for one reading and back (the HUD fading) 16 times, another order 3 times | An order's card (an order, a gourmand ordering or asking) read otherwise is taken only when the next reading agrees (`InnSession.TakeTables`); a new order shows at once. Since 2026-10-21 an order with a dish not served read empty or ordering is gone only after `OrderGoneAfter` (below) |
| An order dropping out of one moment's advice; dishes to cook ahead swapping places as the queues move on | `InnCallHold`: a call no longer valid stays `GraceSeconds` (1.5 s), unless done (queued: at once) or an order needs its kitchen (at once); a dish to cook ahead stays while its kitchen is still to cook something ahead |
| A gourmand's best option changing as the queues move on | `InnWantsHold`: a gourmand's rows stay while each item still meets its own request on the menu, and change when the advice's choice is ready `ClearlySoonerSeconds` (10 s) sooner, or the requests read differently |
| (2026-10-21) A bubble read half-faded with one request, both 0.3 s later; a gourmand's banner put up as the panel opened | A gourmand's banner first goes up once their rows have read the same for `InnWantsHold.ShowAfter` (0.5 s); it is keyed by table and redrawn in place when what it says changes |
| Held frames, the banners' keys | No change needed: a held reading says nothing new, and a banner is keyed by its call, so an unchanged one is never drawn again; the gourmand panel's "about 35 s" changed every tick: the panel is gone |

After: banners 24.7 a minute (none hidden and back within a second; 3 up under half a second), marks 30.2 (52 of the
164 are tab switches), circles 2.6 (none back and forth). What changes now is the player's own doing (opening the
panel, switching tabs, queueing), an order arriving or done, or the advice clearly changing.
Since 2026-10-21 (marks gone the moment a frame shows the panel closed; the gourmands' banners): marks 33.9 a minute,
11 hidden for under a second (each a frame reading the Full Blaze meter between frames reading the panel; at most 8
before), gourmands' banners 3.9 a minute, none hidden under a second or up under half a second.

**The kitchens' calls held steady** (owner, live 2026-10-21, test_yuanyuan4.mp4: "the recommended dish flashes heavily
and it jumps around ... the recommendation should stay steady"). The replay also traces each kitchen's call as the hold
decides it (whether its banner shows or the open panel's mark does), with the queues, open orders, counter and advice
beside each change and every 10 s, and counts (`InnReplay.Steadiness`): a **flash**, a kitchen's call gone and back
within 2 s unless the player queued it; a **jump**, a kitchen's call switching dish, or an order or dish to cook ahead
switching kitchen, when the call that went was still possible and nothing was done to that call (its dish not queued
in its kitchen, nor for its order in another; its order not served, eating or paying, its dish not out on the counter;
its kitchen not full, nor another dish queued in it by the player within the swap window; cooking ahead not stopped).
A dish served to another table or a dish queued in another kitchen excuses nothing (until 2026-10-21 either, within the
grace, excused any call going: in a busy session nearly always true, so the analyser could not see the moves). Its
second fixture is test_yuanyuan4's readings (0–470 s). The causes found, each with its test:

| Cause (measured) | Fix |
|---|---|
| The hold argued with an advice worked out afresh: a dish to cook ahead shown in kitchen 1 claimed the one the advice had put in kitchen 3, whose call went and came back (y3 123.0 s); the counter's last places went to other kitchens' dishes and kitchen 1's shown dish was replaced (y3 235.8 s) | The advice keeps the calls on screen (`KeptCall`, above); a dish to cook ahead is claimed and checked per kitchen |
| An order's card not read for 3 s dropped the order (`TableKeptFor`) and it was called again when read (y3 268.5 s) | An order's card keeps its order until a reading says otherwise |
| The HUD fading reads open orders Empty for up to 3.3 s, each time the same order after (y4 383.4 s: every kitchen's call gone, back 1.5 s later) | Guests never leave: an order with a dish not served read empty or ordering is gone only once every reading has said so for `InnSession.OrderGoneAfter` (5 s) |
| The panel's queue compared with the estimate dish by dish: a soup queued behind the dish cooking taken for one cooked out 130 s before, its call not done (y3 245.4 s); a dish the estimate still had that the panel no longer showed never reached the counter (y3 176.4 s) | The panel's queue lined up first in, first out with the estimate (`InnSession.Align`: dishes cooked out too early at its front, told apart from one queued since by the seconds left on it; then the estimate's; then the player's), dishes moving on and off the counter with it |
| A panel's first frame read its queue empty, taking 30 s of dishes for out (the counter grew to 11 on a counter of 8 and covered every order); a panel with fewer tabs than before ("kitchen 1 of 1": the real panel misread, y4 168.6 s; or, on y3's older readings, the dark bar behind the theme line) | Dishes gone from a queue are taken once the next reading of that kitchen agrees; a panel with fewer tabs than this session has read is a frame the panel was missed on |
| The cooking dish's seconds not read reset it to its whole cook time (y4 179.1 s) | Kept as the estimate has it |
| A dish to cook ahead dropped for the kitchen holding two dishes for a moment (y3 141.6 s), or the counter's room forecast short for 3.3 s (y4 189.3 s) and 5.6 s (y3 271.0 s), then another dish | It stays while its kitchen has a free slot; out of the advice it stays `CookAheadGraceSeconds` (10 s) |
| An order the advice moved to a clearly sooner kitchen stayed on the old one's banner for 15 s, until an unrelated order switched both (y3 364.2 s); 5 s re-decided an order with nothing done (y3 328.5 s: 39 s against 34 s) | Until 2026-10-21 it moved at once with an 8 s margin; since, an order shown is never re-decided (below) |
| With the analyser's blanket excuses gone (a dish served to any table, a queue added to anywhere), an order shown moved to a kitchen 8 s sooner, or a kitchen's order was replaced by the one moving in: y3 321.3 s, 364.2 s, y4 355.5 s, 4 and 2 jumps | The advice keeps an order shown in its kitchen while possible; the hold never replaces one by another order (`InnCallTests.An_order_shown_is_not_moved_when_another_kitchen_becomes_much_sooner`, `InnSteadyTests.An_order_shown_is_not_replaced_by_another_order`) |
| Every opening and closing of the panel took all banners down and slid them in again; a banner going moved those below it | Hidden in place (`InnViews.Away`); each kitchen in its own row (above) |

Measured with the same analyser at the instrumentation-only commit and after (calls per minute, flashes, jumps):
test_yuanyuan3 16.2 → 7.7, 3 → 0, 4 → 0; test_yuanyuan4 8.6 → 4.5, 1 → 0, 3 → 0. Banners as drawn: y3 24.7 → 11.4
changes a minute, y4 12.4 → 6.4, no kitchen's banner moving in the stack. Every new order a kitchen could take was
called at once where a kitchen wasn't showing another order (5 of 5 on each). Fewer order calls than before is mostly
orders whose dish the estimate now knows is out (it no longer loses dishes on a panel reading); the counter itself is
an estimate, never read off the screen, so an order covered by a dish that isn't really there got no call (until the
counter gate went, below).

**No counter gate, replayed** (2026-10-21). Order banners put up, before → after: test_yuanyuan3 7 → 25 (from 250 s:
6 → 19), test_yuanyuan4 6 → 18 (from 250 s: 4 → 14; from 250 s to 350 s: 0 → 4); dishes to cook ahead 14 → 26 and
6 → 18. New orders a kitchen could take: y3 17, y4 13 (5 and 5 before), each called at once where a kitchen could take
it. Still no flash and no jump on either; banners 22.9 and 15.4 changes a minute (11.4 and 6.4 before; each banner up
once and down once, none back within a second), so the replay's bound on that rate is 25 (it was 15). Marks hidden for
under a second on y3: 14 (11 before), the same panel flickers with more kitchens showing a call. Before those last
fixes the change gave y3 1 flash and 23 jumps, y4 7 jumps: an order whose dish had just come out sent again (fixed:
`OutForOrders`), an order on screen measured against a kitchen another order on screen takes (fixed, above), a second
order of a dish waiting behind the first while a kitchen showed only a dish to cook ahead (fixed, above), and the
analyser counting as jumps what its definition excludes: a call going because its order was gone (the table eating or
paying), because the player queued another dish in that kitchen (an order shown there moves once that dish has cooked
past the 5 s it could be swapped out in), or as a dish was served to any table (that order's call going frees its
kitchen, as the player's queueing does). An order counts as one that should show at once only where a kitchen not
showing another order has it out no more than 8 s after the advice's kitchen (`InnReplay.CallStep.FreeFor`), the
hold's own rule. In the simulator (which keeps its counter of 8 and its assumption that a full counter holds a
finished dish in its kitchen) the idle kitchen-seconds of `Following_the_advice_keeps_the_kitchens_busy` went 104 → 107
and the average waits moved by -0.3 to +2.1 s (the most, a player acting every 20 s with the recorded kitchens:
30.6 → 32.7 s); following the advice still serves every order sooner than the first free kitchen.

**No re-deciding, replayed** (2026-10-21). With the strict analyser: y3 4 jumps → 0, y4 2 → 0, no flash before or after;
calls 18.8 → 15.1 and 13.5 → 12.0 changes a minute; new orders shown at once 15 → 16 of 17 and 13 of 13. The existing
simulator player queues the whole advice and never has a call on screen, so its numbers cannot change (107.0 idle
kitchen-seconds, as before). `Following_the_calls_shown_keeps_the_kitchens_busy` plays the overlay's player (one call
a kitchen, the advice keeping the calls shown; acting at once, every 10 s, every 20 s): 132.0 idle kitchen-seconds and
an average wait of 35.34 s, against 132.0 and 35.33 s when the order moved to a kitchen 8 s sooner.

**test_yuanyuan5, replayed** (2026-10-21; the owner's 群鳞会 session, its readings the third fixture, 0–415 s: the
recording's last seconds can show the desktop and are never read). Its readings are the first with the tray of held
dishes, Full Blaze's countdown and where the timer was read. The code before that day's fixes over the same readings, then
after: calls 58 → 68 changes (10.7 → 12.5 a minute), flashes 0 → 0, jumps 1 → 0, new orders a kitchen could take 14 → 10
(the others now covered by a dish in hand), shown at once wherever a kitchen was free of another order's call (3 of 3 both
times; before, one never shown). Swaps calling out a dish the player had queued as called: 3 → 0 (128.7 s 界龙肉卷 and
白瓜肉丸汤, 335.7 s 福寿全, queued in kitchen 1 when the call named kitchen 2: a dish put in any queue while a call names
it counts). One swap is left, at 348.0 s: a "勺里游" the player queued on their own while holding one for table 1's order.
"开启满堂彩（R）" was up 331.5–392.1 s over the readings of that day's build (Full Blaze running read as nothing), 331.5–332.7 s
now. The replay of y3 and y4 with these changes: no flash and no jump; calls 15.9 and 12.0 changes a minute.

**test_yuanyuan6, replayed** (2026-10-21; the owner's 一汤一饭 session, its readings the fourth fixture, 0–515 s). The
owner's report and what the replay showed (main's code of that day, then after):

| Owner (video time) | Measured on the replay (y3 / y4 / y5 / y6) | Cause | Fix |
|---|---|---|---|
| 3:21 (201 s): 主食 kitchen told to cook a dessert ahead | y6 kitchen 1: 金丝小贝 197.7–221.7 s | cooking ahead ranked "spread, then slowest": the slowest is the dish a kitchen isn't set up for | theme match, spread, the kitchen's own types, slowest (above) |
| 6:10: 有菜要排 with no circle | tab marked, no circle drawn: 14 / 9 / 18 / 21 times (94 / 68 / 74 / 61 s) → 0 | circles hidden over the open panel | circles below the panel |
| 有菜要排, no mark on that kitchen's page | switches to a marked tab with no card mark within 1 s: 0 on every replay | not reproduced on the readings; possible live: a kitchen's first frame reading no card (cards were per kitchen), a tab marked for a dish with no known card | cards session-wide; a tab marked only for a known card |
| the blue circle comes and goes | a dish to cook ahead called while the circles weren't drawn: 13 / 11 / 17 / 22 times (105 / 78 / 88 / 91 s) → 0; gone with nothing done: 0 / 1 / 0 / 0 → 0 | the circles hidden over the panel; y4 206.1 s a swap's order not kept | circles below the panel; a swap kept |
| 6:10: gourmand's requirement gone, flashes | gourmand tiles hidden over the panel: 4 / 4 / 7 / 7 times (43 / 31 / 40 / 38 s) → 0; taken down while the table still waited: 0 everywhere | hidden over the open panel, not forgotten | tiles beside the panel |

Calls, before → after: 15.9 / 12.0 / 12.5 / 9.1 → 13.6 / 11.7 / 10.0 / 3.6 changes a minute, no flash and no jump either
way. The checker (`InnReplay.Agreement`, `CookAheadFlicker`, `GourmandLives`, printed at the head of `views.txt`) fails the
replay test on any tab without its circle, kitchen with a call shown on the panel and no card marked, switch to a marked
tab with no card mark, dish to cook ahead called with the circles not drawn or gone with nothing done, gourmand hidden or
taken down while their table still waits.

**test_yuanyuan7, replayed** (2026-10-21; the owner's Luxurious Banquet session, its readings the fifth fixture, 0–390 s; the timer
reads 5:00 at 76 s, so 00:46 is 330 s). The owner: "sometimes the recommended queue will be duplicated in multiple
kitchens", and at 5–6 min "we ended up stuck behind a lot of soups but the kitchen recommendation is not even picking that
up". Two checks were added for it (`InnReplay.DuplicateCircles`, `InnReplay.UncalledWaits`), measured on main's code of that
day with the final checks, then after:

| Measured (y3 / y4 / y5 / y6 / y7) | Before | After |
|---|---|---|
| one dish in two circles (not two tables' orders) | 6 / 9 / 4 / 0 / 9 (91 / 170 / 26 / 0 / 82 s) | 0 everywhere |
| a dish tables wait for, not in hand nor queued, a kitchen able to have it out in time, no circle naming it, over 10 s, not ended by the dish picked up or served | 1 / 1 / 0 / 1 / 2 (y7: immortal-galore-of-fortune 133.8 s from 227.7 s, white-fin-broth 21.6 s from 318.0 s) | 1 / 1 / 0 / 0 / 0 (y3 berry-sweet-sip 17.4 s, y4 sourberry-pudding 17.4 s: each covered by a dish taken to be out for it, inside the 45 s trust) |
| the largest counter estimate (a counter of 8) | 7 / 9 / 11 / 7 / 20 | 7 / 8 / 4 / 3 / 5 |
| calls a minute; flashes, jumps | 13.6 / 11.7 / 10.0 / 3.6 / 14.4; 0, 0 | 12.9 / 12.8 / 14.4 / 5.2 / 16.0; 0, 0 |

The uncalled check counts the counter as nothing: it is never read. A wait the player ended by picking the dish up or
serving it was covered by a dish really there (listed, not counted); one ended by a call at last, by the player queueing
it with no call, or by the time running out counts. The replay test fails on any duplicate and on any counted wait longer
than `InnSession.CoverTrustedFor`. Two kinds of change are not counted as flashes or jumps, each the hold's own words for
one call: a dish to cook ahead that gave way to its dish in another circle, and one whose dish could no longer come out
before serving ends (`CallStep.AheadTooLate`); and a call gone and back while its order's dish was picked up and put
elsewhere (the tray read it). Each correction of the estimate is listed in the trace with its time; `ENDHUD_REPLAY_DETAIL=from-to`
writes every step's state in that window.

`InnReplayTests.Over_the_owners_recorded_sessions_nothing_flashes_or_jumps` replays the six fixtures (y3–y8) on every run;
`InnSteadyTests`, `InnSessionTests` and `InnCallTests` have one test per hold. To trace another recording (text only, to
a folder of your choice):
`ENDHUD_INN_RECORDING=<recording.mp4> ENDHUD_INN_TRACE_OUT=<folder> [ENDHUD_INN_RECORDING_TO=<seconds>] dotnet test
tests/EndHud.Tests --filter InnReplayTests` writes `readings.jsonl.gz` and `views.txt` (~12 min for 470 s at 4K);
`ENDHUD_REPLAY_DUMP=<file with {fixture}>` writes each fixture's `views.txt` from the replay test.

**What the gourmands want, as tiles** (owner, live 2026-10-21: "user cannot distinguish between whether the two circles
are alternatives or two requirements"; then, test_yuanyuan6: "the banner for gormand suggestion is too small for human to
read, make at least the picture bigger, at least same size as the circule today"): [`InnGourmandView`](src/EndHud/InnGourmandBanners.cs)
draws one tile per gourmand ([`OverlayWindow.InnGourmands`](src/EndHud/OverlayWindow.InnGourmands.cs)): per request the best
item's picture with its number (`Inn.Gourmand.Numbers`) on its right corner and the item's name under it (on up to two lines),
the table (`Inn.Gourmand.Table`) on the first picture's left corner. Every picture 112 units, alone or two side by
side (116 × 164 or 234 × 164 at 1080p), the names and pills 20: the tiles' own size, not the circles' (until a1c844b the
circles' constant, so the tiles shrank to 67 with them). A request keeps its own number: with the first served, the
second is still ② (until 2026-10-22 it became ①; `InnGourmandNote.Numbers`). The request in plain words
isn't drawn (no room in the spot; the numbers tell two requests apart). With two requests, the advice's serving
(`GourmandAdvice.Serve`) is put back on the requests, so each item fills its own request when served in the numbers' order.
A request nothing on the menu meets says so (`Inn.Gourmand.None`), a gourmand whose words weren't recognised that
(`Inn.Gourmand.Unknown`); a drink request before any drink of the menu was seen shows the request under an empty disc. Up
to six, one per table (owner, 2026-10-21: "we need to be prepared to support all 6 tables are gourmand").

**Each table at a fixed spot** (owner, live 2026-10-21, test_yuanyuan8: "make the position FIXED, so user does not need to
chase around and not know what to look at"): `InnGourmandLayout.Spot` takes the table and the window only, never the
panel's state or the other tiles; the spots are the owner's (above). A tile stands at its spot's top-left corner; the
player's size makes it smaller, never larger than the spot. Nothing is dragged (the old `InnGourmandRight/Top` and the
sample tile are gone). In the overlay a tile never moves: it is drawn again in place when what it says changes, and fades
in and out. Up until the order is fulfilled (the request kept as above), held steady (above).

**A gourmand's dish is an order for the kitchens** (owner, 2026-10-22: "table 1 wants 肉卷, but it is NEVER suggested in
the kitchen"; 1a00587): the dish their tile names joins the tables' orders, in the order taken (by when their order was
read), until its request reads resolved (its pip, or the bubble's face); a dish in hand covers it as it covers a table's,
and a table ordering the same dish gets its own. The dish is held per request ([`InnGourmandPicks`](src/EndHud/InnGourmandBanners.cs)):
it changes only when it no longer meets the request, or a dish in the player's hand that meets it is ready
`InnWantsHold.ClearlySoonerSeconds` sooner; never for a queued dish coming closer as it cooks, nor for the counter's
estimate. So the tile and the kitchen's call always name the same dish. As shipped in v0.6.2 the call followed the
advice's best of the moment while the tile held another, after every table's order: the replays then had a jump on y4,
y5 and y7 (InnGourmandOrderTests, InnReplayTests). Pictures at 4K over session-two-dishes.jpg and dispatch-panel-queue.jpg
with 1, 3 and 6 gourmands: `InnKitchenCircleTests.The_circles_and_gourmands_drawn_over_a_4K_frame` writes
`.tracking/changes/2026-10-22-test-debt-*.png` (local only).

[`OverlayWindow.Inn`](src/EndHud/OverlayWindow.Inn.cs) shows these only on session frames, and samples (three
circles and a banner) while the menu is open out of the inn (the banner is dragged then). Settings:
`InnHelperEnabled` (on), `InnCallsEnabled` (on; the switch 派单提示（厨房圆圈和横幅） in the card: the circles, the banners
and the panel's marks), `InnSwapsEnabled` (on; the switch 建议换菜 in the card: whether the advice may say to take a dish cooked ahead out for an
order, below), `InnCallX/Y` (the banners; default 0.25, 0.36), `InnStripScale` and `InnStripOpacity` (85 % by default, owner
2026-10-22: "make sure the transparency is not 100%"; settings version 6 turns an older file's 100%, the old default the first
start wrote, into 85 %) (the circles and the gourmands' tiles (smaller only), the banners, the marks and the setup advice); the card's reset puts the
banners back. The circles and the gourmands' tiles have fixed places (above): nothing to drag, and a config still holding
the old `InnCircleX/Y` or `InnGourmandRight/Top` loads.

**What's remembered of the setup** ([`InnSetupMemory`](src/EndHud/InnSetupMemory.cs); owner, live
test 2026-10-19: "remember which theme, cook, menu etc currently picked, UNTIL go back to pick
theme page (or exit the inn mode)"). In memory only, nothing on disk. The store's checklist:

| | |
|---|---|
| Create | From readings: the setup screen gives the theme (name, rules, hot types, objective) and the kitchens with their places and placed chefs (named, or a place seen taken by its red bar); a chef picker its kitchen; the menu screen each card seen (on the menu or not, its stock), the counters and the menu requirements. |
| Read | The setup advice (`ForSetup`, `ForMenu`) and the session's kitchens, Inn Rating and menu. A part never read is a gap, never a guess. |
| Correct | A newer clear reading of a part replaces it: the setup as a whole (its kitchens kept when the new reading has none, a kitchen whose title it missed kept as it was), a card by its id, the requirements number by number. A place read empty (its grey bar, a kitchen showing only its "assign operator +" button) is a fact, not a miss: its chef goes; only a place that couldn't be read keeps its chef. Another theme is another setup: its kitchens as read, no chef of the last one carried over. A reading that fails changes nothing, so a page without the theme never makes it unknown (the "theme unreadable" gap only when none is remembered). The one exception is the chef picker's kitchen: a picker read whose title can't be read has no kitchen, rather than the last picker's (whose advice would be another kitchen's). |
| Remove | Everything at once: on the theme list (a new setup begins), or after 30 s spent reading screens that aren't the inn (`InnSession.ForgetAfter`, in `ReadingClock` time: a fight, the open world; never time the game was in the background), checked on every tick and on the next reading. The setup screen after a session no longer clears the menu. |
| Protect / carry | Nothing outlives the inn visit. |

**The setup advice** (owner, live test 2026-10-19: "recommendation for cook only shows when picking cook, and the
cook for each kitchen shows under each kitchen, not as a single panel. and then in the choose menu window, shows the
recommended menu based on current state"). Small panels where the game shows the choice they're about: under each
kitchen's card on the setup screen, in a chef picker, on the menu screen; nothing on the theme list or in a session
([`InnSetupView.ForScreen`](src/EndHud/InnSetupPanel.cs)); the words are in i18n/zh-CN.xaml (`Inn.Setup.*`) and the
README shows them for the owner's case. No mark on the setup screens' cards: the "recommended" card marks went on
2026-10-21 (owner, live: "the recommendation in the choose cook or dish menu refreshes too slowly and it is often out of
sync, just delete that for now"; then, debug_yuanyuan1: "I ask you to delete the recommend box on the character, not
delete the recommend pannel"), the text panels stay.

- **Chefs** (the setup screen's kitchen columns and a chef picker) come from one plan of
  `SetupInputs()`: the theme's hot types and rules, every kitchen with its places and the chefs
  already placed (kept; only the empty places are planned), the built-in roster (everyone who can
  cook and is in the game: Si and the Endministrator can't cook, and an operator the wiki lists before release
  is on the hand-kept `chefRules.notYetAvailable` of inn/inn-data.json, `InnChefRules.Suggestable`: Argent Flow,
  owner 2026-10-20) and the dishes and drinks the player has: every one the Inn Rating unlocks (reaching the rating
  unlocks it), and of the others (a theme's stars, a mission) those the menu screen has shown, or, before it has shown
  any, all of them. Never only the cards seen so far (owner, live 2026-10-21: the top of the menu list showed soups
  and desserts only, and the plan "only contains 2" types where Rating 4 asks for 3). The menu screen's stock, Inn
  Rating and requirements refine it; unread, the rating's own rules are used (`MenuRules.OfRating`: known for Rating 4,
  at least 3 types, 6 dishes and 3 drinks, 8 of each), never guessed for another rating. Every rule first, then the
  theme's match, then income; a rule not understood is logged once per change (`OverlayWindow.RenderInnSetup`). A column gives the dish types the kitchen will cook (`Inn.Setup.Cooks`),
  then a recommended row (`Inn.Setup.Recommend`) with the chefs for its empty places and a
  substitutes row (`Inn.Setup.Substitutes`, at most three); a full kitchen only says it's set
  (`Inn.Setup.Full`), a kitchen the plan leaves empty that it needs no chef (`Inn.Setup.KitchenUnused`).
  The chefs still to place are the plan's chefs for that kitchen less those placed, by who they are, not by place
  (`InnSuggester`): a player who puts the plan's second chef in first is told only the first.
  A chef picker shows the same under a heading naming its kitchen (`Inn.Setup.PickerTitle`); its
  kitchen is read off its title, and with an unread title it shows nothing.
- **The menu** (the menu screen) comes from a second plan, of `MenuInputs()`: only the kitchens with a
  chef placed, each exactly as it is (its places = its chefs), so the menu fits the chefs actually
  there. With no chef placed anywhere it plans the chefs too and its title says so
  (`Inn.Setup.MenuTitleChefsPicked` instead of `Inn.Setup.MenuTitle`). A tick (`Inn.Setup.OnMenu`)
  marks a dish or drink already on the menu; the dishes and drinks rows each have a substitutes row.
  The menu screen's stock, Inn Rating and requirements refine both plans. Every rule holds, or the panel says the
  dishes on offer can't meet them all (`Inn.Setup.RulesUnmet`); a rule not understood is said above it (and logged).
- **Gaps** (`InnSetupGap`): the theme unreadable with none remembered, no setup screen seen since the
  theme list, no kitchens: no panel at all (owner, live 2026-10-20: the not-seen-yet lines are wrong half of the
  time and disturbing). While a plan is worked out: `Inn.Setup.Working`.
- [`InnSetupPlanning`](src/EndHud/InnSetupPanel.cs) runs the planner and `InnSuggester` on the thread
  pool once per change of the input's key (two of them: the chefs' and the menu's, so switching
  pages doesn't plan again), in two stages (owner, live 2026-10-19: "the page refreshes too slowly"):
  the quick plan (`InnSetupAdvice.Fast`, `PlanQuickly`) is shown as soon as it's ready, then the
  simulator's plan (`InnSetupAdvice.For`) replaces it only when a panel would show something else
  (`InnSetupAdvice.Signature`). New inputs cancel the older work, and a late result for older inputs is
  never stored. Each stage's time is logged ("inn: setup advice in … ms (model), refined in … ms").
  An older plan is shown meanwhile only while everything it names is still available and every chef
  it worked around is still placed where it was: a chef added keeps it on screen (no flicker), a chef
  taken out hides it (owner's screenshot, 2026-10-19: after clearing every place, the plan for the
  chefs placed before called kitchens 1 and 2 set for seconds). Measured (InnSetupPlanningTests), from
  a reading that clears every place to advice on screen: 11.4 s before (the simulator's plan only),
  0.4 s after; with the 0.5 s reading cadence and ~0.3 s of OCR, the advice follows a click in about
  1–1.2 s where it took 2–3 s of reading plus 7–11 s of planning.
  A plan on screen stays (owner, live 2026-10-20: "the recommended recipe keeps changing, it is very hard to
  follow"; test_yuanyuan6, 2026-10-21: "the recommendation should not change, unless some kitchen is filled"):
  [`InnPlanJudge`](src/EndHud/InnSetupPanel.cs) keeps it while it fits the inputs (the same theme: hot types,
  preference, objective; the same kitchens and places; the chefs it worked around still placed, chefs placed since
  being ones it named, everything it names offered, the rules still met), and while it fits no newer plan replaces
  it, however much better. Only a plan put on screen less than 1 s ago (`InnSetupPlanning.RefinedWithin`) may still
  give way to a clearly better one: a better theme match, the objective it misses reached, or at least 5 % more
  income (`InnPlanJudge.ClearlyMoreIncome`; in the simulator when the newer plan is the simulator's). In
  test_yuanyuan6 the simulator's plan came 8.4 s after the quick one and replaced kitchen 1's Antal + Wulfgard with
  Akekuri + Snowshine while the player was in kitchen 1's picker placing Wulfgard (InnSetupSteadyTests replays it on two of the
  recording's frames). The cost: when the simulator takes longer than 1 s, the quick search's plan is the one that
  stays. With nothing fitting on screen, a refined plan ready within 1 s is shown directly; "within 1 s" is when it was
  ready on the planning's clock (`InnClock`, or a test's own), never when the worker got round to looking (the v0.6.5
  CI runner, overloaded, held the worker up past a 1.3 s refinement and it was shown directly). While the menu screen keeps showing cards not seen
  before (scrolling), a fitting plan isn't worked out again until no new card has shown for 1.5 s
  (`InnSetupPlanning.ScrollSettle`). Which cards are on the menu never was in the key: the player following the
  advice only moves the ticks. Measured (InnSetupStabilityTests, a scripted scroll then pick of 19 readings):
  the advice on screen changed 16 times before, 3 after.
  A plan kept for newer inputs keeps its chefs, dishes and drinks, but its suggestions are worked out again for
  them, on the worker (`IInnPlanJudge.Restate`): what each kitchen's panel says follows what's placed (owner, live
  2026-10-21, debug_yuanyuan1: kitchen 1, filled before the plan, said it was set, while kitchens 2 and 3, filled with
  exactly the chefs recommended, went on recommending them, since the kept advice still described the kitchens as
  they were when it was planned). `IInnPlanJudge.Fits` still judges by the inputs the plan was worked out for.
- **Placement** ([`InnAdviceLayout`](src/EndHud/InnAdviceLayout.cs)): each spot is a fraction of the
  window's centred 16:9 area, measured on the fixtures (kitchen cards at x 0.418–0.588, 0.594–0.764,
  0.771–0.941, blank below y ≈ 0.66 to 0.865; the picker's blank x 0.33–0.97, y 0.68–0.86; the menu
  screen's left column x 0.06–0.38, y 0.60–0.835). The panels follow the game's layout, so they aren't
  dragged and have no place in config (the old panel's `InnSetupX`/`InnSetupBottomY` are gone; a
  config still holding them loads). The lines wrap at the spot's width; the menu's panel is drawn
  compact (`InnStripLook.Compact`: 13 px text, 20 px pictures at 1080p) so a full menu fits. The
  strip's size and opacity apply; above 100% a panel can grow past its spot's bottom, and past its right edge where a
  piece that can't wrap (a picture with its name, a label and what's kept with it) is wider than the lines: the lines widen
  to it rather than cutting a name at the card's edge (owner, test_yuanyuan8: words running off the menu panel; a kitchen's
  panel at 200 % at 4K cut a chef's name before; InnSetupLayoutTests checks every word and picture inside its card). The frames are
  made in code and never hit-tested (they don't take the game's clicks). Text only.
- Tests: InnSetupMemoryTests (kept across setup, picker, menu and session; a failed reading changes
  nothing; forgotten on the theme list and after 30 s of other screens, never after an alt-tab; the dishes the player
  has: the rating's and those seen),
  InnSetupStabilityTests (a plan on screen kept until clearly beaten or no longer fitting; the scripted
  picking; the owner's debug_yuanyuan1 sequence: kitchens filled as advised all say they're set, a half-filled
  one names only the chef missing), InnCallTests (the session's calls held through jittery readings; a new order and a queued dish
  change them at once), ReadingClockTests, InnResumeLatencyTests (desktop), InnSetupViewTests (each spot's words on real
  plans: half-placed, full and empty kitchens, placed chefs suggested nowhere, the picker, the menu meeting every
  rule, the tick, nothing on the theme list or in a session), InnSetupRulesTests (a rule cut where it wraps is
  read; a rule not understood said with the menu), InnSetupLayoutTests (each
  spot's fullest panel inside its spot at 1080p, 1440p and 4K; a picture beside every name),
  InnRealFrameTests (setup-chefs-placed, chef-picker, menu-dishes and menu-drinks through the real
  readers; menu-soup-theme read end to end: the soup rule comes off the frame and the plan has a soup and meets
  every rule; the owner's tea-break menu frames, soups and desserts only in view: the plan meets every rule, 3 types
  and both halves of the theme's rule), InnSetupPlanningTests (the quick
  advice first, replaced only when different; older work cancelled; no plan shown for chefs taken
  out; the before/after latency), InnSetupCostTests (what each reader and each planning stage costs,
  numbers only), InnWireUpTests (every inn screen held through a missed frame; the 0.5 s cadence),
  InnSessionTests (a gourmand's request kept through faded and unreadable frames until the table eats; a bubble given
  to the gourmand who said it, not the newest table; a served gourmand's words nobody's new request),
  InnPanelMarksTests and InnPanelMarkLayoutTests (which card is marked, nothing when unsure, the tabs, placement at
  1080p/1440p/4K, no banners while the panel is open), InnSteadyTests (the marks gone the moment the panel is seen
  closed), InnKitchenCircleTests (a slot per kitchen, the kinds
  apart, sizes, place above the tray, nothing over a panel read with all its tabs over both replays, the 4K picture),
  InnSixGourmandsTests (six gourmands kept, a tile each at its table's fixed spot, the same place alone or with five others, clear of the HUD, meter, tray, circles, panel and UID at every size; the player's size never grows a tile past its spot), InnGourmandTests (the gourmands' tiles: a numbered item per request, each on its own request,
  kept while the panel is open, pictures 112 alone or side by side), InnGourmandOrderTests (a gourmand's dish called,
  gone once resolved, covered by a dish in hand, its own call beside a table's of the same dish, the tile's dish and the
  call's the same and held, a request's number kept), InnCirclesFixedTests (the circles' one place), InnRealFrameTests (dispatch-panel-banners.jpg:
  the call marked on its card through the real readers; the owner's soup-and-drink gourmand to its banner), I18nTests (no not-seen-yet, kitchens-full or no-stock line in zh-CN.xaml).

Assumptions the advice rests on, not measured: what Full Blaze's faster kitchens do to cook times
(not modelled: the estimate runs at normal speed and is corrected each time the panel shows a
kitchen); the menu screen's stock is the session's start, less the dishes the estimate saw cooked;
Si's quality assist is taken as 0 (it only breaks ties between kitchens).

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
- `phases`, in order. `when` says when a phase applies: each reading, of the phases whose `when`
  holds the one with the biggest priority is shown, and among equal priorities the first (the last,
  with `lastMatchWins`). When none holds, the first phase without `when` is shown: it's the
  "otherwise" phase. While something can't be read, or when every phase has a `when`, the phase
  already shown stays. See [Choosing the phase](#choosing-the-phase).
- `lastMatchWins` (optional, format 3): `true` shows the last phase whose `when` holds instead of
  the first, among those of equal priority.
- `priority` (optional, format 6): a phase's priority, a whole number (negative allowed), 0 when
  absent. `priorityRules` (optional, format 6): "if this holds, the priority is that", in order,
  each `{ "when": [ … ], "priority": n }` (`when` as a phase's: a list that must all hold, any kind of
  condition). The first rule whose `when` holds sets the phase's priority; when none does, `priority`
  applies. Only for a phase with a `when`: the otherwise phase isn't ranked. Example, a burst phase
  that outranks the others once Laevatain's ultimate is ready:

  ```json
  { "id": "burst", "name": "爆发轴", "when": [ { "kind": "skillPoints", "is": "AtLeast", "value": 2 } ],
    "priority": 1,
    "priorityRules": [ { "when": [ { "kind": "ultimate", "operatorId": "Laevatain", "ready": true } ], "priority": 10 } ],
    "steps": [ … ] }
  ```
- `action`: `BattleSkill`, `Combo`, `Ultimate`, `BasicAttack` (format 4), `HeavyAttack`
  (format 8), `PlungeAttack` (下落攻击, format 9) or `Condition` (format 9, below). The skill being
  available (a skill point for a battle skill, the combo or ultimate ready) is implied; `expect`
  lists anything else. `BasicAttack`, `HeavyAttack` and `PlungeAttack` are drawn only (`StepActions.IsDisplayOnly`):
  the strip shows the operator's avatar as the icon (no skill icon, no small face in the corner)
  labelled with the action's name (`Rotation.Action.*`); it is never seen "used" and its
  availability is unknown, so the phase's `when` says when it shows (e.g. a 普攻 phase gated on
  `skillPoints` `Below` 2).
- `Condition` (format 9): a **condition step** (owner, 2026-10-24: "check whether enemy has 3 ice
  infliction, and show a highlight when satisfied"). No skill and no operator (`operatorId` is
  `""`): its `expect` is the step, every condition must hold, the same kinds and checks as anywhere
  (`ConditionHostKind.ConditionStep`). On the strip its circle holds the first condition's icon by the
  alert pop-up's rule ([`ConditionIcon`](src/EndHud/ConditionIcon.cs): a status's icon, an
  operator's avatar, the skill-points glyph, else "!"), labelled 条件 (or its `label`), with the
  conditions in words under it (or its `note`; two lines at most). It **glows cyan while its
  conditions hold and is dim otherwise**, keys read or not, done or not (`StripCondition.Holds`,
  evaluated on every reading with the phase selector as history; part of the strip's redraw key,
  `RotationStripView.Key`, or the glow would never change). The next phase's preview and the
  editor's preview draw it dim. With the battle-skill keys read
  ([`StepFollower`](src/EndHud.Vision/Rotations/StepFollower.cs)) it is the current step until its
  conditions hold, then done on that reading (and stays done if they stop holding); seen not
  holding, a cast of a later step doesn't pass it; when they can't be told (unknown), the next real
  cast of a later step passes it, as a basic attack is passed. Waiting on it, the reasons under the
  strip are its conditions' needs. A phase isn't played through until its condition steps are done,
  but a phase of nothing but condition steps never counts. Refused: one with no condition
  (`ConditionStepEmpty`) and one in a group (`ConditionStepWithPrevious`: it, or the step after it,
  marked `withPrevious`).
- `operatorId` `"controlled"` (`RotationStep.Controlled`, format 9): an attack step done by whoever
  is controlled at the time (当前操控), e.g. a plunge attack after a combo. The strip shows the
  operator under the chevron in this reading (`PartyState.ActiveSlot`); nobody known (the editor's
  preview, the chevron not read): no face, the label alone. Only for `BasicAttack`, `HeavyAttack`
  and `PlungeAttack`; a skill step on it is refused (`ControlledOperatorOnlyForAttacks`: a skill is
  followed by whose it is). The editor's operator picker offers 当前操控 for an attack only.
- `withPrevious`: used right after the previous step of the same phase, drawn overlapping it.
- `label` (optional): replaces the action's name above the icon, for a variant the game names
  differently (e.g. "战技·意"). `note` (optional): a short hint under the icon.
- `expect` (a step) and `when` (a phase) are lists that must all hold. Each item (`kind`):

  | `kind` | Fields | Holds when |
  |---|---|---|
  | `skillPoints` | `is` (`AtLeast`, `AtMost`, `Exactly`, and from format 4 `Below`, `Above`), `value` (0–3; the editor offers tenths) | the skill point bar compares so |
  | `combo` | `operatorId`, `ready` | that operator's combo is ready (`true`) or cooling (`false`) |
  | `comboCharge` | `operatorId`, `is`, `percent` (0–100) | that operator's combo cooldown bar has refilled so far (ready is 100) |
  | `battleSkill` | `operatorId`, `ready` | that operator's battle skill button is available (`true`) or cooling (its countdown seconds aren't read) |
  | `ultimate` | `operatorId`, `ready` | that operator's ultimate is ready or charging |
  | `ultimateCharge` | `operatorId`, `is`, `percent` (0–100) | that operator's ultimate is charged so far (ready is 100; at most 0 is just used) |
  | `battleSkillActive` | `operatorId`, `active` (format 7) | that operator's lasting battle skill is running (`true`) or not: Si's, from the swirl on her battle-skill button until the butterfly is back (the purple countdown round her stacks' circle, see [Si's battle-skill timer](#sis-battle-skill-timer)). This reading's button decides (swirl or crossed-out butterfly: running; butterfly: not); a reading that can't tell it (an effect, a countdown look) goes by the overlay's timer (`PartyState.RunningSkills`, the estimate while it lasts): running while it shows one, else unknown. Unknown with a controller (the buttons aren't read). Only Si's is read (`SkillRing.Operators`); naming anyone else is refused (`SkillTimeNotRead`), and the editors offer only Si, greyed out without her |
  | `ultimateActive` | `operatorId`, `active` (format 3) | that operator's lasting ultimate is running (`true`) or not, whoever is controlled: one of the running ones from [Lasting ultimates](#lasting-ultimates) (`PartyState.RunningUltimates`), on the time bar or carried on in the background. Unknown while the skill points can't be read, and, for an operator whose ultimate isn't one of the known lasting ones, while a bar shows (it could be hers); with no bar, false |
  | `ultimateTimeLeft` | `operatorId`, `is`, `percent` (0–100) (format 9) | how much of that operator's lasting ultimate is left to run, as a % of its time bar (`LastingUltimateView.LeftShare` of the running ones from [Lasting ultimates](#lasting-ultimates), on the bar or carried on in the background); 0 while it isn't running (as a ready ultimate's charge is 100). Unknown while the skill points can't be read. Only Laevatain, Zhuang Fangyi and Yvonne (`LastingUltimates.Operators`); anyone else is refused (`UltimateTimeNotRead`), and the editors offer only them, greyed out without any. No seconds: no ultimate's length has been measured |
  | `counter` | `operatorId`, `counterId`, `is`, `value` | an operator's own resource compares so (see below) |
  | `bossStatus` | `statusId` (the wiki's English name), `present`, `minStacks` | the enemy shows (or doesn't show) that status; with `minStacks` above 1, with at least that many stacks (inflictions) or that level (Corrosion, Electrification: I–IV as 1–4). An absent status counts as 0; a present one whose count isn't read (covered, a dark scene, or a status whose count EndHud doesn't read: `BossStatusReader.CountsRead`) is unknown |
  | `controlling` | `operatorId` | that operator is the one being controlled |
  | `phaseShown` | `phaseId`, `is`, `times` (format 4) | that phase has come onto the strip so many times this fight (see [Choosing the phase](#choosing-the-phase)); unknown outside a phase selector (step `expect`, alerts) |
  | `phaseCurrent` | `phaseId`, `playedThrough` (optional) (format 9) | that phase is the one on the strip (as of the reading before, as the phase selector sees its own counts). With `playedThrough` `true`: and the step follower has played it through since it last came onto the strip (`StepFollower.PlayedThroughThisShowing`, set when its round is counted, cleared when another phase comes on); `false`: not yet. The played-through part needs the battle-skill keys read; while they aren't it is ignored and only "the phase is X" is asked. Unknown before the first reading of a fight and when the rotation naming it isn't the one followed (alerts: it names a phase of the rotation followed, like `phasePlayed`). A gate on "the phase is me (not played through)" holds the phase until it is played through; see the 祀 rotation |
  | `phasePlayed` | `phaseId`, `is`, `times` (format 5) | that phase has been **played through** so many times this fight: every step of it seen done by the step follower (see [Battle-skill key reading](#battle-skill-key-reading)). Only while the player reads their battle-skill keys (`ReadSkillPresses`); while they don't, it is **ignored**, as if it weren't there (below). Unknown when the rotation naming it isn't the one followed |
  | `all` | `of`: a list of conditions | every one holds (and) |
  | `any` | `of`: a list of conditions | at least one holds (or) |
  | `not` | `of`: one condition (format 3) | it doesn't hold; unknown stays unknown |

  `all` and `any` nest to any depth. Something the screen doesn't show right now (a cutscene, an
  operator's counter while someone else is controlled) is *unknown*, not false: `all` is false if a
  part is false, else unknown if a part is unknown; `any` is true if a part is true, else unknown if
  a part is unknown. A phase's `when` that is unknown doesn't switch to it.
  `Below` and `Above` are strict (`skillPoints` `Below` 2: 1.95 holds, 2 doesn't); `Exactly` allows
  ±0.05. [`ConditionWalk`](src/EndHud.Vision/Rotations/ConditionWalk.cs) opens up groups and `not`
  at any depth; everything that asks "is there a condition of kind X anywhere" (the format needed,
  whether boss statuses must be read, an alert's problems) goes through it.

  **One condition language** (owner, 2026-10-24: "everything that supports condition use the same
  language and cover the same"). Every kind is one entry of
  [`ConditionCatalogue`](src/EndHud.Vision/Rotations/ConditionCatalogue.cs): its `kind` in files,
  its name in the editor's kind picker, the rotation and alert formats that hold it, the operator it
  is about, its comparison, the phase it names, what it needs (a phase, the keys read, boss
  statuses, an operator whose time is read) and where it may be used (every host). Wherever
  conditions are written (a phase's `when`, a priority rule, a step's `expect`, an alert's `when`)
  the same full set is offered and the same checks run
  ([`ConditionRules.Check`](src/EndHud.Vision/Rotations/ConditionCatalogue.cs), given a
  `ConditionHost`: which host, the squad if there is one, the phase the conditions belong to, the
  rotation's phases). What differs is only that context: an operator outside the squad is refused
  where there is a squad (rotations; an alert has none, so such a condition is unknown in a fight
  without that operator), and a deleted phase is refused where the rotation is known. The format
  needed (`Rotation.FormatNeeded`, `AlertFile.FormatNeeded`), whether boss statuses must be read,
  and the operator a condition is about all read the catalogue. A test checks that every
  `JsonDerivedType` of `StateCondition` is in it under the same name.

  Four problems are refused in the editor with a sentence: a phase whose own `when` counts its own
  showings (`PhaseShownOwnGate`: counted on arrival, it would flip on the very reading that shows
  it), a `phaseShown` or `phasePlayed` naming a phase that was deleted (`PhaseShownMissingPhase`,
  `PhasePlayedMissingPhase`), and a phase whose own `when` requires the phase itself played a
  number of times that 0 doesn't satisfy, outside any `any`/`not` (`PhasePlayedOwnGateNeverOpens`:
  it must be shown to be played, so it would never open). A gate on its own plays that holds at 0
  (`AtMost` 2: "play it twice, then leave it") is fine.

  A priority rule's `when` is checked like an entry condition's (an operator not in the squad, an
  empty group, a `phaseShown`/`phasePlayed` naming a deleted phase), and the problem names the rule
  (`RotationProblem.Rule`, from 1). A rule may count its own phase's showings or plays: it only ranks
  the phase once its `when` holds, so it can't flip the phase on by itself. Two problems of their
  own: a rule with no condition (`PriorityRuleWithoutCondition`: it would always hold; set the
  phase's `priority` instead), and a priority or rule on a phase without `when`
  (`PriorityWithoutEntryCondition`: the otherwise phase isn't ranked, so it would do nothing).

  **`phasePlayed` while the battle-skill keys aren't read** (owner, 2026-10-23): nothing is played
  through, so the condition is ignored, as if it weren't there. A part made of nothing but
  `phasePlayed` (a `not` of one, a group of only such parts, at any depth:
  `ConditionWalk.OnlyPlayed`) is dropped from its `all`/`any`, and a group left empty is dropped
  from its own parent the same way (`RotationTracker.Ignored`, told by
  `IPhaseHistory.PlayedIgnored`). A step's `expect` made of nothing else holds (nothing to wait
  for). Two exceptions, so an ignored condition can't turn into "always": a phase's `when` made of
  nothing else **never opens** (holding on every reading would pin the strip to it), and an alert
  made of nothing else **never fires** (it would fire at every fight's start). The editors still
  offer the kind, greyed out with a sentence saying it needs 读取战技按键, and a condition already
  there shows that sentence under it.

- `format`: 9 when it uses `ultimateTimeLeft`, `phaseCurrent`, a `Condition` or `PlungeAttack` step, or the `"controlled"` operator; else 8 when it uses a `HeavyAttack` step; else 7 when it uses `battleSkillActive`; else 6 when a phase sets `priority` (other than 0) or `priorityRules`; else 5 when it uses `phasePlayed`; else 4 when it uses a `BasicAttack` step, `Below`/`Above` or `phaseShown`; else 3 when it
  uses `not`, `ultimateActive` or `lastMatchWins`; else 2. A rotation is saved in the lowest
  format that holds it (`Rotation.FormatNeeded`), so one using nothing new stays readable by older
  builds, and the published `rotations/*.json` stay at format 3 or lower (a test checks). A
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
| Lasting ultimate ([`UltimateTimeBar`](src/EndHud.Vision/UltimateTimeBar.cs)) | the thin line right above the skill points, while a lasting ultimate runs (Laevatain's, Zhuang Fangyi's, Yvonne's): the controlled operator's own if she has one running, else one running in the background | one colour from the left over a grey track; its length is the time left, its colour (`PartyState.UltimateTimeColour`, the mean of the fill's columns) whose it is (see [Lasting ultimates](#lasting-ultimates)). Taken as the bar only with that shape and a line's edges (bright sky there read as a full bar before). Kept, with its colour, through gaps of up to 4 s (PartyTracker). |
| Controlled operator ([`ActiveSlot`](src/EndHud.Vision/ActiveSlot.cs)) | the chevron over the controlled operator's portrait | shape match against the chevron |
| Battle skill ([`BattleSkillButton`](src/EndHud.Vision/BattleSkillButton.cs)) | each battle skill button, bottom right | a dark disc with white digits is cooling; the white icon is available |
| Operator counters ([`OperatorCounters`](src/EndHud.Vision/Operators/OperatorCounters.cs)) | the controlled operator's resource, bottom centre; in combat with keyboard and mouse, each other operator's on the lower left of their squad portrait, about 0.7 the size ([`PortraitCounters`](src/EndHud.Vision/Operators/PortraitCounters.cs), see [Counters on the portraits](#counters-on-the-portraits)) | the same readers in both places. Laevatain's 熔火 (0–4): lit petals of the flower; pink petals mean full. Typhoeus's 猎矢 (`hunting-arrow`, 0–4): bars lit cyan (with a white streak over them when full); and 启示 (`sign`, 0–8): dots lit white, filling from the bottom row. Unknown when a bar or dot is neither lit nor grey, or the lit ones aren't in their fill order (the HUD moving). Zhuang Fangyi's 青霆剑 (`sunderblade`, 0–9): cells of a diamond grid lit cyan. Si's 伤害层数 (`damage-stacks`, no maximum): the badge left of the HP bar where Laevatain's flower is; 0 is a hollow green four-lobed flower with no number, from 1 a green cross with a white number on it, read digit by digit against the game's digit shapes ([`BadgeNumber`](src/EndHud.Vision/BadgeNumber.cs); Windows OCR reads nothing from one or two lone digits). Any number of digits; only 1–2 measured (up to 45 seen) |
| Si's battle-skill button ([`SkillRing`](src/EndHud.Vision/SkillRing.cs)) | her battle-skill button, bottom right, keyboard and mouse only | which icon (butterfly `Original`: not running; swirl `Changed`: running; crossed-out butterfly in a four-pointed ring `SecondCast`: running, cast a second time; a countdown: cooling; none clearly: unknown) by the icon's white shape against three masks; while the swirl or the crossed-out butterfly shows, how much of the grey ring inside the button is left (null when it can't be read). Measured with Si in slots 1 and 3; any slot is read |
| Elemental infliction ([`InflictionRing`](src/EndHud.Vision/InflictionRing.cs)) | the ring at the right end of the boss's HP bar, top centre | present when the white stack segment on top of the ring shows; the element from the colour of the icon inside (heat, cryo, electric, nature). Stacks (1–4, `ReadWithStacks`): four arc segments centred at 12, 3, 6 and 9 o'clock; a lit one is a white arc standing out of its dark outline, an unlit one has no arc and no track (the scene shows through). The lit ones must be the first n clockwise from the top, and every segment must be clearly lit or unlit; anything else is unknown |
| Vulnerable ([`VulnerableRing`](src/EndHud.Vision/VulnerableRing.cs)) | the same ring, in the infliction's place (it isn't in the status row) | a white shield crossed by a dark slash (lower left to upper right) on a dark red disc, with the infliction's stack arcs around it (1–4, the same segments). Present when there's no element, the top arc is near white (≥ 150, so the HUD fading in still counts), the shield is white on both sides of the slash and the slash is at least 80 darker than the shield (106–229 with the shield, at most 55 without it, 32 where the place was white). When a stack is added a grey circle sweeps through the arcs and also lights the gaps between them, where the game draws nothing; a lit gap makes the count unknown (without that check the 2→3 flash reads 4). On the physical-team recording every 0.5 s: 257 right, 9 count unknown, 0 wrong of 266; four arts-team recordings (585 frames) never read Vulnerable. With an infliction too, both show side by side (next row). Fixtures/vulnerable |
| Vulnerable and an infliction together ([`BossStatusReader.RingIcons`](src/EndHud.Vision/BossStatusReader.cs)) | the ring's place, and 94 px (4K) right of it at the same height | the game draws both, the same size, in the order they were applied, each with its own stack arcs: in test_rossi heat then Vulnerable (20–25 s), then Vulnerable then heat (33 s) and Vulnerable then cryo (37 s). The later one arrives in the second place; when the first goes it fades where it stood (25.5 s) and the second moves into its place (25.75 s). The second place is read like the first (an infliction, else Vulnerable) and reported at `BossStatusReading.SecondRingPosition`, but only when the first is read, holds the other kind, and the second's top arc is lit (white with its dark outline). A second icon with no first is a moment of change; two inflictions side by side are one replacing another (Si 20.1–20.6 s: the new nature took the first place while the old cryo slid right and faded; Typhoeus 30 s, nature over nature), so the right one is already off the boss; and at Typhoeus 105.5 s sky with a dark branch passed the shield test in the second place but had no arc. On test_rossi every 0.5 s (79 frames): both read on 20 of the 21 frames showing two (the 21st is the first fading), 64 icons right, 0 wrong; the four arts-team recordings (1,170 frames) and the physical-team one (267) never read a second icon or a Vulnerable that isn't there. Fixtures/vulnerable-and-infliction |
| Enemy statuses ([`BossStatusReader`](src/EndHud.Vision/BossStatusReader.cs)) | the icon row under the boss's bars, top centre | each square is matched against the wiki's status icons; read at most once a second and only when the current rotation (a phase's `when` or a step, at any depth) or an alert rule asks about statuses (about 70 ms a read at 4K). Each reading carries its count where one is read (`BossStatusReading.Stacks`): the ring's stacks (each icon's, when Vulnerable and an infliction show together), or a reaction's level |
| Reaction level ([`ReactionLevel`](src/EndHud.Vision/ReactionLevel.cs)) | the badge on a Corrosion or Electrification icon's bottom-right corner | a dark square with the level as a Roman numeral in the reaction's colour, read in two bands between the serifs: an "I" is one upright run in both bands, a "V" two runs above that close into one below; the runs must spell I–IV. **Level I has no badge** (owner, 2026-10-22): a corner that clearly has none (no numeral, little dark, the square's own background showing, the icon's art in full colour) reads I. A dark corner (a badge, or a dark scene that could hide one), one covered by an effect, or one washed out (an icon fading in) is unknown, never I. Over the four recordings every 0.2 s, 0 of 542 badged readings read I and 329 of 599 badgeless readings did; the rest stayed unknown. Only these two reactions are read (`ReactionLevel.Measured`) |

Status icons come from the wiki's glossary module along with the operators (`statuses.json` in the
operator library), so new statuses need no release. Chinese names are in
[`StatusNames`](src/EndHud.Vision/Operators/StatusInfo.cs).

### Si's battle-skill timer

[`SkillTimer`](src/EndHud.Vision/SkillTimer.cs) turns each reading of her button
(`PartyState.SkillRings`, read in `PartyMonitor` once the squad is named) into what the gauge
shows:

The skill runs from the first sight of the swirl until the butterfly is back. A second cast while
it lasts turns the swirl into a crossed-out butterfly (`SecondCast`) and doesn't end it: the ring
doesn't fill again, it drains on from where it was (measured on two recordings, Si in slot 3 and
in slot 1: [`Fixtures/si-timer-second-cast`](tests/data/Fixtures/si-timer-second-cast)). So the
ring is one clock for the whole skill, and the swirl and the crossed-out butterfly count alike:

- **Swirl or crossed-out butterfly seen, ring read:** the ring is the clock. Time left = the
  ring's share × 22 s (`SkillRing.SiDuration`: full at 13.5 s, gone at 35.5 s, butterfly back at
  35.75 s on the owner's fight, ±0.25 s; whether her skill level changes it is not known). The
  start is re-anchored to that reading.
- **Seen, ring not read** (an effect over it): an **estimate** from the start (the first reading
  the changed icon was seen, or the ring's last anchor) and 22 s (`SkillTimerView.Estimated`; the
  gauge shows no seconds, so it looks the same). Once the estimate runs out it's held at 0 for as long as the changed icon is
  still seen: the ring stands still during cutscenes, so the estimate can run out before the
  skill does (on one recording the ring went from 0.15 to only 0.10 over 8 s of a cutscene).
- **Button not told** (hidden by a cutscene, an effect, a countdown look, the controller
  layout): the skill goes on. The estimate is shown while it lasts; past it, nothing, until the
  button is seen again (a timer held at 0 on nothing but the start would stay up all battle).
- **Butterfly back:** the skill ended; the start is forgotten. Only the butterfly ends it (once,
  on a recording, the ring dropped to empty early and the butterfly followed half a second later).

With a controller the battle skill buttons aren't read, so there is no timer there.

The overlay works the timer out once per reading, before anything reads it
(`OverlayWindow.WithRunningSkills`, as `WithRunningUltimates` does for the lasting ultimates), and puts
it on the state (`PartyState.RunningSkills`): the gauge, the rotation's and the alerts' `battleSkillActive`
all read the same one. Readings the overlay didn't stamp (`endhud-cli`, tests) go by the button alone.

### Choosing the phase

[`PhaseSelector`](src/EndHud.Vision/Rotations/PhaseSelector.cs), once per reading: of the phases
whose `when` holds, the one with the **biggest priority** becomes the one shown; among equal
priorities, the first in the list (the last, with `lastMatchWins`; unknown counts as not holding). When none holds and every `when` could be read, the first phase without `when` is
shown (the "otherwise" phase); while one can't be read, or when every phase has a `when`, the
phase shown stays; before any has held, the first phase without `when` (or the first phase).
Until 0.5.1 the phase shown always stayed when none held, so a phase without `when` never came
back once another had held (a player's 充能轴 I "Laevatain's ultimate at most 50%" never gave way
to 充能轴 II).

**Priorities** (owner, 2026-10-24: "if condition, priority is x"). A phase's priority this reading
is the first of its `priorityRules` whose `when` holds, else its `priority` (0 when not set).
Bigger wins; equal ones go by list order, so a rotation that sets no priority (every rotation from
before format 6) picks exactly as before (`PhaseSelectorTests.All_priorities_0_is_list_order_as_before`
and the older tests pin it). Decided with the owner's three rules:

- Priorities only rank the phases whose `when` holds. The otherwise phase (no `when`) keeps its
  role, shown only when none holds, and a priority on it is refused (above).
- A rule that can't be read (unknown) doesn't hold: the next rule, or the base number, applies. A
  rule made only of ignored parts (`phasePlayed` without the keys, `RotationTracker.Ignored`)
  doesn't hold either: it would hold on every reading.
- What a `when` that can't be read does is unchanged: it only matters when no phase holds, and then
  the phase shown stays.

Rules see the counts as the entry conditions do, before the reading's switch. The strip's reasons
under the icons and the editor's preview don't show priorities.

In the editor, under a phase's entry condition: **优先级**, a box taking a whole number (full-width
digits and signs from a Chinese input method count; anything else is refused under the box, and
saving is refused naming the phase), with a line saying bigger wins and equal ones go by list
order; then **条件优先级**, the rules in order, each "第 n 条：下面的条件都满足时，优先级为 [n]" with ↑ ↓
and 删除这条, its conditions as the same pickers as an entry condition's (`ConditionEditor`), and
添加条件. No rules: only the number. A new rule starts one above the phase's number, with one
condition to change. On a phase without an entry condition both are greyed out, saying why, unless
one is already set there (so it can be put back to 0).

Conditions are evaluated by `RotationTracker.Evaluate`, which takes an `IPhaseHistory`: the
selector passes itself, anything else (`RotationTracker`'s step following, the strip's reasons)
passes `PhaseHistory.None`, where `phaseShown` and `phasePlayed` are unknown. The selector's
`phasePlayed` counts are the step follower's: `PhaseSelector.Update(state, plays)` takes the
rotation's `StepFollower`, or null when the battle-skill keys aren't read (then `phasePlayed` is
ignored, see [The file](#the-file)). Alerts get the history from `RotationFollowing.History`: the
selector of the rotation followed now, `PhaseHistory.KeysNotRead` without the keys, or
`PhaseHistory.None` when no rotation is followed.

The selector also counts, per phase, how many times it has **come onto the strip** this fight
(owner, 2026-10-22: counted on arrival, so the one on the strip now is included; leaving and coming
back counts again). The conditions see the counts as they were before the reading's switch, so a
gate on another phase's count opens on the reading after that phase first shows.
`PhaseSelector.EndFight` forgets the phase shown and every count; the overlay's
[`RotationFollower`](src/EndHud/OverlayWindow.Rotation.cs) calls it when the fight ends
(`StripVisibility` going from in battle to not, so short gaps don't reset it). Reloading the rotations
(`ReloadRotations`, e.g. after the editor saves) starts every selector afresh as well, counts included.

[`RotationTracker`](src/EndHud.Vision/Rotations/RotationTracker.cs) also follows the steps inside a
phase from skills it sees used, as below. The app doesn't use that part: the current step comes from
the battle-skill presses instead (see [Battle-skill key reading](#battle-skill-key-reading)), since
the screen alone can't tell reliably which button was pressed:

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

### Battle-skill key reading

Why: the screen shows that a battle skill went into cooldown, but not reliably which step of the
phase that was, so without the player's keys the strip can't say "you are here". With the player's
consent EndHud reads the keys that cast battle skills, and nothing else, and the strip marks the
current step.

**The switch.** The menu's rotation card has "read my battle-skill keys" (`ReadSkillPresses`, off by
default: the owner's privacy decision, 2026-10-23). It is usable only with the rotation strip and
the squad reading on. Below it, the four keys, one per slot, chosen by clicking "change" and
pressing the key (Esc keeps the old one; a key another slot uses is refused, naming that slot), and
"restore default keys (1-4)". Combos and ultimates are still seen on the screen, as before.

**What is read** ([`SkillInputReader`](src/EndHud/SkillInput/SkillInputReader.cs), on its own thread,
only while the switch is on, the strip and the squad reading are on, and the game itself is in
front: never in another program, never in EndHud's menu):

- The keyboard through Raw Input (it only listens: no key is taken from any program, unlike a
  hotkey on a bare 1-4, which would stop digits working in a browser while EndHud runs). A
  low-level keyboard hook is never used.
- Xbox pads through XInput (Microsoft's documented button bits), PlayStation pads through Raw
  Input's HID reports. A chord is the left shoulder (L1/LB) held plus the face button in the slot's
  place on the diamond: left (square / X) slot 1, top (triangle / Y) slot 2, right (circle / B)
  slot 3, bottom (cross / A) slot 4.
- [`SkillInputDecoder`](src/EndHud/SkillInput/SkillInputDecoder.cs) turns each event into "slot N was
  cast" or nothing. An unbound key is dropped inside the call that receives it; the only state kept
  is which of the four bound keys are held (a held key casts once) and each pad's five buttons.
  Every press becomes a `SkillPress` (slot, time on the reading clock): nothing else about the key
  exists anywhere.
- Turning it off unregisters everything from Windows (`RIDEV_REMOVE`) and ends the thread. If
  Windows refuses to deliver input, the card says so and the reason is logged; the strip then shows
  the phase as without the switch.

**Which presses count** ([`SkillPressJudge`](src/EndHud.Vision/Rotations/SkillPressJudge.cs)). Each
reading takes the presses made up to its own time (`PartyState.ReadAt`) and judges them against the
reading before: a press counts only in battle (a digit typed in a chat window or a menu does not),
for a slot with an operator, whose battle-skill button wasn't cooling down, with a skill point left
once the presses already counted since that reading have spent theirs, and at most once per slot
between two readings (a mashed key casts once). With a controller the button's cooldown isn't read,
so the skill points decide.

**Following the steps** ([`StepFollower`](src/EndHud.Vision/Rotations/StepFollower.cs)). The phase
still comes from `PhaseSelector`; the follower only follows inside it and starts at the first step
when the phase changes or the fight ends. A step is done when it is seen used: a counted press of
its slot for a battle skill, the combo bar emptying for a combo, the ultimate going from ready to
spent. Battle-skill cooldowns seen on the screen are not used (the press is the cast; both would
count it twice). Steps drawn together may be used in either order. A use that matches no reachable
step (off-script, or a later step while an earlier skill is undone) moves nothing. Basic- and heavy-attack
steps never hold the marker: the next real cast of a later step passes them. A phase played
through starts again at its first step.
It also counts, per phase, the rounds **played through** this fight (`StepFollower.TimesPlayed`, for
`phasePlayed`; owner, 2026-10-23): a round counts once, the moment its last step that can be seen
used is done (trailing basic- and heavy-attack steps are never seen, so they don't hold it up). A phase left
half way doesn't count; a phase of nothing but basic or heavy attacks never does; `EndFight` sets every
count back to 0. [`RotationFollower`](src/EndHud/OverlayWindow.Rotation.cs)
runs it once per reading when it is handed the presses (`OverlayWindow.PressesFor`: the reader with
the switch on, none with it off); with none it returns no step marks and the strip is drawn as
without the switch.

**Privacy rules, each held by a test** (`SkillInputPrivacyTests`, `SkillPressTests`,
`SkillInputReaderTests`, `DiagnosticsTests`):

- `SkillPress` has exactly two properties, the slot and the time.
- Every unbound key code, pressed and released, gives nothing and leaves nothing in the decoder.
- The log gets one line when reading starts and one when it stops (and Windows' refusal, with its
  error number); never a key, scan code, button, device name or press time.
- The diagnostics carry only counts (below); nothing about presses goes into `recent/readings.txt`
  (a list of press times would be a record of keystrokes). The four chosen keys are in
  `config.json`, as the menu says.

**Diagnostics.** The summary has, after the last reading:

```
battle-skill key reading: listening: keyboard, PlayStation pads 0, XInput pads 1, other pads 0; PlayStation reports without buttons 0
  slot 1 battle skill: counted 12, cooling 1, no skill points 0, repeat 3, not in battle 4, empty slot 0
  ...
```

The status is `off (not consented)` with the switch off, `off` while not listening (the game not in
front), or `failed (Windows refused, error N)`. The counts are since EndHud started.

**Not covered** (each absent, not a placeholder):

| Cut | Why | What brings it back |
|---|---|---|
| Pads that are neither XInput nor Sony (DirectInput pads) | Their HID button numbers vary by maker; a table would be a guess | A diagnostics zip shows "other pads" with presses never counted, and the pad is named |
| DualSense on Bluetooth after something switches it to its extended report | That report carries no buttons the HID parser can see | Diagnostics shows "PlayStation reports without buttons" above 0 |
| A replay test over a recorded clip with a presses file | No clip with a presses file exists yet; `trace-video --presses` is what makes one usable | A recording with its `seconds,slot` lines |
| Moving on to the next phase of the flow when a phase is finished | `PhaseSelector` stays the phase authority; a finished phase starts over at its first step | The owner asks for it |
| Mashing the same slot across readings on a controller | The controller layout's battle-skill cooldown isn't read, so a second press one reading later is refused only by the skill points | Battle-skill button reading in the controller layout |

## Alerts

### The file

One file for every rule, global (every fight, every squad): `%APPDATA%\EndHud\alerts.json`
([`AlertRule`, `AlertFile`](src/EndHud.Vision/Alerts/AlertRule.cs),
[`AlertFormat`, `AlertStore`](src/EndHud.Vision/Alerts/AlertStore.cs)):

```json
{
  "format": 1,
  "rules": [
    { "id": "3b0e…", "when": [ { "kind": "skillPoints", "is": "AtLeast", "value": 1.8 } ], "message": "可以放战技了" },
    { "id": "9a4c…", "when": [ { "kind": "bossStatus", "statusId": "Cryo Infliction", "present": true, "minStacks": 3 } ], "message": "" }
  ]
}
```

- `id`: kept for the rule's whole life. `when`: every condition must hold; the same condition
  kinds as rotations, groups and `not` included, with the same checks (see
  [One condition language](#the-file)). `phaseShown` and `phasePlayed` are offered: an alert has
  no rotation of its own, so they name a phase of any of the player's rotations (listed as
  "rotation · phase", `AlertEditor.PhaseChoices`), and are known while that rotation is the one the
  strip follows (else unknown: the rule neither fires nor re-arms). `phaseShown` is known with the
  battle-skill keys read or not (the follower hands the alerts its phase selector either way).
- `format`: 4 when a rule uses `phaseShown` (before, the editor refused it), `ultimateTimeLeft` or `phaseCurrent`, else 3 when one uses `battleSkillActive`, else 2 when one uses `phasePlayed`, else 1, the lowest that holds the rules
  (`AlertFile.FormatNeeded`): rules using nothing new stay readable by builds from before format 2,
  which refuse the rest as newer (left as it is, never renamed as unreadable). The share code keeps
  its `EHA1:` prefix (the code's own version).
  `message`: the pop-up's text; empty shows the conditions in words.
- `AlertRule.Problems()`: no conditions, an empty group, or `battleSkillActive` for an operator
  whose battle-skill time isn't read (`SkillTimeNotRead`, as in rotations; before, alerts let it
  through and it could never fire). The editor refuses to save such a rule, with a sentence.
- Share codes: `EHA1:` + the JSON, deflated, in URL-safe base64 (the rotations' `EHR1:` scheme). A
  damaged code is refused; an `EHA2:` code says it's from a newer EndHud; anything else (a
  rotation's code) isn't alert rules. Files export as `战斗提示.endhud-alerts.json`.

### The store, all six answers

| | How |
|---|---|
| **Create** | The editor's 新建 (a rule with 技力 至少 2.0 and no text), 复制 (the selected rule again, right after it, new id), or an import. `AlertStore.Save` writes the whole file to a temp file, then renames it over `alerts.json`. |
| **Read** | `AlertStore.Load` at start and after every save; checked on every reading in battle. A missing file is no rules. |
| **Correct** | Select the rule, change its conditions or text, 保存: `AlertStore.Corrected` rewrites it in its place under its own id. A wrong rule is fixed in place, never by adding a second one. |
| **Remove** | 删除, after confirming ("删除提示“{0}”？此操作无法撤销。"): `AlertStore.Removed`. Nothing is removed automatically: no eviction, no age limit. |
| **Protect** | Every rule stays until the player deletes it. At most 100 (`AlertFile.MaxRules`): 新建 is refused with "最多 100 条提示，请先删除不用的。", `Save` refuses more. Imports never replace (`AlertStore.Import`): every imported rule gets a new id, one with the same conditions and words as a rule already there is skipped, and past 100 the rest is left out; the editor says how many of each. An **unreadable** file is renamed to `alerts.unreadable-<yyyyMMdd-HHmmss>.json` beside it, never overwritten or lost, and the editor says so (`RenamedEarlier` keeps the name for an editor opened later in the run). A file from a **newer** EndHud (format above `AlertFile.CurrentFormat`) is left as it is and saving is refused; so is a file that couldn't be opened (locked, no access). |
| **Carry** | `%APPDATA%` survives updates (the installer and updater don't touch it). Between computers and players: share codes (all rules, or the selected one) and files. There are no published alerts and nothing syncs from GitHub. |

### When a pop-up shows

[`AlertMonitor`](src/EndHud.Vision/Alerts/AlertMonitor.cs), on every reading in battle, evaluates
each rule's conditions as one `all` (`RotationTracker.Evaluate` with the history of the rotation
followed now, for `phasePlayed`; see [Choosing the phase](#choosing-the-phase)):

- A rule **fires** when it is seen holding after it was last seen not holding, or on its first
  sighting this fight. While it keeps holding it stays quiet.
- A reading where it can't be told (unknown: a cutscene, a count not read, an operator not in the
  squad) changes nothing: it neither fires nor re-arms, so a flickering reading can't make it fire
  again and again.
- [`AlertFollower`](src/EndHud/AlertFollower.cs) takes a pop-up down after 3 s
  (`AlertFollower.ShowFor`, also between readings on the overlay's timer) or on the first reading
  where the rule was seen not holding, whichever is first, or when the rule is deleted. At most
  three are up (`MostShown`): the oldest goes.
- Leaving battle (`StripVisibility`, the strip's rule) takes every pop-up down and re-arms every
  rule (`AlertMonitor.EndFight`); so does turning alerts off.
- While the battle-skill keys aren't read, `phasePlayed` parts are ignored; a rule made of nothing
  else never fires.
- The boss's statuses are read when any rule asks about them (`AlertMonitor.NeedsBossStatuses`),
  even when the rotation doesn't.

Measured by `AlertMonitorTests`: 技力 1.7 → 1.8 fires once; staying at 1.8 is quiet; 1.5 then 1.9
fires again; 寒冷附着 2 → 3 stacks fires, an unread count doesn't.

### The editor

[`AlertEditor`](src/EndHud/AlertEditor.xaml.cs) (menu → 战斗提示 → 编辑战斗提示…, or the tray):
the rule list (新建, 复制, 删除), the selected rule's 提示文字 and conditions on the shared
[`ConditionEditor`](src/EndHud/ConditionEditor.cs) pickers (the same full set as a rotation's;
轴段已显示次数 and 轴段已完成次数 list every rotation's phases), and 粘贴分享码,
复制选中的分享码, 复制全部分享码, 导入文件…, 导出文件…, 保存, 关闭. Saving reloads the overlay's rules at
once. The status condition offers 至少 1–4 层 for the four inflictions and 至少 I–IV 级 for Corrosion
and Electrification; for any other status a grey line says its count isn't read, and only
present/absent is offered. Si's damage stacks offer 0–200 (a larger value from a file is kept).

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
  filling the ring's place), at 4K and on simulated 1440p, 1080p and 16:10 screens; the stack count
  on cryo rings at 1–4 stacks from Si's fight (right or unknown, never wrong).
- **Reaction levels** ([`Fixtures/reactions`](tests/data/Fixtures/reactions)): Corrosion III,
  Electrification II, III and IV badges, badgeless Corrosion and Electrification icons read as I,
  other badges and dark scenes read as unknown (never a wrong level, never I where a badge could
  hide); each level-I condition has a test that fails without it.
- **Si** ([`Fixtures/si`](tests/data/Fixtures/si)): her damage stacks (the badge number, the zero
  flower) and her battle-skill button before, during and after her skill (icon, ring left), and
  the timer's three states (no timer before; time left within ±1.5 s during; none after the
  butterfly is back), including the "≈" estimate when the ring is hidden; the gauge row's two
  circles.
- **Si's second cast** ([`Fixtures/si-timer-second-cast`](tests/data/Fixtures/si-timer-second-cast)):
  her button through a second cast while the skill lasts, Si in slot 3 and in slot 1: the
  crossed-out butterfly read as `SecondCast` with its ring, and the timer shown from the swirl
  until the butterfly is back (the ring draining on, never refilled; held at ≈0 past the estimate
  while the icon is still seen; a countdown look not ending it).
- **Lasting ultimates** ([`Fixtures/ultimate-time`](tests/data/Fixtures/ultimate-time),
  [`Fixtures/lasting-ultimate-background`](tests/data/Fixtures/lasting-ultimate-background)):
  the time bar's share and its absence over scenery, at 4K and on simulated 16:9-class screens;
  whose it is, on frames from two recordings where it isn't the controlled operator's (Zhuang
  Fangyi's under Perlica's HP; Zhuang's, Laevatain's and Yvonne's teal one with the player
  switching between the three): each bar given to its colour's operator, a bar whose operator
  isn't in the squad to no one, and the recordings' readings replayed across the switches. "Ultimate active" in
  rotations and alerts holds for Zhuang's while Perlica is controlled.
- **Counters on the portraits** ([`Fixtures/portrait-counters`](tests/data/Fixtures/portrait-counters)):
  51 frames from the owner's four recordings, each switched-out operator's counter read right or
  unknown (never wrong): Si's stacks in slots 1 and 3, Laevatain's 熔火, Typhoeus's 猎矢 and 启示,
  Zhuang Fangyi's 青霆剑; Si with no badge on her clear portrait read 0 (nine frames, red and white
  scenery behind some), and unknown on a tinted, dimmed or faded portrait, under a flash, with her
  number blacked out, or with the skill points gone; nothing out of combat, on a portrait without a counter (but the flower,
  read only on Laevatain's own), on a panel remembered while out of sight, or in the controller
  layout; a switched-out operator's counter live in the gauge row. The Typhoeus recording is 2154
  rows high (its bottom 4 rows lost): its frames have them back, black, so the HUD is where the
  game draws it.
- **Alerts:** the store (create, read, correct in place, remove, an unreadable file renamed, a
  newer one refused, imports never replacing, the 100 cap, share codes), the firing rule
  (`AlertMonitorTests`), the pop-ups' life (`AlertFollowerTests`), the words and icons
  (`AlertTextTests`), the pop-up's layout, and the editor driven through its real buttons
  (`AlertEditorTests`: 技力 至少 1.8 and 寒冷附着 至少 3 层 built from the pickers).
- **Recommended rotations:** synced against a stand-in for GitHub (new, unchanged, corrected and
  removed files; a SHA or id that doesn't match; a 404, an unexpected answer or an unreachable
  GitHub changing nothing; the player's own rotations never touched; deleted ones staying deleted
  until restored); the editor saving a recommended rotation as the player's copy and hiding a
  deleted one; and every file in `rotations/` being valid.
- **Rotations:** the file format and share codes (round trip, damaged codes, newer formats,
  imports never overwriting, format 4 and 5 only when needed, an older format still loading,
  published rotations at format 3 or lower); `phasePlayed` (counted per round, reset at the end of
  a fight, ignored without the keys, a gate or an alert of nothing else never opening or firing:
  `PhasePlayedTests`, `RotationFollowerTests`), alerts at format 2 only when needed;
  the tracker (picking a step with no reset, moving only on seen use, steps used together, going
  off-script, looping, nothing on screen, stacks and levels in status conditions); the phase
  selector's counts and their reset at the end of a fight; the editor's working copy and the
  editor itself (basic- and heavy-attack steps, picking phases and steps with a left click, 0.1 skill points, 1 % charge, a value off the list kept, 复制轴段,
  轴段已显示次数, 轴段已完成次数 greyed out with its reason without the keys); the strip's layout; every UI string translated, including keys built at run time.

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
| `trace-video <video>` `[--every <s>] [--from <s>] [--to <s>] [--presses <file>] [--rotation <rotation.json> --squad <id1>,<id2>,<id3>,<id4>]` | One CSV row per sampled frame: skill points and, per slot, combo, ultimate and battle-skill button, for studying when abilities are used. `--presses`: a file of the battle skills cast in the recording, lines `seconds,slot` (`#` starts a comment; a bad line is refused with its line number); a `presses` column says how each press was judged against the row before (e.g. `2:Counted 2:Repeat`). `--rotation` with `--squad` (the operator ids by slot, so no naming is needed): `phase,step` columns, the phase the strip shows and the step it marks current (1-based; `-` out of battle or with no step marked). |
| `trace-inn <video>` `[--every <s>] [--from <s>] [--to <s>] [--end-margin <s>] [--reference <dir>]` | Text only, never an image: per sampled frame (0.5 s) the inn screen, the timer, each table card's state and item ids and the dispatch panel's dishes and queue; then every order once (counted only after its table was seen choosing, items by a vote over the frames that showed them), the counts per dish and drink, gourmand orders apart, the menu and the theme as the screens state them. For measuring what guests order. `--end-margin` leaves out a recording's last seconds. |
| `fetch-operators <dir>` | Download or update the roster, avatars and skill icons from the wiki into `<dir>/reference`, as the app does (`identify` and `learn` use the same layout). |


---

## Project layout

```
src/EndHud/              the tray app: overlay window (debug panel, combo-ready hint, rotation strip, gauges, alert pop-ups), rotation and alert editors, menu, tray icon, config, i18n
src/EndHud.Vision/       everything that reads the screen
  Capture/               Windows.Graphics.Capture of a single window
  Operators/             operator library, recognition (character model), learning, operator counters, status icons
  Rotations/             rotation format, store, recommended-rotation sync, condition walk, the phase selector and the tracker that finds the current step
  Alerts/                alert rules, their file and store, and when a rule fires (AlertMonitor)
  Inn/                   the inn: its data (InnData), gourmands, and the readers of its screens
  Assets/ccip/           the character recognition model (Git LFS; see tools/ccip-model)
  Assets/operator-portraits.json  in-game portrait references (the model's numbers; built from test fixtures)
  PartyReader.cs         squad panel: level, HP, combo, portraits
  HudLayout.cs, SquadGeometry.cs   keyboard-and-mouse or controller layout, and where the squad is in each
  UltimateGauge.cs       ultimate circles
  PartyTracker.cs        carries readings over (deaths, values that are briefly unreadable)
  ComboHints.cs          when to show the combo-ready hint
  SkillPointBar.cs, ActiveSlot.cs, BattleSkillButton.cs, BossStatusReader.cs   combat readings for rotations
  InflictionRing.cs, VulnerableRing.cs, ReactionLevel.cs   the boss's infliction (element, stacks), Vulnerable (stacks) and reaction levels
  SkillRing.cs, SkillTimer.cs, BadgeNumber.cs   Si's battle-skill button and timer, and badge digits (her stacks)
src/EndHud.Cli/          endhud-cli developer tool
tests/EndHud.Vision.Tests/ tests of everything that reads the screen (fixtures, recordings, screen sizes)
tests/EndHud.Tests/      tests of the tray app (banner layout at any display scaling, windowed capture)
.github/workflows/       CI: tests on every push (tests.yml); releases from version tags (release.yml); fast release by hand, no checks (fast-release.yml)
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
and the D-pad that replaces the squad's letters while LB is held. A PlayStation controller labels
the same badges "L1" (its LB is L1): the "L" alone is matched too (0.89 or more in every controller
fixture, 0.43 at most on keyboard-and-mouse ones, matched at 0.7), so that pad is read as well;
before 0.6.4 its squad was found only while L1 was held. Where each slot's parts are in
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
of them to confirm. Not read yet: levels (the squad isn't drawn out of combat there), battle
skill cooldowns, and so Si's battle-skill timer (`SkillRing.Read` returns nothing there), and the
counters on the switched-out operators' portraits (`PortraitCounters.Read` returns nothing there:
the controlled operator's counter is live, the others' show their last value, dimmed).

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
- Rotations and alerts: Laevatain's 熔火, Typhoeus's 猎矢 and 启示, Zhuang Fangyi's 青霆剑 and Si's
  伤害层数 are read as operator counters so far. In combat with keyboard and mouse they're read for
  the whole squad: beside the HP bar for the controlled operator, on the squad portraits for the
  others (see [Counters on the portraits](#counters-on-the-portraits)). Out of combat, with a
  controller, or under an effect, a switched-out operator's counter shows the last value seen,
  dimmed. Laevatain's flower has no outline to tell it from a portrait's art, so it's read only
  on her own portrait, where it has always been drawn in combat. Si's portrait with no badge is
  read as 0 only where her art shows clearly in the badge's place, measured on her portrait as
  drawn in the owner's two recordings at 4K: if the game draws her portrait differently, or on a
  smaller screen than measured, no badge may stay unknown (her stacks show the last value seen). A battle skill's cooldown is read
  as available or cooling, not its seconds.
- Lasting ultimates: the time bar's owner is told by its colour for Laevatain (red), Zhuang
  Fangyi (gold) and Yvonne (teal) only; a bar of another colour is shown for no one, and
  "ultimate active" for an operator not known to have one is unknown while a bar shows. Yvonne's
  is measured on one recording, and her bar only while she was controlled.
- Enemy counts: the infliction ring's stacks (1–4), Vulnerable's stacks (1–4, drawn in the same
  ring) and the levels of Corrosion and Electrification (I–IV) are read. The levels of Combustion,
  Solidification and any reaction not yet seen with a badge aren't, so the editor offers only
  present/absent for them, and a shared rotation or alert asking for level II+ of one treats that
  check as unknown. Vulnerable is measured on one physical-team recording, and beside an
  infliction on one more (test_rossi: heat 2 and Vulnerable 1, Vulnerable 3 with heat 1 or cryo 1).
  With both on the boss the two show side by side in the order applied and both are read (see
  [What's read for it](#whats-read-for-it)). For the moment the first icon fades out (about a quarter
  of a second) the second isn't read either. Vulnerable is consumed by skills (owner), which is when
  its shield goes. Infliction stacks 3–4
  have been seen only for cryo; the infliction fixtures have no electric ring (its colour range
  comes from the wiki's icon). A badgeless reaction
  over a dark or busy scene stays unknown rather than I. A few boss icons aren't on the wiki's
  glossary and aren't recognised.
- Si: her stacks reader is measured on 1–2 digit numbers (up to 45 seen); her battle-skill timer
  with her in slots 1 and 3 (a second cast included), on keyboard and mouse, and assumes 22 s
  whatever her skill level.

## Game art

EndHud carries the game's art in one place: the operator package
(`operators/operator-library.zip`: avatars, skill icons, status icons from the community wiki,
CC BY-SA 4.0), built in so a first start needs no network. A test checks the build carries exactly
that and the recognition model. The repository's test fixtures, recordings and README pictures are
frames of gameplay, kept for testing and documentation.

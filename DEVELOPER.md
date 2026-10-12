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
operator's avatar and combo skill icon (both from the built-in operator library), their name and
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

### Debug panel

Everything EndHud reads, for checking it (the "debug panel" checkbox in the menu, the tray, or
Ctrl+Shift+D). Off by default. It lists, per squad slot:

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
| Operator name | squad portraits, bottom-left | matched against built-in references (see [Recognising operators](#recognising-operators)) |
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

Or produce a single `EndHud.exe` with everything inside it, operator icons included:

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

[`.github/workflows/release.yml`](.github/workflows/release.yml) runs the tests, then attaches two
files to a new release on the repository's Releases page:

- `EndHud-1.2.0.msi`: the installer. It installs for the current user (no administrator prompt),
  adds a Start menu shortcut, and upgrades an older version in place. See
  [`installer/README.md`](installer/README.md).
- `EndHud-1.2.0-portable.exe`: the same program as one file, to run without installing.

Both include .NET, so nothing else needs installing. Tags must look like `v1.2.0`. "Run workflow"
on the Actions tab does a dry run: it builds both files and attaches them to the run without
creating a release. The files aren't code-signed yet, so Windows SmartScreen warns about an
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

Right-click the tray icon for: status, show HUD, open menu, auto-show on/off, debug panel on/off, **update the operator library** (from the wiki), open the config folder, and quit.

### Settings

`%APPDATA%\EndHud\config.json` is created on first start. The menu changes it for you; if you
edit it by hand, restart EndHud. Delete the file to reset.

| Key | Default | Meaning |
|---|---|---|
| `AutoShowOnGameFocus` | `true` | Show the HUD when the game has focus and hide it otherwise. When `false`, the HUD only shows after Ctrl+Shift+H. |
| `PartyOcrEnabled` | `true` | Read the squad panel (names, HP, skills, levels). |
| `OcrIntervalMs` | `1000` | How often the squad panel is read (minimum 250). |
| `Language` | `zh-CN` | UI language: picks `i18n/<Language>.xaml`. |
| `DebugPanel` | `false` | Show the debug panel. |
| `ComboHintEnabled` | `true` | Show the combo-ready hint (needs `PartyOcrEnabled`). |
| `ComboHintMode` | `WhenReady` | `WhenReady`: for `ComboHintSeconds` after the cooldown finishes. `WhileReady`: until the combo skill is used. |
| `ComboHintSeconds` | `3` | How long a `WhenReady` hint stays up. |
| `ComboHintX`, `ComboHintY` | `0.2`, `0.36` | Where the hint sits: its top-left corner as a fraction of the game window's width and height. |

### Command-line options

| Option | Purpose |
|---|---|
| `--target "<title text>"` | Track any window whose title contains this text instead of the game. Useful to try EndHud without the game, for example over a video of gameplay. |
| `--hide-from-capture` | Keep the HUD out of screenshots and recordings. |

EndHud finds the game as a Unity window whose title contains "Endfield" or the game's Chinese title.

---

## Recognising operators

The game shows no operator names in the squad panel, so EndHud identifies each portrait.

1. **References, downloaded.** Every operator's avatar, their battle skill, combo and ultimate
   icons and Chinese name come from [endfield.wiki.gg](https://endfield.wiki.gg/wiki/Operator).
   They're the game's art, so **EndHud ships none of it**: the first start downloads them (about
   1 MB) into your user data, for the MSI and the portable exe alike. A copy older than 7 days is
   refreshed in the background, which picks up new operators; the tray's **update the operator
   library** refreshes it now. Without internet on the first start, slots show "slot n" and the
   download is retried (after 1, 5, 15 and then every 30 minutes); the menu says so.
   The only built-in operator data is where an avatar sits inside a squad portrait
   (`src/EndHud.Vision/Assets/calibration.json`, a few numbers).
2. **First guess.** Portraits are compared with the avatars by face/hair layout and colour.
   A guess is shown only when it clearly wins; otherwise the slot shows **"slot n"**.
3. **Learning from you.** In the menu (Ctrl+Shift+M) each slot has a dropdown, sorted by most
   likely operator first. Picking a name saves that in-game portrait, and from then on that
   operator is recognised reliably, at any resolution. EndHud never learns from its own
   guesses, so a wrong guess can't make itself permanent.
4. **Correcting and clearing.** Picking a different name later removes the wrong learned
   portraits and saves the right one. "Forget all learned portraits" clears everything learned.

Your own data lives in `%APPDATA%\EndHud\operators\`:

| Path | Contents |
|---|---|
| `reference/` | the roster, avatars and skill icons downloaded from the wiki (`wiki-sha1.json` records the wiki's checksums, so unchanged files aren't downloaded again) |
| `learned/<operator>/*.png` | portraits learned from your picks |
| `calibration.json` | where avatars sit inside a squad portrait, if yours differs from the built-in measurement |

How a download stays safe ([`OperatorReference`](src/EndHud.Vision/Operators/OperatorReference.cs),
[`WikiOperatorSource`](src/EndHud.Vision/Operators/WikiOperatorSource.cs)):

- New operators need nothing: the roster is whatever the wiki's Operator page lists.
- Requests are retried when the wiki is busy (429, 5xx; `Retry-After` honoured) or the connection drops.
- An image that fails, or isn't a valid image, keeps the previous copy's file; the rest still updates.
- The new copy is downloaded beside the current one and swapped in with two renames; an
  interrupted swap is undone on the next start. If the wiki lists far fewer operators than the
  current copy (under 80%), the current copy is kept: operators don't leave the game.

Si and Argent Flow have no skill icons on the wiki yet; they're recognised by portrait only.

---

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
- **Recognition:** guessing, learning, correcting and clearing.
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
| `analyze <screenshot>` `[--debug-dir <dir>]` | Read the squad panel from a screenshot. `--debug-dir` saves the exact images given to OCR. |
| `analyze-video <video>` `[--every <s>]` | HP, combo cooldown and ultimate charge per slot over a recording. |
| `capture <window title text> <out.png>` | Grab one frame from a window (e.g. the game) through the HUD's capture path. |
| `identify <screenshot> --library <dir>` | Show the top operator candidates for each slot. |
| `learn <screenshot> --library <dir> --slots A,B,C,D` | Teach a library the squad in a screenshot (operator ids, slot order). |
| `fetch-operators <dir>` | Download or update the roster, avatars and skill icons from the wiki into `<dir>/reference`, as the app does (`identify` and `learn` use the same layout). |


---

## Project layout

```
src/EndHud/              the tray app: overlay window (debug panel, combo-ready hint), menu, tray icon, config, i18n
src/EndHud.Vision/       everything that reads the screen
  Capture/               Windows.Graphics.Capture of a single window
  Operators/             operator library, portrait matching, learning
  Assets/calibration.json  where avatars sit in a squad portrait (the only built-in operator data)
  PartyReader.cs         squad panel: level, HP, combo, portraits
  UltimateGauge.cs       ultimate circles
  PartyTracker.cs        carries readings over (deaths, values that are briefly unreadable)
  ComboHints.cs          when to show the combo-ready hint
src/EndHud.Cli/          endhud-cli developer tool
tests/EndHud.Vision.Tests/ tests of everything that reads the screen (fixtures, recordings, screen sizes)
tests/EndHud.Tests/      tests of the tray app (banner layout at any display scaling, windowed capture)
.github/workflows/       CI: tests on every push (tests.yml); releases from version tags (release.yml)
installer/               the MSI installer (WiX)
tools/EndHud.ReadmeMedia/ renders the README's animated pictures from a recording
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
- Hints follow the squad panel reading, once a second by default (`OcrIntervalMs`), so a hint can
  appear up to a second after the cooldown finishes.

## Game art

EndHud ships no game art: operator avatars and skill icons are downloaded from the community wiki
onto the user's machine, and the build contains none (a test checks). The repository's test
fixtures, recordings and README pictures are frames of gameplay, kept for testing and
documentation. Earlier commits did contain the wiki art; clean the history before making the
repository public.

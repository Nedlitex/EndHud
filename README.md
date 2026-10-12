# EndHud

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
Ping 164 ms
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
| Ping | "164ms", bottom-left (red when the connection is poor) | OCR, white or red text |

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
| `PartyOcrEnabled` | `true` | Read the squad panel (names, HP, skills, ping). |
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

1. **Built-in references.** Every operator's avatar, their battle skill, combo and ultimate icons and
   Chinese name (34 operators, from [endfield.wiki.gg](https://endfield.wiki.gg/wiki/Operator)),
   plus portraits learned from real gameplay, are **compiled into EndHud**. Nothing is
   downloaded or copied at startup, and recognition works offline.
2. **First guess.** Portraits are compared with the avatars by face/hair layout and colour.
   A guess is shown only when it clearly wins; otherwise the slot shows **"slot n"**.
3. **Learning from you.** In the menu (Ctrl+Shift+M) each slot has a dropdown, sorted by most
   likely operator first. Picking a name saves that in-game portrait, and from then on that
   operator is recognised reliably, at any resolution. EndHud never learns from its own
   guesses, so a wrong guess can't make itself permanent.
4. **Correcting and clearing.** Picking a different name later removes the wrong learned
   portraits and saves the right one. "Forget all learned portraits" clears everything learned.
   Built-in portraits can't be deleted from the exe, so they are switched off for you instead.

Your own data lives in `%APPDATA%\EndHud\operators\`:

| Path | Contents |
|---|---|
| `learned/<operator>/*.png` | portraits learned from your picks |
| `suppressed.json` | built-in portraits you've unlearned |
| `calibration.json` | where avatars sit inside a squad portrait, if yours differs from the built-in one |
| `reference/` | a newer roster from **update the operator library**; used instead of the built-in one while present |

Si and Argent Flow have no skill icons on the wiki yet; they're recognised by portrait only.

---

## Tests

```powershell
git lfs install   # once: the gameplay recordings are stored with Git LFS
dotnet test -c Release
```

All tests run from what's in the repository. They also run on every push to `main` and on pull
requests, on a GitHub-hosted Windows runner ([`.github/workflows/tests.yml`](.github/workflows/tests.yml)).

- **Fixture frames** ([`tests/EndHud.Vision.Tests/Fixtures`](tests/EndHud.Vision.Tests/Fixtures)):
  a 4K exploration screenshot and frames from gameplay recordings, UID blacked out. They cover
  level, HP, combo, ultimate and ping readings in exploration, combat, low HP, a dimmed squad, a heal,
  and with operators fallen.
- **Recognition:** guessing, learning, correcting and clearing.
- **Built-in library:** contents, and how user data layers on top of it.
- **Real capture:** a stand-in game window (shows a full-screen window for a few seconds).
- **Gameplay recordings** ([`tests/EndHud.Vision.Tests/Recordings`](tests/EndHud.Vision.Tests/Recordings),
  Git LFS, HUD areas only): whole fights replayed, checking that values change the way the game
  showed them. Examples:
  - combo cooldowns refill steadily;
  - the ultimate charge only grows;
  - an ultimate is never read in the wrong state on any frame;
  - out of combat, no combo bar is ever reported;
  - through a full boss battle, the panel is followed and operators fall one by one and stay fallen;
  - once read, the panel, ping and ultimates never go blank: unreadable moments keep the last value;
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
| `fetch-operators <dir>` | Download the roster, avatars and skill icons from the wiki. |

To refresh the built-in library after new operators are released:

```powershell
dotnet run --project src/EndHud.Cli -c Release -- fetch-operators src/EndHud.Vision/Assets/operators
```

That keeps the learned portraits already in that folder.

---

## Project layout

```
src/EndHud/              the tray app: overlay window (debug panel, combo-ready hint), menu, tray icon, config, i18n
src/EndHud.Vision/       everything that reads the screen
  Capture/               Windows.Graphics.Capture of a single window
  Operators/             operator library, portrait matching, learning
  Assets/operators/      built-in library (embedded into the assembly)
  PartyReader.cs         squad panel: level, HP, combo, ping, portraits
  UltimateGauge.cs       ultimate circles
  PartyTracker.cs        carries readings over (deaths, values that are briefly unreadable)
  ComboHints.cs          when to show the combo-ready hint
src/EndHud.Cli/          endhud-cli developer tool
tests/EndHud.Vision.Tests/ tests of everything that reads the screen (fixtures, recordings, screen sizes)
tests/EndHud.Tests/      tests of the tray app (banner layout at any display scaling, windowed capture)
.github/workflows/       CI: all tests on a Windows runner
```

Screen positions are measured on 3840×2160 and scaled to the game window's size, each element
keeping its distance from its corner of the window. What works at each size, tested on simulated
screens (see [Tests](#tests)); "read" is the share of levels and pings read on the test frames:

| Game window | HP, combo, ultimate, fallen | Level and ping |
|---|---|---|
| 4K, 5K, 1440p class (2560×1440, 3440×1440, 2560×1600, 5120×1440), 3000×2000 | all | 88–100% read |
| 2880×1800 | all | about 58% read |
| 1080p class (1920×1080, 1920×1200, 2560×1080), 2256×1504 | all | about 62% read (text is enlarged first) |
| 1600×900, 1366×768, 1280×720 | all (HP within about 8%) | not read: too small to read reliably |

A level or ping that isn't read keeps its last value; one that might be wrong is not shown. To
that end: a ping must have as many digits as the text has digit-sized glyphs; a red ping (poor
connection) and a level must be read the same by two attempts; levels are 10 to 90 (the game's
cap). Levels 1–9 are shown as unknown, because a "9" is usually "90" with a digit lost.

Display scaling (125–200% on most laptops) doesn't change what's read: the game draws in real
pixels and EndHud captures them. The combo-ready banner is sized from the game window's real
pixels, so it looks the same on a laptop at 150% as on a desktop at 100%.

## Adding a language

Copy `src/EndHud/i18n/zh-CN.xaml` to, for example, `en-US.xaml`, translate the values (keep `{0}`,
`{1}`), rebuild, and set `"Language": "en-US"` in `config.json`.

## Known limits

- The ultimate charge has only been observed up to about 46%; no recording yet shows one filling completely.
- When something can't be read (cutscenes, HUD fades, skill effects covering an indicator, the ping hidden
  under skill icons), the HUD keeps showing the last value it read. So a value can be a few seconds old,
  for example a combo skill used at the moment its bar was hidden still shows "ready" until the bar is read again.
  After the squad panel leaves the screen (menus, the map) the last squad reading also stays up.
- Everything is tuned on 4K recordings; other sizes are tested on frames scaled from them, not on
  real recordings at those sizes. 16:10 and 21:9 assume the HUD is laid out as described above.
- At 1600×900 and smaller, levels and ping aren't shown, and out of combat the squad panel can't
  always be recognised (its edge marks are under two pixels wide). Both only affect the debug panel.
- Levels 1–9 are shown as unknown (see above).
- The combo-ready hint needs two "cooling" readings in a row before it trusts that a combo skill was used,
  because the bar is briefly drawn as an empty track while the HUD fades in. A cooldown
  refreshed within about two seconds of being used therefore gets no hint.
- Hints follow the squad panel reading, once a second by default (`OcrIntervalMs`), so a hint can
  appear up to a second after the cooldown finishes.

## Game art

The built-in library and the test fixtures contain game art from the community wiki and frames
of gameplay. They're included for personal use. Keep the repository private, or remove them
before publishing it.

# EndHud

**An external HUD overlay for *Arknights: Endfield* (明日方舟：终末地).** EndHud runs as its own
Windows tray app and draws extra information over the game: each operator's name, HP,
连携 (combo skill) cooldown and 终结技 (ultimate) charge.

It gets everything by **looking at the screen**, the same way OBS's window capture does. It
never opens, reads, writes or injects into the game process, never hooks the game, and never
sends input to it.

> ⚠️ Using any third-party tool with an online game is at your own risk. The game uses the ACE
> anti-cheat, and Gryphline's terms may restrict third-party software. EndHud is designed to
> stay outside the game process, but that is not a guarantee of anything.

---

## What it shows

```
ENDHUD
跟踪中：明日方舟：终末地
小队
莱万汀  等级 90  生命 83%
    连携 就绪      终结技 充能 44%
卡缪    等级 90  生命 100%
    连携 冷却中 37%  终结技 就绪
…
延迟 164 毫秒
```

| Field | Where it's read from | How |
|---|---|---|
| Operator name | squad portraits, bottom-left | matched against built-in references (see [Recognising operators](#recognising-operators)) |
| 等级 (level) | "LV.90" under each portrait (outside combat) | Windows' built-in OCR |
| 生命 (HP) | HP bar under each portrait | how much of the bar is coloured |
| 连携 (combo skill) | thin bar above each HP bar (in combat) | grey refill → "冷却中 NN%", white → "就绪" |
| 终结技 (ultimate) | small circle above each 战技 button, bottom-right | coloured arc growing clockwise from 12 o'clock → "充能 NN%"; full colour + icon → "就绪" |
| 延迟 (ping) | "164ms", bottom-left | OCR |

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
| **Ctrl+Shift+Q** | Quit |
| **Esc** | Close the menu |

Right-click the tray icon for: status, show HUD, open menu, auto-show on/off, **更新干员头像库**
(update the operator library from the wiki), open the config folder, and quit.

### Settings

`%APPDATA%\EndHud\config.json` is created on first start. The menu changes it for you; if you
edit it by hand, restart EndHud. Delete the file to reset.

| Key | Default | Meaning |
|---|---|---|
| `AutoShowOnGameFocus` | `true` | Show the HUD when the game has focus and hide it otherwise. When `false`, the HUD only shows after Ctrl+Shift+H. |
| `PartyOcrEnabled` | `true` | Read the squad panel (names, HP, skills, ping). |
| `OcrIntervalMs` | `1000` | How often the squad panel is read (minimum 250). |
| `Language` | `zh-CN` | UI language: picks `i18n/<Language>.xaml`. |

### Command-line options

| Option | Purpose |
|---|---|
| `--target "<title text>"` | Track any window whose title contains this text instead of the game. Useful to try EndHud without the game, for example over a video of gameplay. |
| `--hide-from-capture` | Keep the HUD out of screenshots and recordings. |

EndHud finds the game as a Unity window whose title contains "Endfield" or "终末地".

---

## Recognising operators

The game shows no operator names in the squad panel, so EndHud identifies each portrait.

1. **Built-in references.** Every operator's avatar, their 战技 / 连携 / 终结技 icons and
   Chinese name (34 operators, from [endfield.wiki.gg](https://endfield.wiki.gg/wiki/Operator)),
   plus portraits learned from real gameplay, are **compiled into EndHud**. Nothing is
   downloaded or copied at startup, and recognition works offline.
2. **First guess.** Portraits are compared with the avatars by face/hair layout and colour.
   A guess is shown only when it clearly wins; otherwise the slot shows **"n 号位"**.
3. **Learning from you.** In the menu (Ctrl+Shift+M) each slot has a dropdown, sorted by most
   likely operator first. Picking a name saves that in-game portrait, and from then on that
   operator is recognised reliably, at any resolution. EndHud never learns from its own
   guesses, so a wrong guess can't make itself permanent.
4. **Correcting and clearing.** Picking a different name later removes the wrong learned
   portraits and saves the right one. "清除所有已学习的头像" clears everything learned.
   Built-in portraits can't be deleted from the exe, so they are switched off for you instead.

Your own data lives in `%APPDATA%\EndHud\operators\`:

| Path | Contents |
|---|---|
| `learned/<operator>/*.png` | portraits learned from your picks |
| `suppressed.json` | built-in portraits you've unlearned |
| `calibration.json` | where avatars sit inside a squad portrait, if yours differs from the built-in one |
| `reference/` | a newer roster from **更新干员头像库**; used instead of the built-in one while present |

Si and Argent Flow have no skill icons on the wiki yet; they're recognised by portrait only.

---

## Tests

```powershell
dotnet test -c Release
```

There are 53 tests. Most run on fixture images in [`tests/EndHud.Vision.Tests/Fixtures`](tests/EndHud.Vision.Tests/Fixtures):
a 4K exploration screenshot and frames from two combat recordings, with the player UID blacked out.
- **Readers:** level, HP, 连携 and 终结技 readings on known frames.
- **Recognition:** guessing, learning, correcting and clearing.
- **Built-in library:** contents, and how user data layers on top of it.
- **Real capture:** a stand-in game window (shows a full-screen window for a few seconds).

**Recording tests** replay whole gameplay videos and check that values change the way the game
showed them. Examples: the 连携 cooldown refills steadily, the ultimate charge only grows, and an
ultimate is never read in the wrong state on any frame. The videos are too large for the repo,
so these tests run when the files are found in your Downloads folder (or in
`ENDHUD_TEST_VIDEO_DIR`) and are skipped otherwise:

| File | What happens in it |
|---|---|
| `endhudtest.mp4` | 33 s of combat: slot 1 uses its ultimate, 连携 cooldowns, slot 1 takes a hit |
| `Recording 2026-10-11 172152.mp4` | 38 s: slot 1's ultimate charges from 1% to about 45%, slot 4's 连携 refills, two operators get hurt |

## Developer tool

`endhud-cli` (in `src/EndHud.Cli`) runs the same readers outside the app:

| Command | Purpose |
|---|---|
| `analyze <screenshot>` `[--debug-dir <dir>]` | Read the squad panel from a screenshot. `--debug-dir` saves the exact images given to OCR. |
| `analyze-video <video>` `[--every <s>]` | HP, 连携 and 终结技 per slot over a recording. |
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
src/EndHud/              the tray app: overlay window, menu, tray icon, config, i18n
src/EndHud.Vision/       everything that reads the screen
  Capture/               Windows.Graphics.Capture of a single window
  Operators/             operator library, portrait matching, learning
  Assets/operators/      built-in library (embedded into the assembly)
  PartyReader.cs         squad panel: level, HP, 连携, ping, portraits
  UltimateGauge.cs       终结技 circles
src/EndHud.Cli/          endhud-cli developer tool
tests/EndHud.Vision.Tests/
```

Screen positions are measured on 3840×2160 and scaled to the game window's size. All tuning so
far is from 4K footage. Other resolutions should work, but level OCR is less reliable at 1080p.

## Adding a language

Copy `src/EndHud/i18n/zh-CN.xaml` to, for example, `en-US.xaml`, translate the values (keep `{0}`,
`{1}`), rebuild, and set `"Language": "en-US"` in `config.json`.

## Known limits

- The ultimate charge has only been observed up to about 46%; no recording yet shows one filling completely.
- Readings are briefly unknown ("—") during cutscenes, HUD fades, and when skill effects cover an indicator.
- Everything is tuned on 4K recordings.

## Game art

The built-in library and the test fixtures contain game art from the community wiki and frames
of gameplay. They're included for personal use. Keep the repository private, or remove them
before publishing it.

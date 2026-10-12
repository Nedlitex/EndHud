# Gameplay recordings

Real *Arknights: Endfield* gameplay at 4K, used by `RecordedVideoTests` and meant to be reused
for future HUD features. They are stored with **Git LFS**: run `git lfs install` once, and clones
fetch them automatically. Without LFS you get small pointer files and these tests fail.

Everything except the game's HUD is blacked out (smaller files, and nothing of the world or the
player). What's kept, at 4K positions:

| Area | Region | Contains |
|---|---|---|
| Top centre | x 1100–2740, y 0–280 | Boss name, level, HP bar, status icons |
| Bottom left | x 0–1100, bottom 430 px | Squad panel: portraits, levels, HP, combo bars, death marks, ping (not read) |
| Bottom centre | x 1400–2440, bottom 340 px | Active operator's HP bar, skill-point segments, icons around them |
| Bottom right | right 940 px, bottom 470 px | Ultimate circles, battle skill buttons with cooldown numbers |

The player UID is masked and file metadata is stripped. Frames are 5 fps, encoded without
B-frames and with a keyframe every 1–3 s, so frame-accurate seeking matches the original timing.

| File | Length | What happens |
|---|---|---|
| `combat.mp4` | 33 s | Slot 1 uses its ultimate (~9 s cutscene), combo cooldowns refill, slot 1 takes a hit (~27 s) |
| `ultimate-charging.mp4` | 38 s | Slot 1's ultimate charges from 1% to ~44% (jump at ~31 s), slot 4's combo refills, two operators are hurt; the game dims the HUD around 20 s |
| `out-of-combat.mp4` | 13 s | Exploring: no combo bars (dark portraits where they'd be), colourful scenery behind the ultimate circles |
| `battle-fight1.mp4` | 110 s | First fight of a boss battle, from the end of the menus: low HP (pink frames), ultimate cutscenes, a white-haired operator at 0–5% HP |
| `battle-fight2.mp4` | 78 s | Second fight, from the loading screen: the squad dimmed with F1–F4 labels, heals, operators falling one by one, a full wipe, the screen after |

The two battle files are one recording split at the rewards/loading screen between the
fights, to keep each file small.

## Originals

The full, unmasked recordings are kept locally in `original-recordings/` at the repository root.
That folder is git-ignored because the originals show the player UID. Each clip here was cut from:

| Clip | Original | Cut |
|---|---|---|
| `combat.mp4` | `combat-ultimate-and-combos.mp4` | whole file |
| `ultimate-charging.mp4` | `ultimate-charging.mp4` | whole file |
| `out-of-combat.mp4` | `out-of-combat-exploring.mp4` | whole file |
| `battle-fight1.mp4` | `boss-battle-two-fights-and-wipe.mp4` | `-ss 6 -to 116` |
| `battle-fight2.mp4` | `boss-battle-two-fights-and-wipe.mp4` | `-ss 128` |

`loading-into-arena-and-first-combat.mp4` (33 s: loading screen, entering the arena, the first
seconds of combat) isn't used by any test yet.

Encoding, from an original recording (add `-ss`/`-to` to cut):

```
ffmpeg -i original.mp4 -an -map_metadata -1 -r 5 -c:v libx264 -preset slow -crf 20 -bf 0 -g 5 -pix_fmt yuv420p -filter_complex "[0:v]split=5[a][b][c][d][e];[a]drawbox=x=0:y=0:w=iw:h=ih:color=black:t=fill[bg];[b]crop=1640:280:1100:0[top];[c]crop=1100:430:0:in_h-430[bl];[d]crop=1040:340:1400:in_h-340[bc];[e]crop=940:470:in_w-940:in_h-470[br];[bg][top]overlay=1100:0[t1];[t1][bl]overlay=0:H-430[t2];[t2][bc]overlay=1400:H-340[t3];[t3][br]overlay=W-940:H-470,drawbox=x=205:y=ih-77:w=285:h=50:color=black:t=fill[out]" -map "[out]" out.mp4
```

# README media

Renders animated pictures of the combo-ready banner (`combo-hint.gif`,
`combo-hints-always.gif`) from an original gameplay recording, drawing the banner offline. The
README now shows real gameplay with EndHud running instead (`docs/images/*.webp`, cut from
`original-recordings/boss-battle-with-endhud-combo-hints.mp4`); this stays useful for checking the
banner's look without the game.

```powershell
dotnet run --project tools/EndHud.ReadmeMedia -c Release -- original-recordings/combat-ultimate-and-combos.mp4 out/readme-media
```

The GIFs are written to `out/readme-media`. Needs ffmpeg on PATH and the
original recording (kept locally in `original-recordings/`, not in the repository; see
`tests/data/Recordings/README.md`).

How it works, and why it shows exactly what EndHud would:

1. The recording is read once a second through EndHud's own pipeline: `PartyReader`,
   `PartyTracker`, `SquadIdentifier` (with the built-in operator library) and `ComboHints`. That
   decides when each banner slides in and out, and for whom.
2. Each GIF frame is the recording's frame, scaled to 2560x1440, with EndHud's real banners
   (`ComboBanner`) drawn over it at that moment, at the overlay's default position and scale.
   The banners are posed with `ComboBanner.Pose`, which uses the same timings and easing as the
   overlay's animations, and rendered off-screen: no window is opened.
3. The player UID is blacked out, and the frames are assembled into a GIF with ffmpeg.

The clips (time range, crop, mode) are listed at the top of `Program.cs`.

## The rotation strip over a recording

```powershell
dotnet run --project tools/EndHud.ReadmeMedia -c Release -- rotation <recording.mp4> out/rotation-demo <rotation.json>
```

Replays the recording through the same steps as the app (four readings a second: squad panel,
operator recognition, the controlled operator's counter, the boss's statuses and elemental
infliction once a second) and moves a `RotationTracker` on with each reading. Every frame then gets
the real strip (`RotationStripView`, `RotationStrip`) at its default spot, off-screen, with the UID
blacked out. Writes `rotation-demo.mp4`, `readings.tsv` (what was read at each reading and which
step the strip was on, with each check) and stills of the first ready and waiting moments. The
strip only shows for a squad the rotation is for. Use it to see the strip's style over real
gameplay, and to check a rotation against a recording before publishing it.

## The README's gameplay clips

Cut from `original-recordings/boss-battle-with-endhud-combo-hints.mp4` (4K, EndHud running in
"show while ready" mode): the whole screen at 1280x720, 15 fps, animated WebP (a tenth of the
size of a GIF at this quality), with the player UID under the squad panel blacked out.

```bash
cut() { ffmpeg -ss $2 -t $3 -i original-recordings/boss-battle-with-endhud-combo-hints.mp4 -an   -vf "drawbox=x=200:y=2075:w=300:h=70:color=black:t=fill,fps=15,scale=1280:-2:flags=lanczos"   -c:v libwebp_anim -lossless 0 -q:v 70 -compression_level 6 -loop 0 docs/images/$1.webp; }
cut combo-ready 19.9 3.4          # four combos ready, banners slide in
cut combo-used 26.6 3.8           # combos used: banners slide out, the rest move up
cut combo-becomes-ready 46.2 2.8  # a cooldown finishes: a new banner slides in
```

The controller clip (`controller-battle.webp`) comes from the owner's controller recording
`battle_controller_test_fullbattle2.mp4` (3838x2158, EndHud running), 79.5-85.5 s: the hint list,
the gauges filling, an ultimate becoming ready and LB held. The box over the UID is the text
only, since in the controller layout slot 4's HP bar sits just above it:

```bash
ffmpeg -ss 79.5 -t 6 -i battle_controller_test_fullbattle2.mp4 -an   -vf "drawbox=x=205:y=2100:w=285:h=30:color=black:t=fill,fps=15,scale=1280:-2:flags=lanczos"   -c:v libwebp_anim -lossless 0 -q:v 62 -compression_level 6 -loop 0 docs/images/controller-battle.webp
```

## The counter glyphs beside the game's counters

```powershell
dotnet run --project tools/EndHud.ReadmeMedia -- glyphs tests/data/Fixtures out/counter-glyphs.png
```

A review sheet of `CounterGlyphs`: for each counter, the game's counter cut from the fixture its glyph was measured
on, then the glyph, at the strip's badge size and at 4x the game's 4K pixels (the two line up), and Laevatain's
melting-flame at 0-4 stacks for reference. Off-screen; no window is opened.


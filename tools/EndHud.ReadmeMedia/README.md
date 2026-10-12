# README media

Renders the animated pictures in the README (`docs/images/combo-hint.gif`,
`docs/images/combo-hints-always.gif`) from an original gameplay recording.

```powershell
dotnet run --project tools/EndHud.ReadmeMedia -c Release -- original-recordings/combat-ultimate-and-combos.mp4 out/readme-media
```

Then copy the two GIFs from `out/readme-media` into `docs/images`. Needs ffmpeg on PATH and the
original recording (kept locally in `original-recordings/`, not in the repository; see
`tests/EndHud.Vision.Tests/Recordings/README.md`).

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

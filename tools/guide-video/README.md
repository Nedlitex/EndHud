# Guide videos

Rotation guides are mostly videos (Bilibili, YouTube). `fetch.py` downloads one into the
git-ignored cache so it can be studied, and later turned into a rotation file for `rotations/`.

```powershell
# once, from the repository root (see requirements.txt)
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt

.venv/Scripts/python tools/guide-video/fetch.py https://www.bilibili.com/video/BV1a4KU66Efc/
```

It writes `.cache/guide-videos/<site>-<id>/`:

| File | What it is |
|---|---|
| `video.mp4` | the video, up to 1080p, with sound |
| `source.json` | where it came from (url, title, uploader, upload date, duration), to credit it |
| `sheets/sheet-NN.png` | contact sheets: a frame every 10 s (`--every`), each stamped with its time |

A video file on this computer works too (it's copied in). Fetching the same video again reuses
the download and only redraws the contact sheets.

Bilibili gives 1080p and subtitles only to a logged-in browser; without that you get 720p or less
and no subtitles. `--cookies-from-browser edge` (or `chrome`, `firefox`) uses your own login. The
cookies are read on this computer and not stored.

Before publishing a rotation taken from a guide: check which four operators the guide actually
plays (in the video, not from the title), and credit the guide in the commit message. The
video stays in the cache; it's the author's work and isn't committed.

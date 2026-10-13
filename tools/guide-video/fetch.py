"""Fetch a rotation guide video to study or ingest.

    .venv/Scripts/python tools/guide-video/fetch.py <url or video file> [--every 10] [--cookies-from-browser edge]

Downloads the video (Bilibili, YouTube and anything else yt-dlp supports) into
.cache/guide-videos/<id>/ (git-ignored: it's someone else's work), with:

    video.mp4      the video (up to 1080p, with sound for the narration)
    source.json    where it came from: url, title, uploader, upload date, duration
    sheets/        contact sheets, one frame every --every seconds, 6x6 per sheet,
                   to find the moments that show the squad and the rotation

A local file is copied in instead of downloaded. Fetching the same video again reuses the
download. Bilibili serves 1080p and its subtitles only to a logged-in browser: pass
--cookies-from-browser <edge|chrome|firefox> to use yours (the cookies stay on this computer).
Needs ffmpeg on PATH.
"""

import argparse
import datetime
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys

import yt_dlp

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / ".cache" / "guide-videos"
FONTS = [pathlib.Path("C:/Windows/Fonts/arial.ttf"), pathlib.Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")]


def fetch_url(url: str, cookies_from_browser: str | None) -> pathlib.Path:
    probe_options = {"quiet": True, "skip_download": True}
    if cookies_from_browser:
        probe_options["cookiesfrombrowser"] = (cookies_from_browser,)
    with yt_dlp.YoutubeDL(probe_options) as ydl:
        info = ydl.extract_info(url, download=False)
    folder = CACHE / safe(f"{info['extractor_key']}-{info['id']}".lower())
    video = folder / "video.mp4"
    if video.exists():
        print(f"already fetched: {folder}")
        return folder

    folder.mkdir(parents=True, exist_ok=True)
    options = {
        **probe_options,
        "skip_download": False,
        "outtmpl": str(folder / "video.%(ext)s"),
        # H.264 decodes everywhere (ffmpeg, Windows' own decoder used by EndHud's VideoFile).
        "format": "bv*[height<=1080][vcodec^=avc1]+ba/bv*[height<=1080]+ba/b[height<=1080]/b",
        "merge_output_format": "mp4",
        "writesubtitles": True,
        "subtitleslangs": ["all", "-danmaku"],
    }
    with yt_dlp.YoutubeDL(options) as ydl:
        ydl.download([url])
    write_source(folder, {
        "url": info.get("webpage_url", url),
        "title": info.get("title"),
        "uploader": info.get("uploader"),
        "uploadDate": info.get("upload_date"),
        "duration": info.get("duration"),
        "id": info.get("id"),
        "site": info.get("extractor_key"),
    })
    return folder


def fetch_file(path: pathlib.Path) -> pathlib.Path:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()[:16]
    folder = CACHE / f"file-{digest}"
    video = folder / "video.mp4"
    if video.exists():
        print(f"already fetched: {folder}")
        return folder
    folder.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(path, video)
    write_source(folder, {"file": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    return folder


def write_source(folder: pathlib.Path, source: dict) -> None:
    source["fetchedAt"] = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")
    (folder / "source.json").write_text(json.dumps(source, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def contact_sheets(folder: pathlib.Path, every: float) -> None:
    sheets = folder / "sheets"
    if sheets.exists():
        shutil.rmtree(sheets)
    sheets.mkdir()
    # Each tile is stamped with its time, so a moment can be found again in the video. ffmpeg
    # needs a font file named outright on Windows (it has no font configuration there).
    font = next((f for f in FONTS if f.exists()), None)
    stamp = ""
    if font is not None:
        fontfile = font.as_posix().replace(":", "\\:")
        stamp = f"drawtext=fontfile='{fontfile}':text='%{{pts\\:hms}}':x=8:y=8:fontsize=28:fontcolor=yellow:box=1:boxcolor=black@0.6,"
    subprocess.run([
        "ffmpeg", "-v", "error", "-y", "-i", str(folder / "video.mp4"),
        "-vf", f"fps=1/{every},{stamp}scale=480:-2,tile=6x6",
        str(sheets / "sheet-%02d.png"),
    ], check=True)
    count = len(list(sheets.glob("*.png")))
    print(f"{count} contact sheet(s), a frame every {every:g} s: {sheets}")


def safe(name: str) -> str:
    return "".join(c if c.isalnum() or c in "-_" else "-" for c in name)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="a video URL, or a video file on this computer")
    parser.add_argument("--every", type=float, default=10, help="seconds between contact sheet frames (default 10)")
    parser.add_argument("--cookies-from-browser", help="use this browser's login (Bilibili 1080p and subtitles)")
    args = parser.parse_args()
    if shutil.which("ffmpeg") is None:
        print("ffmpeg isn't on PATH", file=sys.stderr)
        return 1

    local = pathlib.Path(args.source)
    folder = fetch_file(local) if local.is_file() else fetch_url(args.source, args.cookies_from_browser)
    contact_sheets(folder, args.every)
    print(f"video: {folder / 'video.mp4'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

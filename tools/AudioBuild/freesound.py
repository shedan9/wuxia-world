"""从 Freesound 检索与下载 CC0 实录音效（不需要账号或 API 密钥）。

只取 Creative Commons 0 授权的声音：检索页带 CC0 过滤，下载前再到声音详情页核对一次授权，
不是 CC0 的一律拒收。下载的是官方高质量试听版（OGG，约 192 kbps），原始文件需登录，
发行前可在 M3-06 换原档（同一 ID，处理流程不变）。

  检索：python tools/AudioBuild/freesound.py search "sword clash" [--n 15] [--max-dur 5]
  下载：python tools/AudioBuild/freesound.py fetch 12345 67890 ...

下载到 audio_source/sfx/freesound/<id>.ogg，并把作者、标题、链接、授权、标签写进同目录 sources.json，
供 sfx_build.py 与资产台账引用。
"""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "audio_source" / "sfx" / "freesound"
SOURCES = CACHE / "sources.json"
UA = {"User-Agent": "Mozilla/5.0 (wuxia-world sfx fetch)"}
CC0 = "Creative Commons 0"


def get(url: str, retries: int = 3) -> bytes:
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=40) as r:
                return r.read()
        except Exception:  # noqa: BLE001 — 网络抖动重试
            if attempt == retries - 1:
                raise
            time.sleep(2 * (attempt + 1))
    raise RuntimeError("unreachable")


def search(query: str, n: int, max_dur: float, min_dur: float, page: int = 1) -> list[dict]:
    q = urllib.parse.urlencode({"q": query, "f": f'license:"{CC0}"', "s": "Num downloads (most first)", "page": page})
    text = get(f"https://freesound.org/search/?{q}").decode("utf-8", "replace")
    out = []
    for block in text.split('class="bw-player"')[1:]:
        attr = dict(re.findall(r'data-([a-z-]+)="([^"]*)"', block.split(">", 1)[0]))
        rating = re.search(r'aria-label="Average rating of ([0-9.]+)"', block)
        dur = float(attr.get("duration", 0))
        if not (min_dur <= dur <= max_dur):
            continue
        out.append({
            "id": int(attr["sound-id"]),
            "user": attr.get("username", ""),
            "title": html.unescape(attr.get("title", "")),
            "seconds": round(dur, 2),
            "sr": int(float(attr.get("samplerate", 0) or 0)),
            "downloads": int(attr.get("num-downloads", 0) or 0),
            "rating": float(rating.group(1)) if rating else 0.0,
            "ogg": attr.get("ogg", "").replace("-lq.", "-hq."),
        })
        if len(out) >= n:
            break
    return out


def details(sound_id: int, user: str) -> dict:
    """声音详情页：授权、标签、简介（用于核对 CC0 与挑选）。"""
    url = f"https://freesound.org/people/{urllib.parse.quote(user)}/sounds/{sound_id}/"
    text = get(url).decode("utf-8", "replace")
    lic = re.search(r'href="(?:https?:)?//creativecommons\.org/([^"]+)"', text)
    tags = re.findall(r'href="/browse/tags/([^/"?]+)/?"', text)
    desc = re.search(r'<div id="soundDescriptionSection"[^>]*>(.*?)</div>', text, re.S)
    if desc is None:
        desc = re.search(r'<meta name="description" content="([^"]*)"', text)
    plain = re.sub(r"<[^>]+>", " ", html.unescape(desc.group(1))) if desc else ""
    return {
        "url": url,
        "license_url": "https://creativecommons.org/" + lic.group(1) if lic else "",
        "tags": sorted(set(urllib.parse.unquote(t) for t in tags))[:20],
        "description": re.sub(r"\s+", " ", plain).strip()[:400],
    }


def load_sources() -> dict:
    if SOURCES.exists():
        return json.loads(SOURCES.read_text(encoding="utf-8"))
    return {}


def fetch(ids: list[int]) -> None:
    CACHE.mkdir(parents=True, exist_ok=True)
    sources = load_sources()
    for sid in ids:
        if str(sid) in sources and (CACHE / f"{sid}.ogg").exists():
            print(f"{sid}: 已有")
            continue
        # 用 ID 检索拿到作者与试听地址（详情页需要作者名）。
        page = get(f"https://freesound.org/s/{sid}/").decode("utf-8", "replace")
        m = re.search(r'data-sound-id="%d"[^>]*?data-username="([^"]+)"' % sid, page, re.S)
        user = m.group(1) if m else re.search(r"/people/([^/]+)/sounds/%d/" % sid, page).group(1)
        attr = dict(re.findall(r'data-([a-z-]+)="([^"]*)"', page.split(f'data-sound-id="{sid}"', 1)[1].split(">", 1)[0]))
        info = details(sid, user)
        if "publicdomain/zero" not in info["license_url"]:
            print(f"{sid}: 授权不是 CC0（{info['license_url'] or '未知'}），跳过", file=sys.stderr)
            continue
        ogg = attr["ogg"].replace("-lq.", "-hq.")
        (CACHE / f"{sid}.ogg").write_bytes(get(ogg))
        sources[str(sid)] = {
            "title": html.unescape(attr.get("title", "")),
            "user": user,
            "seconds": round(float(attr.get("duration", 0)), 2),
            "license": "CC0 1.0",
            **info,
            "preview": ogg,
        }
        print(f"{sid}: {sources[str(sid)]['title']} — {user}")
        SOURCES.write_text(json.dumps(sources, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        time.sleep(0.5)


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("search")
    s.add_argument("query")
    s.add_argument("--n", type=int, default=15)
    s.add_argument("--max-dur", type=float, default=30)
    s.add_argument("--min-dur", type=float, default=0)
    s.add_argument("--page", type=int, default=1)
    f = sub.add_parser("fetch")
    f.add_argument("ids", type=int, nargs="+")
    args = ap.parse_args()
    if args.cmd == "search":
        for r in search(args.query, args.n, args.max_dur, args.min_dur, args.page):
            print(f"{r['id']:>7}  {r['seconds']:>6.2f}s  ⬇{r['downloads']:>6}  ★{r['rating']:.1f}  {r['sr']:>5}  {r['user'][:16]:<16}  {r['title'][:70]}")
    else:
        fetch(args.ids)


if __name__ == "__main__":
    main()

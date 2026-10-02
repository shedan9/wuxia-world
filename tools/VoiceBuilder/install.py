"""把一组已生成的配音装进游戏工程（开发计划 M2-09）。

读 build/voice/samples/<档案>/manifest.json 与当前章节对白，只安装“生成时的文字哈希与当前台词一致”的句子，
复制到 game/assets/audio/voice/<line_id>.mp3，并写出游戏读取的清单 voice_manifest.json。
文字已改动（音频过期）的句子不安装、列出来等增量重生成；不再需要的旧文件一并删除。
不调用任何接口、不计费。

用法：python tools/VoiceBuilder/install.py [--samples build/voice/samples/minimax_cast] [--status trial]
"""

import argparse
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "game" / "assets" / "audio" / "voice"


def voiced_lines(chapter: Path) -> dict:
    """需要配音的台词（心里话不配音），按 line_id。"""
    data = json.loads(chapter.read_text(encoding="utf-8"))
    return {
        n["line_id"]: n
        for d in data["dialogues"]
        for n in d["nodes"]
        if n["type"] == "line" and not n.get("inner")
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--samples", default="build/voice/samples/minimax_cast")
    ap.add_argument("--chapter", action="append", help="章节对白 JSON；可多次给出，缺省为第一章")
    ap.add_argument("--status", default="trial", help="trial（试听版，未锁稿）或 final（锁稿后经审核的正式配音）")
    args = ap.parse_args()

    samples = ROOT / args.samples
    manifest = json.loads((samples / "manifest.json").read_text(encoding="utf-8"))
    chapters = [ROOT / c for c in (args.chapter or ["content/dialogue/arc01/chapter01.json"])]
    lines = {}
    for c in chapters:
        lines.update(voiced_lines(c))

    generated = {e["line_id"]: e for e in manifest["lines"] if not e.get("variant")}
    DEST.mkdir(parents=True, exist_ok=True)
    installed, stale, missing = {}, [], []
    for line_id, node in lines.items():
        entry = generated.get(line_id)
        if entry is None or not (samples / entry["file"]).exists():
            missing.append(line_id)
            continue

        sha = hashlib.sha256(node["text"].encode("utf-8")).hexdigest()
        if entry["text_sha256"] != sha:
            stale.append(line_id)
            continue

        target = DEST / f"{line_id}.mp3"
        if not target.exists() or target.read_bytes() != (samples / entry["file"]).read_bytes():
            shutil.copyfile(samples / entry["file"], target)
        installed[line_id] = {
            "file": target.name,
            "speaker": node["speaker"],
            "text_sha256": sha,
            "voice": entry["voice_setting"]["voice_id"],
            "review": entry.get("review", "pending"),
        }

    # 不再安装的旧音频（台词删除、改词或改为心里话）连同导入文件一起删除。
    removed = 0
    for f in DEST.glob("*.mp3"):
        if f.stem not in installed:
            f.unlink()
            Path(str(f) + ".import").unlink(missing_ok=True)
            removed += 1

    out = {
        "status": args.status,
        "source": manifest.get("profile", ""),
        "note": "试听版：台词未锁稿，音色为工作选角；锁稿后按改动增量重生成并逐句审核" if args.status == "trial" else "",
        "lines": dict(sorted(installed.items())),
    }
    (DEST / "voice_manifest.json").write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"需配音 {len(lines)} 句：已安装 {len(installed)}，文字已改需重生成 {len(stale)}，缺音频 {len(missing)}，删除旧文件 {removed}")
    for line_id in stale:
        print(f"  过期 {line_id}")
    for line_id in missing:
        print(f"  缺音 {line_id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""对话立绘统一比例：按 jobs/portrait_norm.json 里人工量的头顶、眼线、下巴与两眼中点，
把每张立绘缩放到同一头身比例、脸横向对齐，眼线高低按人物身高排（身高差按 px_per_cm 压缩），
输出到 game/assets/portraits/<人物>_v1.png（832×1216 RGBA），并写同名 .json 记录。

输入为入包前的原图 art_source/ai/characters/<人物>_portrait_v1_ingame.png，重跑结果相同。

用法：python portrait_norm.py [--out 目录]（缺省直接写入游戏包）[--only 人物]
"""

import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
SRC = REPO / "art_source/ai/characters"
DST = REPO / "game/assets/portraits"

# 对话页立绘框 1315 高、画布 1216 高（约 1.08 倍），框顶在 80：画布 y 超过约 925 已在 1080 画面以下。
VISIBLE_BOTTOM = 925


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=DST)
    parser.add_argument("--only")
    args = parser.parse_args()
    spec = json.loads((ROOT / "jobs/portrait_norm.json").read_text(encoding="utf-8"))
    w, h = spec["canvas"]
    args.out.mkdir(parents=True, exist_ok=True)
    for name, p in spec["people"].items():
        if args.only and name != args.only:
            continue
        src_path = SRC / f"{name}_portrait_v1_ingame.png"
        src = Image.open(src_path).convert("RGBA")
        head = math.sqrt((p["chin"] - p["crown"]) * (p["chin"] - p["eyes"]) * 2.2)
        scale = spec["head"][p["build"]] / head
        eyes = spec["ref_eyes"] - (p["stature"] - spec["ref_stature"]) * spec["px_per_cm"]
        ox = spec["face_x"] - p["face_x"] * scale
        oy = eyes - p["eyes"] * scale
        crown = p["crown"] * scale + oy
        resized = src.resize((round(src.width * scale), round(src.height * scale)), Image.LANCZOS)
        out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        out.paste(resized, (round(ox), round(oy)))  # 透明画布上直接覆盖（含透明通道），偏移可为负

        # 自查：顶上被画布切掉的不透明像素（发髻、冲天髻出画）、下缘在画面内露出的硬边。
        alpha = src.getchannel("A")
        top = alpha.getbbox()[1] * scale + oy
        bottom = alpha.getbbox()[3] * scale + oy
        warn = []
        if top < 0:
            warn.append(f"顶部出画 {-top:.0f} 像素")
        if bottom < VISIBLE_BOTTOM:
            warn.append(f"下缘 {bottom:.0f} 露在画面内")
        dst = args.out / f"{name}_v1.png"
        out.save(dst, optimize=True)
        record = {
            "tool": "tools/ArtGen/portrait_norm.py",
            "source": src_path.relative_to(REPO).as_posix(),
            "source_sha256": sha256(src_path),
            "scale": round(scale, 4),
            "offset": [round(ox, 1), round(oy, 1)],
            "crown": round(crown, 1),
            "eyes": round(p["eyes"] * scale + oy, 1),
            "chin": round(p["chin"] * scale + oy, 1),
            "output_sha256": sha256(dst),
        }
        if args.out == DST:
            (SRC / f"{name}_portrait_v1_norm.json").write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{name:14} 缩放 {scale:.3f}  头顶 {crown:.0f}  眼线 {record['eyes']:.0f}  下巴 {record['chin']:.0f}  {'；'.join(warn)}")


if __name__ == "__main__":
    main()

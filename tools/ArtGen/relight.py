"""给纯色底立绘换底色并补一层柔光，作为图生图的底图（光影迁移的预处理）。

用法：
    .venv/Scripts/python relight.py <源图> <输出图> [--bg 左上RGB 右RGB] [--glow x y 半径 强度]

1. 以左上角像素为底色，按颜色距离抠出背景（羽化边缘），换成从左到右的线性渐变；
2. 在 (x, y) 处叠一层暖白径向柔光（滤色混合），人物边缘随之受光；
3. 输出旁写同名 .json：源图哈希、全部参数与输出哈希。
之后用 generate.py 的 init_image 以较低 strength 重绘，让人物明暗与新光照融为一体。
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("output")
    parser.add_argument("--bg", nargs=6, type=int, default=[150, 192, 198, 163, 201, 204], help="渐变左侧与右侧 RGB")
    parser.add_argument("--glow", nargs=4, type=float, default=[0.95, 0.42, 0.55, 0.55], help="光心 x y（占宽高比例）、半径（占宽比例）、强度")
    parser.add_argument("--glow-rgb", nargs=3, type=int, default=[255, 244, 226])
    parser.add_argument("--key", type=float, default=38.0, help="与底色的颜色距离小于此值视为背景")
    args = parser.parse_args()

    src = Image.open(args.source).convert("RGB")
    a = np.asarray(src, np.float32)
    h, w = a.shape[:2]

    key = a[4, 4]
    dist = np.sqrt(((a - key) ** 2).sum(-1))
    bg = (dist < args.key).astype(np.uint8) * 255
    alpha = np.asarray(Image.fromarray(bg).filter(ImageFilter.GaussianBlur(1.5)), np.float32)[..., None] / 255

    left, right = np.array(args.bg[:3], np.float32), np.array(args.bg[3:], np.float32)
    t = np.linspace(0, 1, w, dtype=np.float32)[None, :, None]
    grad = np.broadcast_to(left + (right - left) * t, a.shape)
    out = a * (1 - alpha) + grad * alpha

    gx, gy, gr, gs = args.glow
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt((xx - gx * w) ** 2 + (yy - gy * h) ** 2) / (gr * w)
    g = (np.clip(1 - d, 0, 1) ** 2 * gs)[..., None]
    light = np.array(args.glow_rgb, np.float32)
    screened = 255 - (255 - out) * (255 - light) / 255
    out = out * (1 - g) + screened * g

    dst = Path(args.output)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(dst)
    meta = {
        "source": args.source,
        "source_sha256": hashlib.sha256(Path(args.source).read_bytes()).hexdigest(),
        "bg": args.bg,
        "glow": args.glow,
        "glow_rgb": args.glow_rgb,
        "key": args.key,
        "key_color": key.tolist(),
        "background_ratio": round(float((bg > 0).mean()), 3),
        "sha256": hashlib.sha256(dst.read_bytes()).hexdigest(),
    }
    dst.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{dst.name}  背景占比 {meta['background_ratio']}")


if __name__ == "__main__":
    main()

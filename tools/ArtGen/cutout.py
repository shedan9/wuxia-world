"""纯色底立绘抠图：从图像边缘按颜色距离做连通填充，只去掉与边框相连的底色，
人物身上与底色相近的饰物（如青色发带）不会被误删。输出 RGBA PNG 与同名 .json 记录。

用法：python cutout.py <源图> <输出.png> [--threshold 30] [--feather 1.5]
"""

import argparse
import hashlib
import json
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def cutout(src: Path, dst: Path, threshold: float, feather: float, enclosed: float = 0, soft: tuple[float, float] | None = None) -> None:
    rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    key = np.median(border, axis=0)
    dist = np.sqrt(((rgb - key) ** 2).sum(axis=2))

    # 与边框连通且接近底色的像素才算背景。
    background = np.zeros((h, w), dtype=bool)
    queue = deque()
    for x in range(w):
        queue.extend([(0, x), (h - 1, x)])
    for y in range(h):
        queue.extend([(y, 0), (y, w - 1)])
    while queue:
        y, x = queue.popleft()
        if background[y, x] or dist[y, x] > threshold:
            continue
        background[y, x] = True
        if y > 0:
            queue.append((y - 1, x))
        if y < h - 1:
            queue.append((y + 1, x))
        if x > 0:
            queue.append((y, x - 1))
        if x < w - 1:
            queue.append((y, x + 1))

    # enclosed > 0：被前景围住、未与边框连通的底色小块（树冠枝条间的空隙）也去掉，只取离底色更近的像素。
    if enclosed > 0:
        background |= dist <= enclosed

    alpha = Image.fromarray(np.where(background, 0, 255).astype(np.uint8))
    alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(feather))
    a = np.asarray(alpha).astype(np.float32) / 255.0

    # soft = (t0, t1)：按与底色的色差给半透明度（t0 以下全透、t1 以上不透、中间平滑过渡），
    # 用于模型把底色混进前景的地方（柳树后排枝条的雾状灰层）：灰层变成半透明枝条，去溢色后透出后面的地面。
    if soft is not None:
        t0, t1 = soft
        x = np.clip((dist - t0) / (t1 - t0), 0, 1)
        a = np.minimum(a, x * x * (3 - 2 * x))

    # 半透明边缘去溢色：把底色成分按透明度扣回，避免青色描边。
    edge = (a > 0) & (a < 1)
    out = rgb.copy()
    safe = np.clip(a, 1e-3, 1)[..., None]
    out[edge] = np.clip((rgb[edge] - key * (1 - safe[edge])) / safe[edge], 0, 255)

    rgba = np.dstack([out, a * 255]).astype(np.uint8)
    dst.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(dst, optimize=True)
    record = {
        "tool": "tools/ArtGen/cutout.py",
        "source": src.as_posix(),
        "source_sha256": sha256(src),
        "key_rgb": [round(float(c), 1) for c in key],
        "threshold": threshold,
        "feather": feather,
        "enclosed": enclosed,
        "soft": list(soft) if soft else None,
        "output_sha256": sha256(dst),
    }
    dst.with_suffix(".json").write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{dst}  背景 {background.mean():.1%}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("src", type=Path)
    parser.add_argument("dst", type=Path)
    parser.add_argument("--threshold", type=float, default=30)
    parser.add_argument("--feather", type=float, default=1.2)
    parser.add_argument("--enclosed", type=float, default=0, help="另去掉离底色不超过此距离的封闭小块（0 为不去）")
    parser.add_argument("--soft", help="t0,t1：按与底色的色差给半透明度（软抠图），去掉混入前景的雾状底色")
    args = parser.parse_args()
    soft = tuple(float(v) for v in args.soft.split(",")) if args.soft else None
    cutout(args.src, args.dst, args.threshold, args.feather, args.enclosed, soft)

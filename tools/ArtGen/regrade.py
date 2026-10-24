"""按引导图分区调色：保留 AI 件的明暗与材质细节，把每个色块区的平均色拉回占位布局的配色。

文生图 + 边线约束画出的材质好，但配色常被提示词带偏（客栈内墙整面被“朱漆”染红、粉壁画成木板）；
图生图保住配色又只会照描平涂占位。引导图本身是平涂色块，每个连通的同色块就是一个“区”
（粉壁、立柱、裙板、门帘、每只瓷瓶……）。在 Lab 空间里算出 AI 图在该区的平均色与引导色之差，
按 light / chroma 比例平移该区像素；平移量经高斯模糊后再加，区与区之间不留硬边。
面积小于 min_area 的区（描边、抗锯齿边缘）不调，由周围的模糊平移量带过。

用法（通常经 place.py --regrade 调用）：
    .venv/Scripts/python regrade.py <AI 图> <引导图> <输出> [--light 0.7] [--chroma 1.0]
"""

from __future__ import annotations

import argparse
import sys

import numpy as np
from PIL import Image


def regrade(image: Image.Image, guide: Image.Image, light: float = 0.7, chroma: float = 1.0,
            min_area: int = 300, blur: float = 3.0) -> Image.Image:
    import cv2

    alpha = image.getchannel("A") if image.mode == "RGBA" else None
    g = np.asarray(guide.convert("RGBA").resize(image.size, Image.NEAREST))
    rgb = np.asarray(image.convert("RGB"), dtype=np.float32) / 255
    lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB)
    glab = cv2.cvtColor(g[..., :3].astype(np.float32) / 255, cv2.COLOR_RGB2LAB)

    # 引导色量化后按颜色 + 连通性分区；透明边距不参与。
    q = (g[..., :3] // 6).astype(np.int32)
    key = q[..., 0] * 1_000_000 + q[..., 1] * 1000 + q[..., 2]
    key[g[..., 3] < 250] = -1

    shift = np.zeros_like(lab)
    weight = np.zeros(lab.shape[:2], dtype=np.float32)
    scale = np.array([light, chroma, chroma], dtype=np.float32)
    for k in np.unique(key):
        if k < 0:
            continue
        n, labels, stats, _ = cv2.connectedComponentsWithStats((key == k).astype(np.uint8), connectivity=4)
        for i in range(1, n):
            if stats[i, cv2.CC_STAT_AREA] < min_area:
                continue
            x, y, w, h = stats[i, :4]
            sub = labels[y:y + h, x:x + w] == i
            d = (glab[y:y + h, x:x + w][sub].mean(0) - lab[y:y + h, x:x + w][sub].mean(0)) * scale
            shift[y:y + h, x:x + w][sub] = d
            weight[y:y + h, x:x + w][sub] = 1

    # 平移量与权重同样模糊后相除：分区边界平滑过渡，未分区的细边取周围的平移量。
    shift = cv2.GaussianBlur(shift * weight[..., None], (0, 0), blur)
    weight = cv2.GaussianBlur(weight, (0, 0), blur)
    shift /= np.maximum(weight, 1e-3)[..., None]
    out = cv2.cvtColor(lab + shift, cv2.COLOR_LAB2RGB)
    result = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))
    if alpha is not None:
        result.putalpha(alpha)
    return result


def match_low(image: Image.Image, guide: Image.Image, sigma: float, light: float = 0.8, chroma: float = 1.0) -> Image.Image:
    """渐变引导图（舆图山峦）用：两图各按 alpha 加权做大半径高斯模糊，把 AI 图的低频色（Lab）平移到引导图的低频色，
    细节（岩纹、林木、沟壑）原样保留，整体配色与受光面、背光面的明暗分布回到程序化画法。sigma 为模糊半径（像素）。"""
    import cv2

    alpha = image.getchannel("A")
    a = np.asarray(alpha, dtype=np.float32) / 255
    lab = cv2.cvtColor(np.asarray(image.convert("RGB"), dtype=np.float32) / 255, cv2.COLOR_RGB2LAB)
    g = guide.convert("RGBA").resize(image.size, Image.LANCZOS)
    ga = np.asarray(g.getchannel("A"), dtype=np.float32) / 255
    glab = cv2.cvtColor(np.asarray(g.convert("RGB"), dtype=np.float32) / 255, cv2.COLOR_RGB2LAB)

    def low(x, w):
        return cv2.GaussianBlur(x * w[..., None], (0, 0), sigma) / np.maximum(cv2.GaussianBlur(w, (0, 0), sigma), 1e-3)[..., None]

    shift = (low(glab, ga) - low(lab, a)) * np.array([light, chroma, chroma], dtype=np.float32)
    out = cv2.cvtColor(lab + shift, cv2.COLOR_LAB2RGB)
    result = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))
    result.putalpha(alpha)
    return result


def keep_lines(image: Image.Image, guide: Image.Image, line: float = 0.3, snow: float = 0.88, opacity: float = 0.85) -> Image.Image:
    """把引导图里的墨线与积雪叠回 AI 图（舆图山峦）：引导图本身已按前后山正确遮挡，取其中亮度低于 line 的像素
    （天际线勾线，墨色 #293D42）与亮度高于 snow 的像素（积雪）作遮罩，按原色叠上。前后山交叠处的轮廓、
    高峰积雪是程序化画法的辨识点，AI 件和低频调色都会把它们抹淡。"""
    import cv2

    g = guide.convert("RGBA").resize(image.size, Image.LANCZOS)
    rgb = np.asarray(g.convert("RGB"), dtype=np.float32) / 255
    ga = np.asarray(g.getchannel("A"), dtype=np.float32) / 255
    lum = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    m_line = np.clip((line - lum) / 0.08, 0, 1)
    m_snow = np.clip((lum - snow) / 0.05, 0, 1)
    mask = np.maximum(m_line, m_snow) * ga * opacity
    mask = cv2.GaussianBlur(mask, (0, 0), 0.6)[..., None]
    base = np.asarray(image.convert("RGB"), dtype=np.float32) / 255
    out = base * (1 - mask) + rgb * mask
    result = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))
    result.putalpha(image.getchannel("A"))
    return result


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("image")
    parser.add_argument("guide")
    parser.add_argument("out")
    parser.add_argument("--light", type=float, default=0.7)
    parser.add_argument("--chroma", type=float, default=1.0)
    args = parser.parse_args()
    regrade(Image.open(args.image), Image.open(args.guide), args.light, args.chroma).save(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""夜间发光遮罩：从已入库的 AI 件里取出窗纸与灯笼的暖色像素，存成同框的发光图（M3-01 光影）。

夜里引擎把发光图以暖色叠加在件上（只亮窗纸与灯笼，不亮整栋房），并按遮罩里各块的位置在地上铺灯光。
发光图由入库件自动派生，不另生成；件换图后重跑本脚本。

  .venv/Scripts/python glow_mask.py town.house.inn --core-sat 0.48 --core-val 0.22 --loose-sat 0.42 --loose-val 0.18 --core-ratio 0.3
  .venv/Scripts/python glow_mask.py town.house.inn --preview out/glow_inn.png
  .venv/Scripts/python glow_mask.py town.house.north.1 town.house.south.1 --lattice   # 白天画法的格子窗（窗纸灰暗）

--lattice：民居 AI 件的窗是深色窗棂配灰白窗纸，没有亮窗纸可取。改为先找“白墙上的一块深色格子”——深色连通块、
四周一圈是亮墙、外框高大于宽（门楣宽大于高、门高宽比更大，都排除），块内按 Otsu 分出比窗棂亮的窗格，
夜里窗格换成暖色窗纸亮起、窗棂保持深色。各窗亮度按 id 取固定随机数略有出入，约四分之一的窗不点灯（至少留一扇亮）。

输出 game/assets/art/<地区>/<id>.glow.png（颜色取原件、窗外一圈暖色柔光，alpha 为发光强度）与 .glow.json（origin、px 同原件；
blobs 为各发光窗块在投影坐标中的中心与面积，lanterns 为灯笼中心，供引擎铺地面灯光与光晕）。
"""
import argparse
import json
import zlib
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]


def hue_sat_val(rgb):
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn + 1e-6
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = (mx - mn) / (mx + 1e-6)
    return h, s, mx


def mask(rgba, args):
    rgb = rgba[..., :3]
    alpha = rgba[..., 3]
    h, s, v = hue_sat_val(rgb)
    warm = (h < args.hue_max) | (h > 340)
    # 宽阈值连成窗格整块；严阈值是“确实是亮窗纸 / 灯笼”的核心。木板、门、招牌偏暗偏灰，核心像素少，整块剔除。
    loose = warm & (s > args.loose_sat) & (v > args.loose_val) & (alpha > 0.5)
    core = warm & (s > args.core_sat) & (v > args.core_val) & (alpha > 0.5)
    loose = loose.astype(np.uint8) * 255
    kernel = np.ones((5, 5), np.uint8)
    loose = cv2.morphologyEx(loose, cv2.MORPH_CLOSE, kernel)
    loose = cv2.morphologyEx(loose, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    count, labels, stats, centroids = cv2.connectedComponentsWithStats(loose, connectivity=8)
    keep = np.zeros_like(loose)
    blobs = []
    for i in range(1, count):
        area = stats[i, cv2.CC_STAT_AREA]
        if area < args.min_area:
            continue
        part = labels == i
        ratio = core[part].mean()
        if ratio < args.core_ratio:
            continue
        keep[part] = 255
        blobs.append((float(centroids[i][0]), float(centroids[i][1]), int(area)))
    # 灯笼：纯红、饱和度极高的小块，单独取中心（引擎在灯笼处加光晕、地上铺光），也并进发光图。
    red = ((h < 12) | (h > 340)) & (s > 0.75) & (v > 0.4) & (alpha > 0.5)
    red = cv2.morphologyEx(red.astype(np.uint8) * 255, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    count, labels, stats, centroids = cv2.connectedComponentsWithStats(red, connectivity=8)
    lanterns = []
    for i in range(1, count):
        if stats[i, cv2.CC_STAT_AREA] >= args.lantern_area:
            keep[labels == i] = 255
            lanterns.append((float(centroids[i][0]), float(centroids[i][1]), int(stats[i, cv2.CC_STAT_AREA])))
    # 羽化：边缘 2 像素渐隐，叠加时不出硬边。
    soft = cv2.GaussianBlur(keep, (0, 0), args.feather)
    return np.maximum(soft, (keep * 0.85).astype(np.uint8)), blobs, lanterns


def lattice_windows(rgba, args, seed):
    """白天画法的格子窗：返回发光图的 RGB、核心遮罩（0–1）与各亮窗块（中心 x、y、面积）。"""
    rgb = rgba[..., :3]
    alpha = rgba[..., 3]
    _, s, v = hue_sat_val(rgb)
    dark = ((v < 0.45) & (alpha > 0.5)).astype(np.uint8) * 255
    dark = cv2.morphologyEx(dark, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(dark, connectivity=8)
    rng = np.random.default_rng(seed)
    core = np.zeros(v.shape, np.float32)
    color = np.zeros(rgb.shape, np.float32)
    paper = np.array([0.92, 0.6, 0.3], np.float32)
    found = []
    for i in range(1, count):
        x, y, w, h, area = stats[i]
        if area < 150 or w * h > 20000 or w < 14 or h < 14 or not 1.1 < h / w < 1.6:
            continue
        comp = (labels == i).astype(np.uint8) * 255
        closed = cv2.morphologyEx(comp, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
        contours, _ = cv2.findContours(closed, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        hull = np.zeros_like(comp)
        cv2.drawContours(hull, contours, -1, 255, -1)
        inside = hull > 0
        ring = (cv2.dilate(hull, np.ones((13, 13), np.uint8)) > 0) & ~inside & (alpha > 0.5)
        if not ring.any() or v[ring].mean() < 0.6:
            continue
        inner = cv2.erode(hull, np.ones((5, 5), np.uint8)) > 0
        values = (v[inner] * 255).astype(np.uint8)
        if values.size < 50:
            continue
        cut, _ = cv2.threshold(values.reshape(-1, 1), 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        pane = inner & (v * 255 > cut) & (s < 0.3)
        ratio = pane.sum() / inner.sum()
        if not 0.15 < ratio < 0.8:
            continue
        cy, cx = np.argwhere(inside).mean(0)
        found.append((pane, float(cx), float(cy), int(inside.sum())))
    if not found:
        return color, core, []
    lit = rng.random(len(found)) >= args.unlit
    if not lit.any():
        lit[rng.integers(len(found))] = True
    blobs = []
    for (pane, cx, cy, area), on in zip(found, lit):
        if not on:
            continue
        level = rng.uniform(0.75, 1.0)
        # 窗纸本身的明暗留一点纹理：比该窗平均亮的格更亮。
        lum = v[pane]
        tex = 0.85 + 0.6 * (lum - lum.mean())
        color[pane] = np.clip(paper[None, :] * tex[:, None], 0, 1)
        core[pane] = level
        blobs.append((cx, cy, area))
    return color, core, blobs


def main():
    p = argparse.ArgumentParser()
    p.add_argument("ids", nargs="+")
    p.add_argument("--hue-max", type=float, default=36)
    p.add_argument("--loose-sat", type=float, default=0.32)
    p.add_argument("--loose-val", type=float, default=0.28)
    p.add_argument("--core-sat", type=float, default=0.5)
    p.add_argument("--core-val", type=float, default=0.42)
    p.add_argument("--core-ratio", type=float, default=0.18)
    p.add_argument("--min-area", type=int, default=60)
    p.add_argument("--lantern-area", type=int, default=20)
    p.add_argument("--halo", type=float, default=7, help="窗外柔光的模糊半径（像素）")
    p.add_argument("--halo-strength", type=float, default=0.45)
    p.add_argument("--feather", type=float, default=1.5)
    p.add_argument("--lattice", action="store_true", help="白天画法的格子窗：认出窗棂间的窗格，夜里换成暖色窗纸")
    p.add_argument("--unlit", type=float, default=0.25, help="--lattice 时不点灯的窗的比例")
    p.add_argument("--no-lanterns", action="store_true", help="不取红色灯笼（门上的红门环等会被误认）")
    p.add_argument("--preview", help="另存一张原图 + 遮罩的对照图")
    args = p.parse_args()

    for id_ in args.ids:
        region = id_.split(".", 1)[0]
        base = ROOT / "game" / "assets" / "art" / region / id_
        meta = json.loads(Path(f"{base}.json").read_text(encoding="utf-8"))
        rgba = np.asarray(Image.open(f"{base}.png").convert("RGBA")).astype(np.float32) / 255
        m, blobs, lanterns = mask(rgba, args)
        if args.no_lanterns:
            lanterns = []
        h, w = m.shape
        # 颜色取原件本身（叠加后窗格、灯笼的花纹仍在，只是亮起来），窗外一圈柔光取暖色；alpha 为内核遮罩加一圈外晕。
        core = m.astype(np.float32) / 255
        src = rgba[..., :3]
        warm = np.array([1.0, 0.78, 0.5], np.float32)
        rgb = np.where(core[..., None] > 0.05, np.clip(src * 1.15 + warm * 0.12, 0, 1), warm)
        if args.lattice:
            # 格子窗：窗格换暖色窗纸（只亮窗格，窗棂不亮）；灯笼仍按上面的红色取法。
            seed = zlib.crc32(id_.encode("utf-8"))
            pane_rgb, pane_core, blobs = lattice_windows(rgba, args, seed)
            red = np.zeros_like(core)
            for x, y, a in lanterns:
                r = max(3, int(np.sqrt(a / np.pi) + 2))
                cv2.circle(red, (int(x), int(y)), r, 1.0, -1)
            red = np.minimum(core, red)
            rgb = np.where(pane_core[..., None] > 0, pane_rgb, np.where(red[..., None] > 0.05, rgb, warm))
            soft = cv2.GaussianBlur(pane_core, (0, 0), args.feather * 0.6)
            core = np.maximum(np.maximum(pane_core * 0.9, soft), red)
            m = (core * 255).astype(np.uint8)
        halo = cv2.GaussianBlur(core, (0, 0), args.halo) * args.halo_strength
        alpha = np.maximum(core, halo)
        out = np.zeros((h, w, 4), np.uint8)
        out[..., :3] = (rgb * 255).astype(np.uint8)
        out[..., 3] = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
        glow = base.parent / f"{id_}.glow.png"
        Image.fromarray(out).save(glow)
        ox, oy = meta["origin"]
        px = meta["px"]
        doc = {
            "id": f"{id_}.glow",
            "origin": meta["origin"],
            "px": px,
            "derived_from": f"{id_}.png",
            "tool": "tools/ArtGen/glow_mask.py",
            "blobs": [[round(ox + x / px, 1), round(oy + y / px, 1), round(a / px / px, 1)] for x, y, a in blobs],
            "lanterns": [[round(ox + x / px, 1), round(oy + y / px, 1)] for x, y, _ in lanterns],
        }
        base.parent.joinpath(f"{id_}.glow.json").write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{id_}: {len(blobs)} 块窗、{len(lanterns)} 盏灯，发光像素 {int((m > 128).sum())}")
        if args.preview:
            src = (rgba * 255).astype(np.uint8)
            sheet = np.concatenate([src, out], axis=1)
            Image.fromarray(sheet).save(args.preview)


if __name__ == "__main__":
    main()

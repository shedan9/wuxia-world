"""把立绘局部重绘（jobs/m2_eye_fix.json、jobs/m2_portrait_fix.json）的选定结果套回游戏内对话立绘。

只替换遮罩内的像素：取选定输出在遮罩（羽化）内的 RGB，眼部任务再把虹膜里偏橙的亮棕压暗为深棕
（黑发黄肤的人物默认深棕 / 黑褐眼珠；任务写 "iris": false 则跳过），其余像素与透明通道沿用
游戏内现有立绘，不改动抠图。任务写 "target" 时套到该人物（串联的第二步重绘用）。

用法：.venv/Scripts/python eye_place.py [--set m2_portrait_fix] xiao_feng=44 tang_shouting=44 ...
串联任务按先后顺序传入（如 xiao_feng=33 xiao_feng_b=33）。
写回 game/assets/portraits/<名>_v1.png 与 art_source/ai/characters/<名>_portrait_v1_cutout.png，
并在 out/<set>/picks.json 记录选定种子与输出哈希。
"""

from __future__ import annotations

import colorsys
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from inpaint import shape_mask

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]


def darken_iris(rgb: np.ndarray, region: np.ndarray) -> np.ndarray:
    """遮罩内饱和的橙棕像素：明度压到 0.62 倍、色相拉向 25°（深棕）。"""
    out = rgb.astype(np.float32) / 255
    ys, xs = np.where(region > 0.05)
    for y, x in zip(ys, xs):
        h, s, v = colorsys.rgb_to_hsv(*out[y, x])
        if s > 0.35 and 0.0 <= h <= 0.12 and v > 0.25:
            w = region[y, x]
            nh = h + (25 / 360 - h) * 0.6
            nv = v * (1 - 0.38 * w)
            ns = min(1.0, s * 0.95)
            out[y, x] = colorsys.hsv_to_rgb(nh, ns, nv)
    return (out * 255).round().clip(0, 255).astype(np.uint8)


def blend_into(path: Path, fixed: np.ndarray, mask: Image.Image) -> None:
    """按羽化遮罩把修好的 RGB 贴进 RGBA 立绘，透明通道不变。"""
    base = np.asarray(Image.open(path).convert("RGBA")).copy()
    w = (np.asarray(mask, np.float32) / 255)[..., None]
    base[..., :3] = (fixed * w + base[..., :3] * (1 - w)).round().astype(np.uint8)
    Image.fromarray(base, "RGBA").save(path)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    args = sys.argv[1:]
    set_name = "m2_eye_fix"
    if args[:1] == ["--set"]:
        set_name, args = args[1], args[2:]
    out = ROOT / "out" / set_name
    spec = json.loads((ROOT / "jobs" / f"{set_name}.json").read_text(encoding="utf-8"))
    jobs = {j["name"]: j for j in spec["jobs"]}
    picks_path = out / "picks.json"
    picks = json.loads(picks_path.read_text(encoding="utf-8")) if picks_path.exists() else {}
    for arg in args:
        name, seed = arg.split("=")
        job = jobs[name]
        target = job.get("target", name)
        game = REPO / "game" / "assets" / "portraits" / f"{target}_v1.png"
        cutout = REPO / "art_source" / "ai" / "characters" / f"{target}_portrait_v1_cutout.png"
        pick = Image.open(out / f"{name}_{seed}.png").convert("RGB")
        mask = shape_mask(pick.size, job["mask"]).filter(ImageFilter.GaussianBlur(job.get("feather", 3)))
        fixed = np.asarray(pick)
        if job.get("iris", True):
            fixed = darken_iris(fixed, np.asarray(mask, np.float32) / 255)
        # 抠图源（源图坐标）直接贴回。
        blend_into(cutout, fixed, mask)
        if tf := job.get("game_transform"):
            # 入包立绘是源图缩放平移后的版本：把修好的源图与遮罩按同一变换搬到入包坐标。
            size = (round(pick.width * tf["scale"]), round(pick.height * tf["scale"]))
            canvas = Image.new("RGB", pick.size)
            canvas.paste(Image.fromarray(fixed).resize(size, Image.LANCZOS), tuple(tf["offset"]))
            moved = Image.new("L", pick.size, 0)
            moved.paste(mask.resize(size, Image.LANCZOS), tuple(tf["offset"]))
            blend_into(game, np.asarray(canvas), moved)
        else:
            blend_into(game, fixed, mask)
        picks[name] = {"seed": int(seed), "source": f"out/{set_name}/{name}_{seed}.png",
                       "iris": "遮罩内橙棕压暗为深棕" if job.get("iris", True) else "不调色",
                       "sha256": hashlib.sha256(game.read_bytes()).hexdigest()}
        print(f"{name}: 种子 {seed} → {game.relative_to(REPO)}")
    picks_path.write_text(json.dumps(picks, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""把一把画好的兵器贴进人物原图（M3-02 持兵战斗帧）：先由本脚本按骨架手位贴上兵器，再用 inpaint.py 只重绘握兵器的手。

用法：
    .venv/Scripts/python weapon.py jobs/m3_hero_sword_armed.json

AI 直接在手里画剑时，剑形、长短、曲直每张都不一样（常画成木棍、光束或弯刀），出手帧的手也改不成握拳。
这里兵器由代码画成赛璐璐样式（黑色描线、两阶明暗、中脊），按 pommel → tip 两点摆放，与人物线稿同一画法；
手的重绘交给 inpaint.py（遮罩只盖手，不盖剑身）。

任务字段：
  source   人物原图（生成原图，灰底）
  canvas   [宽, 高]：向右补宽（灰底延续左上角颜色），伸出画外的剑身留在补出的部分；place.py 横向比例按高度算
  pommel / tip  剑首与剑尖的画布坐标
  behind   可选，[[x, y, r], ...]：这些圆内保留人物原像素（手指压在剑柄上），之后再由 inpaint 修手
输出 out/<set>/<name>.png 与同名 .json（源图哈希、全部参数）。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent

SS = 4  # 超采样倍数：在 4 倍画布上画，缩回时得到抗锯齿边

OUTLINE = (27, 31, 38)
BLADE_LIT = (232, 238, 241)
BLADE_SHADE = (168, 182, 194)
RIDGE = (128, 142, 158)
GUARD_LIT = (176, 142, 78)
GUARD_SHADE = (122, 94, 48)
GRIP = (70, 48, 36)
GRIP_WRAP = (42, 28, 22)


def jian(length: float) -> list[tuple[str, list[tuple[float, float]], tuple[int, int, int]]]:
    """直剑各部件的多边形（剑身坐标：x 自剑首 0 到剑尖 length，y 为垂直方向，负值为受光一侧）。"""
    k = length / 520
    pommel_r = 11 * k
    grip0, grip1 = 12 * k, 96 * k
    guard0, guard1 = 96 * k, 112 * k
    blade0 = guard1
    point = length - 40 * k
    half = 13 * k
    near = 10.5 * k
    parts = []
    # 剑首：小圆钮。
    parts.append(("pommel", [(pommel_r + pommel_r * math.cos(a), pommel_r * math.sin(a)) for a in [i * math.pi / 10 for i in range(20)]], GUARD_LIT))
    # 剑柄：两头略收。
    gw = 9 * k
    parts.append(("grip", [(grip0, -gw * 0.85), (grip1, -gw), (grip1, gw), (grip0, gw * 0.85)], GRIP))
    # 剑格：中间厚、两翼收尖的菱形，翼稍向剑尖弯。
    gh = 28 * k
    parts.append(("guard", [(guard0, -gh * 0.35), (guard0 + 4 * k, -gh), (guard1, -gh * 0.55), (guard1 + 3 * k, 0),
                            (guard1, gh * 0.55), (guard0 + 4 * k, gh), (guard0, gh * 0.35)], GUARD_LIT))
    # 剑身受光半边与背光半边（以中脊为界），剑尖收成锋。
    parts.append(("blade_lit", [(blade0, -half), (point, -near), (length, 0), (blade0, 0)], BLADE_LIT))
    parts.append(("blade_shade", [(blade0, 0), (length, 0), (point, near), (blade0, half)], BLADE_SHADE))
    return parts


def draw_jian(size: tuple[int, int], pommel: tuple[float, float], tip: tuple[float, float]) -> Image.Image:
    """在 size 画布上按剑首、剑尖两点画一把直剑，返回 RGBA 图层。"""
    W, H = size[0] * SS, size[1] * SS
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    px, py = pommel[0] * SS, pommel[1] * SS
    dx, dy = (tip[0] - pommel[0]) * SS, (tip[1] - pommel[1]) * SS
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    # 受光一侧取剑身朝上的法向（光从左上来）。
    nx, ny = -uy, ux
    if ny > 0:
        nx, ny = -nx, -ny

    def world(p: tuple[float, float]) -> tuple[float, float]:
        return (px + ux * p[0] + nx * -p[1], py + uy * p[0] + ny * -p[1])

    stroke = 3.2 * SS
    parts = jian(length)
    # 先画整体黑色描线（各部件外扩），再填色。
    for _, poly, _ in parts:
        pts = [world(p) for p in poly]
        d.polygon(pts, fill=OUTLINE)
        d.line(pts + [pts[0]], fill=OUTLINE, width=int(stroke * 2), joint="curve")
    for name, poly, color in parts:
        pts = [world(p) for p in poly]
        d.polygon(pts, fill=color)
        if name == "guard":
            # 剑格背光半边。
            half = [world(p) for p in poly if p[1] >= 0] + [world((poly[0][0], 0))]
            d.polygon(half, fill=GUARD_SHADE)
        if name == "pommel":
            d.polygon([world(p) for p in poly if p[1] >= 0], fill=GUARD_SHADE)
    k = length / 520
    # 中脊与缠绳。
    d.line([world((112 * k, 0)), world((length - 30 * k, 0))], fill=RIDGE, width=int(1.6 * SS))
    for i in range(8):
        x0 = 16 * k + i * 10 * k
        d.line([world((x0, -8 * k)), world((x0 + 7 * k, 8 * k))], fill=GRIP_WRAP, width=int(2.2 * SS))
    return layer.resize(size, Image.LANCZOS)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    args = parser.parse_args()
    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)
    for job in spec["jobs"]:
        src_path = ROOT / job["source"]
        src = Image.open(src_path).convert("RGB")
        size = tuple(job.get("canvas", src.size))
        base = Image.new("RGB", size, tuple(int(v) for v in src.getpixel((4, 4))))
        base.paste(src, (0, 0))
        sword = draw_jian(size, tuple(job["pommel"]), tuple(job["tip"]))
        armed = base.copy()
        armed.paste(sword, (0, 0), sword)
        if job.get("behind"):
            keep = Image.new("L", size, 0)
            kd = ImageDraw.Draw(keep)
            for x, y, r in job["behind"]:
                kd.ellipse([x - r, y - r, x + r, y + r], fill=255)
            armed = Image.composite(base, armed, keep)
        path = out_dir / f"{job['name']}.png"
        armed.save(path)
        meta = {"name": job["name"], "pose": job["pose"], "source": job["source"],
                "source_sha256": hashlib.sha256(src_path.read_bytes()).hexdigest(), "job": job,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(job["name"], "→", path.name)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

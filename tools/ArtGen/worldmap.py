"""大地图舆图重绘（架构文档 10.3“大地图单独处理”：色块草图 → 图生图 → 引擎叠交互层）。

用法：
    .venv/Scripts/python worldmap.py jobs/m3_worldmap.json --stage a [--seeds 11,22]
    .venv/Scripts/python worldmap.py jobs/m3_worldmap.json --stage b --from out/m3_worldmap/a_<名称>_<种子>.png

引导图由 Godot 导出（PieceGuideExport --region=worldmap → out/guides/worldmap/worldmap.guide.png，4608×2560）：
程序化舆图只含地形、江河与平视山峦，道路、题字与地标由引擎画在上层，因此重绘只负责底图。
- a 段（整图）：引导图缩到约 1.5 MP，图生图 + Canny ControlNet（海岸、江河、山体轮廓按引导图落位），
  一次画完整张，保证全图色调与笔触一致；
- b 段（细化）：a 段结果放大回原尺寸，按 tile 边长、overlap 重叠分块，每块以 a 段放大图为底、
  引导图同块边线为控制，低 strength 重绘出细节，再以羽化权重拼回，避免接缝。
每张输出旁有同名 .json：引导图哈希、模型、种子、参数与输出哈希。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import time
from pathlib import Path

import numpy as np
from PIL import Image

from generate import MODELS
from piece import CONTROLNET, load_controlnet_pipe, work_size

ROOT = Path(__file__).resolve().parent


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canny(image: Image.Image, low: int, high: int) -> Image.Image:
    import cv2

    edges = cv2.Canny(cv2.cvtColor(np.asarray(image.convert("RGB")), cv2.COLOR_RGB2GRAY), low, high)
    return Image.fromarray(edges).convert("RGB")


def run(pipe, image: Image.Image, control: Image.Image, spec: dict, seed: int, strength: float, scale: float):
    import torch

    return pipe(
        prompt=spec["prompt"],
        negative_prompt=spec["negative"],
        image=image,
        control_image=control,
        strength=strength,
        controlnet_conditioning_scale=scale,
        control_guidance_end=spec.get("control_end", 0.8),
        num_inference_steps=spec.get("steps", 30),
        guidance_scale=spec.get("cfg", 6.0),
        generator=torch.Generator("cuda").manual_seed(seed),
    ).images[0]


def feather(size: tuple[int, int], overlap: int, edges: tuple[bool, bool, bool, bool]) -> np.ndarray:
    """拼块权重：与相邻块重叠的边线性淡出（左、上、右、下各自判断是否有邻块）。"""
    w, h = size
    wx = np.ones(w, np.float32)
    wy = np.ones(h, np.float32)
    ramp = np.linspace(0.0, 1.0, overlap, dtype=np.float32)
    left, top, right, bottom = edges
    if left:
        wx[:overlap] = ramp
    if right:
        wx[-overlap:] = np.minimum(wx[-overlap:], ramp[::-1])
    if top:
        wy[:overlap] = ramp
    if bottom:
        wy[-overlap:] = np.minimum(wy[-overlap:], ramp[::-1])
    return np.outer(wy, wx)[..., None]


def starts(total: int, tile: int, overlap: int) -> list[int]:
    if total <= tile:
        return [0]
    n = int(np.ceil((total - overlap) / (tile - overlap)))
    step = (total - tile) / (n - 1)
    return [round(i * step) for i in range(n)]


def record(out: Path, meta: dict) -> None:
    meta["output_sha256"] = sha(out)
    out.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("job")
    ap.add_argument("--stage", choices=["a", "b"], required=True)
    ap.add_argument("--seeds", default="")
    ap.add_argument("--from", dest="source", default="")
    ap.add_argument("--name", default="")
    args = ap.parse_args()

    spec = json.loads((ROOT / args.job).read_text(encoding="utf-8"))
    guide_path = ROOT / spec["guide"]
    guide = Image.open(guide_path).convert("RGB")
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)
    pipe = load_controlnet_pipe(spec.get("model", "sdxl"))
    stage = spec[args.stage]
    base_meta = {
        "guide": spec["guide"], "guide_sha256": sha(guide_path), "model": MODELS[spec.get("model", "sdxl")],
        "controlnet": CONTROLNET, "prompt": spec["prompt"], "negative": spec["negative"],
        "steps": spec.get("steps", 30), "cfg": spec.get("cfg", 6.0), "control_end": spec.get("control_end", 0.8),
    }

    if args.stage == "a":
        size = work_size(*guide.size, stage.get("pixels", 1_500_000))
        image = guide.resize(size, Image.LANCZOS)
        control = canny(image, *stage.get("canny", [60, 140]))
        control.save(out_dir / "a__control.png")
        name = args.name or stage.get("name", "map")
        for seed in [int(s) for s in (args.seeds or ",".join(map(str, stage["seeds"]))).split(",")]:
            t = time.time()
            result = run(pipe, image, control, spec, seed, stage["strength"], stage["control"])
            out = out_dir / f"a_{name}_{seed}.png"
            result.save(out)
            record(out, {**base_meta, "stage": "a", "seed": seed, "size": list(size), "strength": stage["strength"],
                         "controlnet_conditioning_scale": stage["control"], "canny": stage.get("canny", [60, 140])})
            print(f"{out.name}  {time.time() - t:.1f}s", flush=True)
        return 0

    source_path = ROOT / args.source
    up = Image.open(source_path).convert("RGB").resize(guide.size, Image.LANCZOS)
    tile, overlap = stage.get("tile", 1024), stage.get("overlap", 256)
    seed = int(args.seeds or stage.get("seed", 11))
    acc = np.zeros((guide.size[1], guide.size[0], 3), np.float32)
    weight = np.zeros((guide.size[1], guide.size[0], 1), np.float32)
    xs, ys = starts(guide.size[0], tile, overlap), starts(guide.size[1], tile, overlap)
    t0 = time.time()
    for j, y in enumerate(ys):
        for i, x in enumerate(xs):
            box = (x, y, x + tile, y + tile)
            piece = up.crop(box)
            control = canny(guide.crop(box), *stage.get("canny", [60, 140]))
            result = run(pipe, piece, control, spec, seed + j * 100 + i, stage["strength"], stage["control"])
            wmask = feather((tile, tile), overlap, (i > 0, j > 0, i < len(xs) - 1, j < len(ys) - 1))
            acc[y:y + tile, x:x + tile] += np.asarray(result, np.float32) * wmask
            weight[y:y + tile, x:x + tile] += wmask
            print(f"块 {j},{i}  {time.time() - t0:.0f}s", flush=True)
    merged = Image.fromarray(np.clip(acc / np.maximum(weight, 1e-6), 0, 255).astype(np.uint8))
    name = args.name or source_path.stem.removeprefix("a_")
    out = out_dir / f"b_{name}.png"
    merged.save(out)
    record(out, {**base_meta, "stage": "b", "source": args.source, "source_sha256": sha(source_path), "seed": seed,
                 "tile": tile, "overlap": overlap, "strength": stage["strength"],
                 "controlnet_conditioning_scale": stage["control"], "canny": stage.get("canny", [60, 140])})
    print(out.name)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

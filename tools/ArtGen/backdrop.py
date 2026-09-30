"""整张生成不需要行走的布景（战斗背景等，架构文档 10.3 第 2 步“整张生成，再局部重绘修正”）。

用法：
    .venv/Scripts/python backdrop.py jobs/m0_battle_backdrop.json [--only 名称前缀] [--preview]

战斗页的站位、地平线与水岸线由版式决定（BattlePreview：我方与敌方脚底在 1080p 的 y 540–684），
AI 只能贴合它，因此每个任务先按设计坐标（1920×1080）画一张色块草图 sketch：
    {"rect": [x0, y0, x1, y1], "fill": "#RRGGBB"}                     矩形
    {"vgrad": [x0, y0, x1, y1], "from": "#...", "to": "#..."}           竖向渐变矩形
    {"poly": [[x, y], ...], "fill": "#..."}                             多边形
    {"ellipse": [x0, y0, x1, y1], "fill": "#..."}                        椭圆
    {"line": [[x, y], ...], "fill": "#...", "width": 6}                  折线
    {"flagstones": [y0, y1], "rows": 6, "fill": "#...", "seed": 3}       地面条石缝（行高自远而近渐增、错缝），给地面结构
    {"circle": [x, y], "r": 40, "fill": "#...", "outline": "#...", "width": 6}  圆（可只描边）
草图缩到生成尺寸 size（约 1 MP、16:9）后按 mode 使用：
    txt2img        只用提示词（对照组）；
    img2img        草图作图生图底图（strength 越高越自由）；
    canny          草图的 Canny 边线作 ControlNet 引导的文生图（xinsir/controlnet-canny-sdxl-1.0）；
    img2img_canny  两者同时。
随后放大到 1920×1080，以低强度图生图补细节（refine），输出 out/<任务集>/<名称>_<种子>.png，
旁有 __gen.png（放大前）与同名 .json（草图哈希、模型、种子、参数、提示词、输出哈希）。
--preview 只输出草图与边线图，不加载模型。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from generate import FP16_VAE, MODELS, build_prompt
from figure import encode_long
from piece import CONTROLNET, load_controlnet_pipe

ROOT = Path(__file__).resolve().parent
DESIGN = (1920, 1080)


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def hex_rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def draw_shapes(image: Image.Image, shapes: list[dict]) -> None:
    """按图像坐标依次画色块（矩形、竖向渐变、多边形、椭圆、折线、地面条石缝），prop_guide.py 画道具引导图时共用。"""
    draw = ImageDraw.Draw(image)
    for s in shapes:
        if "vgrad" in s:
            x0, y0, x1, y1 = s["vgrad"]
            a, b = np.array(hex_rgb(s["from"]), float), np.array(hex_rgb(s["to"]), float)
            for y in range(int(y0), int(y1)):
                t = (y - y0) / max(1, y1 - y0 - 1)
                draw.line([(x0, y), (x1, y)], fill=tuple(int(v) for v in a + (b - a) * t) + ((255,) if image.mode == "RGBA" else ()))
        elif "rect" in s:
            draw.rectangle(s["rect"], fill=s["fill"])
        elif "poly" in s:
            draw.polygon([tuple(p) for p in s["poly"]], fill=s["fill"])
        elif "ellipse" in s:
            draw.ellipse(s["ellipse"], fill=s["fill"])
        elif "line" in s:
            draw.line([tuple(p) for p in s["line"]], fill=s["fill"], width=s.get("width", 6), joint="curve")
        elif "circle" in s:
            (x, y), r = s["circle"], s["r"]
            draw.ellipse([x - r, y - r, x + r, y + r], fill=s.get("fill"), outline=s.get("outline"), width=s.get("width", 1))
        elif "flagstones" in s:
            flagstones(draw, s)


def draw_sketch(shapes: list[dict], noise: float, seed: int) -> Image.Image:
    """按设计坐标画色块草图；noise 叠一层低频明暗，免得图生图把大面积平涂照搬成平涂。"""
    image = Image.new("RGB", DESIGN, "#808080")
    draw_shapes(image, shapes)
    if noise > 0:
        rng = np.random.default_rng(seed)
        low = rng.normal(0, 1, (DESIGN[1] // 24, DESIGN[0] // 24))
        low = np.asarray(Image.fromarray(low.astype(np.float32)).resize(DESIGN, Image.BICUBIC))
        arr = np.asarray(image, float) + low[..., None] * noise
        image = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    return image


def flagstones(draw: ImageDraw.ImageDraw, s: dict) -> None:
    """侧视地面的条石缝：行高按等比自远（y0）而近（y1）增大，每行条石长度约为行高的 2–4 倍、随机错缝。"""
    import random

    rng = random.Random(s.get("seed", 3))
    y0, y1 = s["flagstones"]
    rows, grow = s.get("rows", 6), s.get("grow", 1.35)
    unit = (y1 - y0) * (grow - 1) / (grow**rows - 1)
    y, h = y0, unit
    width = s.get("width", 3)
    for _ in range(rows):
        draw.line([(0, y), (DESIGN[0], y)], fill=s["fill"], width=width)
        x = -rng.random() * h * 3
        while x < DESIGN[0]:
            x += h * rng.uniform(2.0, 4.0)
            draw.line([(x, y), (x + rng.uniform(-0.2, 0.2) * h, y + h)], fill=s["fill"], width=width)
        y += h
        h *= grow


def edges(image: Image.Image, low: int, high: int) -> Image.Image:
    import cv2

    gray = cv2.cvtColor(np.asarray(image), cv2.COLOR_RGB2GRAY)
    return Image.fromarray(cv2.Canny(gray, low, high)).convert("RGB")


def run(pipe, spec: dict, job: dict, out_dir: Path, preview: bool) -> None:
    import torch

    get = lambda key, default=None: job.get(key, spec.get(key, default))  # noqa: E731
    size = tuple(get("size", [1360, 768]))
    sketch_full = draw_sketch(get("sketch", []), get("noise", 10), 7)
    sketch_path = out_dir / f"{job['name']}__sketch.png"
    sketch_full.save(sketch_path)
    sketch = sketch_full.filter(ImageFilter.GaussianBlur(get("blur", 0))).resize(size, Image.LANCZOS)
    low, high = get("canny", [40, 120])
    control = edges(sketch_full.resize(size, Image.LANCZOS), low, high)
    control.save(out_dir / f"{job['name']}__control.png")
    if preview:
        print(f"{job['name']}  草图 {size[0]}×{size[1]}")
        return

    from diffusers import (
        StableDiffusionXLControlNetPipeline,
        StableDiffusionXLImg2ImgPipeline,
        StableDiffusionXLPipeline,
    )

    parts = {k: v for k, v in pipe.components.items() if k != "controlnet"}
    plain_text = StableDiffusionXLPipeline(**parts)
    plain_img = StableDiffusionXLImg2ImgPipeline(**parts)
    text_ctrl = StableDiffusionXLControlNetPipeline(**pipe.components)

    positive, negative = build_prompt(spec["style"], job)
    # 场景描述加风格词常超过 77 token，分段编码（figure.py），否则风格词被静默截断。
    embeds, chunks = encode_long(pipe, positive, negative)
    mode = get("mode", "img2img")
    refine = get("refine", {"strength": 0.3, "steps": 30})
    for seed in get("seeds", [1]):
        started = time.time()
        gen = lambda: torch.Generator("cpu").manual_seed(seed)  # noqa: E731
        common = {
            **embeds,
            "num_inference_steps": get("steps", 30),
            "guidance_scale": get("cfg", 5.5),
        }
        ctrl = {
            "controlnet_conditioning_scale": get("control", 0.6),
            "control_guidance_end": get("control_end", 0.6),
        }
        if mode == "txt2img":
            image = plain_text(width=size[0], height=size[1], generator=gen(), **common).images[0]
        elif mode == "img2img":
            image = plain_img(image=sketch, strength=get("strength", 0.85), generator=gen(), **common).images[0]
        elif mode == "canny":
            image = text_ctrl(image=control, width=size[0], height=size[1], generator=gen(), **common, **ctrl).images[0]
        else:
            image = pipe(image=sketch, control_image=control, strength=get("strength", 0.85), generator=gen(),
                         **common, **ctrl).images[0]
        stem = f"{job['name']}_{seed}"
        image.save(out_dir / f"{stem}__gen.png")
        # 放大到 1920×1088（SDXL 边长须为 8 的倍数），低强度图生图补细节，再裁回 1080。
        big = image.resize((DESIGN[0], 1088), Image.LANCZOS)
        if refine and refine.get("strength", 0) > 0:
            big = plain_img(image=big, strength=refine["strength"], generator=gen(),
                            **{**common, "num_inference_steps": refine.get("steps", 30)}).images[0]
        final = big.crop((0, 4, DESIGN[0], 4 + DESIGN[1]))
        path = out_dir / f"{stem}.png"
        final.save(path)
        meta = {
            "name": job["name"],
            "seed": seed,
            "mode": mode,
            "sketch_sha256": sha(sketch_path),
            "model": MODELS[spec.get("model", "sdxl")],
            "vae": FP16_VAE,
            **({"controlnet": CONTROLNET, **ctrl, "canny": [low, high]} if "canny" in mode else {}),
            **({"strength": get("strength", 0.85)} if "img2img" in mode else {}),
            "size": list(size),
            "steps": common["num_inference_steps"],
            "cfg": common["guidance_scale"],
            "refine": refine,
            "prompt": positive,
            "negative": negative,
            "prompt_chunks": chunks,
            "sha256": sha(path),
            "seconds": round(time.time() - started, 1),
            "gpu": torch.cuda.get_device_name(0),
        }
        path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{path.name}  {meta['seconds']}s")


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only", help="只生成名称以此开头的任务")
    parser.add_argument("--preview", action="store_true", help="只输出草图与边线图，不加载模型")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)
    pipe = None if args.preview else load_controlnet_pipe(spec.get("model", "sdxl"))
    for job in jobs:
        run(pipe, spec, job, out_dir, args.preview)
    return 0


if __name__ == "__main__":
    sys.exit(main())

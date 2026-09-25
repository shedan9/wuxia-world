"""按占位件引导图生成布景正式件（架构文档 10.3 方案 C：布局 → 引导出件 → 拼装，2026-09-25 用户选定）。

用法：
    .venv/Scripts/python piece.py jobs/m0_town_pieces.json [--only 名称前缀] [--preview]

引导图由 Godot 从布局数据渲染（PieceGuideExport，输出 out/guides/<地区>/<件 id>.png/.json），
与游戏同一投影、同一受光、透明底。每个任务对一件：
1. 把引导图铺在纯色底上，缩放到约 1 MP（边长取 8 的倍数）作为图生图底图；
2. 从引导图提取 Canny 边线作 ControlNet 引导（xinsir/controlnet-canny-sdxl-1.0，Apache-2.0），
   墙角、檐口、屋脊按布局几何落位，AI 只负责材质、笔触与细节；
3. 生成后按引导图 alpha（可外扩 grow 像素给柳丝等留余量）抠出，输出 RGBA 精灵；
   多部件的件（平桥 = 桥面 + 两道栏杆）另按各部件遮罩拆成 <名称>_<种子>__<部件>.png。
每张输出旁有同名 .json：引导图哈希、投影原点 origin 与像素比例 px（引擎按它们贴回原位）、
模型、种子、提示词与输出哈希。--preview 只输出底图与边线图，不加载模型。

纹理任务（"kind": "texture"）生成可无缝平铺的方形地面纹理：卷积层改为环绕填充，左右、上下边缘自然相接。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from generate import FP16_VAE, MODELS, build_prompt

ROOT = Path(__file__).resolve().parent
CONTROLNET = "xinsir/controlnet-canny-sdxl-1.0"  # Apache-2.0


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def work_size(w: int, h: int, pixels: int) -> tuple[int, int]:
    k = (pixels / (w * h)) ** 0.5
    return max(8, round(w * k / 8) * 8), max(8, round(h * k / 8) * 8)


def flatten(image: Image.Image, size: tuple[int, int], background: str) -> Image.Image:
    rgba = image.convert("RGBA").resize(size, Image.LANCZOS)
    base = Image.new("RGBA", size, background)
    base.alpha_composite(rgba)
    return base.convert("RGB")


def prepare(guide: Image.Image, shape: Image.Image, size: tuple[int, int], background: str, low: int, high: int):
    """底图（引导图压在纯色底上）、ControlNet 边线图（取自 shape：结构图或完整引导图）、alpha。"""
    import cv2

    base = flatten(guide, size, background)
    gray = cv2.cvtColor(np.asarray(flatten(shape, size, background)), cv2.COLOR_RGB2GRAY)
    edges = cv2.Canny(gray, low, high)
    control = Image.fromarray(edges).convert("RGB")
    return base, control, guide.convert("RGBA").resize(size, Image.LANCZOS).getchannel("A")


def cut(image: Image.Image, alpha: Image.Image, grow: int, feather: float) -> Image.Image:
    a = alpha
    if grow > 0:
        a = a.filter(ImageFilter.MaxFilter(grow * 2 + 1))
    if feather > 0:
        a = a.filter(ImageFilter.GaussianBlur(feather))
    out = image.convert("RGBA")
    out.putalpha(a)
    return out


def load_controlnet_pipe(model_key: str):
    import torch
    from diffusers import (
        AutoencoderKL,
        ControlNetModel,
        EulerAncestralDiscreteScheduler,
        StableDiffusionXLControlNetImg2ImgPipeline,
    )

    from generate import EULER_A, FP16_VARIANT

    vae = AutoencoderKL.from_pretrained(FP16_VAE, torch_dtype=torch.float16)
    controlnet = ControlNetModel.from_pretrained(CONTROLNET, torch_dtype=torch.float16)
    pipe = StableDiffusionXLControlNetImg2ImgPipeline.from_pretrained(
        MODELS[model_key],
        vae=vae,
        controlnet=controlnet,
        torch_dtype=torch.float16,
        variant="fp16" if model_key in FP16_VARIANT else None,
        use_safetensors=True,
    )
    if model_key in EULER_A:
        pipe.scheduler = EulerAncestralDiscreteScheduler.from_config(pipe.scheduler.config)
    if torch.cuda.get_device_properties(0).total_memory / 2**30 < 12:
        pipe.enable_model_cpu_offload()
    else:
        pipe.to("cuda")
    pipe.vae.enable_tiling()
    return pipe


_TEXT_PIPE = {}


def text_pipe(pipe):
    """同一组子模型的 ControlNet 文生图管线（不另占显存，同 inpaint.py 的约定）。"""
    from diffusers import StableDiffusionXLControlNetPipeline

    if id(pipe) not in _TEXT_PIPE:
        _TEXT_PIPE[id(pipe)] = StableDiffusionXLControlNetPipeline(**pipe.components)
    return _TEXT_PIPE[id(pipe)]


def seamless(pipe, on: bool) -> None:
    """卷积层改为环绕填充：生成结果左右、上下边缘相接，可无缝平铺。"""
    import torch

    modules = list(pipe.unet.modules()) + list(pipe.vae.modules())
    if getattr(pipe, "controlnet", None) is not None:
        modules += list(pipe.controlnet.modules())
    for module in modules:
        if isinstance(module, torch.nn.Conv2d):
            module.padding_mode = "circular" if on else "zeros"


def run_piece(pipe, spec: dict, job: dict, out_dir: Path, preview: bool) -> None:
    import torch

    guide_dir = ROOT / spec["guides"]
    guide_path = guide_dir / f"{job['piece']}.png"
    meta_in = json.loads((guide_dir / f"{job['piece']}.json").read_text(encoding="utf-8"))
    guide = Image.open(guide_path)
    # control_from：shape 取只有面与轮廓的结构图（细节交给模型），guide 取带贴花的完整引导图。
    control_from = job.get("control_from", spec.get("control_from", "shape"))
    shape = Image.open(guide_dir / f"{job['piece']}__shape.png") if control_from == "shape" else guide
    size = work_size(*guide.size, job.get("pixels", spec.get("pixels", 1_150_000)))
    low, high = job.get("canny", spec.get("canny", [60, 160]))
    base, control, alpha = prepare(guide, shape, size, job.get("background", spec.get("background", "#B9C7C2")), low, high)
    if preview:
        base.save(out_dir / f"{job['name']}__base.png")
        control.save(out_dir / f"{job['name']}__control.png")
        print(f"{job['name']}  底图 {size[0]}×{size[1]}")
        return

    positive, negative = build_prompt(spec["style"], job)
    scale = size[0] / guide.size[0]
    masks = {
        part: Image.open(guide_dir / f"{job['piece']}__{part}.png").resize(size, Image.LANCZOS).getchannel("A")
        for part in meta_in["parts"]
        if len(meta_in["parts"]) > 1
    }
    # mode：img2img 以引导图配色为底；txt2img 只受边线约束，配色与材质全由模型画。
    mode = job.get("mode", spec.get("mode", "img2img"))
    for seed in job.get("seeds", spec.get("seeds", [1])):
        started = time.time()
        params = {
            "mode": mode,
            "control_from": control_from,
            "controlnet_conditioning_scale": job.get("control", spec.get("control", 0.7)),
            "control_guidance_end": job.get("control_end", spec.get("control_end", 1.0)),
            "steps": job.get("steps", spec.get("steps", 30)),
            "cfg": job.get("cfg", spec.get("cfg", 6.0)),
        }
        common = {
            "prompt": positive,
            "negative_prompt": negative,
            "controlnet_conditioning_scale": params["controlnet_conditioning_scale"],
            "control_guidance_end": params["control_guidance_end"],
            "num_inference_steps": params["steps"],
            "guidance_scale": params["cfg"],
            "generator": torch.Generator("cpu").manual_seed(seed),
        }
        if mode == "txt2img":
            image = text_pipe(pipe)(image=control, width=size[0], height=size[1], **common).images[0]
        else:
            params["strength"] = job.get("strength", spec.get("strength", 0.7))
            image = pipe(image=base, control_image=control, strength=params["strength"], **common).images[0]
        stem = f"{job['name']}_{seed}"
        image.save(out_dir / f"{stem}__raw.png")
        grow, feather = job.get("grow", 0), job.get("feather", 0.6)
        path = out_dir / f"{stem}.png"
        cut(image, alpha, grow, feather).save(path)
        parts = {}
        for part, mask in masks.items():
            part_path = out_dir / f"{stem}__{part}.png"
            cut(image, mask, grow, feather).save(part_path)
            parts[part] = sha(part_path)
        meta = {
            "name": job["name"],
            "piece": job["piece"],
            "seed": seed,
            "guide": str(guide_path.relative_to(ROOT)).replace("\\", "/"),
            "guide_sha256": sha(guide_path),
            # 引擎贴图：图像左上角对应的投影坐标与每投影单位像素数（引导图 px 按缩放换算）。
            "origin": meta_in["origin"],
            "px": meta_in["px"] * scale,
            "view": meta_in["view"],
            "model": MODELS[spec.get("model", "sdxl")],
            "controlnet": CONTROLNET,
            "vae": FP16_VAE,
            "size": list(size),
            **params,
            "canny": [low, high],
            "grow": grow,
            "prompt": positive,
            "negative": negative,
            "sha256": sha(path),
            "parts": parts,
            "seconds": round(time.time() - started, 1),
            "gpu": torch.cuda.get_device_name(0),
        }
        path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{path.name}  {meta['seconds']}s")


def bond_guide(bond: dict, size: tuple[int, int]) -> Image.Image:
    """错缝条石的可循环格线（白线黑底）：tile 为一格纹理覆盖的世界边长，rows 行，每行由若干条石拼满一格，
    行间错缝随机。线条跨边环绕绘制，作 ControlNet 引导时生成结果仍可无缝平铺。"""
    import random

    from PIL import ImageDraw

    rng = random.Random(bond.get("seed", 7))
    tile, rows, lengths = bond["tile"], bond["rows"], bond["lengths"]
    k = size[0] / tile
    row_h = tile / rows
    image = Image.new("L", size, 0)
    draw = ImageDraw.Draw(image)
    width = bond.get("line", 3)
    for r in range(rows):
        y0, y1 = r * row_h * k, (r + 1) * row_h * k
        for dy in (0, -size[1], size[1]):
            draw.line([(0, y0 + dy), (size[0], y0 + dy)], fill=255, width=width)
        # 条石长度随机拼满一格，整行再随机错开。
        joints, x = [], 0.0
        while x < tile - min(lengths):
            joints.append(x)
            x += rng.choice(lengths)
        shift = rng.random() * tile
        for j in joints:
            px = ((j + shift) % tile) * k
            for dx in (0, -size[0], size[0]):
                draw.line([(px + dx, y0), (px + dx, y1)], fill=255, width=width)
    return image.convert("RGB")


def run_texture(spec: dict, job: dict, out_dir: Path) -> None:
    import torch

    positive, negative = build_prompt(spec["style"], job)
    width, height = job.get("size", [1024, 1024])
    control = None
    if "bond" in job:
        # 按布局给条石格线（方案 C 用于地面）：控制条石尺寸、走向与错缝，材质交给模型。
        from diffusers import StableDiffusionXLControlNetPipeline

        base = load_controlnet_pipe(spec.get("model", "sdxl"))
        pipe = StableDiffusionXLControlNetPipeline(**base.components)
        control = bond_guide(job["bond"], (width, height))
        control.save(out_dir / f"{job['name']}__control.png")
    else:
        from generate import load_pipeline

        pipe, _ = load_pipeline(spec.get("model", "sdxl"))
    seamless(pipe, True)
    for seed in job.get("seeds", spec.get("seeds", [1])):
        started = time.time()
        extra = {"image": control, "controlnet_conditioning_scale": job.get("control", 0.8),
                 "control_guidance_end": job.get("control_end", 1.0)} if control is not None else {}
        image = pipe(
            prompt=positive,
            negative_prompt=negative,
            width=width,
            height=height,
            num_inference_steps=job.get("steps", spec.get("steps", 30)),
            guidance_scale=job.get("cfg", spec.get("cfg", 6.0)),
            generator=torch.Generator("cpu").manual_seed(seed),
            **extra,
        ).images[0]
        path = out_dir / f"{job['name']}_{seed}.png"
        image.save(path)
        # 2×2 平铺预览，检查接缝。
        tiled = Image.new("RGB", (width * 2, height * 2))
        for dx in (0, width):
            for dy in (0, height):
                tiled.paste(image, (dx, dy))
        tiled.resize((width, height)).save(out_dir / f"{job['name']}_{seed}__tiled.jpg", quality=90)
        meta = {
            "name": job["name"],
            "seed": seed,
            "kind": "texture",
            "seamless": "circular padding (unet + vae" + (" + controlnet)" if control is not None else ")"),
            "world_size": job.get("world_size"),
            "model": MODELS[spec.get("model", "sdxl")],
            "vae": FP16_VAE,
            "size": [width, height],
            "steps": job.get("steps", spec.get("steps", 30)),
            "cfg": job.get("cfg", spec.get("cfg", 6.0)),
            "prompt": positive,
            "negative": negative,
            "sha256": sha(path),
            "seconds": round(time.time() - started, 1),
            "gpu": torch.cuda.get_device_name(0),
            **({"controlnet": CONTROLNET, "bond": job["bond"], "control": extra["controlnet_conditioning_scale"],
                "control_guidance_end": extra["control_guidance_end"]} if control is not None else {}),
        }
        path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{path.name}  {meta['seconds']}s")
    seamless(pipe, False)
    del pipe
    torch.cuda.empty_cache()


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only", help="只生成名称以此开头的任务")
    parser.add_argument("--preview", action="store_true", help="只输出底图与边线图，不加载模型")
    parser.add_argument("--model", choices=sorted(MODELS), help="临时换基础模型比较效果，输出到 <任务集>_<模型>")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    if args.model:
        spec["model"] = args.model
        spec["set"] = f"{spec['set']}_{args.model}"
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)

    textures = [j for j in jobs if j.get("kind") == "texture"]
    pieces = [j for j in jobs if j.get("kind") != "texture"]
    if not args.preview:
        for job in textures:
            run_texture(spec, job, out_dir)
    if pieces:
        pipe = None if args.preview else load_controlnet_pipe(spec.get("model", "sdxl"))
        for job in pieces:
            run_piece(pipe, spec, job, out_dir, args.preview)
    return 0


if __name__ == "__main__":
    sys.exit(main())

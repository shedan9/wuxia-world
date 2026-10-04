"""同一人物的新姿势 / 新朝向（M3-02 行走帧与战斗关键姿势）：参照格局部重绘。

用法：
    .venv/Scripts/python figure_sheet.py jobs/m3_hero_frames.json [--only 名称前缀] [--preview]

单靠提示词与种子，同一人物换个姿势就换了脸、换了衣纹。这里把已入库形象的原图（灰底、832×1216）放在一张双格画布的
左格，右格按目标骨架（OpenPose，figure.py 同一套关节）以 ControlNet 引导、strength 1.0 重绘，只重绘右格：
模型在同一张图里照着左格的人物画右格，脸、发式、服饰与配色跟得住。
左格骨架画参照的原姿势、右格画目标姿势，两格骨架一起作引导；出图后裁出右格，按 figure.py 的方式抠图并记脚底。
--preview 只画双格骨架与遮罩，不加载模型。
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

from cutout import cutout
from figure import CANVAS, CONTROLNET, POSES, draw_pose, encode_long, foot_line
from generate import EULER_A, FP16_VAE, FP16_VARIANT, MODELS, build_prompt

ROOT = Path(__file__).resolve().parent

# 新姿势（背面、行走、战斗关键姿势）定义在 figure.POSES，入库脚本 place.py 也按它取脚底。


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_pipe(model_key: str):
    import torch
    from diffusers import AutoencoderKL, ControlNetModel, EulerAncestralDiscreteScheduler, StableDiffusionXLControlNetInpaintPipeline

    vae = AutoencoderKL.from_pretrained(FP16_VAE, torch_dtype=torch.float16)
    controlnet = ControlNetModel.from_pretrained(CONTROLNET, torch_dtype=torch.float16)
    pipe = StableDiffusionXLControlNetInpaintPipeline.from_pretrained(
        MODELS[model_key], vae=vae, controlnet=controlnet, torch_dtype=torch.float16,
        variant="fp16" if model_key in FP16_VARIANT else None, use_safetensors=True,
    )
    if model_key in EULER_A:
        pipe.scheduler = EulerAncestralDiscreteScheduler.from_config(pipe.scheduler.config)
    pipe.to("cuda")
    pipe.vae.enable_tiling()
    return pipe


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only")
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)
    w, h = CANVAS
    size = (w * 2, h)

    pipe = None
    for job in jobs:
        ref = Image.open(ROOT / job["reference"]).convert("RGB").resize(CANVAS)
        bg = tuple(int(v) for v in np.asarray(ref)[:8, :8].reshape(-1, 3).mean(0))
        canvas = Image.new("RGB", size, bg)
        canvas.paste(ref, (0, 0))
        # init 为 reference 时右格也先铺参照图（strength < 1 时配色、衣料从参照起步，姿势由骨架拉过去）。
        if job.get("init", spec.get("init")) == "reference":
            canvas.paste(ref, (w, 0))
        control = Image.new("RGB", size, (0, 0, 0))
        control.paste(draw_pose(job["reference_pose"], CANVAS), (0, 0))
        control.paste(draw_pose(job["pose"], CANVAS), (w, 0))
        mask = Image.new("L", size, 0)
        mask.paste(255, (w + 8, 0, size[0], h))
        mask = mask.filter(ImageFilter.GaussianBlur(4))
        stem_base = job["name"]
        control.save(out_dir / f"{stem_base}__control.png")
        if args.preview:
            preview = Image.blend(canvas, control, 0.5)
            preview.save(out_dir / f"{stem_base}__preview.png")
            print(stem_base, "preview")
            continue

        import torch

        pipe = pipe or load_pipe(spec.get("model", "animagine4"))
        positive, negative = build_prompt(spec["style"], job)
        embeds, segments = encode_long(pipe, positive, negative)
        for seed in job.get("seeds", spec.get("seeds", [1])):
            started = time.time()
            image = pipe(
                **embeds,
                image=canvas,
                mask_image=mask,
                control_image=control,
                width=size[0],
                height=size[1],
                strength=job.get("strength", spec.get("strength", 1.0)),
                controlnet_conditioning_scale=job.get("control", spec.get("control", 0.8)),
                control_guidance_end=job.get("control_end", spec.get("control_end", 0.8)),
                num_inference_steps=job.get("steps", spec.get("steps", 30)),
                guidance_scale=job.get("cfg", spec.get("cfg", 5.0)),
                generator=torch.Generator("cpu").manual_seed(seed),
            ).images[0]
            stem = f"{stem_base}_{seed}"
            image.save(out_dir / f"{stem}__sheet.png")
            raw = out_dir / f"{stem}__raw.png"
            image.crop((w, 0, size[0], h)).save(raw)
            path = out_dir / f"{stem}.png"
            cutout(raw, path, job.get("threshold", spec.get("threshold", 26)), 1.0)
            cut = json.loads(path.with_suffix(".json").read_text(encoding="utf-8"))
            meta = {
                "name": job["name"], "seed": seed, "pose": job["pose"], "reference": job["reference"],
                "reference_sha256": sha(ROOT / job["reference"]), "reference_pose": job["reference_pose"],
                "model": MODELS[spec.get("model", "animagine4")], "controlnet": CONTROLNET, "vae": FP16_VAE,
                "pipeline": "StableDiffusionXLControlNetInpaintPipeline，双格画布左格为参照、只重绘右格",
                "strength": job.get("strength", spec.get("strength", 1.0)),
                "init": job.get("init", spec.get("init", "background")),
                "control": job.get("control", spec.get("control", 0.8)),
                "control_end": job.get("control_end", spec.get("control_end", 0.8)),
                "steps": job.get("steps", spec.get("steps", 30)), "cfg": job.get("cfg", spec.get("cfg", 5.0)),
                "prompt_segments": segments, "prompt": positive, "negative": negative,
                "cutout": {k: cut[k] for k in ("key_rgb", "threshold", "feather")},
                "sha256": sha(path), **foot_line(path),
                "seconds": round(time.time() - started, 1), "gpu": torch.cuda.get_device_name(0),
            }
            path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"{path.name}  {meta['seconds']}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())

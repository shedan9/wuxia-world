"""按任务文件批量生成 AI 美术草图（SDXL）。

用法：
    .venv/Scripts/python generate.py jobs/m0_simple.json [--only 名称前缀] [--dry-run]

每张图输出到 out/<任务集>/<名称>_<种子>.png，并写同名 .json 记录模型、种子、
提示词与图像哈希，供资产台账（docs/art/ASSET_LEDGER.md）引用与复现。
选用的图片再由人工挑选、修整后放入 art_source/，本目录输出不入库。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent

# 模型均需允许商用；更换模型须同步 README 与资产台账。
MODELS = {
    "sdxl": "stabilityai/stable-diffusion-xl-base-1.0",  # CreativeML Open RAIL++-M
    # 赛璐璐/厚涂人物立绘（2026-09-24 用户改定画风）；同为 CreativeML Open RAIL++-M。
    "animagine4": "cagliostrolab/animagine-xl-4.0",
}
# 仓库带 fp16 变体文件的模型；其余按全精度文件读取后转 fp16。
FP16_VARIANT = {"sdxl"}
# 模型卡推荐 Euler Ancestral 采样。
EULER_A = {"animagine4"}
FP16_VAE = "madebyollin/sdxl-vae-fp16-fix"  # MIT


def load_pipeline(model_key: str):
    import torch
    from diffusers import AutoencoderKL, EulerAncestralDiscreteScheduler, StableDiffusionXLPipeline

    vae = AutoencoderKL.from_pretrained(FP16_VAE, torch_dtype=torch.float16)
    pipe = StableDiffusionXLPipeline.from_pretrained(
        MODELS[model_key],
        vae=vae,
        torch_dtype=torch.float16,
        variant="fp16" if model_key in FP16_VARIANT else None,
        use_safetensors=True,
    )
    if model_key in EULER_A:
        pipe.scheduler = EulerAncestralDiscreteScheduler.from_config(pipe.scheduler.config)
    vram_gb = torch.cuda.get_device_properties(0).total_memory / 2**30
    if vram_gb < 12:
        # 8GB 显卡（本机 RTX 4060）：按需搬运子模型，速度较慢但不爆显存。
        pipe.enable_model_cpu_offload()
    else:
        pipe.to("cuda")
    pipe.vae.enable_tiling()
    return pipe, vram_gb


def build_prompt(style: dict, job: dict) -> tuple[str, str]:
    parts = [job["prompt"], style.get(job.get("style", "default"), ""), style.get(job.get("extra", ""), "")]
    positive = ", ".join(p for p in parts if p)
    # 共享负面词已接近 77 token 上限；人物等需要专门排除项时用 negative_style 换一套预设。
    base_negative = style.get(job.get("negative_style", "negative"), "")
    negative = ", ".join(p for p in (base_negative, job.get("negative", "")) if p)
    return positive, negative


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only", help="只生成名称以此开头的任务")
    parser.add_argument("--dry-run", action="store_true", help="只打印提示词，不加载模型")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)

    if args.dry_run:
        # SDXL 文本编码器只读前 77 个 token，超出部分被静默截断。
        from transformers import CLIPTokenizer

        tokenizer = CLIPTokenizer.from_pretrained(MODELS[spec.get("model", "sdxl")], subfolder="tokenizer")
        over = 0
        for job in jobs:
            positive, negative = build_prompt(spec["style"], job)
            counts = [len(tokenizer(t).input_ids) for t in (positive, negative)]
            flag = "  超长！" if max(counts) > 77 else ""
            over += bool(flag)
            print(f"{job['name']}  正向 {counts[0]} / 负向 {counts[1]} token{flag}")
        return 1 if over else 0

    import torch

    pipe, vram_gb = load_pipeline(spec.get("model", "sdxl"))
    print(f"GPU {torch.cuda.get_device_name(0)} {vram_gb:.1f} GiB")
    if spec.get("tier") == "complex" and vram_gb < 12:
        print("提示：复杂资源约定在家用台式机（16 GB）生成；本机可跑但很慢，建议只做小批量试图。")

    img2img = None
    for job in jobs:
        positive, negative = build_prompt(spec["style"], job)
        width, height = job.get("size", spec.get("size", [1024, 1024]))
        # init_image：以已选图为底重绘（图生图），保留构图与脸，strength 越大改动越多。
        init = None
        if "init_image" in job:
            from diffusers import StableDiffusionXLImg2ImgPipeline
            from PIL import Image

            if img2img is None:
                # 共用子模型，不另占显存（同 inpaint.py 的约定）。
                img2img = StableDiffusionXLImg2ImgPipeline(**pipe.components)
            init_path = ROOT / job["init_image"]
            init = Image.open(init_path).convert("RGB").resize((width, height))
        for seed in job.get("seeds", spec.get("seeds", [1])):
            started = time.time()
            extra = {"image": init, "strength": job.get("strength", 0.6)} if init else {"width": width, "height": height}
            image = (img2img if init else pipe)(
                prompt=positive,
                negative_prompt=negative,
                **extra,
                num_inference_steps=job.get("steps", spec.get("steps", 30)),
                guidance_scale=job.get("cfg", spec.get("cfg", 6.0)),
                generator=torch.Generator("cpu").manual_seed(seed),
            ).images[0]
            path = out_dir / f"{job['name']}_{seed}.png"
            image.save(path)
            meta = {
                "name": job["name"],
                "seed": seed,
                "model": MODELS[spec.get("model", "sdxl")],
                "vae": FP16_VAE,
                "sampler": "euler_a" if spec.get("model", "sdxl") in EULER_A else "default",
                "size": [width, height],
                "steps": job.get("steps", spec.get("steps", 30)),
                "cfg": job.get("cfg", spec.get("cfg", 6.0)),
                "prompt": positive,
                "negative": negative,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                **(
                    {
                        "init_image": job["init_image"],
                        "init_sha256": hashlib.sha256(init_path.read_bytes()).hexdigest(),
                        "strength": job.get("strength", 0.6),
                    }
                    if init
                    else {}
                ),
                "seconds": round(time.time() - started, 1),
                "gpu": torch.cuda.get_device_name(0),
            }
            path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"{path.name}  {meta['seconds']}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())

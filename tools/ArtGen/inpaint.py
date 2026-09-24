"""对已生成的图做局部重绘（SDXL inpaint，沿用 generate.py 的同一模型，不新增权重）。

用法：
    .venv/Scripts/python inpaint.py jobs/m0_fix.json [--only 名称前缀] [--preview]

每个任务读取一张源图，按顺序做以下几步：
0. canvas（可选）：把源图缩放后放到更大的画布上，用于向外扩展（如半身补成全身），
   之后所有坐标都以新画布计；新增区域先填底色，再由 mask 覆盖以 strength 1.0 生成；
1. erase：用周围像素填平指定区域（去掉旧物件，如短篙、飘带）；
2. paint / tint：画入粗略新形状（如延长的船篙）或对区域调色（如晒肤）；
3. 以 mask 为范围、strength 为强度重绘，让新旧部分融为同一画风。
重绘结果只按羽化后的 mask 贴回原图，其余像素保持不变。

--preview 只输出预处理图与遮罩叠加图，不加载模型，用来核对坐标。
多步修整用多个任务串联：后一任务的 source 指向前一任务选定的输出。
每张输出旁有同名 .json：源图哈希、全部形状参数、提示词、种子与输出哈希。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

from generate import FP16_VAE, MODELS, load_pipeline

ROOT = Path(__file__).resolve().parent


def shape_mask(size: tuple[int, int], shapes: list[dict]) -> Image.Image:
    mask = Image.new("L", size, 0)
    d = ImageDraw.Draw(mask)
    for s in shapes:
        if "ellipse" in s:
            cx, cy, rx, ry = s["ellipse"]
            d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
        elif "rect" in s:
            d.rectangle(s["rect"], fill=255)
        elif "poly" in s:
            d.polygon([tuple(p) for p in s["poly"]], fill=255)
        elif "line" in s:
            d.line(s["line"], fill=255, width=s.get("width", 12))
        else:
            raise ValueError(f"未知形状：{s}")
    return mask


def _box_blur(a: np.ndarray, r: int) -> np.ndarray:
    """浮点方框模糊（积分图），避免 8 位量化在洞中心放大误差。"""
    pad = np.pad(a, [(r + 1, r), (r + 1, r)] + [(0, 0)] * (a.ndim - 2), mode="edge")
    c = pad.cumsum(0).cumsum(1)
    k = 2 * r + 1
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def fill_from_surroundings(img: np.ndarray, hole: np.ndarray, ignore: np.ndarray, radius: int = 5) -> np.ndarray:
    """用洞边已知像素逐层向内填平（无需 OpenCV），保留周围纸色的渐变；ignore 区（如手）不作为取色来源。"""
    out = img.astype(np.float64)
    known = (~hole & ~ignore).astype(np.float64)
    remaining = hole.copy()
    while remaining.any():
        weight = _box_blur(known, radius)
        acc = _box_blur(out * known[..., None], radius)
        ready = remaining & (weight > 0.25)
        if not ready.any():
            ready = remaining & (weight > 0)
        out[ready] = acc[ready] / weight[ready, None]
        known[ready] = 1
        remaining &= ~ready
    return out.clip(0, 255).astype(np.uint8)


def paint_strokes(img: Image.Image, strokes: list[dict]) -> None:
    d = ImageDraw.Draw(img)
    for s in strokes:
        x1, y1, x2, y2 = s["line"]
        w = s.get("width", 12)
        d.line(s["line"], fill=tuple(s["color"]), width=w)
        # 竹节：沿线等距画短横，颜色更深
        if "nodes" in s:
            length = ((x2 - x1) ** 2 + (y2 - y1) ** 2) ** 0.5
            nx, ny = -(y2 - y1) / length, (x2 - x1) / length
            t = s["nodes"]["every"] / length
            k = t
            while k < 1:
                px, py = x1 + (x2 - x1) * k, y1 + (y2 - y1) * k
                h = w / 2 + 1
                d.line([px - nx * h, py - ny * h, px + nx * h, py + ny * h], fill=tuple(s["nodes"]["color"]), width=3)
                k += t


def tint_region(img: np.ndarray, region: np.ndarray, rgb: list[float], min_luma: int, max_luma: int) -> np.ndarray:
    """只对区域内亮度落在皮肤区间的像素乘色：跳过头发、眼睛、墨线（过暗）和纸底（过亮）。"""
    luma = img.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    weight = region * (luma > min_luma) * (luma < max_luma)
    weight = np.asarray(Image.fromarray((weight * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3)), np.float32)[..., None] / 255
    tinted = img.astype(np.float32) * np.array(rgb, np.float32)
    return (img * (1 - weight) + tinted * weight).clip(0, 255).astype(np.uint8)


def prepare(job: dict) -> tuple[Image.Image, Image.Image, Image.Image]:
    """返回（预处理后的图、重绘遮罩、贴回用的羽化遮罩）。"""
    src = Image.open(ROOT / job["source"]).convert("RGB")
    if job.get("canvas"):
        c = job["canvas"]
        scaled = src.resize((round(src.width * c["scale"]), round(src.height * c["scale"])), Image.LANCZOS)
        # 底色默认取源图左上角，纯色底立绘可直接延续。
        fill = tuple(c.get("fill", src.getpixel((4, 4))))
        src = Image.new("RGB", tuple(c["size"]), fill)
        src.paste(scaled, tuple(c["offset"]))
    arr = np.asarray(src).copy()
    keep = np.asarray(shape_mask(src.size, job.get("keep", [])), bool)
    if job.get("erase"):
        hole = np.asarray(shape_mask(src.size, job["erase"]), bool) & ~keep
        arr = fill_from_surroundings(arr, hole, keep)
    if job.get("tint"):
        region = np.asarray(shape_mask(src.size, job["tint"]["shapes"]), np.float32) / 255
        arr = tint_region(arr, region, job["tint"]["rgb"], job["tint"].get("min_luma", 90), job["tint"].get("max_luma", 256))
    img = Image.fromarray(arr)
    if job.get("paint"):
        paint_strokes(img, job["paint"])
        # 保留区（如握篙的手）盖回原像素，新画的篙从手后穿过
        img = Image.composite(src, img, Image.fromarray(keep.astype(np.uint8) * 255))
    mask = ImageChops.subtract(shape_mask(src.size, job["mask"]), shape_mask(src.size, job.get("keep", [])))
    blend = mask.filter(ImageFilter.GaussianBlur(job.get("feather", 6)))
    return img, mask, blend


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only", help="只处理名称以此开头的任务")
    parser.add_argument("--preview", action="store_true", help="只输出预处理图和遮罩叠加，不加载模型")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)

    if args.preview:
        for job in jobs:
            img, mask, _ = prepare(job)
            img.save(out_dir / f"{job['name']}_prepared.png")
            overlay = Image.composite(Image.new("RGB", img.size, (255, 0, 80)), img, mask.point(lambda v: v * 0.45))
            overlay.save(out_dir / f"{job['name']}_mask.png")
            print(f"{job['name']}  预处理图与遮罩已输出")
        return 0

    import torch
    from diffusers import StableDiffusionXLInpaintPipeline

    base, _ = load_pipeline(spec.get("model", "sdxl"))
    # 直接共用子模型；diffusers 0.40 的 from_pipe 会复制一份权重，16 GB 显存溢出到共享内存后慢 30 倍以上。
    pipe = StableDiffusionXLInpaintPipeline(**base.components)
    del base
    gpu = torch.cuda.get_device_name(0)

    for job in jobs:
        img, mask, blend = prepare(job)
        src_path = ROOT / job["source"]
        for seed in job.get("seeds", spec.get("seeds", [1])):
            started = time.time()
            result = pipe(
                prompt=job["prompt"],
                negative_prompt=job.get("negative", spec.get("negative", "")),
                image=img,
                mask_image=mask,
                width=img.width,
                height=img.height,
                strength=job["strength"],
                num_inference_steps=job.get("steps", spec.get("steps", 40)),
                guidance_scale=job.get("cfg", spec.get("cfg", 6.0)),
                padding_mask_crop=job.get("padding_mask_crop"),
                generator=torch.Generator("cpu").manual_seed(seed),
            ).images[0]
            final = Image.composite(result.resize(img.size), img, blend)
            path = out_dir / f"{job['name']}_{seed}.png"
            final.save(path)
            meta = {
                "name": job["name"],
                "seed": seed,
                "model": MODELS[spec.get("model", "sdxl")],
                "vae": FP16_VAE,
                "pipeline": "StableDiffusionXLInpaintPipeline",
                "source": job["source"],
                "source_sha256": hashlib.sha256(src_path.read_bytes()).hexdigest(),
                "job": job,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "seconds": round(time.time() - started, 1),
                "gpu": gpu,
            }
            path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"{path.name}  {meta['seconds']}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())

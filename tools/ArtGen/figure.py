"""按统一骨架姿势生成全身人物（探索与战斗形象，架构文档 10.2）。

用法：
    .venv/Scripts/python figure.py jobs/m0_figures.json [--only 名称前缀] [--preview]

同一姿势的所有人物用同一副 OpenPose 骨架作 ControlNet 引导（xinsir/controlnet-openpose-sdxl-1.0，Apache-2.0），
保证身高、站位与朝向一致，引擎可按统一比例缩放。骨架画的是鼻尖偏右的四分之三身，出图为人物朝画面右侧（右肩在画面左、靠近观者）；
朝左由引擎水平翻转（第一阶段允许）。
流程：
1. 按任务的 pose 画骨架图（关节坐标以 832×1216 画布为准，按实际尺寸缩放）；
2. Animagine XL 4.0 文生图，纯灰底；
3. cutout.py 从边框按底色连通抠图，输出 RGBA 与同名 .json（模型、种子、提示词、骨架哈希、脚底位置）。
--preview 只画骨架图，不加载模型。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from cutout import cutout
from generate import EULER_A, FP16_VAE, FP16_VARIANT, MODELS, build_prompt

ROOT = Path(__file__).resolve().parent
CONTROLNET = "xinsir/controlnet-openpose-sdxl-1.0"  # Apache-2.0
CANVAS = (832, 1216)

# OpenPose COCO-18 关节：0 鼻 1 颈 2 右肩 3 右肘 4 右腕 5 左肩 6 左肘 7 左腕 8 右胯 9 右膝 10 右踝
# 11 左胯 12 左膝 13 左踝 14 右眼 15 左眼 16 右耳 17 左耳（“右”为人物自身右侧）。
LIMBS = [(1, 2), (1, 5), (2, 3), (3, 4), (5, 6), (6, 7), (1, 8), (8, 9), (9, 10), (1, 11), (11, 12), (12, 13),
         (1, 0), (0, 14), (14, 16), (0, 15), (15, 17)]
COLORS = [(255, 0, 0), (255, 85, 0), (255, 170, 0), (255, 255, 0), (170, 255, 0), (85, 255, 0), (0, 255, 0),
          (0, 255, 85), (0, 255, 170), (0, 255, 255), (0, 170, 255), (0, 85, 255), (0, 0, 255), (85, 0, 255),
          (170, 0, 255), (255, 0, 255), (255, 0, 170), (255, 0, 85)]

# 姿势：四分之三侧身（右肩在画面左、靠近观者），出图为人物朝画面右侧。缺省的关节（被挡住的左耳）为 None。
# 脚底约在 y = 1110，头顶约在 y = 90，身高约 7 头。
POSES: dict[str, list[tuple[float, float] | None]] = {
    # 站立：双臂自然下垂。
    "stand": [(452, 190), (410, 272), (348, 284), (334, 432), (340, 574), (468, 280), (482, 428), (486, 566),
              (382, 580), (374, 822), (368, 1072), (448, 578), (458, 818), (474, 1066),
              (432, 172), (466, 174), (392, 180), None],
    # 持篙站立：远侧（左）手在胸前握住竖起的长篙，近侧手下垂。
    "stand_pole": [(452, 190), (410, 272), (348, 284), (334, 432), (340, 574), (468, 280), (530, 396), (548, 300),
                   (382, 580), (374, 822), (368, 1072), (448, 578), (458, 818), (474, 1066),
                   (432, 172), (466, 174), (392, 180), None],
    # 迎敌架势：两脚前后分开、膝微屈，双手在身前持械。
    "guard": [(468, 214), (420, 292), (358, 304), (390, 432), (478, 470), (476, 298), (540, 404), (560, 468),
              (388, 596), (318, 812), (262, 1068), (458, 592), (548, 800), (590, 1062),
              (450, 196), (484, 198), (406, 204), None],
    # 坐姿：坐在长凳上，大腿朝前平伸、小腿垂地，双手搁在身前桌面高度。
    "sit": [(470, 430), (420, 512), (360, 522), (358, 668), (452, 720), (478, 520), (520, 660), (578, 700),
            (388, 816), (600, 826), (606, 1080), (446, 812), (650, 818), (660, 1076),
            (450, 412), (484, 414), (410, 420), None],
    # ── M3-02 行走帧与战斗关键姿势（figure_sheet.py 以入库形象为参照格生成）。地面统一在 y ≈ 1070。
    # 背面站立：人物背对镜头、朝画面右上；近观者的是左肩（画面左），看不到脸（鼻、眼不画，只画两耳）。
    "back_stand": [None, (420, 272), (474, 282), (488, 430), (482, 572), (356, 284), (342, 432), (348, 574),
                   (446, 580), (452, 822), (462, 1070), (384, 582), (378, 822), (372, 1068),
                   None, None, (452, 186), (392, 184)],
    # 背面行走：前进方向为画面右上，领先的脚落点偏右偏上、后脚偏左偏下。a 近侧（左）腿领先，b 远侧（右）腿领先；手臂与腿反向摆。
    "back_walk_a": [None, (420, 272), (474, 282), (500, 424), (520, 552), (356, 284), (330, 430), (310, 560),
                    (446, 580), (420, 826), (380, 1078), (384, 582), (432, 808), (476, 1046),
                    None, None, (452, 186), (392, 184)],
    "back_walk_b": [None, (420, 272), (474, 282), (470, 432), (456, 560), (356, 284), (372, 424), (400, 552),
                    (446, 580), (490, 808), (530, 1046), (384, 582), (360, 826), (320, 1078),
                    None, None, (452, 186), (392, 184)],
    # 正面行走（四分之三正面朝画面右侧）：a 远侧（左）腿向前着地、近侧右臂前摆；b 近侧（右）腿向前、远侧左臂前摆。
    "walk_front_a": [(456, 192), (414, 272), (352, 284), (360, 430), (400, 560), (472, 280), (468, 426), (440, 556),
                     (386, 580), (352, 814), (316, 1040), (452, 578), (500, 812), (548, 1062),
                     (436, 174), (470, 176), (396, 182), None],
    "walk_front_b": [(456, 192), (414, 272), (352, 284), (322, 428), (300, 560), (472, 280), (500, 424), (526, 552),
                     (386, 580), (430, 814), (476, 1064), (452, 578), (420, 812), (384, 1040),
                     (436, 174), (470, 176), (396, 182), None],
    # 战斗·蓄势：两脚大开、重心在后腿，近侧手收在腰间蓄力，远侧手前伸护在身前。
    "windup": [(440, 212), (400, 292), (338, 304), (300, 420), (330, 520), (462, 300), (540, 360), (600, 330),
               (380, 600), (330, 820), (280, 1066), (450, 598), (530, 800), (580, 1064),
               (420, 196), (452, 198), (380, 204), None],
    # 战斗·出掌：前弓步冲出，近侧手掌平推向前，远侧手收回腰间，上身前倾。
    "strike": [(520, 228), (470, 300), (412, 306), (520, 340), (640, 330), (520, 308), (500, 420), (470, 480),
               (400, 612), (300, 830), (200, 1060), (470, 610), (580, 800), (620, 1066),
               (500, 210), (532, 214), (462, 214), None],
    # 战斗·受击：上身后仰、头被打得后甩，两臂向外甩开，脚下仍站着。
    "hit": [(390, 214), (360, 300), (300, 316), (250, 420), (220, 520), (418, 300), (480, 360), (540, 330),
            (380, 600), (340, 830), (290, 1068), (446, 596), (480, 820), (520, 1064),
            (366, 200), (398, 194), (334, 224), None],
    # 战斗·跪倒：后膝着地，前腿屈膝撑地，低头弓背，近侧手撑在膝上、远侧手撑地。
    "down": [(500, 520), (450, 470), (390, 480), (400, 640), (430, 770), (500, 480), (560, 640), (600, 1000),
             (380, 760), (330, 1040), (230, 1070), (450, 760), (540, 860), (540, 1068),
             (480, 500), (512, 506), (450, 486), None],
}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def draw_pose(name: str, size: tuple[int, int]) -> Image.Image:
    """按 controlnet_aux 的 OpenPose 画法：半透明椭圆肢段 + 彩色关节点，黑底。"""
    points = POSES[name]
    kx, ky = size[0] / CANVAS[0], size[1] / CANVAS[1]
    pts = [None if p is None else (p[0] * kx, p[1] * ky) for p in points]
    stick = 10 * kx
    canvas = np.zeros((size[1], size[0], 3), dtype=np.float32)
    for i, (a, b) in enumerate(LIMBS):
        if pts[a] is None or pts[b] is None:
            continue
        (x0, y0), (x1, y1) = pts[a], pts[b]
        layer = Image.new("L", size, 0)
        length = math.hypot(x1 - x0, y1 - y0)
        angle = math.atan2(y1 - y0, x1 - x0)
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        ellipse = [(cx + math.cos(angle) * length / 2 * math.cos(t) - math.sin(angle) * stick * math.sin(t),
                    cy + math.sin(angle) * length / 2 * math.cos(t) + math.cos(angle) * stick * math.sin(t))
                   for t in np.linspace(0, math.tau, 48)]
        ImageDraw.Draw(layer).polygon(ellipse, fill=255)
        mask = np.asarray(layer, dtype=np.float32)[..., None] / 255
        canvas = canvas * (1 - mask) + np.array(COLORS[i], dtype=np.float32) * 0.6 * mask
    image = Image.fromarray(canvas.astype(np.uint8))
    draw = ImageDraw.Draw(image)
    r = 10 * kx
    for i, p in enumerate(pts):
        if p is not None:
            draw.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=COLORS[i])
    return image


def load_pipe(model_key: str):
    import torch
    from diffusers import AutoencoderKL, ControlNetModel, EulerAncestralDiscreteScheduler, StableDiffusionXLControlNetPipeline

    vae = AutoencoderKL.from_pretrained(FP16_VAE, torch_dtype=torch.float16)
    controlnet = ControlNetModel.from_pretrained(CONTROLNET, torch_dtype=torch.float16)
    pipe = StableDiffusionXLControlNetPipeline.from_pretrained(
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


def encode_long(pipe, positive: str, negative: str):
    """长提示词分段编码：按逗号把提示词切成每段不超过 75 token 的若干段，两个文本编码器逐段编码后沿序列拼接
    （正负提示词补齐到同样段数），池化向量取第一段。人物服装描述常超过 SDXL 的 77 token 上限，直接传入会被静默截断。"""
    import torch

    def chunks(text: str) -> list[str]:
        out, cur = [], []
        for tag in [t.strip() for t in text.split(",") if t.strip()]:
            trial = ", ".join(cur + [tag])
            if cur and len(pipe.tokenizer(trial).input_ids) - 2 > 75:
                out.append(", ".join(cur))
                cur = [tag]
            else:
                cur.append(tag)
        out.append(", ".join(cur))
        return out

    def encode(parts: list[str]):
        embeds, pooled = [], None
        for part in parts:
            hidden = []
            for tokenizer, encoder in ((pipe.tokenizer, pipe.text_encoder), (pipe.tokenizer_2, pipe.text_encoder_2)):
                ids = tokenizer(part, padding="max_length", max_length=77, truncation=True, return_tensors="pt").input_ids.to(pipe.device)
                out = encoder(ids, output_hidden_states=True)
                hidden.append(out.hidden_states[-2])
                if encoder is pipe.text_encoder_2 and pooled is None:
                    pooled = out[0]
            embeds.append(torch.cat(hidden, dim=-1))
        return torch.cat(embeds, dim=1), pooled

    pos, neg = chunks(positive), chunks(negative)
    n = max(len(pos), len(neg))
    pos += [""] * (n - len(pos))
    neg += [""] * (n - len(neg))
    with torch.no_grad():
        pe, pp = encode(pos)
        ne, np_ = encode(neg)
    return {"prompt_embeds": pe, "pooled_prompt_embeds": pp, "negative_prompt_embeds": ne, "negative_pooled_prompt_embeds": np_}, n


def foot_line(rgba: Path) -> dict:
    """抠图后的实际外框与脚底：引擎按脚底中点对齐地面、按外框高度折算身高。"""
    a = np.asarray(Image.open(rgba).getchannel("A")) > 128
    ys, xs = np.nonzero(a)
    top, bottom, left, right = int(ys.min()), int(ys.max()), int(xs.min()), int(xs.max())
    # 脚底中点取最底部 3% 高度内不透明像素的水平中位数（长篙、刀鞘拖地时会偏，另可在任务里给 foot_x）。
    band = ys >= bottom - (bottom - top) * 0.03
    return {"bbox": [left, top, right, bottom], "foot": [int(np.median(xs[band])), bottom]}


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("jobfile")
    parser.add_argument("--only", help="只生成名称以此开头的任务")
    parser.add_argument("--preview", action="store_true", help="只画骨架图，不加载模型")
    args = parser.parse_args()

    spec = json.loads(Path(args.jobfile).read_text(encoding="utf-8"))
    jobs = [j for j in spec["jobs"] if not args.only or j["name"].startswith(args.only)]
    out_dir = ROOT / "out" / spec["set"]
    out_dir.mkdir(parents=True, exist_ok=True)
    size = tuple(spec.get("size", CANVAS))

    poses = {}
    for pose in sorted({j.get("pose", "stand") for j in jobs}):
        path = out_dir / f"pose_{pose}.png"
        draw_pose(pose, size).save(path)
        poses[pose] = path
        print(path.name)
    if args.preview:
        return 0

    import torch

    pipe = load_pipe(spec.get("model", "animagine4"))
    for job in jobs:
        positive, negative = build_prompt(spec["style"], job)
        pose = job.get("pose", "stand")
        control = Image.open(poses[pose]).convert("RGB")
        for seed in job.get("seeds", spec.get("seeds", [1])):
            started = time.time()
            params = {
                "controlnet_conditioning_scale": job.get("control", spec.get("control", 0.8)),
                "control_guidance_end": job.get("control_end", spec.get("control_end", 0.8)),
                "steps": job.get("steps", spec.get("steps", 28)),
                "cfg": job.get("cfg", spec.get("cfg", 5.0)),
            }
            embeds, segments = encode_long(pipe, positive, negative)
            params["prompt_segments"] = segments
            image = pipe(
                **embeds,
                image=control,
                width=size[0],
                height=size[1],
                controlnet_conditioning_scale=params["controlnet_conditioning_scale"],
                control_guidance_end=params["control_guidance_end"],
                num_inference_steps=params["steps"],
                guidance_scale=params["cfg"],
                generator=torch.Generator("cpu").manual_seed(seed),
            ).images[0]
            stem = f"{job['name']}_{seed}"
            raw = out_dir / f"{stem}__raw.png"
            image.save(raw)
            path = out_dir / f"{stem}.png"
            cutout(raw, path, job.get("threshold", spec.get("threshold", 26)), 1.0)
            cut_record = json.loads(path.with_suffix(".json").read_text(encoding="utf-8"))
            meta = {
                "name": job["name"],
                "seed": seed,
                "pose": pose,
                "pose_sha256": sha(poses[pose]),
                "model": MODELS[spec.get("model", "animagine4")],
                "controlnet": CONTROLNET,
                "vae": FP16_VAE,
                "size": list(size),
                **params,
                "prompt": positive,
                "negative": negative,
                "raw_sha256": sha(raw),
                "cutout": {k: cut_record[k] for k in ("key_rgb", "threshold", "feather")},
                "sha256": sha(path),
                **foot_line(path),
                "seconds": round(time.time() - started, 1),
                "gpu": torch.cuda.get_device_name(0),
            }
            path.with_suffix(".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"{path.name}  {meta['seconds']}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())

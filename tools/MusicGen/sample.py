"""按任务文件用本地 ACE-Step 1.5 生成 BGM 样带。

用 ACE-Step 自带的虚拟环境运行（它不在本仓库内，默认位置 C:/code/ACE-Step-1.5，
可用环境变量 ACESTEP_HOME 改）：

    C:/code/ACE-Step-1.5/.venv/Scripts/python tools/MusicGen/sample.py tools/MusicGen/jobs/m1_bgm_samples.json
    ... --only bgm.town.luwan --seeds 11 22     # 只生成一首、指定种子
    ... --dry-run                                # 只列出将生成的条目

输出写到 build/music/samples/<任务名>/（不入库）：每个种子一个 WAV、
manifest.json（模型、参数、种子、时长、SHA-256、耗时）与试听页 index.html。
参数与种子都未变且文件还在的条目沿用旧结果，不重复生成。
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import shutil
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ACESTEP_HOME = Path(os.environ.get("ACESTEP_HOME", "C:/code/ACE-Step-1.5"))
SAMPLES = REPO / "build" / "music" / "samples"
RUNTIME: dict = {}  # 本次运行的显存分档、卸载与量化，写进清单


def track_params(job: dict, track: dict) -> dict:
    """合并任务默认值与单曲参数，得到决定生成结果的全部参数。"""
    merged = {**job.get("defaults", {}), **track}
    return {
        "model": job["model"],
        "lm": job.get("lm"),
        "caption": merged["caption"],
        "lyrics": merged.get("structure", "[Instrumental]"),
        "bpm": merged.get("bpm"),
        "keyscale": merged.get("keyscale", ""),
        "timesignature": merged.get("timesignature", ""),
        "duration": merged["duration"],
        "inference_steps": merged.get("inference_steps", 8),
        "shift": merged.get("shift", 3.0),
        "guidance_scale": merged.get("guidance_scale", 7.0),
        "lm_temperature": merged.get("lm_temperature", 0.85),
        "thinking": merged.get("thinking", True),
        # 沿用另一条结果的 LM 音乐编码（旋律与结构），只换 DiT 重新渲染；路径相对 build/music/samples
        "codes": merged.get("codes"),
        # cover：以已有音频为底（相对 build/music/samples）重新渲染，保留结构与旋律
        "task": merged.get("task", "text2music"),
        "src_audio": merged.get("src_audio"),
        "cover_strength": merged.get("cover_strength", 1.0),
    }


def params_hash(params: dict, seed: int) -> str:
    keyed = {**params, "seed": seed}
    if params.get("codes"):
        keyed["codes"] = hashlib.sha256(read_codes(params["codes"]).encode("utf-8")).hexdigest()
    if params.get("src_audio"):
        keyed["src_audio"] = file_sha256(SAMPLES / params["src_audio"])
    text = json.dumps(keyed, ensure_ascii=False, sort_keys=True)
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def read_codes(rel: str) -> str:
    return (SAMPLES / rel).read_text(encoding="utf-8").strip()


def file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_models(job: dict):
    sys.path.insert(0, str(ACESTEP_HOME))
    from acestep.gpu_config import get_gpu_config
    from acestep.handler import AceStepHandler
    from acestep.llm_inference import LLMHandler

    # 默认按 ACE-Step 的显存分档取卸载与量化：16 GB 下 XL 不卸载、不量化时峰值约 18.5 GB，
    # 溢出到共享内存后每步扩散慢到约 31 秒；XL 开 INT8 时加载 19 GB 权重又会耗尽 32 GB 内存
    # （2026-10-01 实测）。任务文件可用 offload_to_cpu / quantization 覆盖，2B 模型不必量化。
    gpu = get_gpu_config()
    offload = job.get("offload_to_cpu", gpu.offload_to_cpu_default)
    quant = job["quantization"] if "quantization" in job else ("int8_weight_only" if gpu.quantization_default else None)
    print(f"显存分档 {gpu.tier}：卸载到 CPU={offload}，DiT 量化={quant}")
    RUNTIME.update(tier=gpu.tier, offload_to_cpu=offload, quantization=quant)

    dit = AceStepHandler()
    msg, ok = dit.initialize_service(
        project_root=str(ACESTEP_HOME), config_path=job["model"], device="auto",
        offload_to_cpu=offload, offload_dit_to_cpu=gpu.offload_dit_to_cpu_default, quantization=quant,
    )
    if not ok:
        raise SystemExit(f"DiT 初始化失败：{msg}")
    print(f"DiT：{msg}")

    llm = LLMHandler()
    if job.get("lm"):
        msg, ok = llm.initialize(
            checkpoint_dir=str(ACESTEP_HOME / "checkpoints"),
            lm_model_path=job["lm"],
            backend=job.get("lm_backend", "pt"),
            device="auto",
            offload_to_cpu=offload,
        )
        if not ok:
            raise SystemExit(f"LM 初始化失败：{msg}")
        print(f"LM：{msg}")
    return dit, llm


def generate_one(dit, llm, params: dict, seed: int, tmp_dir: Path) -> tuple[Path, dict]:
    from acestep.inference import GenerationConfig, GenerationParams, generate_music

    gp = GenerationParams(
        task_type=params["task"],
        src_audio=str(SAMPLES / params["src_audio"]) if params.get("src_audio") else None,
        audio_cover_strength=params["cover_strength"],
        caption=params["caption"],
        lyrics=params["lyrics"],
        instrumental=True,
        vocal_language="unknown",
        bpm=params["bpm"],
        keyscale=params["keyscale"],
        timesignature=params["timesignature"],
        duration=params["duration"],
        inference_steps=params["inference_steps"],
        shift=params["shift"],
        guidance_scale=params["guidance_scale"],
        seed=seed,
        audio_codes=read_codes(params["codes"]) if params.get("codes") else "",
        thinking=params["thinking"],
        lm_temperature=params["lm_temperature"],
        # 保留我们写的描述与节拍、调式，不让 LM 改写
        use_cot_caption=False,
        use_cot_metas=False,
        use_cot_language=False,
    )
    cfg = GenerationConfig(batch_size=1, use_random_seed=False, seeds=[seed], audio_format="wav")
    result = generate_music(dit, llm, gp, cfg, save_dir=str(tmp_dir))
    if not result.success:
        raise RuntimeError(result.error or result.status_message)
    audio = result.audios[0]
    codes = (audio.get("params") or {}).get("audio_codes") or ""
    if isinstance(codes, list):
        codes = codes[0] if codes else ""
    return Path(audio["path"]), {"sample_rate": audio.get("sample_rate"), "codes": codes}


def write_index(out_dir: Path, job: dict, manifest: dict) -> None:
    parts = [
        "<!doctype html><meta charset='utf-8'><title>%s</title>" % html.escape(job["name"]),
        "<style>body{font-family:sans-serif;max-width:960px;margin:24px auto;padding:0 16px;background:#f6f1e4;color:#2a2822}"
        "h2{margin-top:32px}.row{display:flex;gap:12px;align-items:center;margin:6px 0}"
        ".row span{width:260px}audio{flex:1}p.c{font-size:13px;color:#5e5546}</style>",
        "<h1>%s</h1><p>%s</p>" % (html.escape(job["name"]), html.escape(job.get("note", ""))),
        "<p>模型：%s　LM：%s</p>" % (html.escape(job["model"]), html.escape(str(job.get("lm")))),
    ]
    for track in job["tracks"]:
        entries = [e for e in manifest["entries"].values() if e["track"] == track["id"]]
        entries.sort(key=lambda e: e["seed"])
        parts.append("<h2>%s <small>%s</small></h2>" % (html.escape(track["title"]), html.escape(track["id"])))
        parts.append("<p class='c'>%s BPM · %s · %s 秒<br>%s</p>" % (
            track.get("bpm"), html.escape(track.get("keyscale", "")), track["duration"], html.escape(track["caption"])))
        for e in entries:
            model = "XL" if "-xl-" in e["params"]["model"] else "2B"
            src = e["params"].get("src_audio") or e["params"].get("codes")
            label = "%s · 以 %s 为底 · 种子 %d" % (model, Path(src).stem, e["seed"]) if src else "%s · 种子 %d" % (model, e["seed"])
            parts.append("<div class='row'><span>%s</span><audio controls preload='none' src='%s'></audio></div>"
                         % (html.escape(label), html.escape(e["file"])))
    (out_dir / "index.html").write_text("\n".join(parts), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("job")
    ap.add_argument("--only", nargs="*", help="只生成这些曲目 id")
    ap.add_argument("--seeds", nargs="*", type=int, help="覆盖任务文件里的种子")
    ap.add_argument("--force", action="store_true", help="已有结果也重新生成（旧文件改名为 .prev.wav，用于核对可复现）")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    job = json.loads(Path(args.job).read_text(encoding="utf-8"))
    out_dir = SAMPLES / job["name"]
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = out_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {"entries": {}}

    todo = []
    for track in job["tracks"]:
        if args.only and track["id"] not in args.only:
            continue
        params = track_params(job, track)
        seeds = args.seeds or {**job.get("defaults", {}), **track}["seeds"]
        for seed in seeds:
            key = f"{track['id']}@{seed}"
            h = params_hash(params, seed)
            old = manifest["entries"].get(key)
            if old and old["params_hash"] == h and (out_dir / old["file"]).exists() and not args.force:
                continue
            todo.append((track, params, seed, key, h))

    print(f"待生成 {len(todo)} 条：")
    for track, params, seed, _, _ in todo:
        print(f"  {track['id']}  种子 {seed}  {params['duration']} 秒")
    if args.dry_run or not todo:
        write_index(out_dir, job, manifest)
        return

    dit, llm = load_models(job)
    tmp_dir = out_dir / "_tmp"
    for track, params, seed, key, h in todo:
        t0 = time.time()
        src, extra = generate_one(dit, llm, params, seed, tmp_dir)
        name = f"{track['id']}_{seed}.wav"
        old = manifest["entries"].get(key)
        if (out_dir / name).exists():
            shutil.move(str(out_dir / name), out_dir / name.replace(".wav", ".prev.wav"))
        shutil.move(str(src), out_dir / name)
        elapsed = time.time() - t0
        sha = file_sha256(out_dir / name)
        if old and old["params_hash"] == h:
            print(f"  与上次结果{'一致' if old['sha256'] == sha else '不一致'}")
        codes_file = None
        if extra["codes"]:
            codes_file = name.replace(".wav", ".codes.txt")
            (out_dir / codes_file).write_text(extra["codes"], encoding="utf-8")
        manifest["entries"][key] = {
            "track": track["id"], "seed": seed, "file": name, "codes_file": codes_file, "params_hash": h, "params": params,
            "sha256": sha, "elapsed_s": round(elapsed, 1),
            "sample_rate": extra["sample_rate"], "runtime": dict(RUNTIME), "review": "candidate",
            "generated": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        write_index(out_dir, job, manifest)
        print(f"完成 {name}，用时 {elapsed:.0f} 秒")
    shutil.rmtree(tmp_dir, ignore_errors=True)
    print(f"试听页：{out_dir / 'index.html'}")


if __name__ == "__main__":
    main()

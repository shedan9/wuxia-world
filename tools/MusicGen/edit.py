"""按剪辑任务修改已选中的 BGM：段落替换、局部重渲染音色（cover 加噪）与段落重画（repaint）。

    C:/code/ACE-Step-1.5/.venv/Scripts/python tools/MusicGen/edit.py tools/MusicGen/jobs/m1_bgm_edit.json
    ... --only bgm.battle.common --dry-run      # 只打印对齐到拍点后的时间

路径相对 build/music（以 samples/ 或 edits/ 开头），旧任务里不带前缀的路径按 build/music/samples 解析。
任务文件每个 edit（按 splice → rerender → repaint 的顺序作用于底稿）：
  base       底稿
  splice     [{from, start, end, src_start?, inner?}]：用 from 的 [start, end) 替换底稿同一时间段；两稿须同一节拍网格。
             给 src_start 时改取 from 自该时刻起的同长度段落（与 start 相差整数拍）；inner 时淡化落在区间内
  rerender   [{start, end, inner?}]：整首以底稿加噪为起点由 XL 重新渲染（cover，保留音符与节奏、换音色质感），
             只取这些区间换回；同一候选的各区间取自同一次渲染，音色前后一致。
             rerender_noise 列出加噪保留度（cover_noise_strength，越大越接近原稿），rerender_cover_strength 列出结构跟随度
             （audio_cover_strength），两者组合 × 每个种子各出一版。cover 只能换音色质感、换不了乐器：保留度 ≥ 0.4 时
             描述词几乎不起作用，≤ 0.2 时乐器会变但音高也跟着变（2026-10-02 实测）
  flow_edit  {source_caption, n_max: [...], n_min?, steps?, guidance?}：rerender 改用 flow-edit（须 XL sft / base 模型），
             把原稿按“原稿描述 → 目标描述（caption）”改编，n_max 越小改动越少、越保留原结构
  repaint    [{start, end, caption?, strength?, inner?}]：用 XL 重画该区间（区间内从噪声重新生成，旋律会变），
             区间外由 ACE-Step 拼回原波形
  snap       true 时时间点对齐到拍点（节拍由 grid_from 或底稿的起音包络估出；底稿含重画段时用原稿估更准）；inner 为 true 时区间只向内对齐，不越过两侧须保留的段落
  seeds      候选种子；有 splice 时另出一版“仅交叉淡化”的拼接稿
ACE-Step 输出按峰值归一化；改动区以外与底稿逐采样比较求出增益并还原（还原后会削波则不还原，清单里注明）。
输出到 build/music/edits/<任务名>/：WAV、manifest.json（实际时间点、种子、增益、SHA-256）与试听页。
"""

from __future__ import annotations

import argparse
import html
import json
import os
import shutil
import time
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import stft

from sample import REPO, RUNTIME, SAMPLES, file_sha256, load_models

MUSIC = REPO / "build" / "music"
EDITS = MUSIC / "edits"
FRAME_HOP = 480  # 48 kHz 下 100 帧 / 秒


def resolve(rel: str) -> Path:
    return MUSIC / rel if rel.split("/")[0] in ("samples", "edits") else SAMPLES / rel


def beat_grid(path: Path, bpm_hint: float | None) -> tuple[float, float]:
    """由起音包络的自相关估节拍周期，再用梳状求和找相位。返回 (周期秒, 相位秒)。"""
    x, sr = sf.read(path)
    mono = x.mean(axis=1) if x.ndim > 1 else x
    _, _, z = stft(mono, sr, nperseg=2048, noverlap=2048 - FRAME_HOP)
    spec = np.log1p(np.abs(z))
    env = np.maximum(np.diff(spec, axis=1), 0).sum(axis=0)
    env = (env - env.mean()) / env.std()
    fr = sr / FRAME_HOP
    lo, hi = (bpm_hint * 0.9, bpm_hint * 1.1) if bpm_hint else (60, 200)
    ac = np.correlate(env, env, "full")[len(env) - 1:]
    lags = np.arange(int(fr * 60 / hi), int(fr * 60 / lo) + 1)
    k = lags[np.argmax(ac[lags])]
    a, b, c = ac[k - 1], ac[k], ac[k + 1]
    period = (k + 0.5 * (a - c) / (a - 2 * b + c)) / fr
    best = (-np.inf, 0.0)
    for ph in np.linspace(0, period, 200, endpoint=False):
        idx = (np.arange(ph, len(env) / fr, period) * fr).astype(int)
        best = max(best, (env[idx[idx < len(env)]].sum(), ph))
    return period, best[1]


def snap(t: float, grid: tuple[float, float] | None, duration: float, how: str = "nearest") -> float:
    """对齐到拍点。how 为 ceil / floor 时只向后 / 向前取，用于区间不越过须原样保留的段落。"""
    if grid is None or t <= 0 or t >= duration:
        return round(t, 3)
    period, phase = grid
    x = (t - phase) / period
    eps = 0.01  # 已在拍点上（如上一轮对齐过的时间）时不因浮点误差多退一拍
    n = np.ceil(x - eps) if how == "ceil" else np.floor(x + eps) if how == "floor" else np.round(x)
    return round(phase + n * period, 3)


def snap_ranges(ranges: list[dict], grid, duration: float) -> list[dict]:
    out = []
    for r in ranges:
        inner = r.get("inner")
        out.append({**r, "start": snap(r["start"], grid, duration, "ceil" if inner else "nearest"),
                    "end": min(snap(r["end"], grid, duration, "floor" if inner else "nearest"), duration)})
    return out


def blend(dst: np.ndarray, src: np.ndarray, s0: int, s1: int, fade: int, inside: bool) -> np.ndarray:
    """把 src 的 [s0, s1) 换进 dst，两端等功率交叉淡化。inside 时淡化落在区间内（不碰区间外），否则跨在边界上。"""
    out = dst.copy()
    out[s0:s1] = src[s0:s1]
    t = np.linspace(0, np.pi / 2, fade)[:, None]
    edges = ((s0, s0 + fade), (s1 - fade, s1)) if inside else ((s0 - fade // 2, s0 - fade // 2 + fade),
                                                                (s1 - fade // 2, s1 - fade // 2 + fade))
    for i, (a, b) in enumerate(edges):
        if a < 0 or b > len(out):
            continue
        g_in, g_out = (np.sin(t), np.cos(t)) if i == 0 else (np.cos(t), np.sin(t))
        out[a:b] = dst[a:b] * g_out + src[a:b] * g_in
    return out


def run_acestep(dit, llm, edit: dict, seed: int, tmp: Path, **task) -> Path:
    from acestep.inference import GenerationConfig, GenerationParams, generate_music

    fields = dict(
        caption=edit["caption"],
        lyrics=edit.get("structure", "[Instrumental]"),
        instrumental=True,
        bpm=edit.get("bpm"),
        keyscale=edit.get("keyscale", ""),
        timesignature=edit.get("timesignature", "4"),
        duration=-1,
        inference_steps=edit.get("inference_steps", 8),
        shift=edit.get("shift", 3.0),
        seed=seed,
        thinking=False,
    )
    gp = GenerationParams(**{**fields, **task})
    cfg = GenerationConfig(batch_size=1, use_random_seed=False, seeds=[seed], audio_format="wav")
    result = generate_music(dit, llm, gp, cfg, save_dir=str(tmp))
    if not result.success:
        raise RuntimeError(result.error or result.status_message)
    return Path(result.audios[0]["path"])


def restore_gain(out: np.ndarray, ref: np.ndarray, changed: list[tuple[float, float]], sr: int) -> tuple[np.ndarray, float, bool]:
    """用改动区以外（各留 0.5 秒余量）求 ACE-Step 归一化增益并还原；还原后削波则不还原。"""
    mask = np.ones(len(ref), bool)
    for a, b in changed:
        mask[max(int((a - 0.5) * sr), 0):int((b + 0.5) * sr)] = False
    r, o = ref[mask].ravel(), out[mask].ravel()
    g = float(np.dot(r, o) / np.dot(r, r)) if mask.any() else 1.0
    restored = out / g
    if np.abs(restored).max() > 0.999:
        return out, g, False
    return restored, g, True


def write_index(out_dir: Path, job: dict, manifest: dict) -> None:
    rows = ["<!doctype html><meta charset='utf-8'><title>%s</title>" % html.escape(job["name"]),
            "<style>body{font-family:sans-serif;max-width:960px;margin:24px auto;padding:0 16px;background:#f6f1e4;color:#2a2822}"
            "h2{margin-top:32px}.row{display:flex;gap:12px;align-items:center;margin:6px 0}.row span{width:320px}"
            "audio{flex:1}p.c{font-size:13px;color:#5e5546}</style>",
            "<h1>%s</h1><p>%s</p>" % (html.escape(job["name"]), html.escape(job.get("note", "")))]
    for edit in job["edits"]:
        info = manifest["edits"].get(edit["id"])
        if not info:
            continue
        rows.append("<h2>%s <small>%s</small></h2><p class='c'>%s</p>" % (
            html.escape(edit["title"]), html.escape(edit["id"]), html.escape(info["summary"])))
        for label, rel in info["refs"]:
            rows.append("<div class='row'><span>原稿 · %s</span><audio controls preload='none' src='%s'></audio></div>"
                        % (html.escape(label), html.escape(rel)))
        for c in info["candidates"]:
            rows.append("<div class='row'><span>%s</span><audio controls preload='none' src='%s'></audio></div>"
                        % (html.escape(c["label"]), html.escape(c["file"])))
    (out_dir / "index.html").write_text("\n".join(rows), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("job")
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--seeds", nargs="*", type=int, help="覆盖任务文件里的种子")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    job = json.loads(Path(args.job).read_text(encoding="utf-8"))
    out_dir = EDITS / job["name"]
    out_dir.mkdir(parents=True, exist_ok=True)
    tmp = out_dir / "_tmp"
    manifest_path = out_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {"edits": {}}

    plans = []
    for edit in (e for e in job["edits"] if not args.only or e["id"] in args.only):
        base = resolve(edit["base"])
        duration = sf.info(base).duration
        grid = beat_grid(resolve(edit.get("grid_from", edit["base"])), edit.get("bpm")) if edit.get("snap") else None
        if grid:
            print(f"{edit['id']}：节拍 {60 / grid[0]:.2f} BPM，相位 {grid[1]:.3f} 秒")
        splices = snap_ranges(edit.get("splice", []), grid, duration)
        for sp in splices:  # 来源起点按整拍平移：与目标起点相差整数拍，节拍不错位
            if "src_start" in sp and grid:
                sp["src_start"] = round(sp["start"] + round((sp["src_start"] - sp["start"]) / grid[0]) * grid[0], 3)
        rerenders = snap_ranges(edit.get("rerender", []), grid, duration)
        repaints = snap_ranges(edit.get("repaint", []), grid, duration)
        for s in splices:
            src = f" 的 {s['src_start']:.3f} 秒起" if "src_start" in s else ""
            print(f"  替换 {s['start']:.3f}–{s['end']:.3f} 秒 ← {s['from']}{src}")
        for r in rerenders:
            print(f"  重渲染音色 {r['start']:.3f}–{r['end']:.3f} 秒")
        for r in repaints:
            print(f"  重画 {r['start']:.3f}–{r['end']:.3f} 秒")
        plans.append((edit, base, grid, splices, rerenders, repaints))
    if args.dry_run:
        return

    dit = llm = None
    if any(p[4] or p[5] for p in plans):
        dit, llm = load_models(job)

    for edit, base, grid, splices, rerenders, repaints in plans:
        audio, sr = sf.read(base)
        refs = [("底稿", os.path.relpath(base, out_dir).replace("\\", "/"))]
        for s in splices:
            ins, sr2 = sf.read(resolve(s["from"]))
            assert sr2 == sr and ins.shape[1:] == audio.shape[1:], "替换稿须与底稿同采样率、同声道"
            s0, s1 = int(s["start"] * sr), int(s["end"] * sr)
            if "src_start" in s:  # 从来源的另一时间取同长度段落，平移到 [start, end)
                src0 = int(s["src_start"] * sr)
                shifted = audio.copy()
                shifted[s0:s1] = ins[src0:src0 + (s1 - s0)]
                ins = shifted
            else:
                assert ins.shape == audio.shape, "同一时间轴替换须与底稿同长度"
            audio = blend(audio, ins, s0, s1, int(sr * edit.get("splice_fade", 0.03)), bool(s.get("inner")))
            refs.append(("替换来源", os.path.relpath(resolve(s["from"]), out_dir).replace("\\", "/")))
        candidates = []
        work = base
        if splices:
            work = out_dir / f"{edit['id']}_spliced.wav"
            sf.write(work, audio, sr, subtype="FLOAT")
            candidates.append({"label": "仅交叉淡化拼接", "file": work.name, "sha256": file_sha256(work)})

        fe = edit.get("flow_edit")
        if rerenders and fe:
            variants = [{"tag": f"_fe{n}", "label": f"XL flow-edit 换乐器 · 改动幅度 {n}", "n_max": n,
                         "task": dict(task_type="text2music", flow_edit_morph=True,
                                      flow_edit_source_caption=fe["source_caption"],
                                      flow_edit_n_min=fe.get("n_min", 0.0), flow_edit_n_max=n,
                                      flow_edit_n_avg=fe.get("n_avg", 1), inference_steps=fe.get("steps", 50),
                                      guidance_scale=fe.get("guidance", 7.0), shift=fe.get("shift", 1.0))}
                        for n in fe["n_max"]]
        elif rerenders:
            noises = edit.get("rerender_noise", [0.7])
            strengths = edit.get("rerender_cover_strength", [edit.get("cover_strength", 1.0)])
            variants = [{"tag": f"_n{n}" + (f"_c{c}" if len(strengths) > 1 else ""), "noise": n, "cover_strength": c,
                         "label": "XL 重渲染音色 · 保留度 %.2f · 结构跟随 %.1f" % (n, c),
                         "task": dict(task_type="cover", audio_cover_strength=c, cover_noise_strength=n)}
                        for n in noises for c in strengths]
        else:
            variants = [{"tag": "", "label": "XL 重画", "task": None}]
        changed = [(r["start"], r["end"]) for r in rerenders + repaints]
        for seed in (args.seeds or edit.get("seeds", [])) if (rerenders or repaints) else []:
            for v in variants:
                t0 = time.time()
                current, ref = work, sf.read(work)[0]
                if rerenders:
                    cov_path = run_acestep(dit, llm, edit, seed, tmp, src_audio=str(current), **v["task"])
                    cov, _ = sf.read(cov_path)
                    cov = cov[:len(ref)] * (np.sqrt(np.mean(ref ** 2)) / np.sqrt(np.mean(cov[:len(ref)] ** 2)))
                    mixed = ref
                    fade = int(sr * edit.get("rerender_fade", 0.08))
                    for r in rerenders:
                        mixed = blend(mixed, cov, int(r["start"] * sr), int(r["end"] * sr), fade, True)
                    tmp.mkdir(parents=True, exist_ok=True)
                    current = tmp / f"rerender_{seed}{v['tag']}.wav"
                    sf.write(current, mixed, sr, subtype="FLOAT")
                for r in repaints:
                    current = run_acestep(dit, llm, edit, seed, tmp, task_type="repaint", src_audio=str(current),
                                          repainting_start=r["start"], repainting_end=r["end"], chunk_mask_mode="explicit",
                                          repaint_mode=r.get("mode", "balanced"), repaint_strength=r.get("strength", 0.5),
                                          repaint_wav_crossfade_sec=r.get("wav_crossfade", 0.05),
                                          caption=r.get("caption", edit["caption"]))
                final, _ = sf.read(current)
                final, gain, restored = restore_gain(final[:len(ref)], ref, changed, sr)
                for r in repaints:  # inner 区间：ACE-Step 的边界淡化会伸出区间约 25 毫秒，收回区间内，区间外强制取底稿
                    if r.get("inner"):
                        final = blend(ref, final, int(r["start"] * sr), int(r["end"] * sr), int(sr * 0.025), True)
                name = f"{edit['id']}_s{seed}{v['tag']}.wav"
                sf.write(out_dir / name, final, sr, subtype="FLOAT")
                candidates.append({"label": f"{v['label']} · 种子 {seed}", "file": name, "seed": seed,
                                   **{k: v[k] for k in ("noise", "cover_strength", "n_max") if k in v},
                                   "gain": round(gain, 4), "gain_restored": restored,
                                   "sha256": file_sha256(out_dir / name), "elapsed_s": round(time.time() - t0, 1)})
                print(f"完成 {name}（归一化增益 {gain:.3f}，{'已还原' if restored else '还原会削波，未还原'}）")

        parts = [f"替换 {s['start']:.2f}–{s['end']:.2f} 秒" for s in splices]
        parts += [f"重渲染音色 {r['start']:.2f}–{r['end']:.2f} 秒" for r in rerenders]
        parts += [f"重画 {r['start']:.2f}–{r['end']:.2f} 秒" for r in repaints]
        manifest["edits"][edit["id"]] = {
            "base": edit["base"], "splices": splices, "rerenders": rerenders, "repaints": repaints,
            "beat": {"period_s": grid[0], "phase_s": grid[1]} if grid else None,
            "summary": "；".join(parts) + ("（时间已对齐拍点）" if grid else ""),
            "refs": refs, "candidates": candidates, "runtime": dict(RUNTIME),
            "generated": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        write_index(out_dir, job, manifest)
    shutil.rmtree(tmp, ignore_errors=True)
    print(f"试听页：{out_dir / 'index.html'}")


if __name__ == "__main__":
    main()

"""把 audio_source/music 的 BGM 母带做成游戏运行时用的循环 Ogg。

对每首曲目：
1. 估出尾部淡出段，循环终点不进淡出；
2. 在开头 2–24 秒内逐 10 ms 搜索循环起点，循环长度取整数小节（按母带记录的 BPM），
   比较起点与终点前后各 1.5 秒的频谱（对数幅度、逐帧余弦相似度），取最像的一对；
3. 终点前 0.25 秒与起点前 0.25 秒做等功率交叉淡化，播放到文件末尾跳回起点时没有断口；
4. 按 BS.1770 整体响度统一到目标值（默认 −18 LUFS），峰值超过 −1 dBFS 时整体压低；
5. 写 game/assets/audio/music/<id>.ogg 与 music_manifest.json（循环起点、响度、接缝相似度，供游戏读取与复查）。

用法（在仓库根目录，用 ACE-Step 的虚拟环境，需 numpy / scipy / soundfile）：
    C:/code/ACE-Step-1.5/.venv/Scripts/python tools/AudioBuild/bgm_runtime.py [--only bgm.town.luwan] [--target -18]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import stft

sys.path.insert(0, str(Path(__file__).parent))
from loudness import integrated, peak_db  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "audio_source" / "music"
OUT = ROOT / "game" / "assets" / "audio" / "music"

# 母带记录里只有芦湾一首写了 BPM；两首战斗曲出自同一任务的普通战斗条目（148 BPM）。
TRACKS = {
    "bgm.town.luwan": {"bpm": 88, "title": "芦湾水镇·探索"},
    "bgm.battle.common": {"bpm": 148, "title": "普通战斗"},
    "bgm.boss.old_ferry": {"bpm": 148, "title": "旧渡首领战"},
}

HOP = 480  # 10 ms @ 48 kHz
XFADE = 0.25


def spectrogram(mono: np.ndarray, sr: int) -> np.ndarray:
    _, _, z = stft(mono, fs=sr, nperseg=2048, noverlap=2048 - HOP, boundary=None, padded=False)
    mag = np.log1p(np.abs(z[:256]))  # 0–6 kHz 足够比较和声与节奏
    return mag.T  # (帧, 频点)


def fade_tail(mono: np.ndarray, sr: int) -> float:
    """尾部淡出 / 余音开始的时刻：最后一次 RMS 高于中位数 −9 dB 的位置。"""
    win = sr // 10
    rms = np.sqrt(np.convolve(mono**2, np.ones(win) / win, mode="same") + 1e-12)
    db = 20 * np.log10(rms)
    loud = np.where(db > np.median(db) - 9)[0]
    return loud[-1] / sr if len(loud) else len(mono) / sr


def window(spec: np.ndarray, t: float, length: float) -> np.ndarray:
    a = int(t * 100)
    return spec[a : a + int(length * 100)]


def similarity(spec: np.ndarray, s: float, e: float) -> float:
    # 起点 / 终点前后各 1.5 秒逐帧余弦相似度的平均。
    total = 0.0
    for offset in (-1.5, 0.0):
        a = window(spec, s + offset, 1.5)
        b = window(spec, e + offset, 1.5)
        n = min(len(a), len(b))
        a, b = a[:n], b[:n]
        num = (a * b).sum(axis=1)
        den = np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1) + 1e-9
        total += float((num / den).mean())
    return total / 2


def find_loop(x: np.ndarray, sr: int, bpm: float) -> tuple[float, float, float]:
    mono = x.mean(axis=1)
    spec = spectrogram(mono, sr)
    duration = len(mono) / sr
    tail = min(fade_tail(mono, sr), duration - 2.0)
    bar = 4 * 60 / bpm
    best = (-1.0, 0.0, 0.0)
    raw = 0.0
    for s in np.arange(2.0, 24.0, 0.01):
        k_max = int((tail - 1.6 - s) // bar)
        for k in range(k_max, 0, -1):
            if s + k * bar < s + 0.55 * (tail - s):
                break
            # 生成曲的速度不一定严格等于标称 BPM：循环长度在整数小节附近 ±60 ms 内微调。
            for d in np.arange(-0.06, 0.061, 0.01):
                e = s + k * bar + d
                if e > tail - 1.6:
                    continue
                sim = similarity(spec, s, e)
                score = sim + 0.002 * k  # 同样像时偏好更长的循环
                if score > best[0]:
                    best, raw = (score, float(s), float(e)), sim
    return best[1], best[2], raw


def write_ogg(dst: Path, data: np.ndarray, sr: int) -> None:
    """分块写 Ogg Vorbis：libsndfile 一次写入几百万帧会直接崩溃退出，分成小块写就没事。"""
    with sf.SoundFile(dst, "w", sr, data.shape[1], format="OGG", subtype="VORBIS") as f:
        for i in range(0, len(data), 8192):
            f.write(data[i : i + 8192].astype(np.float32))


def build(track: str, meta: dict, target: float) -> dict:
    src = SRC / f"{track}.wav"
    x, sr = sf.read(src, always_2d=True, dtype="float64")
    start, end, score = find_loop(x, sr, meta["bpm"])
    s, e, xf = int(start * sr), int(end * sr), int(XFADE * sr)
    out = x[:e].copy()
    t = np.linspace(0, np.pi / 2, xf)[:, None]
    out[-xf:] = x[e - xf : e] * np.cos(t) + x[s - xf : s] * np.sin(t)

    before = integrated(out, sr)
    gain = target - before
    out *= 10 ** (gain / 20)
    if peak_db(out) > -1.0:
        out *= 10 ** ((-1.0 - peak_db(out)) / 20)
    after = integrated(out, sr)

    OUT.mkdir(parents=True, exist_ok=True)
    dst = OUT / f"{track}.ogg"
    write_ogg(dst, out, sr)
    seam = float(np.abs(out[-1] - x[s - 1] * 10 ** (gain / 20)).max()) if s > 0 else 0.0
    return {
        "file": dst.name,
        "title": meta["title"],
        "loop_offset": round(start, 4),
        "loop_end": round(end, 4),
        "loop_bars": round((end - start) / (4 * 60 / meta["bpm"]), 2),
        "seam_similarity": round(score, 4),
        "seam_jump": round(seam, 5),
        "lufs_source": round(before, 2),
        "lufs": round(after, 2),
        "peak_db": round(peak_db(out), 2),
        "source": f"audio_source/music/{track}.wav",
        "source_sha256": hashlib.sha256(src.read_bytes()).hexdigest(),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--target", type=float, default=-18.0)
    args = ap.parse_args()
    manifest_path = OUT / "music_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {"tracks": {}}
    manifest["note"] = "tools/AudioBuild/bgm_runtime.py 生成：循环起点 loop_offset（秒），播放到文件末尾跳回；响度按 BS.1770 统一。"
    manifest["target_lufs"] = args.target
    for track, meta in TRACKS.items():
        if args.only and track != args.only:
            continue
        entry = build(track, meta, args.target)
        manifest["tracks"][track] = entry
        print(f"{track}: 循环 {entry['loop_offset']:.2f}–{entry['loop_end']:.2f} 秒（{entry['loop_bars']} 小节），"
              f"接缝相似度 {entry['seam_similarity']}，响度 {entry['lufs_source']} → {entry['lufs']} LUFS，峰值 {entry['peak_db']} dB")
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

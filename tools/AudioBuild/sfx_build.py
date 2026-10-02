"""把 Freesound CC0 实录素材按配方剪成游戏音效与环境声（替换 sfx_synth.py 的程序合成版）。

配方在 tools/AudioBuild/sfx_recipes.json：
  sfx.<id>        短音效。variants>1 时输出 <id>.1 … <id>.N，游戏随机挑一个（SoundDirector 已支持）。
                  from: 素材列表，每项 {id, start?, end?, split?, pitch?, gain?}
                    split: 按起音切出单个事件（脚步、击打录音里一段多下），取最整齐的几下
                  layer: 叠在主体上的层（同一格式，加 at 秒偏移），例如击中声叠一层布料
                  hp / lp: 高通 / 低通（Hz）；max: 最长秒数；level: 目标峰值 dBFS
  ambience.<id>   环境声：从素材取一段或多段混合（立体声），做首尾交叉淡化的循环，按 LUFS 定响度。
                  layers: [{id, start, len, gain, hp, lp}]；lufs；seconds

素材缓存在 audio_source/sfx/freesound/（freesound.py fetch 下载，sources.json 记作者与授权）。
输出 game/assets/audio/sfx|amb/*.ogg 与 sfx_manifest.json（来源 ID、作者、时长、峰值 / 响度）。
用法：C:/code/ACE-Step-1.5/.venv/Scripts/python tools/AudioBuild/sfx_build.py [--only 前缀]
处理确定性：不含随机数，重跑同一配方逐采样一致。
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import butter, resample_poly, sosfiltfilt

sys.path.insert(0, str(Path(__file__).parent))
from bgm_runtime import write_ogg  # noqa: E402
from loudness import integrated, peak_db  # noqa: E402

SR = 48000
ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "audio_source" / "sfx" / "freesound"
OUT = ROOT / "game" / "assets" / "audio"
RECIPES = Path(__file__).parent / "sfx_recipes.json"


# ── 读取与基本处理 ────────────────────────────────────────


_cache: dict[int, np.ndarray] = {}


def source(sid: int) -> np.ndarray:
    """(n, 2) 立体声 48 kHz；单声道素材复制成两声道。"""
    if sid not in _cache:
        x, sr = sf.read(str(CACHE / f"{sid}.ogg"), always_2d=True)
        if x.shape[1] == 1:
            x = np.repeat(x, 2, axis=1)
        x = x[:, :2]
        if sr != SR:
            g = np.gcd(sr, SR)
            x = resample_poly(x, SR // g, sr // g, axis=0)
        _cache[sid] = x.astype(np.float64)
    return _cache[sid]


def filt(x: np.ndarray, hp: float | None, lp: float | None) -> np.ndarray:
    if hp:
        x = sosfiltfilt(butter(2, hp, btype="high", fs=SR, output="sos"), x, axis=0)
    if lp:
        x = sosfiltfilt(butter(2, lp, btype="low", fs=SR, output="sos"), x, axis=0)
    return x


def pitch(x: np.ndarray, semitones: float) -> np.ndarray:
    """变速变调（像磁带快慢放）：升调变短，降调变长、更沉。"""
    if not semitones:
        return x
    ratio = 2 ** (semitones / 12)
    up, down = 1000, round(1000 * ratio)
    return resample_poly(x, up, down, axis=0)


def envelope(x: np.ndarray, win: float = 0.005) -> np.ndarray:
    m = np.abs(x).max(axis=1)
    k = max(1, int(win * SR))
    return np.convolve(m, np.ones(k) / k, mode="same")


def trim(x: np.ndarray, floor_db: float = -42, pre: float = 0.004, tail: float = 0.03) -> np.ndarray:
    """去掉首尾静音：首个超过 峰值+floor_db 的点前留 pre 秒，末个之后留 tail 秒并淡出。"""
    env = envelope(x)
    thr = env.max() * 10 ** (floor_db / 20)
    idx = np.where(env > thr)[0]
    if len(idx) == 0:
        return x
    a = max(0, idx[0] - int(pre * SR))
    b = min(len(x), idx[-1] + int(tail * SR))
    return x[a:b]


def fades(x: np.ndarray, fin: float = 0.002, fout: float = 0.03) -> np.ndarray:
    x = x.copy()
    a, b = min(len(x) // 4, int(fin * SR)), min(len(x) // 3, int(fout * SR))
    if a:
        x[:a] *= np.linspace(0, 1, a)[:, None]
    if b:
        x[-b:] *= (np.linspace(1, 0, b) ** 2)[:, None]
    return x


def onsets(x: np.ndarray, min_gap: float, rel_db: float = -18) -> list[int]:
    """简单起音检测：包络从低处跃过阈值、且离上一个至少 min_gap 秒。"""
    env = envelope(x, 0.004)
    thr = env.max() * 10 ** (rel_db / 20)
    low = env.max() * 10 ** ((rel_db - 10) / 20)
    out, armed = [], True
    for i, v in enumerate(env):
        if armed and v > thr and (not out or i - out[-1] > min_gap * SR):
            out.append(i)
            armed = False
        elif v < low:
            armed = True
    return out


def split_events(x: np.ndarray, spec: dict) -> list[np.ndarray]:
    """把一段多下的录音切成单个事件，按峰值排序后取中间那部分（太响太轻都去掉，变体更整齐）。"""
    gap = spec.get("gap", 0.25)
    length = spec.get("len", 0.4)
    pts = onsets(x, gap, spec.get("rel_db", -18))
    events = []
    for i, p in enumerate(pts):
        a = max(0, p - int(0.01 * SR))
        b = min(len(x), p + int(length * SR), pts[i + 1] - int(0.01 * SR) if i + 1 < len(pts) else len(x))
        if b - a > 0.06 * SR:
            events.append(x[a:b])
    if not events:
        return []
    peaks = np.array([np.abs(e).max() for e in events])
    med = np.median(peaks)
    keep = [e for e, p in zip(events, peaks) if 0.5 * med <= p <= 2.0 * med]
    return keep


def take(item: dict) -> list[np.ndarray]:
    x = source(item["id"])
    a = int(item.get("start", 0) * SR)
    b = int(item["end"] * SR) if "end" in item else len(x)
    x = x[a:b]
    x = pitch(x, item.get("pitch", 0))
    x = x * 10 ** (item.get("gain", 0) / 20)
    if item.get("split"):
        return split_events(x, item["split"])
    return [x]


def level(x: np.ndarray, peak: float) -> np.ndarray:
    return x * (10 ** (peak / 20) / (np.abs(x).max() + 1e-9))


def build_sfx(name: str, r: dict) -> list[tuple[str, np.ndarray, list[int]]]:
    pool: list[tuple[np.ndarray, int]] = []
    for item in r["from"]:
        pool += [(e, item["id"]) for e in take(item)]
    n = r.get("variants", 1)
    if len(pool) < n:
        raise SystemExit(f"{name}: 只切出 {len(pool)} 个事件，不够 {n} 个变体")
    # 从池里均匀挑 n 个（素材间交错，避免变体都来自同一段）。
    picks = [pool[round(i * (len(pool) - 1) / max(1, n - 1))] for i in range(n)] if n > 1 else [pool[0]]
    out = []
    for i, (x, sid) in enumerate(picks):
        ids = [sid]
        x = filt(x, r.get("hp"), r.get("lp"))
        x = trim(x, r.get("floor_db", -42), tail=r.get("tail", 0.03))
        if r.get("max"):
            x = x[: int(r["max"] * SR)]
        for lay in r.get("layer", []):
            y = take(lay)[lay.get("pick", 0) % max(1, len(take(lay)))]
            y = trim(filt(y, lay.get("hp"), lay.get("lp")))
            y = level(y, r.get("level", -3) + lay.get("rel", -8))
            at = int(lay.get("at", 0) * SR)
            n_ = max(len(x), at + len(y))
            z = np.zeros((n_, 2))
            z[: len(x)] += level(x, r.get("level", -3))
            z[at: at + len(y)] += y
            x = z
            ids.append(lay["id"])
        x = fades(level(x, r.get("level", -3)), fout=r.get("fade", 0.03))
        mono = r.get("mono", True)
        y = x.mean(axis=1, keepdims=True) if mono else x
        y = level(y, r.get("level", -3))
        out.append((f"{name}.{i + 1}" if n > 1 else name, y, ids))
    return out


def loopify(x: np.ndarray, fade: float) -> np.ndarray:
    f = int(fade * SR)
    body = x[:-f].copy()
    w = np.linspace(0, np.pi / 2, f)[:, None]
    body[:f] = body[:f] * np.sin(w) + x[-f:] * np.cos(w)
    return body


def build_ambience(name: str, r: dict) -> tuple[np.ndarray, list[int]]:
    seconds = r.get("seconds", 60)
    fade = r.get("fade", 3.0)
    n = int((seconds + fade) * SR)
    mix = np.zeros((n, 2))
    ids = []
    for lay in r["layers"]:
        x = source(lay["id"])
        a = int(lay.get("start", 0) * SR)
        seg = x[a: a + n]
        if len(seg) < n:
            # 素材不够长：首尾交叉接一遍。
            reps = int(np.ceil(n / max(1, len(seg)))) + 1
            seg = np.concatenate([seg] * reps)[:n]
        seg = filt(seg, lay.get("hp"), lay.get("lp"))
        if lay.get("mono_width") is not None:
            mid = seg.mean(axis=1, keepdims=True)
            seg = mid + (seg - mid) * lay["mono_width"]
        lufs = integrated(seg, SR)
        seg = seg * 10 ** ((lay.get("lufs", -30) - lufs) / 20)
        mix += seg
        ids.append(lay["id"])
    mix = loopify(mix, fade)
    mix = mix * 10 ** ((r["lufs"] - integrated(mix, SR)) / 20)
    if peak_db(mix) > -1:
        mix = level(mix, -1)
    return mix, ids


def credit(ids: list[int], sources: dict) -> list[dict]:
    seen, out = set(), []
    for i in ids:
        if i in seen:
            continue
        seen.add(i)
        s = sources[str(i)]
        out.append({"freesound": i, "title": s["title"], "author": s["user"], "license": s["license"], "url": s["url"]})
    return out


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    recipes = json.loads(RECIPES.read_text(encoding="utf-8"))
    sources = json.loads((CACHE / "sources.json").read_text(encoding="utf-8"))
    manifest_path = OUT / "sfx" / "sfx_manifest.json"
    old = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    manifest = {
        "note": "tools/AudioBuild/sfx_build.py 按 sfx_recipes.json 从 Freesound CC0 实录剪辑（freesound.py 下载，来源见各条 sources）。clap 字段为 sfx_check.py 零样本听辨结果，未经人耳审听。",
        "sfx": {} if not args.only else old.get("sfx", {}),
        "ambience": {} if not args.only else old.get("ambience", {}),
    }

    for name, r in recipes["sfx"].items():
        if args.only and not name.startswith(args.only):
            continue
        # 清掉旧变体文件，避免变体数减少后游戏仍随机到旧文件。
        for f in [*(OUT / "sfx").glob(f"{name}.*.ogg"), OUT / "sfx" / f"{name}.ogg"]:
            if f.exists() and (f.stem == name or f.stem[len(name) + 1:].isdigit()):
                f.unlink()
                Path(str(f) + ".import").unlink(missing_ok=True)
        for key in [k for k in manifest["sfx"] if k == name or (k.startswith(name + ".") and k[len(name) + 1:].isdigit())]:
            del manifest["sfx"][key]
        for out_name, y, ids in build_sfx(name, r):
            write_ogg(OUT / "sfx" / f"{out_name}.ogg", y, SR)
            manifest["sfx"][out_name] = {"file": f"{out_name}.ogg", "seconds": round(len(y) / SR, 3), "peak_db": round(peak_db(y), 1), "sources": credit(ids, sources)}
        print(f"{name}: {r.get('variants', 1)} 个")

    (OUT / "amb").mkdir(parents=True, exist_ok=True)
    for name, r in recipes["ambience"].items():
        if args.only and not name.startswith(args.only):
            continue
        x, ids = build_ambience(name, r)
        write_ogg(OUT / "amb" / f"{name}.ogg", x, SR)
        manifest["ambience"][name] = {"file": f"{name}.ogg", "seconds": round(len(x) / SR, 2), "lufs": round(integrated(x, SR), 1), "sources": credit(ids, sources)}
        print(f"{name}: {len(x) / SR:.1f} 秒，{integrated(x, SR):.1f} LUFS")

    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

"""定点衰减一个持续音：逐帧跟踪基频，只压该音的基频与泛音，其余声音不动。

    C:/code/ACE-Step-1.5/.venv/Scripts/python tools/MusicGen/note_gate.py <输入.wav> <输出.wav> \
        --f0 560 630 --start 53.15 --end 54.55 --mode remove      # 整个音删掉
        --mode swell --full-at 54.0                                 # 不删，从 start 渐强到 full-at 恢复原音量

只处理 [start - 0.3, end + 0.3] 一段，再在增益为 1 的边缘等功率淡化接回原波形，处理段以外逐采样不变。
同目录写 <输出>.json 记录参数与输入、输出的 SHA-256。用于 AI 重画会连带改动其他声音、只需去掉或收敛一个音的场合。
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import istft, stft


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--f0", nargs=2, type=float, required=True, help="基频搜索范围 Hz")
    ap.add_argument("--start", type=float, required=True)
    ap.add_argument("--end", type=float, required=True)
    ap.add_argument("--mode", choices=["remove", "swell"], default="remove")
    ap.add_argument("--full-at", type=float, help="swell 模式恢复原音量的时刻")
    ap.add_argument("--depth", type=float, default=-30.0, help="最大衰减 dB")
    ap.add_argument("--harmonics", type=int, default=8)
    ap.add_argument("--ramp", type=float, default=0.06, help="remove 模式起止的增益过渡秒数")
    ap.add_argument("--fill", action="store_true",
                    help="频谱修补：被压的频点不挖空，而是削到左右相邻频点的背景电平（保留相位），避免删音后留下空洞")
    args = ap.parse_args()

    x, sr = sf.read(args.src)
    pad = 0.3
    a, b = int((args.start - pad) * sr), int((args.end + pad) * sr)
    seg = x[a:b]
    nper, hop = 4096, 256
    f, t, z = stft(seg.T, sr, nperseg=nper, noverlap=nper - hop)  # z: [声道, 频率, 帧]
    t = t + args.start - pad
    power = (np.abs(z) ** 2).sum(axis=0)
    band = (f >= args.f0[0]) & (f <= args.f0[1])
    f0 = f[band][np.argmax(power[band], axis=0)]  # 逐帧基频

    # 时间增益曲线（dB）：remove 在 [start, end] 压到 depth，两端 ramp 过渡；swell 自 start 的 depth 线性升到 full-at 的 0
    g_db = np.zeros_like(t)
    if args.mode == "remove":
        up = np.clip((t - args.start) / args.ramp, 0, 1) * np.clip((args.end - t) / args.ramp, 0, 1)
        g_db = args.depth * up
    else:
        full = args.full_at
        g_db = np.where(t < args.start, 0.0, np.where(t >= full, 0.0, args.depth * (1 - (t - args.start) / (full - args.start))))
        lead = np.clip((t - (args.start - args.ramp)) / args.ramp, 0, 1)  # 进入 depth 前的短过渡
        g_db = np.where(t < args.start, args.depth * lead, g_db)

    mask = np.ones((len(f), len(t)))
    mag = np.sqrt(power)  # 两声道合计的幅度，用于估背景电平
    for k in range(len(t)):
        if g_db[k] == 0:
            continue
        gain = 10 ** (g_db[k] / 20)
        notch = np.zeros(len(f), bool)
        for h in range(1, args.harmonics + 1):
            notch |= np.abs(f - h * f0[k]) <= max(18.0, 0.03 * h * f0[k])
        if not args.fill:
            mask[notch, k] = gain
            continue
        # 每个泛音周围 ±3 倍宽度内、不在凹口里的频点取中位数作背景；凹口内削到背景（不低于背景），
        # 再按时间曲线在原幅度与修补后幅度之间插值：gain 为 depth 时完全修补，为 1 时不动
        for h in range(1, args.harmonics + 1):
            centre, width = h * f0[k], max(18.0, 0.03 * h * f0[k])
            sel = np.abs(f - centre) <= width
            near = (np.abs(f - centre) <= 3 * width) & ~notch
            if not near.any():
                continue
            floor = np.median(mag[near, k])
            target = np.minimum(1.0, floor / np.maximum(mag[sel, k], 1e-12))
            amount = 1 - gain  # 0 = 不动，接近 1 = 完全修补
            mask[sel, k] = np.minimum(mask[sel, k], 1 - amount * (1 - target))
    _, y = istft(z * mask[None], sr, nperseg=nper, noverlap=nper - hop)
    y = y.T[: len(seg)]

    # 处理段两端增益为 1：等功率淡化接回原波形，段外逐采样不变
    fade = int(0.1 * sr)
    w = np.ones(len(seg))
    w[:fade] = np.sin(np.linspace(0, np.pi / 2, fade)) ** 2
    w[-fade:] = np.cos(np.linspace(0, np.pi / 2, fade)) ** 2
    out = x.copy()
    out[a:b] = seg * (1 - w[:, None]) + y * w[:, None]
    sf.write(args.dst, out, sr, subtype="FLOAT")

    meta = {**vars(args), "src_sha256": sha(Path(args.src)), "dst_sha256": sha(Path(args.dst)),
            "f0_tracked_hz": [round(float(v), 1) for v in f0[(t >= args.start) & (t <= args.end)][::8]]}
    Path(args.dst + ".json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"写出 {args.dst}；跟踪到的基频 {f0[(t >= args.start) & (t <= args.end)].min():.0f}–{f0[(t >= args.start) & (t <= args.end)].max():.0f} Hz")


if __name__ == "__main__":
    main()

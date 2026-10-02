"""ITU-R BS.1770-4 响度测量（K 加权、400 ms 块、75% 重叠、绝对 −70 LUFS 与相对 −10 LU 门限）。

只依赖 numpy / scipy；系数按采样率由双线性变换求出，不写死 48 kHz 的表。
"""

from __future__ import annotations

import math

import numpy as np
from scipy.signal import lfilter


def _shelf(sr: float) -> tuple[np.ndarray, np.ndarray]:
    # 高架（头部声学效应）：f0 1681.97 Hz，增益 +4 dB，Q 0.7072（BS.1770 附录参考值）。
    f0, gain_db, q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    k = math.tan(math.pi * f0 / sr)
    vh = 10 ** (gain_db / 20)
    vb = vh ** 0.4996667741545416
    a0 = 1 + k / q + k * k
    b = np.array([(vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0])
    a = np.array([1.0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0])
    return b, a


def _highpass(sr: float) -> tuple[np.ndarray, np.ndarray]:
    # RLB 高通：f0 38.14 Hz，Q 0.5003。
    f0, q = 38.13547087602444, 0.5003270373238773
    k = math.tan(math.pi * f0 / sr)
    a0 = 1 + k / q + k * k
    b = np.array([1.0, -2.0, 1.0])
    a = np.array([1.0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0])
    return b, a


def integrated(x: np.ndarray, sr: int) -> float:
    """整体响度（LUFS）。x 为 (样本数,) 或 (样本数, 声道数)。"""
    if x.ndim == 1:
        x = x[:, None]
    b1, a1 = _shelf(sr)
    b2, a2 = _highpass(sr)
    y = lfilter(b2, a2, lfilter(b1, a1, x, axis=0), axis=0)
    block = int(0.4 * sr)
    step = block // 4
    if len(y) < block:
        z = [float(np.mean(y**2, axis=0).sum())]
    else:
        z = [float(np.mean(y[i : i + block] ** 2, axis=0).sum()) for i in range(0, len(y) - block + 1, step)]
    z = np.array(z)
    loud = -0.691 + 10 * np.log10(np.maximum(z, 1e-12))
    gated = z[loud > -70]
    if len(gated) == 0:
        return -70.0
    rel = -0.691 + 10 * math.log10(gated.mean()) - 10
    final = z[(loud > -70) & (loud > rel)]
    return -0.691 + 10 * math.log10(max(final.mean(), 1e-12))


def peak_db(x: np.ndarray) -> float:
    return 20 * math.log10(max(float(np.abs(x).max()), 1e-9))

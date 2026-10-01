"""估算语音的中位基频，用来粗查音色性别是否设计反了（依赖 numpy、miniaudio，见 requirements.txt）。

男声说话的中位基频通常低于约 165 Hz，女声通常高于约 180 Hz；两者之间判为“不确定”，交人工试听。
"""

from __future__ import annotations

MALE_MAX = 165.0
FEMALE_MIN = 180.0


def available() -> bool:
    try:
        import miniaudio  # noqa: F401
        import numpy  # noqa: F401
    except ImportError:
        return False
    return True


def median_f0(path: str) -> float:
    import miniaudio
    import numpy as np

    sr, n, hop = 16000, 640, 320
    d = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.FLOAT32, nchannels=1, sample_rate=sr)
    x = np.asarray(d.samples, dtype=np.float32)
    rms = float(np.sqrt(np.mean(x ** 2))) if len(x) else 0.0
    lo, hi = sr // 400, sr // 60
    f0s = []
    # 自相关逐帧估基频，只取较响、周期性明显的浊音帧。
    for i in range(0, len(x) - n, hop):
        fr = x[i:i + n] - x[i:i + n].mean()
        if np.sqrt(np.mean(fr ** 2)) < rms * 0.6:
            continue
        ac = np.correlate(fr, fr, "full")[n - 1:]
        k = lo + int(np.argmax(ac[lo:hi]))
        if ac[k] / (ac[0] + 1e-9) > 0.45:
            f0s.append(sr / k)
    return float(np.median(f0s)) if f0s else 0.0


def judge(f0: float) -> str | None:
    if f0 <= 0:
        return None
    if f0 < MALE_MAX:
        return "male"
    if f0 > FEMALE_MIN:
        return "female"
    return None

"""用 CLAP（laion/larger_clap_general，Apache-2.0）给音效做零样本听辨，替代人耳初筛。

制作方不能直接听声音，挑素材和验收成品时用它核对“这段声音像不像它该是的东西”：
  - label 模式：每个文件对一组候选描述打分，打印前三名；期望描述没排第一就标 ✗。
  - scan 模式：长环境声按 5 秒窗口扫一遍，报出“人声说话、音乐、汽车、飞机、警笛”等不该出现的东西，
    以及钢琴 / 电子音这类合成感的声音。

  python tools/AudioBuild/sfx_check.py label <文件或目录> [--expect 描述]
  python tools/AudioBuild/sfx_check.py scan <文件> [--window 5]
  python tools/AudioBuild/sfx_check.py built     # 按 sfx_recipes.json 的 expect 逐个核对入包成品

模型目录默认 C:/code/models/larger_clap_general（环境变量 CLAP_MODEL 可改）。
CLAP 是粗筛：分数只说明“更像哪一类”，不代表音质；结论写进 sfx_manifest.json 的 clap 字段，台账注明未经人耳。
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

ROOT = Path(__file__).resolve().parents[2]
MODEL = os.environ.get("CLAP_MODEL", "C:/code/models/larger_clap_general")
SR = 48000

# 通用候选描述：既有本作需要的声音，也有常见的“错类”（钢琴、电子音、人声、交通）。
LABELS = [
    "footsteps on dirt", "footsteps on wooden floor", "footsteps on stone", "footsteps on gravel",
    "sword swinging through the air", "a fist punching a body", "a sword cutting flesh", "swords clashing, metal clang",
    "a body falling to the ground", "unsheathing a sword", "a deep breath", "a gong being struck", "a bell ringing",
    "a singing bowl", "a wooden block knock", "a wooden click", "paper rustling", "turning a book page", "unrolling a paper scroll",
    "coins jingling", "cloth rustling", "a whoosh", "rowing a boat with oars, water splashes", "a small water splash",
    "a flowing river stream", "wind blowing through reeds and grass", "birds singing in the countryside", "a fire crackling",
    "people chatting in a restaurant", "dishes and cutlery clinking", "a drum hit",
    "piano", "electronic synthesizer beep", "music", "a man speaking", "a woman speaking", "car traffic", "an airplane",
    "a siren", "a dog barking", "rain", "silence",
]
UNWANTED = ["a man speaking", "a woman speaking", "music", "piano", "car traffic", "an airplane", "a siren",
            "electronic synthesizer beep", "a dog barking", "a motorcycle", "a phone ringing"]


class Clap:
    def __init__(self) -> None:
        import torch
        from transformers import ClapModel, ClapProcessor

        self.torch = torch
        self.model = ClapModel.from_pretrained(MODEL, use_safetensors=False).eval().to("cuda")
        self.proc = ClapProcessor.from_pretrained(MODEL)
        self._text: dict[str, np.ndarray] = {}

    def text(self, labels: list[str]) -> np.ndarray:
        missing = [l for l in labels if l not in self._text]
        if missing:
            inp = self.proc(text=missing, return_tensors="pt", padding=True).to("cuda")
            with self.torch.no_grad():
                emb = self.model.get_text_features(**inp)
            emb = emb / emb.norm(dim=-1, keepdim=True)
            for l, e in zip(missing, emb.cpu().numpy()):
                self._text[l] = e
        return np.stack([self._text[l] for l in labels])

    def audio(self, clips: list[np.ndarray]) -> np.ndarray:
        inp = self.proc(audio=clips, sampling_rate=SR, return_tensors="pt").to("cuda")
        with self.torch.no_grad():
            emb = self.model.get_audio_features(**inp)
        emb = emb / emb.norm(dim=-1, keepdim=True)
        return emb.cpu().numpy()

    def scores(self, clip: np.ndarray, labels: list[str]) -> np.ndarray:
        logits = 100 * self.audio([clip]) @ self.text(labels).T
        p = np.exp(logits - logits.max())
        return (p / p.sum())[0]


def load(path: Path) -> np.ndarray:
    x, sr = sf.read(str(path), always_2d=True)
    x = x.mean(axis=1)
    if sr != SR:
        g = np.gcd(sr, SR)
        x = resample_poly(x, SR // g, sr // g)
    return x.astype(np.float32)


def label(clap: Clap, path: Path, expect: str | None, labels: list[str]) -> tuple[str, float, bool]:
    x = load(path)
    if expect and expect not in labels:
        labels = labels + [expect]
    s = clap.scores(x[: SR * 10], labels)
    order = np.argsort(-s)[:3]
    top = " | ".join(f"{labels[i]} {s[i]:.2f}" for i in order)
    ok = expect is None or labels[order[0]] == expect or (expect in [labels[i] for i in order[:2]] and s[labels.index(expect)] > 0.2)
    mark = "" if expect is None else ("✓" if ok else "✗")
    print(f"{mark:1} {path.name:<34} {top}")
    return labels[order[0]], float(s[order[0]]), bool(ok)


def scan(clap: Clap, path: Path, window: float, labels: list[str]) -> list[tuple[float, str, float]]:
    x = load(path)
    hits = []
    n = int(window * SR)
    allow = [l for l in labels if l not in UNWANTED]
    for at in range(0, max(1, len(x) - n // 2), n):
        seg = x[at: at + n]
        if len(seg) < SR:
            break
        s = clap.scores(seg, allow + UNWANTED)
        names = allow + UNWANTED
        i = int(np.argmax(s))
        bad = {u: float(s[names.index(u)]) for u in UNWANTED}
        worst = max(bad, key=bad.get)
        if names[i] in UNWANTED or bad[worst] > 0.25:
            hits.append((at / SR, worst, bad[worst]))
    print(f"{path.name}: {len(x) / SR:.0f} 秒，可疑窗口 {len(hits)} 个" + "".join(f"\n   {t:6.1f}s {w} {p:.2f}" for t, w, p in hits[:12]))
    return hits


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("label")
    a.add_argument("paths", nargs="+", type=Path)
    a.add_argument("--expect")
    b = sub.add_parser("scan")
    b.add_argument("paths", nargs="+", type=Path)
    b.add_argument("--window", type=float, default=5)
    sub.add_parser("built")
    args = ap.parse_args()
    clap = Clap()
    if args.cmd == "label":
        files = [f for p in args.paths for f in (sorted(p.glob("*.ogg")) if p.is_dir() else [p])]
        for f in files:
            label(clap, f, args.expect, LABELS)
    elif args.cmd == "scan":
        for p in args.paths:
            scan(clap, p, args.window, LABELS)
    else:
        recipes = json.loads((Path(__file__).parent / "sfx_recipes.json").read_text(encoding="utf-8"))
        manifest_path = ROOT / "game" / "assets" / "audio" / "sfx" / "sfx_manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        bad = 0
        # 脚步单独一下太短、离开上下文连人耳也难分辨：按游戏里的步频（约 0.42 秒一步）把变体连成一串再听辨。
        for group, entry in recipes["sfx"].items():
            if not group.startswith("step.") or entry.get("variants", 1) < 2:
                continue
            seq = np.zeros(int(SR * 0.42 * 8 + SR))
            for i in range(8):
                x = load(ROOT / "game" / "assets" / "audio" / "sfx" / f"{group}.{i % entry['variants'] + 1}.ogg")
                at = int(i * 0.42 * SR)
                seq[at: at + len(x)] += x[: len(seq) - at]
            s = clap.scores(seq, LABELS)
            order = np.argsort(-s)[:3]
            ok = LABELS[order[0]] == entry["expect"]
            bad += not ok
            print(f"{'✓' if ok else '✗'} {group + ' 连走':<34} " + " | ".join(f"{LABELS[i]} {s[i]:.2f}" for i in order))
        for kind, folder in (("sfx", "sfx"), ("ambience", "amb")):
            for name, entry in manifest[kind].items():
                expect = recipes[kind][name.rsplit(".", 1)[0] if name[-1].isdigit() else name]["expect"]
                top, p, ok = label(clap, ROOT / "game" / "assets" / "audio" / folder / entry["file"], expect, LABELS)
                entry["clap"] = {"expect": expect, "top": top, "p": round(p, 2), "ok": ok}
                bad += not ok and not name.startswith("step.")
                if kind == "ambience":
                    hits = scan(clap, ROOT / "game" / "assets" / "audio" / folder / entry["file"], 5, LABELS)
                    entry["clap"]["suspect_windows"] = len(hits)
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"不符 {bad} 个")


if __name__ == "__main__":
    main()

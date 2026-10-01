"""按音色设计档案为每位说话人设计原创音色，只取回试听音频，不做合成。

试听文本取该说话人在章节里的前 N 句台词拼接。结果写到 build/voice/design/<档案名>/：
每人一个试听 MP3，result.json 记录 voice_id、描述与试听文本。设计出的 voice_id 首次用于
合成时 MiniMax 才扣音色费，所以本脚本不会把它们用于合成。已有结果且描述未变的说话人跳过。

用法：
    python tools/VoiceBuilder/design.py
    python tools/VoiceBuilder/design.py --only char.hero char.huang_rong --redo
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
import minimax  # noqa: E402
import pitch  # noqa: E402

ROOT = minimax.ROOT


def load(path: str):
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        return json.load(f)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--profile", default="voice_source/profiles/minimax_design_v1.json")
    ap.add_argument("--chapter", default="content/dialogue/arc01/chapter01.json")
    ap.add_argument("--per-speaker", type=int, default=2)
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--redo", action="store_true", help="描述没变也重新设计")
    ap.add_argument("--retries", type=int, default=2, help="试听音高与档案性别不符时重新设计的次数")
    args = ap.parse_args()

    profile = load(args.profile)
    common = profile.get("common", "")
    chapter = load(args.chapter)
    lines = [n for d in chapter["dialogues"] for n in d["nodes"] if n["type"] == "line"]

    name = os.path.splitext(os.path.basename(args.profile))[0]
    out_dir = os.path.join(ROOT, "build", "voice", "design", name)
    os.makedirs(out_dir, exist_ok=True)
    result_path = os.path.join(out_dir, "result.json")
    results: dict = load(os.path.relpath(result_path, ROOT)) if os.path.exists(result_path) else {}

    client = minimax.Client()
    check = pitch.available()
    if not check:
        print("未安装 numpy / miniaudio，跳过音高检查（pip install -r tools/VoiceBuilder/requirements.txt）")
    for speaker, v in profile["voices"].items():
        if args.only and speaker not in args.only and speaker.split("@", 1)[0] not in args.only:
            continue
        prompt = f"{v['prompt']}{common}"
        base = speaker.split("@", 1)[0]
        picked = [n for n in lines if n["speaker"] == base][: args.per_speaker]
        preview = "".join(n["text"] for n in picked)[:500]
        old = results.get(speaker)
        if old and old["prompt"] == prompt and old["preview_text"] == preview and not args.redo \
                and os.path.exists(os.path.join(out_dir, old["file"])):
            print(f"  = {speaker}（沿用已设计 {old['voice_id']}）")
            continue
        file = f"{speaker}.mp3"
        want = v.get("gender")
        f0, got = 0.0, None
        for round_ in range(args.retries + 1):
            for attempt in range(6):
                try:
                    voice_id, audio = client.design(prompt, preview)
                    break
                except minimax.MiniMaxError as e:
                    if "1002" in str(e) and attempt < 5:
                        print(f"  … 限流，{20 * (attempt + 1)} 秒后重试")
                        time.sleep(20 * (attempt + 1))
                        continue
                    print(f"  ✗ {speaker}：{e}")
                    return 1
            with open(os.path.join(out_dir, file), "wb") as f:
                f.write(audio)
            if not (check and want):
                break
            f0 = pitch.median_f0(os.path.join(out_dir, file))
            got = pitch.judge(f0)
            if got == want:
                break
            print(f"  ! {speaker} 试听音高 {f0:.0f} Hz，与档案性别 {want} 不符" + ("，重新设计" if round_ < args.retries else "，请人工试听"))
        results[speaker] = {
            "voice_id": voice_id,
            "prompt": prompt,
            "preview_text": preview,
            "preview_line_ids": [n["line_id"] for n in picked],
            "file": file,
            "designed_at": time.strftime("%Y-%m-%dT%H:%M:%S"),
            "used_in_synthesis": False,
            "gender": want,
            "median_f0_hz": round(f0) if f0 else None,
            "gender_check": None if not (check and want) else ("ok" if got == want else "mismatch"),
        }
        print(f"  ✓ {speaker} → {voice_id}（试听 {len(preview)} 字" + (f"，音高 {f0:.0f} Hz" if f0 else "") + "）")
        with open(result_path, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)

    print(f"完成：{len(results)} 位说话人；结果 {os.path.relpath(result_path, ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

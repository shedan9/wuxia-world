"""按音色档案为对白生成试听小样，并写出清单与试听页。

小样写到 build/voice/samples/<档案名>/（不入库）：每句一个音频文件，
manifest.json 记录 line_id、说话人、文本哈希、模型、音色参数与计费字符数，
index.html 可在浏览器里逐句对照文本试听。

用法：
    python tools/VoiceBuilder/sample.py                         # 每位说话人取前 2 句
    python tools/VoiceBuilder/sample.py --per-speaker 3
    python tools/VoiceBuilder/sample.py --lines ch01.opening_luwan.lu.001 ch01.inn_council.qiao.001
    python tools/VoiceBuilder/sample.py --dry-run               # 只列出将生成的句子与字数，不调用接口

档案 voices 的键可写成“人物ID@候选名”，同一说话人的每个候选都会把选中的句子各生成一遍，便于比较。

档案里标了 "designed": true 的是音色设计得到的音色，每次合成都要用户事先确认（首次使用扣 9.9 元）：
不在 --confirm-designed 里逐个列出的设计音色，脚本拒绝运行。
    python tools/VoiceBuilder/sample.py --profile voice_source/profiles/minimax_cast.json --per-speaker 999         --confirm-designed char.hero char.huang_rong
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
import minimax  # noqa: E402

ROOT = minimax.ROOT


def load(path: str):
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        return json.load(f)


def chapter_lines(chapters: list[str]) -> list[dict]:
    """需要配音的台词：对白里的台词（心里话 inner 只显示字幕、不配音）与战斗喊声（barks）。"""
    lines = []
    for chapter in chapters:
        data = load(chapter)
        lines += [n for d in data.get("dialogues", []) for n in d["nodes"] if n["type"] == "line" and not n.get("inner")]
        lines += data.get("barks", [])
    return lines


def default_chapters() -> list[str]:
    """第一篇全部对白文件（章节对白与战斗喊声），按文件名排序。"""
    folder = os.path.join(ROOT, "content", "dialogue", "arc01")
    return [f"content/dialogue/arc01/{f}" for f in sorted(os.listdir(folder)) if f.endswith(".json")]


def _write_manifest(out_dir: str, profile: str, manifest: list[dict]) -> None:
    with open(os.path.join(out_dir, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump({"profile": profile, "lines": manifest}, f, ensure_ascii=False, indent=2)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--profile", default="voice_source/profiles/minimax_trial.json")
    ap.add_argument("--chapter", action="append", help="对白 JSON（章节对白或战斗喊声）；可多次给出，缺省为第一篇全部对白文件。"
                    "清单只保留本次涉及的句子，所以通常不要缩小范围，用 --lines 指定要新合成的句子即可")
    ap.add_argument("--per-speaker", type=int, default=2)
    ap.add_argument("--lines", nargs="*", help="指定 line_id；给出时忽略 --per-speaker")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--confirm-designed", nargs="*", default=[], help="用户已确认本次可用的设计音色说话人（人物ID或人物ID@候选名）")
    args = ap.parse_args()

    profile = load(args.profile)
    voices: dict = profile["voices"]
    variants: dict[str, list[str]] = {}
    for key in voices:
        variants.setdefault(key.split("@", 1)[0], []).append(key)
    lines = chapter_lines(args.chapter or default_chapters())

    if args.lines:
        wanted = set(args.lines)
        picked = [n for n in lines if n["line_id"] in wanted]
        missing = wanted - {n["line_id"] for n in picked}
        if missing:
            print("找不到这些 line_id：" + "、".join(sorted(missing)))
            return 1
    else:
        count: dict[str, int] = {}
        picked = []
        for n in lines:
            if n["speaker"] in variants and count.get(n["speaker"], 0) < args.per_speaker:
                picked.append(n)
                count[n["speaker"]] = count.get(n["speaker"], 0) + 1

    no_voice = sorted({n["speaker"] for n in picked if n["speaker"] not in variants})
    if no_voice:
        print("档案里没有这些说话人的音色：" + "、".join(no_voice))
        return 1

    name = os.path.splitext(os.path.basename(args.profile))[0]
    out_dir = os.path.join(ROOT, "build", "voice", "samples", name)
    audio = profile["audio"]
    ext = audio.get("format", "mp3")

    # 断点续跑：文本与音色参数都没变、文件还在的句子沿用旧结果，不重复计费。
    old: dict[str, dict] = {}
    old_path = os.path.join(out_dir, "manifest.json")
    if os.path.exists(old_path):
        with open(old_path, encoding="utf-8") as f:
            old = {m["line_id"] + (f"@{m['variant']}" if m.get("variant") else ""): m for m in json.load(f)["lines"]}

    jobs = []
    for n in picked:
        for key in variants[n["speaker"]]:
            v = voices[key]
            variant = key.split("@", 1)[1] if "@" in key else None
            setting = {"voice_id": v["voice_id"], "speed": v.get("speed", 1.0), "vol": v.get("vol", 1.0), "pitch": v.get("pitch", 0)}
            if v.get("emotion"):
                setting["emotion"] = v["emotion"]
            modify = v.get("modify") or None
            uid = f"{n['line_id']}@{variant}" if variant else n["line_id"]
            sha = hashlib.sha256(n["text"].encode("utf-8")).hexdigest()
            prev = old.get(uid)
            reuse = bool(prev and prev["text_sha256"] == sha and prev["voice_setting"] == setting and prev["model"] == profile["model"]
                         and prev["audio_setting"] == audio and prev.get("voice_modify") == modify
                         and os.path.exists(os.path.join(out_dir, prev["file"])))
            jobs.append({"n": n, "key": key, "variant": variant, "uid": uid, "sha": sha, "setting": setting, "modify": modify,
                         "file": f"{uid}.{ext}", "prev": prev if reuse else None})

    todo = [j for j in jobs if j["prev"] is None]
    # 设计音色只拦真正要合成的句子；沿用旧音频不再计费，不需要确认。
    designed = sorted({j["key"] for j in todo if voices[j["key"]].get("designed")})
    unconfirmed = [k for k in designed if k not in args.confirm_designed and k.split("@", 1)[0] not in args.confirm_designed]
    if designed:
        print("本次要用设计音色合成：" + "、".join(designed))
    chars = sum(len(j["n"]["text"]) for j in todo)
    print(f"共 {len(jobs)} 条，沿用 {len(jobs) - len(todo)} 条，需合成 {len(todo)} 条、{chars} 字（模型 {profile['model']}）")
    if unconfirmed and not args.dry_run:
        print("未经用户确认的设计音色，拒绝合成：" + "、".join(unconfirmed) + "（确认后用 --confirm-designed 列出，或用 --lines 避开这些角色）")
        return 1
    if args.dry_run:
        for j in todo:
            print(f"  {j['uid']}  {j['key']}  {j['n']['text']}")
        return 0

    os.makedirs(out_dir, exist_ok=True)
    client = minimax.Client() if todo else None
    text_table = {}
    for path in ("content/regions/jiangnan/text/zh-Hans.json",):
        text_table.update(load(path))

    manifest = []
    billed = 0
    for j in jobs:
        n, key, variant, uid, sha, setting, modify, file = (j[k] for k in ("n", "key", "variant", "uid", "sha", "setting", "modify", "file"))
        if j["prev"]:
            manifest.append(j["prev"])
            continue
        for attempt in range(6):
            try:
                data, extra = client.synthesize(n["text"], setting, profile["model"], audio, modify=modify)
                break
            except minimax.MiniMaxError as e:
                # 1002 为限流（每分钟请求数），等一会儿再试；其他错误直接停下。
                if "1002" in str(e) and attempt < 5:
                    print(f"  … 限流，{20 * (attempt + 1)} 秒后重试")
                    time.sleep(20 * (attempt + 1))
                    continue
                print(f"  ✗ {uid}：{e}")
                _write_manifest(out_dir, args.profile, manifest)
                return 1
        with open(os.path.join(out_dir, file), "wb") as f:
            f.write(data)
        used = int(extra.get("usage_characters") or 0)
        billed += used
        manifest.append({
            "line_id": n["line_id"],
            "speaker": n["speaker"],
            "variant": variant,
            "text": n["text"],
            "text_sha256": sha,
            "provider": profile["provider"],
            "model": profile["model"],
            "voice_setting": setting,
            "voice_modify": modify,
            "audio_setting": audio,
            "audio_length_ms": extra.get("audio_length"),
            "usage_characters": used,
            "file": file,
            "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S"),
            "review": "pending",
        })
        print(f"  ✓ {uid}（{extra.get('audio_length')} ms，计费 {used} 字）")

    _write_manifest(out_dir, args.profile, manifest)

    # 清掉已不需要配音的旧音频：台词已删除，或改成了心里话。
    voiced = {n["line_id"] for n in lines}
    for f in os.listdir(out_dir):
        if f.endswith(f".{ext}") and f.removesuffix(f".{ext}").split("@", 1)[0] not in voiced:
            os.remove(os.path.join(out_dir, f))
            print(f"  - 删除 {f}（台词已删除或为心里话，不配音）")

    def who(s: str) -> str:
        return text_table.get(f"{s}.name", s)

    rows = []
    for m in manifest:
        key = f"{m['speaker']}@{m['variant']}" if m.get("variant") else m["speaker"]
        note = voices[key].get("note", "")
        rows.append(
            f"<tr><td>{html.escape(who(m['speaker']))}{'·' + html.escape(m['variant']) if m.get('variant') else ''}<br><small>{html.escape(m['voice_setting']['voice_id'])}</small></td>"
            f"<td>{html.escape(m['text'])}<br><small>{html.escape(m['line_id'])}</small></td>"
            f"<td><audio controls preload=\"none\" src=\"{html.escape(m['file'])}\"></audio></td>"
            f"<td><small>{html.escape(note)}</small></td></tr>"
        )
    page = f"""<!doctype html><meta charset="utf-8"><title>配音试听 · {html.escape(name)}</title>
<style>body{{font:15px/1.6 system-ui,"Microsoft YaHei",sans-serif;margin:24px;background:#f4f1e8;color:#223}}
table{{border-collapse:collapse;width:100%}}td{{border-bottom:1px solid #ccc;padding:8px;vertical-align:top}}
small{{color:#667}}audio{{width:260px}}</style>
<h1>配音试听：{html.escape(name)}</h1>
<p>{html.escape(profile.get('status', ''))}</p>
<p>模型 {html.escape(profile['model'])} · {len(manifest)} 句 · 计费 {billed} 字</p>
<table><tr><th>说话人 / 音色</th><th>台词</th><th>试听</th><th>声线方向</th></tr>
{''.join(rows)}
</table>
"""
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write(page)
    print(f"完成：{len(manifest)} 句，计费 {billed} 字；试听页 {os.path.relpath(os.path.join(out_dir, 'index.html'), ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

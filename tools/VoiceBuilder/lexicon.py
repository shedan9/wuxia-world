"""专名发音词典（开发计划 M3-06，架构文档 10.5）：读取、按台词挑词条、校验与覆盖报告。

词典在 voice_source/lexicon/zh-Hans.json。sample.py 合成时只把台词里出现的词条交给供应商，
并记进清单；install.py 据此列出“词典改过、音频按旧读音生成”的句子。

用法：
    python tools/VoiceBuilder/lexicon.py               # 校验词典并列出每条在哪些台词里生效
    python tools/VoiceBuilder/lexicon.py --polyphones  # 另列词典未覆盖的常见多音字出现处，供人工过目
校验不通过时退出码为 1：拼音格式或字数不符、词条重复、互相包含的词条读音不一致、
人物名 / 地名节点 / 书名 / 经典武学名缺词条。不调用任何接口。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEXICON = ROOT / "voice_source" / "lexicon" / "zh-Hans.json"
SYLLABLE = re.compile(r"^[a-zü]+[1-5]$")

# 按上下文容易读错、值得人工过目的多音字（--polyphones 只做提示，不判失败）。
WATCH = "还当倒落为只难空处号差模押扎令重传调将降见曾藏转薄削塞宿省恶折系散量背单劲朝称奔"


def load(path: Path = LEXICON) -> list[dict]:
    return json.loads(path.read_text(encoding="utf-8"))["entries"]


def matches(text: str, entries: list[dict]) -> list[tuple[int, dict]]:
    """从左到右取最长匹配、互不重叠的词条出现处。"""
    by_len = sorted(entries, key=lambda e: -len(e["term"]))
    found, i = [], 0
    while i < len(text):
        hit = next((e for e in by_len if text.startswith(e["term"], i)), None)
        if hit:
            found.append((i, hit))
            i += len(hit["term"])
        else:
            i += 1
    return found


def tone_list(text: str, entries: list[dict]) -> list[str]:
    """这句台词要交给 MiniMax pronunciation_dict.tone 的词条，如“令狐/(ling2)(hu2)”；按词条去重、排序稳定。"""
    terms = {e["term"]: e for _, e in matches(text, entries)}
    return [f"{t}/" + "".join(f"({s})" for s in terms[t]["pinyin"].split()) for t in sorted(terms)]


def voiced_lines() -> list[dict]:
    """第一篇全部要配音的台词（心里话不配音）与战斗喊声。"""
    lines = []
    for p in sorted((ROOT / "content" / "dialogue" / "arc01").glob("*.json")):
        data = json.loads(p.read_text(encoding="utf-8"))
        lines += [n for d in data.get("dialogues", []) for n in d["nodes"] if n["type"] == "line" and not n.get("inner")]
        lines += data.get("barks", [])
    return lines


def required_terms() -> dict[str, str]:
    """必须有读音词条的专名：人物名、大地图节点、原著书名与经典人物武学，键为文字、值为来源键。"""
    region = json.loads((ROOT / "content" / "regions" / "jiangnan" / "text" / "zh-Hans.json").read_text(encoding="utf-8"))
    shared = json.loads((ROOT / "content" / "shared" / "text" / "zh-Hans.json").read_text(encoding="utf-8"))
    canon = json.loads((ROOT / "content" / "shared" / "skills" / "canon.json").read_text(encoding="utf-8"))
    out = {}
    for k, v in region.items():
        if k.endswith(".name") and k.split(".")[0] in ("char", "node", "work") and k != "char.hero.name":
            out[v] = k
    for s in canon:
        for part in shared[f"{s['id']}.name"].split("·"):
            out[part] = f"{s['id']}.name"
    return out


def check(entries: list[dict]) -> list[str]:
    errors, seen = [], {}
    for e in entries:
        term, syl = e["term"], e["pinyin"].split()
        if term in seen:
            errors.append(f"词条重复：{term}")
        seen[term] = e
        if len(syl) != len(term):
            errors.append(f"{term}：拼音 {len(syl)} 个音节，与 {len(term)} 个字不符")
        bad = [s for s in syl if not SYLLABLE.match(s)]
        if bad:
            errors.append(f"{term}：拼音格式不对 {bad}（小写字母加 1–5 声调数字）")
    # 互相包含的词条读音必须一致，否则长短匹配结果不同会读成两样。
    for a in entries:
        for b in entries:
            if a is b or a["term"] not in b["term"]:
                continue
            i = b["term"].index(a["term"])
            if b["pinyin"].split()[i:i + len(a["term"])] != a["pinyin"].split():
                errors.append(f"{a['term']} 与 {b['term']} 里同一段读音不一致")
    for name, key in required_terms().items():
        covered = set()
        for i, e in matches(name, entries):
            covered.update(range(i, i + len(e["term"])))
        if len(covered) != len(name):
            errors.append(f"缺专名词条：{name}（{key}）")
    return errors


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--polyphones", action="store_true", help="列出词典未覆盖的常见多音字出现处")
    args = ap.parse_args()

    entries = load()
    errors = check(entries)
    lines = voiced_lines()
    hits: dict[str, list[str]] = {e["term"]: [] for e in entries}
    for n in lines:
        for _, e in matches(n["text"], entries):
            if n["line_id"] not in hits[e["term"]]:
                hits[e["term"]].append(n["line_id"])
    used = sum(1 for n in lines if matches(n["text"], entries))
    print(f"词典 {len(entries)} 条；配音台词 {len(lines)} 句，其中 {used} 句用到词条")
    for e in entries:
        ids = hits[e["term"]]
        print(f"  {e['term']}（{e['pinyin']}）{len(ids)} 句" + (f"：{'、'.join(ids[:4])}{' …' if len(ids) > 4 else ''}" if ids else ""))

    if args.polyphones:
        print("词典未覆盖的多音字（人工过目，按默认读音无误则不必收词）：")
        for n in lines:
            covered = set()
            for i, e in matches(n["text"], entries):
                covered.update(range(i, i + len(e["term"])))
            marks = [c for i, c in enumerate(n["text"]) if c in WATCH and i not in covered]
            if marks:
                print(f"  [{''.join(marks)}] {n['line_id']}  {n['text']}")

    for msg in errors:
        print(f"✗ {msg}")
    print("校验通过" if not errors else f"校验未通过：{len(errors)} 处")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

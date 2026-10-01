"""把几轮试听并排放进一页，便于逐个角色对比：build/voice/compare.html。

每一列是一个试听来源：sample.py 的逐句小样目录（含 manifest.json），或 design.py 的设计试听目录（含 result.json）。

用法：
    python tools/VoiceBuilder/compare.py samples/minimax_trial samples/minimax_natural design/minimax_design_v1
"""

from __future__ import annotations

import argparse
import html
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
VOICE = os.path.join(ROOT, "build", "voice")


def load(path: str):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sources", nargs="+", help="相对 build/voice 的目录")
    ap.add_argument("--speakers", nargs="*", help="只列这些人物 ID")
    ap.add_argument("-o", "--output", default="compare.html", help="写到 build/voice 下的文件名")
    args = ap.parse_args()

    text: dict[str, str] = {}
    for p in ("content/regions/jiangnan/text/zh-Hans.json",):
        text.update(load(os.path.join(ROOT, p)))

    columns = []  # (标题, 说明, {speaker: [(file, caption)]})
    order: list[str] = []
    for src in args.sources:
        d = os.path.join(VOICE, src)
        clips: dict[str, list[tuple[str, str]]] = {}
        if os.path.exists(os.path.join(d, "manifest.json")):
            m = load(os.path.join(d, "manifest.json"))
            prof = load(os.path.join(ROOT, m["profile"]))
            for line in m["lines"]:
                tag = f"【{line['variant']}：{prof['voices'][line['speaker'] + '@' + line['variant']].get('note', '')}】" if line.get("variant") else ""
                clips.setdefault(line["speaker"], []).append((f"{src}/{line['file']}", tag + line["text"]))
            note = prof.get("status", "")
        elif os.path.exists(os.path.join(d, "result.json")):
            r = load(os.path.join(d, "result.json"))
            for key, v in r.items():
                tag = f"【{key.split('@', 1)[1]}】" if "@" in key else ""
                clips.setdefault(key.split("@", 1)[0], []).append((f"{src}/{v['file']}", tag + v["prompt"][:60] + "…"))
            note = "音色设计试听（文字描述生成的原创音色）"
        else:
            print(f"跳过 {src}：没有 manifest.json 或 result.json")
            continue
        for s in clips:
            if args.speakers and s not in args.speakers:
                continue
            if s not in order:
                order.append(s)
        columns.append((src, note, clips))

    head = "".join(f"<th>{html.escape(t)}<br><small>{html.escape(n)}</small></th>" for t, n, _ in columns)
    rows = []
    for s in order:
        cells = []
        for _, _, clips in columns:
            items = clips.get(s, [])
            cells.append("<td>" + "".join(
                f"<div class=clip><audio controls preload=none src=\"{html.escape(f)}\"></audio><small>{html.escape(c)}</small></div>"
                for f, c in items) + "</td>")
        rows.append(f"<tr><th>{html.escape(text.get(f'{s}.name', s))}</th>{''.join(cells)}</tr>")

    page = f"""<!doctype html><meta charset="utf-8"><title>配音对比</title>
<style>body{{font:14px/1.5 system-ui,"Microsoft YaHei",sans-serif;margin:20px;background:#f4f1e8;color:#223}}
table{{border-collapse:collapse}}th,td{{border-bottom:1px solid #ccc;padding:8px;vertical-align:top;text-align:left}}
thead th{{max-width:380px;font-weight:600}}small{{color:#667;display:block;max-width:360px}}
.clip{{margin-bottom:8px}}audio{{width:300px}}</style>
<h1>配音对比</h1>
<table><thead><tr><th>角色</th>{head}</tr></thead><tbody>{''.join(rows)}</tbody></table>
"""
    out = os.path.join(VOICE, args.output)
    with open(out, "w", encoding="utf-8") as f:
        f.write(page)
    print(f"已写出 {os.path.relpath(out, ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

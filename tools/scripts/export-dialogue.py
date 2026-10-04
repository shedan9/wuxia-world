"""把一章对白 JSON 导出为供审阅的 Markdown 阅读版。

阅读版只是生成物，写到 build/（不入库）；台词的唯一可编辑来源仍是
content/dialogue/arcXX/chapterXX.json。审阅意见请按 line_id 指明哪一句。

用法：
    python tools/scripts/export-dialogue.py                       # 默认第一篇第一章
    python tools/scripts/export-dialogue.py content/dialogue/arc01/chapter01.json -o build/review/dialogue/arc01-chapter01.md
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def load_json(path: str):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def load_text() -> dict[str, str]:
    text: dict[str, str] = {}
    for path in sorted(glob.glob(os.path.join(ROOT, "content", "**", "text", "zh-Hans.json"), recursive=True)):
        text.update(load_json(path))
    return text


def load_list(pattern: str) -> list[dict]:
    items: list[dict] = []
    for path in sorted(glob.glob(os.path.join(ROOT, pattern), recursive=True)):
        data = load_json(path)
        items.extend(data if isinstance(data, list) else [])
    return items


class Namer:
    """把 ID 翻成文本表里的中文名；查不到时原样保留 ID，便于发现缺字。"""

    # 选择类事实的取值是人物简称，阅读版里换成人名。
    SHORT = {"linghu": "char.linghu_chong", "huang": "char.huang_rong", "xiao": "char.xiao_feng"}
    VALUE = {"true": "是", "false": "否", "public": "公开", "sealed": "密封", "full": "全额", "late": "迟到",
             "sword": "剑术", "fist": "拳掌", "inner": "内功"}
    AXIS = {"affection": "好感", "trust": "信任"}
    # 事实不进玩家文本表，阅读版用这里的说明代替 ID；查不到的仍显示 ID。
    FACT = {
        "fact.hero.style": "主角流派",
        "fact.ch01.arrived": "已到旧渡",
        "fact.ch01.companion": "同行者",
        "fact.ch01.copy_custody": "副页处置",
        "fact.ch01.council_done": "客栈议事已结束",
        "fact.ch01.council_stance": "议事时主角的立场",
        "fact.ch01.disguise_seen": "识破伪装",
        "fact.ch01.escort_beaten": "已击退押运队",
        "fact.ch01.ferryman_freed": "渡工已救出",
        "fact.ch01.ferryman_freed_early": "开闸前救出渡工",
        "fact.ch01.helper": "水门援手",
        "fact.ch01.mentor": "讨教对象",
        "fact.ch01.showed_photo": "给陆青禾看过照片",
        "fact.ch01.side01_result": "失踪渡工支线结果",
        "fact.ch01.sluice_jammed": "闸绳已被卡住",
    }

    def fact(self, id_: str) -> str:
        return f"〈{self.FACT[id_]}〉" if id_ in self.FACT else f"`{id_}`"

    def __init__(self, text: dict[str, str]):
        self.text = text

    def name(self, id_: str) -> str:
        if id_ == "narrator":
            return "旁白"
        return self.text.get(f"{id_}.name", id_)

    def value(self, v: str) -> str:
        if v in self.SHORT:
            return self.name(self.SHORT[v])
        return self.VALUE.get(v, v)

    def objective(self, quest: str, obj: str) -> str:
        return self.text.get(f"{quest}.objective.{obj}", obj)

    def stage(self, quest: str, stage: str) -> str:
        return self.text.get(f"{quest}.stage.{stage}", stage)

    def condition(self, c: dict) -> str:
        t = c.get("type")
        if t == "all":
            return " 且 ".join(self.condition(x) for x in c["of"])
        if t == "any":
            return " 或 ".join(self.condition(x) for x in c["of"])
        if t == "not":
            inner = c["of"]
            inner = inner[0] if isinstance(inner, list) else inner
            return f"并非（{self.condition(inner)}）"
        if t == "fact_equals":
            return f"{self.fact(c['id'])} = {self.value(str(c['value']))}"
        if t == "party_contains":
            return f"{self.name(c['id'])}在队中"
        if t == "clue_known":
            return f"已知线索「{self.name(c['id'])}」"
        if t == "quest_state_is":
            s = f"「{self.name(c['id'])}」{c.get('status', '')}"
            if "stage" in c:
                s += f"·{self.stage(c['id'], c['stage'])}"
            return s
        if t == "has_item":
            return f"持有「{self.name(c['id'])}」"
        return json.dumps(c, ensure_ascii=False)

    def effect(self, e: dict) -> str:
        t = e["type"]
        i = e.get("id", "")
        if t == "set_fact":
            return f"记下 {self.fact(i)} = {self.value(str(e['value']))}"
        if t == "change_relationship":
            amount = e["amount"]
            return f"{self.name(i)}{self.AXIS.get(e['axis'], e['axis'])} {'+' if amount >= 0 else ''}{amount}"
        if t == "meet_character":
            return f"结识{self.name(i)}"
        if t == "join_party":
            return f"{self.name(i)}入队"
        if t == "leave_party":
            return f"{self.name(i)}离队"
        if t == "complete_objective":
            return f"完成目标「{self.objective(i, e['value'])}」"
        if t == "add_clue":
            return f"得线索「{self.name(i)}」"
        if t == "grant_item":
            n = e.get("amount", 1)
            return f"得物品「{self.name(i)}」" + (f" ×{n}" if n != 1 else "")
        if t == "learn_skill":
            return f"学会「{self.name(i)}」"
        if t == "start_quest":
            return f"接取任务「{self.name(i)}」"
        if t == "request_battle":
            s = f"开战：{self.name(i)}"
            wins = e.get("on_victory") or []
            if wins:
                s += "（胜后：" + "；".join(self.effect(w) for w in wins) + "）"
            return s
        if t == "advance_clock":
            return f"时间过去 {e['amount']} 个时辰"
        if t == "request_travel":
            return f"前往{self.name(i)}"
        return json.dumps(e, ensure_ascii=False)

    def effects(self, effs: list[dict]) -> str:
        return "；".join(self.effect(e) for e in effs)


def render_dialogue(dlg: dict, events: dict[str, dict], interactables: dict[str, str], namer: Namer) -> list[str]:
    nodes = {n["id"]: n for n in dlg["nodes"]}
    out: list[str] = []

    # 先找出被选项或分流指向的节点，在正文里标成可跳转的小节。
    targets: set[str] = set()
    for n in dlg["nodes"]:
        for o in n.get("options", []):
            targets.add(o["next"])
        for b in n.get("branches", []):
            targets.add(b["next"])
        if n["type"] == "branch" and "next" in n:
            targets.add(n["next"])
        if n["type"] == "jump":
            targets.add(n["next"])

    def anchor(node_id: str) -> str:
        return f"{dlg['id']}--{node_id}".replace(".", "-").replace("_", "-")

    def link(node_id: str) -> str:
        return f"[〔{node_id}〕](#{anchor(node_id)})"

    done: set[str] = set()

    def walk(start: str) -> None:
        cur: str | None = start
        while cur is not None:
            if cur in done:
                out.append(f"> → 接 {link(cur)}")
                out.append("")
                return
            done.add(cur)
            n = nodes[cur]
            if cur in targets:
                out.append(f'<a id="{anchor(cur)}"></a>**〔{cur}〕**')
                out.append("")
            t = n["type"]
            if t == "line":
                mood = f"（{n['mood']}）" if n.get("mood") else ""
                if n.get("inner"):
                    out.append(f"**{namer.name(n['speaker'])}**〔心里话·不配音〕：*{n['text']}*  ")
                else:
                    out.append(f"**{namer.name(n['speaker'])}**{mood}：{n['text']}  ")
                out.append(f"<sub>`{n['line_id']}`</sub>")
                out.append("")
                cur = n.get("next")
            elif t == "stage":
                kind = {"scene": "场景", "action": "动作", "closeup": "特写", "title": "标题卡"}.get(n.get("kind", ""), n.get("kind", ""))
                out.append(f"> 🎬 〔演出·{kind}〕{n.get('direction', '')}")
                if n.get("caption"):
                    out.append(f"> 画面文字：「{n['caption']}」 <sub>`{n.get('caption_id', '')}`</sub>")
                out.append("")
                cur = n.get("next")
            elif t == "effect":
                out.append(f"> 〔效果〕{namer.effects(n.get('effects', []))}")
                out.append("")
                cur = n.get("next")
            elif t == "choice":
                out.append("> **玩家选择**")
                for k, o in enumerate(n["options"]):
                    letter = chr(ord("A") + k)
                    extra = []
                    if o.get("when"):
                        extra.append(f"条件：{namer.condition(o['when'])}")
                    if o.get("locked_hint"):
                        extra.append(f"不满足时提示“{o['locked_hint']}”")
                    if o.get("effects"):
                        extra.append(f"效果：{namer.effects(o['effects'])}")
                    tail = f"（{'；'.join(extra)}）" if extra else ""
                    out.append(f"> - **{letter}. {o['text']}**{tail} → {link(o['next'])}  <sub>`{o['line_id']}`</sub>")
                out.append("")
                for o in n["options"]:
                    if o["next"] not in done:
                        walk(o["next"])
                return
            elif t == "branch":
                out.append("> **条件分流**")
                for b in n["branches"]:
                    out.append(f"> - 若 {namer.condition(b['when'])} → {link(b['next'])}")
                if "next" in n:
                    out.append(f"> - 否则 → {link(n['next'])}")
                out.append("")
                for b in n["branches"]:
                    if b["next"] not in done:
                        walk(b["next"])
                if "next" in n and n["next"] not in done:
                    walk(n["next"])
                return
            elif t == "jump":
                out.append(f"> → 跳至 {link(n['next'])}")
                out.append("")
                return
            elif t == "end":
                out.append("> 〔本段结束〕")
                out.append("")
                return
            else:
                out.append(f"> 〔未识别节点 {t}〕")
                cur = n.get("next")

    walk(dlg["entry"])
    missing = [nid for nid in nodes if nid not in done]
    if missing:
        out.append(f"> ⚠ 从入口走不到的节点：{'、'.join(missing)}")
        out.append("")

    head: list[str] = []
    ev = events.get(dlg["id"])
    if ev:
        where = namer.name(ev["map"])
        trig = "进入地图自动触发" if ev.get("auto") else "与人物交谈或走近触发"
        cond = f"；条件：{namer.condition(ev['when'])}" if ev.get("when") else ""
        who = "、".join(namer.name(p) for p in ev.get("participants", []))
        head.append(f"*地点：{where}　触发：{trig}{cond}" + (f"　在场：{who}" if who else "") + "*")
    elif dlg["id"] in interactables:
        head.append(f"*地点：{interactables[dlg['id']]}　触发：调查交互物*")
    return head + [""] + out


BARK_TRIGGERS = {
    "battle_start": "开战",
    "skill": "出招",
    "charge": "蓄力预兆",
    "phase": "阶段",
    "low_hp": "重伤",
    "downed": "倒下",
    "victory": "胜利",
}


def render_barks(data: dict, barks: list[dict], namer: "Namer", path: str) -> list[str]:
    """战斗喊声一览：按时机分组，写说话人、字幕、限定的遭遇 / 招式 / 阶段与优先级。"""
    md = [
        "## 战斗喊声",
        "",
        f"> 来自 `{path}`。状态：{data.get('status', '')}",
        "",
        "战斗中按事件挑句：同一时机只说一句；同一人的回合里后一句须更要紧；倍速只说优先级 3。“限定”一栏为空表示任何战斗都可能说。",
        "",
    ]
    for trigger, label in BARK_TRIGGERS.items():
        group = [b for b in barks if b["trigger"] == trigger]
        if not group:
            continue
        md += [f"### {label}（{len(group)} 句）", "", "| 说话人 | 字幕 | 限定 | 优先级 | line_id |", "|---|---|---|---|---|"]
        for b in group:
            scope = "、".join(namer.name(b[k]) for k in ("encounter", "skill", "phase") if b.get(k))
            again = f"，隔 {b['cooldown_rounds']} 轮可再说" if b.get("cooldown_rounds") else ""
            md.append(f"| {namer.name(b['speaker'])} | {b['text']} | {scope} | {b.get('priority', 1)}{again} | `{b['line_id']}` |")
        md.append("")
    return md


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("chapter", nargs="?", default="content/dialogue/arc01/chapter01.json")
    ap.add_argument("-o", "--output", default=None)
    args = ap.parse_args()

    src = os.path.join(ROOT, args.chapter)
    data = load_json(src)
    arc = data["arc"].split(".")[-1]
    chap = data["chapter"].split(".")[-1]
    output = args.output or os.path.join("build", "review", "dialogue", f"arc{arc}-chapter{chap}.md")
    output = os.path.join(ROOT, output)

    namer = Namer(load_text())
    events = {e["dialogue"]: e for e in load_list("content/regions/**/events/*.json") if "dialogue" in e}

    # 交互物（石痕、告示、船牌等）从地图数据里找它引用的对白。
    interactables: dict[str, str] = {}
    for path in glob.glob(os.path.join(ROOT, "content", "regions", "**", "maps", "*.json"), recursive=True):
        maps = load_json(path)
        for m in maps if isinstance(maps, list) else []:
            def scan(x):
                if isinstance(x, dict):
                    d = x.get("dialogue")
                    if isinstance(d, str):
                        interactables.setdefault(d, namer.name(m["id"]))
                    for v in x.values():
                        scan(v)
                elif isinstance(x, list):
                    for v in x:
                        scan(v)
            scan(m)

    dialogues = data["dialogues"]
    lines = sum(1 for d in dialogues for n in d["nodes"] if n["type"] == "line")
    stages = sum(1 for d in dialogues for n in d["nodes"] if n["type"] == "stage")
    speakers: dict[str, int] = {}
    for d in dialogues:
        for n in d["nodes"]:
            if n["type"] == "line":
                speakers[n["speaker"]] = speakers.get(n["speaker"], 0) + 1

    md: list[str] = [
        f"# 对白阅读版：第{int(arc)}篇 第{int(chap)}章",
        "",
        f"> 由 `{args.chapter}` 生成，**不要直接改这份文件**；意见请写明 `line_id`（每句下方的小字），改动落回 JSON。",
        f"> 状态：{data.get('status', '')}",
        "",
        f"共 {len(dialogues)} 段 {lines} 句台词、{stages} 个演出提示。各人句数：" + "、".join(f"{namer.name(k)} {v}" for k, v in sorted(speakers.items(), key=lambda kv: -kv[1])) + "。",
        "",
        "读法：从每段开头往下读；遇到“玩家选择”或“条件分流”时，各分支依次列在后面，〔节点名〕可点击跳转，“→ 接”表示分支在此汇回前文。“〔效果〕”是这一步实际结算的事实、关系、物品与战斗。“🎬 演出”不朗读、不配音，是给画面、动作与镜头的说明；“画面文字”是玩家在画面上看到的物件文字或标题。“〔心里话〕”只限主角，显示字幕、不配音。",
        "",
        "## 目录",
        "",
    ]
    for k, d in enumerate(dialogues, 1):
        n = sum(1 for x in d["nodes"] if x["type"] == "line")
        md.append(f"{k}. [{d['id']}](#{d['id'].replace('.', '').replace('_', '_')})（{n} 句，{'未锁稿' if d['status'] == 'draft' else '已锁稿'}）")
    md.append("")
    for k, d in enumerate(dialogues, 1):
        md.append(f"## {d['id']}")
        md.append("")
        md.append(f"第 {k} 段 · {'未锁稿' if d['status'] == 'draft' else '已锁稿'}")
        md.extend(render_dialogue(d, events, interactables, namer))

    # 同章的战斗喊声（chapterXX_battle.json）附在最后。
    battle = src.removesuffix(".json") + "_battle.json"
    barks = load_json(battle).get("barks", []) if os.path.exists(battle) else []
    if barks:
        md.extend(render_barks(load_json(battle), barks, namer, os.path.relpath(battle, ROOT).replace(os.sep, "/")))

    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(md).rstrip() + "\n")
    print(f"已写出 {os.path.relpath(output, ROOT)}：{len(dialogues)} 段 {lines} 句" + (f"，战斗喊声 {len(barks)} 句" if barks else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())

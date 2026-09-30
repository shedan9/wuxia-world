"""按任务文件里的色块画战斗道具的引导图（不经 Godot 布局的件，如战斗页的水门机关），供 piece.py 引导出件。

用法：
    .venv/Scripts/python prop_guide.py jobs/m0_battle_props.json

任务文件的 "props" 每项：id、画布 size [w, h]、shapes（同 backdrop.py 草图的色块写法，按画布像素坐标）；
带 "detail": true 的色块只画进完整引导图、不进结构图（木纹、铁箍等交给模型）。输出到任务文件的 "guides" 目录：
<id>.png（透明底，alpha 即抠图遮罩）、<id>__shape.png（结构图，Canny 边线取自它）与 <id>.json
（origin [0, 0]、px 1、parts ["body"]、view "front"：正立面，不经探索投影）。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

from backdrop import draw_shapes

ROOT = Path(__file__).resolve().parent


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    spec = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    out = ROOT / spec["guides"]
    out.mkdir(parents=True, exist_ok=True)
    for prop in spec["props"]:
        size = tuple(prop["size"])
        full = Image.new("RGBA", size, (0, 0, 0, 0))
        draw_shapes(full, prop["shapes"])
        shape = Image.new("RGBA", size, (0, 0, 0, 0))
        draw_shapes(shape, [s for s in prop["shapes"] if not s.get("detail")])
        full.save(out / f"{prop['id']}.png")
        shape.save(out / f"{prop['id']}__shape.png")
        meta = {"id": prop["id"], "px": 1, "origin": [0, 0], "size": list(size), "parts": ["body"], "view": "front"}
        (out / f"{prop['id']}.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{prop['id']}  {size[0]}×{size[1]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""把选定的布景件入库：原件与生成记录进 art_source/ai/<地区>/，游戏用图进 game/assets/art/<地区>/。

用法：
    .venv/Scripts/python place.py out/m0_town_pieces/r5_house_g_44.png town.house.north.1
    .venv/Scripts/python place.py out/m0_town_pieces/r4_bridge_55.png town.bridge --parts
    .venv/Scripts/python place.py out/m0_town_pieces_sdxl/r3_willow_11.png town.tree.1 --recut 28

游戏端按 <id>.png 与 <id>.json（origin：图像左上角的投影坐标；px：每投影单位像素数）把图贴回布局原位；
地面纹理（piece.py 的 texture 任务）记 world_size，着色器按世界坐标循环取样。
见 game/scripts/preview/Pages/PieceArt.cs。--parts 把多部件件按部件拆成 <id>.<部件>.png（平桥的桥面与两道栏杆）。
--recut 阈值：不用引导图 alpha，改用 cutout.py 从生成原图（__raw）按底色抠图（树冠等与占位轮廓不一致的件）；
--soft t0,t1 再按色差软抠图，把模型混进树冠的雾状底色变成半透明枝条。
--deshadow 比例：清掉图底部一段里树干根部以外的像素（模型自带的地面阴影），树类批量入库用，代替逐张框 --erase。
--regrade light,chroma：按引导图的平涂色块分区调色（regrade.py），文生图材质好但配色跑偏时用（客栈内墙）。
--figure：全身人物（figure.py 的输出，id 形如 figure.hero）：按不透明外框裁边、缩放到身高 --stature 像素（默认 800，
战斗 1080p 下约 2 倍源图），json 记脚底中点 foot 与身高 stature，引擎按它们对齐地面与缩放（FigureArt.cs）；
--top 给出头顶的 y（原图像素），长篙、刀尖高出头顶时用它代替外框顶边算身高。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("source", help="piece.py 的输出 PNG（抠好的那张，不是 __raw）")
    parser.add_argument("id", help="游戏内件 id，如 town.house.north.1")
    parser.add_argument("--parts", action="store_true", help="按部件遮罩拆成多张")
    parser.add_argument("--recut", type=float, help="改用 cutout.py 按底色从原图抠出（阈值）")
    parser.add_argument("--enclosed", type=float, default=0, help="recut 时另去掉封闭的底色小块（阈值）")
    parser.add_argument("--soft", help="recut 时按色差软抠图 t0,t1（去掉树冠里混入的雾状底色）")
    parser.add_argument("--erase", action="append", default=[], help="清掉 x0,y0,x1,y1 矩形内的像素（AI 自带的地面阴影等，边缘羽化 14 像素），可多次")
    parser.add_argument("--deshadow", type=float, help="清掉图底部这一比例内、树干根部以外的像素（模型自带的地面阴影），并切掉底边残条")
    parser.add_argument("--figure", action="store_true", help="全身人物：裁边、缩放到统一身高，记脚底与身高")
    parser.add_argument("--stature", type=int, default=800, help="--figure：头顶到脚底的输出像素")
    parser.add_argument("--top", type=int, help="--figure：头顶 y（原图像素），默认取外框顶边")
    parser.add_argument("--regrade", help="按引导图分区调色 light,chroma（如 0.7,1.0）：保留材质细节，把各色块区的平均色拉回布局配色（regrade.py）")
    args = parser.parse_args()

    src = (ROOT / args.source).resolve() if not Path(args.source).is_absolute() else Path(args.source)
    record = json.loads(src.with_suffix(".json").read_text(encoding="utf-8"))
    region = args.id.split(".", 1)[0]
    art_dir = REPO / "art_source" / "ai" / region
    game_dir = REPO / "game" / "assets" / "art" / region
    art_dir.mkdir(parents=True, exist_ok=True)
    game_dir.mkdir(parents=True, exist_ok=True)

    if args.recut is not None:
        from cutout import cutout

        raw = src.with_name(src.stem + "__raw.png")
        recut = src.with_name(src.stem + "__recut.png")
        soft = tuple(float(v) for v in args.soft.split(",")) if args.soft else None
        cutout(raw, recut, args.recut, 1.2, args.enclosed, soft)
        record["recut"] = {"from": raw.name, "threshold": args.recut, "feather": 1.2, "enclosed": args.enclosed, "soft": list(soft) if soft else None}
        src = recut

    if args.erase:
        from PIL import Image, ImageDraw

        import numpy as np
        from PIL import ImageFilter

        image = Image.open(src).convert("RGBA")
        mask = Image.new("L", image.size, 0)
        draw = ImageDraw.Draw(mask)
        for box in args.erase:
            draw.rectangle([int(v) for v in box.split(",")], fill=255)
        # 边缘羽化，避免在树根、草皮上留下一刀切的直边。
        keep = 1 - np.asarray(mask.filter(ImageFilter.GaussianBlur(14)), dtype=np.float32) / 255
        alpha = np.asarray(image.getchannel("A"), dtype=np.float32) * keep
        image.putalpha(Image.fromarray(alpha.astype(np.uint8)))
        erased = src.with_name(src.stem + "__erased.png")
        image.save(erased)
        record["erase"] = args.erase
        src = erased

    if args.deshadow:
        import numpy as np
        from PIL import Image, ImageFilter

        image = Image.open(src).convert("RGBA")
        alpha = np.asarray(image.getchannel("A"))
        h, w = alpha.shape
        y0 = int(h * (1 - args.deshadow))
        # 树干：分界线上方一行里最宽的不透明段（柳丝、枝条都比它窄），左右各放宽 1 倍给根部。
        solid = alpha[max(0, y0 - 24)] > 200
        runs, x = [], 0
        while x < w:
            if solid[x]:
                x1 = x
                while x1 < w and solid[x1]:
                    x1 += 1
                runs.append((x, x1))
                x = x1
            else:
                x += 1
        t0, t1 = max(runs, key=lambda r: r[1] - r[0]) if runs else (w // 2, w // 2)
        pad = int((t1 - t0) * 1.0)
        shadow = np.zeros_like(alpha, dtype=bool)
        shadow[y0:, :] = True
        shadow[y0:, max(0, t0 - pad):min(w, t1 + pad)] = False
        shadow[int(h * 0.985):, :] = True
        mask = Image.fromarray((shadow * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6))
        keep = 1 - np.asarray(mask, dtype=np.float32) / 255
        image.putalpha(Image.fromarray((np.asarray(image.getchannel("A"), dtype=np.float32) * keep).astype(np.uint8)))
        cleaned = src.with_name(src.stem + "__deshadow.png")
        image.save(cleaned)
        record["deshadow"] = args.deshadow
        src = cleaned

    if args.regrade:
        from PIL import Image

        from regrade import regrade

        light, chroma = (float(v) for v in args.regrade.split(","))
        graded = src.with_name(src.stem + "__regrade.png")
        regrade(Image.open(src), Image.open(ROOT / record["guide"]), light, chroma).save(graded)
        record["regrade"] = {"light": light, "chroma": chroma}
        src = graded

    if args.figure:
        return place_figure(args, src, record, art_dir, game_dir)

    outputs = {args.id: src}
    if args.parts:
        outputs = {f"{args.id}.{part}": src.with_name(f"{src.stem}__{part}.png") for part in record["parts"]}

    for game_id, path in outputs.items():
        shutil.copyfile(path, art_dir / f"{game_id}.png")
        shutil.copyfile(path, game_dir / f"{game_id}.png")
        # 地面纹理按世界边长循环取样；布景件按投影原点与像素比例贴回原位。
        placement = {"world_size": record["world_size"]} if record.get("kind") == "texture" else {"origin": record["origin"], "px": record["px"]}
        placed = {
            "id": game_id,
            **placement,
            "source": f"art_source/ai/{region}/{game_id}.png",
            "sha256": sha(path),
        }
        (game_dir / f"{game_id}.json").write_text(json.dumps(placed, ensure_ascii=False, indent=2), encoding="utf-8")
        (art_dir / f"{game_id}.json").write_text(
            json.dumps({**record, "placed_as": game_id, "placed_sha256": placed["sha256"]}, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        print(f"{game_id}  ←  {path.name}  sha256 {placed['sha256'][:8]}…")
    return 0


def place_figure(args, src: Path, record: dict, art_dir: Path, game_dir: Path) -> int:
    import numpy as np
    from PIL import Image

    import cv2

    image = Image.open(src).convert("RGBA")
    # 底色常带渐变，画框边上会残留一条抠不掉的亮带：只留最大连通块（人物）和不贴左右边、面积不小于其 3% 的部件。
    a = np.asarray(image.getchannel("A")).copy()
    count, labels, stats, _ = cv2.connectedComponentsWithStats((a > 16).astype(np.uint8), connectivity=8)
    main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    keep = np.zeros(count, dtype=bool)
    for i in range(1, count):
        x, w, area = stats[i, cv2.CC_STAT_LEFT], stats[i, cv2.CC_STAT_WIDTH], stats[i, cv2.CC_STAT_AREA]
        keep[i] = i == main or (area >= stats[main, cv2.CC_STAT_AREA] * 0.03 and x > 0 and x + w < a.shape[1])
    a[~keep[labels]] = 0
    image.putalpha(Image.fromarray(a))
    alpha = a > 16
    ys, xs = np.nonzero(alpha)
    left, top, right, bottom = int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())
    # 锚点按生成时的骨架（figure.POSES）取：脚底 x 为两踝中点（坐姿为两胯中点，与占位坐姿的落点一致），
    # 脚底 y 为两踝附近列里最低的不透明像素，头顶为鼻子附近列里最高的不透明像素——长篙、刀尖、棍梢都不计入身高。
    from figure import CANVAS, POSES

    pose = POSES[record["pose"]]
    kx, ky = image.width / CANVAS[0], image.height / CANVAS[1]
    anchor = (pose[8], pose[11]) if record["pose"] == "sit" else (pose[10], pose[13])
    foot_x = (anchor[0][0] + anchor[1][0]) / 2 * kx
    feet_cols = np.zeros(image.width, dtype=bool)
    for ankle in (pose[10], pose[13]):
        feet_cols[max(0, int(ankle[0] * kx) - 70):int(ankle[0] * kx) + 70] = True
    bottom = int(ys[feet_cols[xs]].max())
    head_cols = np.abs(xs - pose[0][0] * kx) < 70
    head = args.top if args.top is not None else int(ys[head_cols].min())
    k = args.stature / (bottom - head)
    pad = 6
    box = (max(0, left - pad), max(0, top - pad), min(image.width, right + pad + 1), min(image.height, int(ys.max()) + pad + 1))
    out = image.crop(box)
    out = out.resize((round(out.width * k), round(out.height * k)), Image.LANCZOS)
    foot = [round((foot_x - box[0]) * k, 1), round((bottom - box[1]) * k, 1)]
    path = art_dir / f"{args.id}.png"
    out.save(path, optimize=True)
    shutil.copyfile(path, game_dir / f"{args.id}.png")
    placed = {"id": args.id, "foot": foot, "stature": args.stature, "source": f"art_source/ai/figure/{args.id}.png", "sha256": sha(path)}
    (game_dir / f"{args.id}.json").write_text(json.dumps(placed, ensure_ascii=False, indent=2), encoding="utf-8")
    fig = {"crop": list(box), "scale": round(k, 4), "top": head, "bottom": bottom, "stature": args.stature, "foot": foot}
    (art_dir / f"{args.id}.json").write_text(
        json.dumps({**record, "figure": fig, "placed_as": args.id, "placed_sha256": placed["sha256"]}, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"{args.id}  ←  {src.name}  {out.width}×{out.height}  foot {foot}  sha256 {placed['sha256'][:8]}…")
    return 0


if __name__ == "__main__":
    sys.exit(main())

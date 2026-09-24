"""把一组生成图拼成 4×2 对比图，左上角标种子，方便挑选。

用法：.venv/Scripts/python sheet.py out/<任务集> <名称前缀>
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

folder, prefix = Path(sys.argv[1]), sys.argv[2]
files = sorted(folder.glob(f"{prefix}_*.png"))
w, h = 416, 608
cols = 4
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (w * cols, h * rows), "white")
for i, f in enumerate(files):
    im = Image.open(f).convert("RGB").resize((w, h))
    ImageDraw.Draw(im).text((8, 8), f.stem.rsplit("_", 1)[-1], fill="red")
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
out = folder / f"sheet_{prefix}.jpg"
sheet.save(out, quality=88)
print(out)

"""Тёмная кайма для текстур RimWorld (кристаллы, артефакты, предметы).

Использование:
    python add_outline.py файл.png [файл2.png ...]
    python add_outline.py папка/          (все .png в папке, без _m масок)
Ключи:
    --width N    толщина каймы в пикселях на 512 px картинки (по умолчанию 6)
    --color R,G,B   цвет каймы (по умолчанию 18,14,22)
    --suffix S   сохранить рядом с суффиксом вместо перезаписи

Что делает: огрубляет край альфы (полупрозрачная бахрома становится чёткой),
расширяет силуэт на N пикселей и кладёт под предмет тёмную кайму с мягким
внешним краем в 1 px. Нужен Pillow (pip install pillow).
"""
import sys, os
from PIL import Image, ImageFilter, ImageChops

def outline(path, width=6, color=(18, 14, 22), suffix=None):
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    px = max(2, round(width * max(w, h) / 512))
    a = img.getchannel("A")
    # 1) harden the fringe: alpha above ~35% becomes solid, below it fades out
    hard = a.point(lambda v: 255 if v > 90 else int(v * 255 / 90 * 0.5))
    body = img.copy(); body.putalpha(hard)
    # 2) grow the silhouette for the rim
    mask = hard.point(lambda v: 255 if v > 40 else 0)
    grown = mask.filter(ImageFilter.MaxFilter(px * 2 + 1))
    grown = grown.filter(ImageFilter.GaussianBlur(0.8)).point(lambda v: 255 if v > 200 else int(v * 1.2))
    rim = Image.new("RGBA", (w, h), color + (0,)); rim.putalpha(grown)
    out = Image.alpha_composite(rim, body)
    dst = path if not suffix else os.path.splitext(path)[0] + suffix + ".png"
    out.save(dst)
    print("outlined", dst, f"({px}px)")

def main(argv):
    width, color, suffix, files = 6, (18, 14, 22), None, []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--width": width = float(argv[i + 1]); i += 2; continue
        if a == "--color": color = tuple(int(x) for x in argv[i + 1].split(",")); i += 2; continue
        if a == "--suffix": suffix = argv[i + 1]; i += 2; continue
        if os.path.isdir(a):
            files += [os.path.join(a, f) for f in sorted(os.listdir(a))
                      if f.lower().endswith(".png") and not f.lower().endswith("_m.png")]
        else: files.append(a)
        i += 1
    if not files: print(__doc__); return
    for f in files: outline(f, width, color, suffix)

if __name__ == "__main__":
    main(sys.argv[1:])

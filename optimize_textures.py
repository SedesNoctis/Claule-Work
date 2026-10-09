#!/usr/bin/env python3
"""
Shrink a RimWorld mod's textures without changing how they look in game.

What it does, per PNG in <mod>/Textures:
  * finds how big the texture is drawn: reads every <graphicData> in <mod>/Defs (texPath + drawSize)
    and allows 256 pixels per cell of drawSize (sharp even at full zoom), never less than 512;
    textures not named in any def get a cap by folder (UI 1024, pawns/motes 512, the rest 512);
  * downscales anything larger than its cap (high-quality Lanczos, aspect kept);
  * drops an alpha channel that is fully opaque, strips metadata, saves with maximum PNG compression;
  * optional --quantize: 256-colour palette with alpha (often 3-4x smaller still; may band soft glows).

Nothing is overwritten unless you pass --inplace: by default the result goes to <mod>/Textures_optimized,
so you can compare first. A report shows the biggest savings.

Usage (Windows: install Python, then `pip install pillow`):
    python optimize_textures.py "C:/.../Mods/AvirApexPredator"
    python optimize_textures.py "C:/.../Mods/AvirApexPredator" --inplace
    python optimize_textures.py "<mod>" --ppc 192 --quantize --inplace
"""
import argparse
import math
import os
import re
import shutil
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is needed:  pip install pillow")

SUFFIXES = ("_north", "_south", "_east", "_west")
FOLDER_CAPS = [("UI/", 1024), ("Things/Pawn/", 512), ("Things/Mote/", 512), ("Terrain/", 1024), ("", 512)]


def def_caps(mod, ppc):
    """texPath -> largest side in pixels it may need, from every graphicData in the defs."""
    caps = {}
    rx_block = re.compile(r"<(graphicData|wornGraphicData|uiIconPath)[^>]*>(.*?)</\1>", re.S)
    rx_path = re.compile(r"<texPath>\s*([^<\s]+)\s*</texPath>")
    rx_size = re.compile(r"<drawSize>\s*\(?\s*([\d.]+)\s*(?:,\s*([\d.]+))?\s*\)?\s*</drawSize>")
    for root, _, files in os.walk(os.path.join(mod, "Defs")):
        for f in files:
            if not f.endswith(".xml"):
                continue
            try:
                text = open(os.path.join(root, f), encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            for _, body in rx_block.findall(text):
                m = rx_path.search(body)
                if not m:
                    continue
                s = rx_size.search(body)
                size = max(float(s.group(1)), float(s.group(2) or s.group(1))) if s else 1.0
                # never below 512: code sometimes draws a def's texture larger than its drawSize
                px = max(512, int(math.ceil(size)) * ppc)
                key = m.group(1).replace("\\", "/").strip("/")
                caps[key] = max(caps.get(key, 0), px)
    return caps


def cap_for(rel, caps):
    """rel: path under Textures without extension, with forward slashes."""
    base = rel
    for suf in SUFFIXES:
        if base.endswith(suf) or base.endswith(suf + "m"):
            base = base[: base.rfind(suf)]
            break
    if base.endswith("_m"):
        base = base[:-2]
    # motes and flecks are scaled at run time - their drawSize says nothing
    if rel.startswith("Things/Mote/"):
        return 512, "folder"
    if base in caps:
        return caps[base], "def"
    parent = base.rsplit("/", 1)[0]          # Graphic_Random / Graphic_Appearances folders
    if parent in caps:
        return caps[parent], "def"
    for prefix, px in FOLDER_CAPS:
        if rel.startswith(prefix):
            return px, "folder"
    return 512, "folder"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mod", help="the mod folder (the one with About, Defs, Textures)")
    ap.add_argument("--ppc", type=int, default=256, help="pixels per cell of drawSize (default 256)")
    ap.add_argument("--quantize", action="store_true", help="256-colour palette (smaller, may band soft glows)")
    ap.add_argument("--inplace", action="store_true", help="overwrite the textures (default: write Textures_optimized)")
    a = ap.parse_args()

    tex = os.path.join(a.mod, "Textures")
    if not os.path.isdir(tex):
        sys.exit("No Textures folder in " + a.mod)
    out_root = tex if a.inplace else os.path.join(a.mod, "Textures_optimized")
    caps = def_caps(a.mod, a.ppc)
    before = after = 0
    rows = []
    for root, _, files in os.walk(tex):
        for f in files:
            src = os.path.join(root, f)
            rel_file = os.path.relpath(src, tex).replace("\\", "/")
            dst = os.path.join(out_root, rel_file)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            size0 = os.path.getsize(src)
            before += size0
            if not f.lower().endswith(".png"):
                if not a.inplace:
                    shutil.copy2(src, dst)
                after += size0
                continue
            cap, why = cap_for(rel_file[:-4], caps)
            im = Image.open(src)
            im.load()
            w, h = im.size
            if max(w, h) > cap:
                k = cap / max(w, h)
                im = im.convert("RGBA").resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)
            if im.mode in ("RGBA", "LA") and im.getchannel("A").getextrema()[0] == 255:
                im = im.convert("RGB")
            if a.quantize:
                im = im.quantize(256, method=Image.FASTOCTREE if im.mode == "RGBA" else Image.MEDIANCUT)
            tmp = dst + ".tmp"
            im.save(tmp, "PNG", optimize=True)
            size1 = os.path.getsize(tmp)
            if size1 >= size0 and (w, h) == im.size:
                os.remove(tmp)                    # no gain: keep the original bytes
                if not a.inplace:
                    shutil.copy2(src, dst)
                size1 = size0
            else:
                os.replace(tmp, dst)
            after += size1
            rows.append((size0 - size1, rel_file, (w, h), im.size, cap, why))

    rows.sort(reverse=True)
    print("Biggest savings:")
    for saved, rel, s0, s1, cap, why in rows[:30]:
        print("  %7.1f KB  %-70s %s -> %s (cap %d, %s)" % (saved / 1024, rel, s0, s1, cap, why))
    print("\nTextures: %.1f MB -> %.1f MB (%.0f%% smaller)" % (before / 2**20, after / 2**20, 100 * (1 - after / max(1, before))))
    print("Written to: " + out_root)
    if not a.inplace:
        print("Check them in game; to use them, replace Textures with Textures_optimized (or run again with --inplace).")


if __name__ == "__main__":
    main()

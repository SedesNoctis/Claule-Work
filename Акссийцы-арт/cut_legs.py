import sys, math
from PIL import Image, ImageFilter
import numpy as np
src, out = sys.argv[1], sys.argv[2]
im0 = Image.open(src).convert('RGBA'); W, H = im0.size
a = np.asarray(im0).astype(float)
alpha0 = a[:, :, 3] / 255.0
rng = np.random.default_rng(7)
# irregular feather tips like the drawn ones: uneven width and length, sharp points leaning back
_tip = np.zeros(4096)
x = 0
while x < 4096:
    w = int(rng.integers(9, 22)); d = float(rng.uniform(6, 17)); lean = float(rng.uniform(0.55, 0.8))
    for i in range(w):
        if x + i >= 4096: break
        u = i / w
        s = u / lean if u < lean else (1 - u) / (1 - lean)
        _tip[x + i] = d * s ** 0.7
    x += w
def tips(x):
    return _tip[x]
keep = np.ones((H, W))
X = np.arange(W)
def cut_below(xs, base_of):
    for x in xs:
        yb = base_of(x) + tips(x)
        y0 = int(yb)
        if y0 < H: keep[max(0, y0):, x] = 0
# no legs at all: one smooth lower contour from the tail root over a rounded haunch, a tucked waist, to the chest
pts=[(950,505),(1000,545),(1060,570),(1120,568),(1170,550),(1225,518),(1295,502),(1365,506),(1420,524),(1480,560),(1535,586)]
def curve(x):
    for (xa,ya),(xb,yb) in zip(pts,pts[1:]):
        if xa<=x<=xb:
            u=(x-xa)/(xb-xa); u=u*u*(3-2*u)
            return ya+(yb-ya)*u
    return None
cut_below(range(pts[0][0], pts[-1][0]), curve)
keep[600:, 860:1540] = 0
keep[548:, 860:955] = 0
# soft, anti-aliased edge
m = Image.fromarray((keep * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))
k = np.asarray(m) / 255.0
alpha = alpha0 * k
# thin soft outline along the new edge only (where the cut, not the drawing, made the border)
edge = np.clip((1 - np.asarray(Image.fromarray((keep * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.8))) / 255.0) * 2.2, 0, 1)
edge *= (k > 0.05) * (alpha0 > 0.5)
rgb = a[:, :, :3]
ink = np.array([70, 66, 68.0])
w = (edge * 0.45)[:, :, None]
rgb = rgb * (1 - w) + ink * w
res = Image.fromarray(np.dstack([rgb, alpha * 255]).astype(np.uint8), 'RGBA')
res = res.crop(res.getbbox()); res.save(out); print(res.size)

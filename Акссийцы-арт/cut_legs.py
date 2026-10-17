import sys, math
from PIL import Image, ImageFilter, ImageChops, ImageDraw
import numpy as np
src, out = sys.argv[1], sys.argv[2]
im0 = Image.open(src).convert('RGBA'); W, H = im0.size
im = im0.convert('RGB')
a = np.asarray(im).astype(int)
src_alpha = np.asarray(im0)[:, :, 3]
# background: near-white connected to the border
white = (a.min(axis=2) > 238)
from collections import deque
bg = np.zeros((H, W), bool); q = deque()
for x in range(W):
    for y in (0, H-1):
        if white[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
for y in range(H):
    for x in (0, W-1):
        if white[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
while q:
    y, x = q.popleft()
    for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
        ny, nx = y+dy, x+dx
        if 0 <= ny < H and 0 <= nx < W and white[ny, nx] and not bg[ny, nx]:
            bg[ny, nx] = True; q.append((ny, nx))
alpha = np.where(bg, 0, src_alpha).astype(np.uint8)
cut = np.zeros((H, W), bool)
X = np.arange(W)
def ragged(x, base, amp=5, period=26):
    return base + amp * np.abs(np.sin(x * math.pi / period))
# far leg: everything below the belly line between x 1140 and 1450
x0, x1 = 1140, 1450
for x in range(x0, x1):
    t = (x - x0) / (x1 - x0)
    yb = 572 + (548 - 572) * t - 10 * math.sin(t * math.pi) + ragged(x, 0)
    cut[int(yb):, x] = True
# near leg: keep the feathered thigh, round its lower edge
cx, rx, top, depth = 1020, 120, 555, 62
for x in range(cx - rx, cx + rx):
    t = (x - cx) / rx
    yb = top + depth * math.sqrt(max(0, 1 - t * t)) + ragged(x, 0, 4, 22)
    cut[int(yb):, x] = True
cut[640:, 860:1470] = True
alpha[cut] = 0
A = Image.fromarray(alpha)
# dark outline where the cut made a new edge
cutimg = Image.fromarray((cut * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))
edge = np.asarray(ImageChops.multiply(cutimg, A)) > 0
rgb = a.copy()
ol = np.array([62, 58, 60])
rgb[edge] = (rgb[edge] * 0.25 + ol * 0.75).astype(int)
res = Image.fromarray(np.dstack([rgb.astype(np.uint8), alpha]), 'RGBA')
res = res.crop(res.getbbox())
res.save(out)
print(res.size)

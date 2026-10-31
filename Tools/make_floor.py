"""Repaint GroundFlesh.png in the 'readable tissue' style.

Keeps the exact alpha mask (so slicing / rule tiles keep working) and repaints the colours:
dark muted crimson floor, soft pink membrane rim, a few low-contrast cells, rare capillaries.
Details stay away from 32px tile borders so tiles join seamlessly under rotation/flip.
Usage: python3 -I make_floor.py <original.png> <out.png> <preview.png>
"""
import random
import sys
from collections import deque

from PIL import Image

TILE = 32
MARGIN = 4  # keep cells/capillaries this far from tile borders

BASE = (90, 20, 36)          # #5a1424
BASE_DARK = (82, 17, 32)     # dither tones, very close to base
BASE_LIGHT = (97, 23, 40)
CELL_FILL = (104, 28, 46)    # cells only slightly lighter than floor
CELL_WALL = (74, 15, 29)
CELL_SHINE = (118, 36, 56)
CAPILLARY = (122, 30, 44)
RIM = {  # distance from transparent edge -> colour
    1: (214, 104, 128),   # light pink membrane
    2: (170, 62, 88),
    3: (122, 34, 56),
    4: (70, 13, 27),      # shadow just inside the membrane
}

src, out_path, preview_path = sys.argv[1], sys.argv[2], sys.argv[3]
im = Image.open(src).convert("RGBA")
W, H = im.size
alpha = [[im.getpixel((x, y))[3] for x in range(W)] for y in range(H)]
solid = lambda x, y: 0 <= x < W and 0 <= y < H and alpha[y][x] > 0

# Chebyshev distance from each solid pixel to the nearest transparent pixel (sheet edges count as solid,
# because the art continues into neighbouring tiles in the level).
INF = 99
dist = [[INF if alpha[y][x] else 0 for x in range(W)] for y in range(H)]
q = deque((x, y) for y in range(H) for x in range(W) if not alpha[y][x])
while q:
    x, y = q.popleft()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            nx, ny = x + dx, y + dy
            if 0 <= nx < W and 0 <= ny < H and dist[ny][nx] > dist[y][x] + 1:
                dist[ny][nx] = dist[y][x] + 1
                q.append((nx, ny))

out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = out.load()

# 1) Floor: base colour with a sparse, tile-periodic dither so seams never show.
rng = random.Random(7)
dither = [[rng.random() for _ in range(TILE)] for _ in range(TILE)]
for y in range(H):
    for x in range(W):
        if not alpha[y][x]:
            continue
        d = dither[y % TILE][x % TILE]
        c = BASE_DARK if d < 0.12 else BASE_LIGHT if d > 0.90 else BASE
        px[x, y] = c + (255,)


def interior(x, y, need):
    """Pixel is far enough from both the tissue edge and its tile border."""
    lx, ly = x % TILE, y % TILE
    return (MARGIN <= lx < TILE - MARGIN and MARGIN <= ly < TILE - MARGIN
            and dist[y][x] >= need)


# 2) Cells and capillaries, placed per tile.
for ty in range(H // TILE):
    for tx in range(W // TILE):
        trng = random.Random(1000 + ty * 31 + tx)
        ox, oy = tx * TILE, ty * TILE

        for _ in range(trng.choice((1, 2, 2, 3))):
            r = trng.choice((3, 4, 4, 5))
            cx = ox + trng.randint(MARGIN + r, TILE - MARGIN - r - 1)
            cy = oy + trng.randint(MARGIN + r, TILE - MARGIN - r - 1)
            if not all(interior(cx + dx, cy + dy, 7)
                       for dx in (-r, 0, r) for dy in (-r, 0, r)):
                continue
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    e = (dx * dx + dy * dy) / (r * r + 0.5)
                    if e > 1.0:
                        continue
                    x, y = cx + dx, cy + dy
                    if e > 0.62:
                        col = CELL_WALL
                    elif dx <= -1 and dy <= -1 and e < 0.35:
                        col = CELL_SHINE  # tiny top-left highlight
                    else:
                        col = CELL_FILL
                    px[x, y] = col + (255,)

        # Rare thin capillary: a short wandering line.
        if trng.random() < 0.22:
            x = ox + trng.randint(MARGIN + 2, TILE - MARGIN - 3)
            y = oy + trng.randint(MARGIN + 2, TILE - MARGIN - 3)
            for _ in range(trng.randint(6, 11)):
                if not interior(x, y, 6):
                    break
                if px[x, y][:3] in (BASE, BASE_DARK, BASE_LIGHT):
                    px[x, y] = CAPILLARY + (255,)
                x += trng.choice((-1, 0, 1, 1))
                y += trng.choice((-1, 0, 1))

# 3) Membrane rim along the tissue edge (drawn last so it sits on top).
for y in range(H):
    for x in range(W):
        if alpha[y][x] and dist[y][x] in RIM:
            px[x, y] = RIM[dist[y][x]] + (255,)

out.save(out_path)

# Preview: old vs new, 3x, on a neutral background, with a unit-coloured dot for contrast checking.
scale = 3
prev = Image.new("RGBA", (W * 2 * scale + 24, H * scale), (40, 40, 48, 255))
prev.alpha_composite(im.resize((W * scale, H * scale), Image.NEAREST), (0, 0))
prev.alpha_composite(out.resize((W * scale, H * scale), Image.NEAREST), (W * scale + 24, 0))
prev.save(preview_path)
print("wrote", out_path, preview_path)

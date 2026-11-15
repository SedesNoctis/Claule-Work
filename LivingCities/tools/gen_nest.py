"""Prototype of new Avir nest layouts (Living Cities), drawn from the author's Wildsong Nesting stages:
round halls and rooms at the edges, rectangular modules inside, a central spine, rooms of one purpose in identical rows,
a ring of rooms round a great round dining hall, a 'clover' of round nursery rooms, a dome with classes and a temple,
round fields with a totem, sky wells in every big round room, heavy doors only to the outside, some parts reached only
by flight. Writes a preview PNG (and a JSON plan the C# generator will read later).
usage: gen_nest.py <template> <seed> <out.png>"""
import sys, math, json, random
from PIL import Image, ImageDraw, ImageFont

EMPTY, GROUND, FLOOR, WALL, DOOR, HEAVY, WATER, BRIDGE, ROCK, FIELD = range(10)
N = 140

ROLE_COL = {
    'hall': (214, 178, 110), 'dining': (214, 178, 110), 'kitchen': (200, 140, 90), 'bedroom': (120, 150, 200),
    'couple': (150, 130, 210), 'nursery': (240, 170, 190), 'staff': (110, 170, 170), 'cell': (120, 110, 110),
    'workshop': (200, 120, 70), 'artificer': (230, 120, 60), 'library': (150, 110, 80), 'ritual': (190, 120, 220),
    'temple': (230, 210, 120), 'class': (170, 200, 120), 'store': (140, 140, 110), 'guest': (130, 200, 140),
    'gallery': (175, 165, 150), 'street': (175, 165, 150), 'herbalist': (110, 190, 90), 'barracks': (180, 90, 90),
    'freezer': (170, 210, 230), 'market': (220, 200, 90), 'shop': (235, 190, 70), 'slaves': (120, 95, 80), 'shed': (150, 120, 90),
}
ROLE_RU = {
    'dining': 'столовая', 'hall': 'зал', 'kitchen': 'кухня', 'bedroom': 'спальня', 'couple': 'спальня пары',
    'nursery': 'птенческая', 'staff': 'комнаты персонала', 'cell': 'камеры', 'workshop': 'мастерская',
    'artificer': 'мастерская артефактора', 'library': 'библиотека', 'ritual': 'ритуалист', 'temple': 'храм',
    'class': 'класс', 'store': 'склад', 'guest': 'гостевые', 'gallery': 'галерея', 'street': 'улица',
    'herbalist': 'алхимик и садик', 'barracks': 'казарма', 'freezer': 'морозильник', 'market': 'рынок', 'shop': 'лавки',
    'slaves': 'барак рабов', 'shed': 'склад двора',
}


class Plan:
    def __init__(self, seed):
        self.g = [[GROUND] * N for _ in range(N)]
        self.room = [[-1] * N for _ in range(N)]
        self.rooms = []        # [{role, cells}]
        self.wells = []        # (x, z, r)
        self.totems = []
        self.posts = []        # (x, z, kind) - guard posts
        self.rnd = random.Random(seed)

    def ok(self, x, z): return 0 <= x < N and 0 <= z < N

    # a 16-gon with flat sides to the four directions: stair-stepped, no bare diagonal
    @staticmethod
    def inpoly(dx, dz, r, n=16):
        return max(dx * math.cos(2 * math.pi * k / n) + dz * math.sin(2 * math.pi * k / n) for k in range(n)) <= r

    def new_room(self, role):
        self.rooms.append({'role': role, 'cells': []}); return len(self.rooms) - 1

    def paint(self, cells, role):
        """Cells become one room: floor inside, walls round its rim (shared with neighbours)."""
        rid = self.new_room(role)
        S = set(cells)
        for (x, z) in S:
            if not self.ok(x, z): continue
            rim = any((x + a, z + b) not in S for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if rim:
                if self.g[x][z] not in (DOOR, HEAVY): self.g[x][z] = WALL
                self.room[x][z] = -1
            else:
                self.g[x][z] = FLOOR; self.room[x][z] = rid
        return rid

    def disc(self, cx, cz, r, role, n=16):
        cells = [(cx + dx, cz + dz) for dx in range(-r - 1, r + 2) for dz in range(-r - 1, r + 2) if self.inpoly(dx, dz, r + 0.5, n)]
        return self.paint(cells, role)

    def rect(self, x0, z0, w, h, role):
        return self.paint([(x, z) for x in range(x0, x0 + w) for z in range(z0, z0 + h)], role)

    def ring(self, cx, cz, r1, r2, parts, roles, start=0.0):
        """A ring of wedge rooms between r1 and r2, cut by stepped spokes."""
        ids = []
        for i in range(parts):
            a0 = start + 2 * math.pi * i / parts; a1 = start + 2 * math.pi * (i + 1) / parts
            cells = []
            for dx in range(-r2 - 1, r2 + 2):
                for dz in range(-r2 - 1, r2 + 2):
                    if not self.inpoly(dx, dz, r2 + 0.5) or self.inpoly(dx, dz, r1 - 0.5): continue
                    a = math.atan2(dz, dx) % (2 * math.pi)
                    lo, hi = a0 % (2 * math.pi), a1 % (2 * math.pi)
                    inside = lo <= a < hi if lo < hi else (a >= lo or a < hi)
                    if inside: cells.append((cx + dx, cz + dz))
            ids.append(self.paint(cells, roles[i % len(roles)]))
        return ids

    def well(self, rid, r):
        cs = self.rooms[rid]['cells_floor'] if 'cells_floor' in self.rooms[rid] else None
        xs = [(x, z) for x in range(N) for z in range(N) if self.room[x][z] == rid]
        if not xs: return
        mx = sum(c[0] for c in xs) / len(xs); mz = sum(c[1] for c in xs) / len(xs)
        self.wells.append((round(mx), round(mz), r))

    def field(self, cx, cz, r):
        for dx in range(-r, r + 1):
            for dz in range(-r, r + 1):
                if self.inpoly(dx, dz, r + 0.5) and self.ok(cx + dx, cz + dz) and self.g[cx + dx][cz + dz] == GROUND:
                    self.g[cx + dx][cz + dz] = FIELD
        self.totems.append((cx, cz))

    def wide_field(self, x0, z0, w, h):
        for x in range(x0, x0 + w):
            for z in range(z0, z0 + h):
                if self.ok(x, z) and self.g[x][z] == GROUND: self.g[x][z] = FIELD

    def slave_yard(self, x0, z0, fields_w=36, fields_h=12):
        """The slave yard just outside the city (the author): a barrack, a shed for what they gather (a carrier takes it
        into the city), wide fields beside them."""
        self.rect(x0, z0, 12, 8, 'slaves')
        self.rect(x0 + 11, z0, 6, 8, 'shed')
        self.wide_field(x0 - 2, z0 - fields_h - 3, fields_w, fields_h)
        self.posts.append((x0 + 14, z0 + 8, 'overseer'))

    def river(self, x0, width, wobble):
        for z in range(N):
            c = x0 + int(wobble * math.sin(z / 17.0))
            for x in range(c - width // 2, c + width // 2 + 1):
                if self.ok(x, z) and self.g[x][z] == GROUND: self.g[x][z] = WATER

    def bridge(self, z, x0, x1):
        """A pair of jetties from both banks that do not meet (the author: the bridges do not join the banks - the Avir
        fly across; the one way over is flight)."""
        water = [x for x in range(x0, x1 + 1) if self.ok(x, z) and self.g[x][z] == WATER]
        if not water: return
        mid = (water[0] + water[-1]) // 2
        for x in water:
            if abs(x - mid) <= 1: continue
            for dz in (0, 1, 2):
                if self.ok(x, z + dz) and self.g[x][z + dz] == WATER: self.g[x][z + dz] = BRIDGE

    def mountain(self, cx, cz, r):
        for x in range(N):
            for z in range(N):
                d = math.hypot(x - cx, z - cz) + 6 * math.sin(x / 9.0) * math.cos(z / 11.0)
                if d < r: self.g[x][z] = ROCK

    # ---------------------------------------------------------------- doors
    max_gates = 3

    def open_out(self, c):
        """A wall cell facing open ground for a few cells - a real way out, not a nook between two buildings."""
        x, z = c
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if all(self.ok(x + dx * k, z + dz * k) and self.g[x + dx * k][z + dz * k] in (GROUND, FIELD, BRIDGE) for k in (1, 2, 3, 4)): return True
        return False

    def cut(self, cell):
        if isinstance(cell[0], tuple):
            for (x, z) in cell: self.g[x][z] = DOOR
        else: self.g[cell[0]][cell[1]] = DOOR

    def doors(self, flight_only=()):
        """Plain doors joining every room into one tree from the galleries/streets; heavy doors from a gallery/street
        out to open ground (the only heavy doors). flight_only: roles left without a ground way (reached by air)."""
        nr = len(self.rooms)
        adj = {}
        for x in range(1, N - 1):
            for z in range(1, N - 1):
                if self.g[x][z] != WALL: continue
                for (a, b), (c, d) in (((x - 1, z), (x + 1, z)), ((x, z - 1), (x, z + 1))):
                    # a straight wall: walls on the other two sides
                    if (a, b) == (x - 1, z):
                        side = self.g[x][z - 1] == WALL and self.g[x][z + 1] == WALL
                    else:
                        side = self.g[x - 1][z] == WALL and self.g[x + 1][z] == WALL
                    if not side: continue
                    r1, r2 = self.room[a][b], self.room[c][d]
                    if r1 >= 0 and r2 >= 0 and r1 != r2:
                        adj.setdefault((min(r1, r2), max(r1, r2)), []).append((x, z))
                    elif (r1 >= 0) != (r2 >= 0):
                        rr = r1 if r1 >= 0 else r2
                        oc = (c, d) if r1 >= 0 else (a, b)
                        if self.g[oc[0]][oc[1]] in (GROUND, FIELD, BRIDGE):
                            adj.setdefault((rr, -1), []).append((x, z))
        # two walls back to back between two rooms (a module set against another): a door through both
        for x in range(2, N - 2):
            for z in range(2, N - 2):
                if self.g[x][z] != WALL: continue
                for (ax, az), (bx, bz), (cx, cz), sx, sz in (((x - 1, z), (x + 1, z), (x + 2, z), 0, 1), ((x, z - 1), (x, z + 1), (x, z + 2), 1, 0)):
                    if self.g[bx][bz] != WALL: continue
                    if not all(self.g[x + sx * k][z + sz * k] == WALL and self.g[bx + sx * k][bz + sz * k] == WALL for k in (-1, 1)): continue
                    r1, r2 = self.room[ax][az], self.room[cx][cz]
                    if r1 >= 0 and r2 >= 0 and r1 != r2:
                        adj.setdefault((min(r1, r2), max(r1, r2)), []).append(((x, z), (bx, bz)))
        hubs = [i for i, r in enumerate(self.rooms) if r['role'] in ('gallery', 'street')]
        if not hubs: hubs = [0]
        seen = set(hubs); frontier = list(hubs)
        # heavy doors: only in the outer walls, to open ground, few and far apart - the main gallery first
        def area(i): return sum(1 for x in range(N) for z in range(N) if self.room[x][z] == i)
        gates = []
        for h in sorted(hubs, key=area, reverse=True):
            cands = [c for c in adj.get((h, -1), []) if self.open_out(c)]
            self.rnd.shuffle(cands)
            for (x, z) in cands:
                if len(gates) >= self.max_gates: break
                if any(abs(x - a) + abs(z - b) < 18 for a, b in gates): continue
                gates.append((x, z))
                if len([g for g in gates if g in cands]) >= 2: break
        for (x, z) in gates:
            self.g[x][z] = HEAVY; self.posts.append((x, z, 'gate'))
        while frontier:
            r = frontier.pop(0)
            for (a, b), cells in adj.items():
                if b == -1: continue
                o = b if a == r else a if b == r else None
                if o is None or o in seen: continue
                if self.rooms[o]['role'] in flight_only: continue
                self.cut(cells[len(cells) // 2])
                seen.add(o); frontier.append(o)
        # rooms left without a door are joined to any neighbour already reached
        changed = True
        while changed:
            changed = False
            for (a, b), cells in adj.items():
                if b == -1: continue
                if (a in seen) != (b in seen):
                    o = b if a in seen else a
                    if self.rooms[o]['role'] in flight_only: continue
                    self.cut(cells[len(cells) // 2]); seen.add(o); changed = True
        # the slave yard stands outside the city: its own plain doors to the open ground, none into the city
        for i, r in enumerate(self.rooms):
            if r['role'] not in ('slaves', 'shed'): continue
            cands = [c for c in adj.get((i, -1), []) if self.open_out(c)]
            if cands: self.cut(cands[len(cands) // 2])
            for (a, b), cells in adj.items():
                if b != -1 and {a, b} == {i, next((k for k, q in enumerate(self.rooms) if q['role'] == ('shed' if r['role'] == 'slaves' else 'slaves')), -2)}:
                    self.cut(cells[len(cells) // 2])
            seen.add(i)
        self.unreached = [i for i in range(nr) if i not in seen and self.rooms[i]['role'] not in flight_only]


# ======================================================================= templates

def t_ring(seed):
    """The Ring Nest: a great round dining hall within a ring of wedge rooms, a gallery round it all; a clover of
    round nurseries to the north, a street of identical staff rooms to the east, round fields with totems to the south."""
    p = Plan(seed); c = N // 2
    p.disc(c, c, 23, 'gallery')                                   # outer gallery (rooms laid over it)
    p.ring(c, c, 10, 20, 8, ['couple', 'couple', 'bedroom', 'workshop', 'kitchen', 'store', 'library', 'artificer'], start=math.pi / 8)
    d = p.disc(c, c, 9, 'dining'); p.well(d, 3)
    # clover nursery: three round rooms round a small hub, north
    hz = c + 31
    hub = p.disc(c, hz, 4, 'gallery')
    for ang in (90, 210, 330):
        x = c + int(8 * math.cos(math.radians(ang))); z = hz + int(8 * math.sin(math.radians(ang)))
        n = p.disc(x, z, 5, 'nursery'); p.well(n, 1)
    p.rect(c - 1, c + 22, 3, 6, 'gallery')                         # passage gallery -> clover
    # street of staff rooms, east
    sx = c + 23
    p.rect(sx, c - 2, 34, 5, 'street')
    for i in range(5):
        p.rect(sx + 2 + i * 6, c + 2, 7, 7, 'staff'); p.rect(sx + 2 + i * 6, c - 8, 7, 7, 'shop' if i < 4 else 'cell')
    cd = p.disc(sx + 39, c, 6, 'dining'); p.well(cd, 1)
    p.slave_yard(c - 18, c - 42, 40, 14)
    p.doors(); return p, 'Кольцевое гнездовье'


def t_river(seed):
    """The River Nest: the village on both banks of a river, two bridges that do not join the banks for walkers -
    the Avir fly across. West: a great hall within a ring of homes; east: the guild quarter - workshops round a court,
    a row of cells, the alchemist's room with a garden."""
    p = Plan(seed); c = N // 2
    p.river(c + 4, 9, 6)
    w = c - 30
    p.disc(w, c, 21, 'gallery')
    p.ring(w, c, 9, 18, 6, ['couple', 'bedroom', 'couple', 'kitchen', 'store', 'nursery'])
    d = p.disc(w, c, 8, 'dining'); p.well(d, 2)
    e = c + 34
    p.rect(e - 14, c - 26, 6, 52, 'street')
    for i in range(6):
        p.rect(e - 9, c - 24 + i * 8, 9, 9, ['workshop', 'artificer', 'staff', 'staff', 'library', 'ritual'][i])
    for i in range(3):
        p.rect(e - 21, c - 18 + i * 4, 8, 5, 'cell')
    p.rect(e - 1, c + 5, 6, 3, 'gallery'); p.rect(e - 1, c - 15, 6, 3, 'gallery')
    hb = p.disc(e + 10, c + 6, 7, 'herbalist'); p.well(hb, 2)
    gd = p.disc(e + 10, c - 14, 7, 'guest'); p.well(gd, 1)
    p.bridge(c - 40, c - 6, c + 14); p.bridge(c + 38, c - 6, c + 14)
    p.slave_yard(w - 14, c - 48, 34, 12)
    for i in range(3): p.rect(e - 21, c + 2 + i * 6, 8, 7, 'shop')
    p.doors(); return p, 'Речное гнездовье'


def t_dome(seed):
    """The Dome Nest: a great dome - a temple with the great circle at its heart, classes round it; a residential row
    to the south, a clover of chick rooms to the west, the artificers' round hall and the library to the east."""
    p = Plan(seed); c = N // 2
    p.disc(c, c + 6, 22, 'gallery')
    p.ring(c, c + 6, 9, 19, 6, ['class', 'class', 'library', 'class', 'ritual', 'store'], start=math.pi / 6)
    t = p.disc(c, c + 6, 8, 'temple'); p.well(t, 3)
    p.rect(c - 26, c - 30, 52, 5, 'street')
    for i in range(7):
        p.rect(c - 25 + i * 7, c - 25, 8, 9, 'couple' if i % 3 == 0 else 'bedroom')
        p.rect(c - 25 + i * 7, c - 38, 8, 9, ['staff', 'staff', 'kitchen', 'dining', 'freezer', 'guest', 'guest'][i])
    p.rect(c - 2, c - 25, 5, 10, 'gallery')                          # spine dome -> street
    hz, hx = c + 6, c - 36
    p.disc(hx, hz, 4, 'gallery'); p.rect(hx + 4, hz - 1, 11, 3, 'gallery')
    for ang in (90, 180, 270):
        n = p.disc(hx + int(8 * math.cos(math.radians(ang))), hz + int(8 * math.sin(math.radians(ang))), 5, 'nursery'); p.well(n, 1)
    a = p.disc(c + 38, c + 6, 9, 'artificer'); p.well(a, 2)
    p.rect(c + 21, c + 4, 9, 5, 'gallery')
    p.slave_yard(c - 16, c - 58, 44, 10)
    for i in range(3): p.rect(c + 30, c - 30 + i * 6, 8, 7, 'shop')
    p.rect(c + 25, c - 30, 6, 5, 'street')
    p.doors(); return p, 'Купольное гнездовье'


def t_mountain(seed):
    """The Mountain Nest: halls carved into the rock, every one open to the sky by a well; galleries cut as tunnels;
    the only heavy door at the mouth of the valley. The upper halls (the guild's) are reached only by flight."""
    p = Plan(seed); c = N // 2
    p.mountain(c, c, 60)
    def carve(cells):
        for (x, z) in cells:
            if p.ok(x, z): p.g[x][z] = GROUND
    carve([(x, z) for x in range(c - 4, c + 5) for z in range(0, c - 10)])     # the valley mouth
    p.rect(c - 3, c - 40, 7, 32, 'gallery')
    h = p.disc(c, c, 12, 'hall'); p.well(h, 4)
    p.ring(c, c, 13, 22, 7, ['couple', 'bedroom', 'nursery', 'kitchen', 'store', 'workshop', 'barracks'], start=-math.pi / 2 + 0.3)
    for i, (dx, dz, role) in enumerate(((-30, 18, 'library'), (30, 18, 'artificer'), (0, 34, 'temple'))):
        r = p.disc(c + dx, c + dz, 7, role); p.well(r, 2)
    for i in range(3):
        p.rect(c + 3, c - 36 + i * 7, 8, 8, 'cell' if i < 2 else 'staff')
        p.rect(c - 10, c - 36 + i * 7, 8, 8, 'staff')
    p.slave_yard(c - 8, c - 66, 24, 8)
    for i in range(2): p.rect(c - 10, c - 15 + i * 7, 8, 8, 'shop')
    p.doors(flight_only=('library', 'artificer', 'temple')); return p, 'Горное гнездовье'


TEMPLATES = {'ring': t_ring, 'river': t_river, 'dome': t_dome, 'mountain': t_mountain}


def render(p, title, out, scale=6):
    S = scale
    img = Image.new('RGB', (N * S, N * S + 150), (24, 22, 26))
    d = ImageDraw.Draw(img)
    base = {GROUND: (78, 92, 62), WATER: (58, 88, 140), BRIDGE: (140, 105, 60), ROCK: (70, 66, 72), FIELD: (96, 132, 60), EMPTY: (0, 0, 0)}
    used = set()
    for x in range(N):
        for z in range(N):
            v = p.g[x][z]; y = N - 1 - z
            if v == FLOOR:
                role = p.rooms[p.room[x][z]]['role']; used.add(role); col = ROLE_COL.get(role, (200, 200, 200))
            elif v == WALL: col = (40, 36, 34)
            elif v == DOOR: col = (230, 230, 230)
            elif v == HEAVY: col = (255, 70, 40)
            else: col = base.get(v, (0, 0, 0))
            d.rectangle([x * S, y * S, x * S + S - 1, y * S + S - 1], fill=col)
    for (x, z, r) in p.wells:
        y = N - 1 - z
        d.ellipse([(x - r) * S, (y - r) * S, (x + r + 1) * S, (y + r + 1) * S], outline=(130, 220, 255), width=2)
    for (x, z, k) in p.posts:
        y = N - 1 - z
        d.polygon([(x * S + S // 2, y * S - 6), (x * S - 5, y * S + S + 4), (x * S + S + 5, y * S + S + 4)], outline=(255, 255, 255))
    for (x, z) in p.totems:
        y = N - 1 - z; d.ellipse([x * S - 3, y * S - 3, x * S + S + 3, y * S + S + 3], fill=(250, 240, 120))
    try:
        f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 14)
        fb = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 20)
    except Exception:
        f = fb = None
    top = N * S + 8
    d.text((10, top), title, fill=(240, 230, 210), font=fb)
    items = [(ROLE_RU[r], ROLE_COL[r]) for r in sorted(used) if r in ROLE_RU] + [('тяжёлая дверь (наружу)', (255, 70, 40)), ('дверь', (230, 230, 230)), ('лётный колодец', (130, 220, 255)), ('поле (работают рабы)', (96, 132, 60)), ('пост (треугольник)', (255, 255, 255))]
    x0, y0 = 10, top + 34
    for i, (name, col) in enumerate(items):
        cx = x0 + (i % 4) * (N * S // 4); cy = y0 + (i // 4) * 22
        d.rectangle([cx, cy + 3, cx + 14, cy + 17], fill=col)
        d.text((cx + 20, cy + 2), name, fill=(220, 215, 200), font=f)
    img.save(out)


def export(p, title, out):
    rooms = []
    for i, r in enumerate(p.rooms):
        cells = [(x, z) for x in range(N) for z in range(N) if p.room[x][z] == i]
        if cells: rooms.append({'role': r['role'], 'cells': len(cells)})
    json.dump({'title': title, 'size': N, 'grid': [''.join('.gfwdHrbRF'[p.g[x][z]] for x in range(N)) for z in range(N)],
               'wells': p.wells, 'totems': p.totems, 'posts': p.posts, 'rooms': rooms, 'unreached': p.unreached}, open(out, 'w'), ensure_ascii=False)


if __name__ == '__main__':
    name, seed, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    p, title = TEMPLATES[name](seed)
    render(p, title, out)
    export(p, title, out.replace('.png', '.json'))
    print(title, 'rooms', len(p.rooms), 'unreached', len(p.unreached), 'gates', sum(1 for x in range(N) for z in range(N) if p.g[x][z] == HEAVY))

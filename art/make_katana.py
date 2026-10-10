# builds the katana model for Iaido_Katana: one mesh for both katanas and a 128x128 pixel texture per katana.
# run: blender -b --python make_katana.py -- [--render]
# the mesh is written in the unity frame of krom's mesh node: y along the sword from the pommel (0), x toward the
# cutting edge, z the blade's thickness. the mod puts it under the item's "attach" like krom's own mesh.
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
TEX = 128

# --- shape, meters ------------------------------------------------------------------------------------------
KASHIRA = (0.000, 0.022)
TSUKA = (0.022, 0.415)
FUCHI = (0.415, 0.437)
TSUBA = (0.437, 0.452)
HABAKI_LEN = 0.050
BLADE_START = TSUBA[1]
BLADE_LEN = 1.702     # 1.205, then 1.386 (+15%), then +30 cm in game (0.316 here, the player model is scaled 0.95)
SORI = 0.037          # depth of the curve over the blade (grows with the length)
MOTOHABA = 0.046      # width at the base
SAKIHABA = 0.034      # width at the start of the point
KASANE = (0.011, 0.0075)
KISSAKI = 0.087       # length of the point (grows with the length)
SHINOGI = 0.30        # ridge line, share of the width from the spine
TSUKA_R = (0.0178, 0.0138)  # half width toward the edge and half thickness
EDGE_SIDE = -1        # which side of the mesh's x the edge ends up on in the exported obj
TSUBA_R = (0.056, 0.050)

# --- atlas regions in pixels: x0, y0, x1, y1 (y up, like uv) --------------------------------------------------
R_HIRA = (0, 96, 128, 120)
R_SHINOGIJI = (0, 120, 128, 124)
R_MUNE = (0, 124, 128, 128)
R_HABAKI = (16, 40, 32, 56)
R_TSUKA = (0, 0, 96, 32)
R_FITTING = (0, 40, 16, 56)
R_TSUBA = (96, 0, 128, 32)
R_RIM = (96, 32, 128, 40)


def uv_in(region, u, v):
    # u, v in 0..1 inside a region, kept half a pixel off its border so point sampling never bleeds
    x0, y0, x1, y1 = region
    u = np.clip(u, 0, 1)
    v = np.clip(v, 0, 1)
    return ((x0 + 0.5 + u * (x1 - x0 - 1)) / TEX, (y0 + 0.5 + v * (y1 - y0 - 1)) / TEX)


class Mesh:
    def __init__(self):
        self.v, self.n, self.uv, self.tris = [], [], [], []

    def add(self, p, n, uv):
        self.v.append(np.asarray(p, float))
        self.n.append(np.asarray(n, float) / (np.linalg.norm(n) + 1e-12))
        self.uv.append(uv)
        return len(self.v) - 1

    def tri(self, a, b, c):
        # unity draws a triangle's front where cross(b-a, c-a) points, so orient by the vertex normals
        va, vb, vc = self.v[a], self.v[b], self.v[c]
        cr = np.cross(vb - va, vc - va)
        if np.linalg.norm(cr) < 1e-6:  # slivers under 0.5 mm2 at the point are invisible and their facing is only rounding
            return
        if np.dot(cr, self.n[a] + self.n[b] + self.n[c]) < 0:
            b, c = c, b
        self.tris.append((a, b, c))

    def quad(self, a, b, c, d):
        self.tri(a, b, c)
        self.tri(a, c, d)


def strip(mesh, rings_a, rings_b, normals_a, normals_b, uvs_a, uvs_b):
    # one faceted band between two point paths that run along the sword
    ia = [mesh.add(p, n, uv) for p, n, uv in zip(rings_a, normals_a, uvs_a)]
    ib = [mesh.add(p, n, uv) for p, n, uv in zip(rings_b, normals_b, uvs_b)]
    for i in range(len(ia) - 1):
        mesh.quad(ia[i], ia[i + 1], ib[i + 1], ib[i])


# --- blade -------------------------------------------------------------------------------------------------
RADIUS = BLADE_LEN ** 2 / (8 * SORI) + SORI / 2


def blade_frame(s):
    # spine point, unit toward the edge, unit along the blade at arc length s
    a = s / RADIUS
    spine0 = -MOTOHABA / 2
    p = np.array([spine0 - (RADIUS - RADIUS * math.cos(a)), BLADE_START + RADIUS * math.sin(a), 0.0])
    edge = np.array([math.cos(a), math.sin(a), 0.0])
    along = np.array([-math.sin(a), math.cos(a), 0.0])
    return p, edge, along


def blade_section(s):
    # width and thickness at s, with the point narrowing along a curved edge (fukura)
    body = BLADE_LEN - KISSAKI
    if s <= body:
        t = s / body
        return MOTOHABA + (SAKIHABA - MOTOHABA) * t, KASANE[0] + (KASANE[1] - KASANE[0]) * t
    u = min(1.0, (s - body) / KISSAKI)
    return SAKIHABA * math.sqrt(max(0.0, 1 - u ** 2)), KASANE[1] * (1 - u) ** 0.8


# cross section: (share of width from the spine, share of thickness), going around
PROFILE = [(1.0, 0.0), (SHINOGI, 0.5), (0.06, 0.36), (0.0, 0.0), (0.06, -0.36), (SHINOGI, -0.5)]
# which texture band each side of the section uses: hira (flat with the temper line), shinogi-ji, mune
BANDS = ["hira", "shinogiji", "mune", "mune", "shinogiji", "hira"]


def blade_points(s, grow=0.0):
    p, edge, _ = blade_frame(s)
    w, t = blade_section(s)
    pts = []
    for a, z in PROFILE:
        q = p + edge * (a * w + (grow if a > 0.5 else -grow if a < 0.05 else 0))
        q = q + np.array([0, 0, z * t + np.sign(z) * grow])
        pts.append(q)
    return pts


def band_uv(band, u, v):
    region = {"hira": R_HIRA, "shinogiji": R_SHINOGIJI, "mune": R_MUNE}[band]
    return uv_in(region, u, v)


def build_blade(mesh):
    body = BLADE_LEN - KISSAKI
    ss = list(np.linspace(0, body, 44)) + list(np.linspace(body, BLADE_LEN * 0.9995, 14)[1:])
    rings = [blade_points(s) for s in ss]
    n = len(PROFILE)
    for j in range(n):
        k = (j + 1) % n
        pa = [r[j] for r in rings]
        pb = [r[k] for r in rings]
        # side normal: across the band, away from the blade's middle
        normals = []
        for i, s in enumerate(ss):
            _, _, along = blade_frame(s)
            d = pb[i] - pa[i]
            nrm = np.cross(along, d) if np.linalg.norm(d) > 1e-9 else np.array([0, 0, 1.0])
            mid = np.mean(rings[i], axis=0)
            if np.dot(nrm, (pa[i] + pb[i]) / 2 - mid) < 0:
                nrm = -nrm
            normals.append(nrm)
        band = BANDS[j]
        # v runs from the edge to the shinogi on the hira, so the temper line can follow the edge
        va, vb = (0.0, 1.0) if j == 0 else (1.0, 0.0) if j == n - 1 else (0.0, 1.0)
        uva = [band_uv(band, s / BLADE_LEN, va) for s in ss]
        uvb = [band_uv(band, s / BLADE_LEN, vb) for s in ss]
        strip(mesh, pa, pb, normals, normals, uva, uvb)
    # tip: close the last ring to one point
    tip, _, along = blade_frame(BLADE_LEN)
    last = rings[-1]
    for j in range(n):
        k = (j + 1) % n
        nrm = along
        a = mesh.add(last[j], nrm, band_uv(BANDS[j], 1, 0.5))
        b = mesh.add(last[k], nrm, band_uv(BANDS[j], 1, 0.5))
        c = mesh.add(tip, nrm, band_uv(BANDS[j], 1, 0.5))
        mesh.tri(a, b, c)


def build_habaki(mesh):
    ss = np.linspace(0, HABAKI_LEN, 4)
    grow = [0.0042, 0.0040, 0.0036, 0.0032]
    rings = [blade_points(s, g) for s, g in zip(ss, grow)]
    n = len(PROFILE)
    for j in range(n):
        k = (j + 1) % n
        pa = [r[j] for r in rings]
        pb = [r[k] for r in rings]
        normals = []
        for i in range(len(ss)):
            _, _, along = blade_frame(ss[i])
            nrm = np.cross(along, pb[i] - pa[i])
            if np.dot(nrm, (pa[i] + pb[i]) / 2 - np.mean(rings[i], axis=0)) < 0:
                nrm = -nrm
            normals.append(nrm)
        uva = [uv_in(R_HABAKI, s / HABAKI_LEN, j / n) for s in ss]
        uvb = [uv_in(R_HABAKI, s / HABAKI_LEN, (j + 1) / n) for s in ss]
        strip(mesh, pa, pb, normals, normals, uva, uvb)
    # top lip of the collar, from the collar down to the blade
    _, _, along = blade_frame(HABAKI_LEN)
    outer = rings[-1]
    inner = blade_points(HABAKI_LEN)
    for j in range(n):
        k = (j + 1) % n
        ids = [mesh.add(p, along, uv_in(R_HABAKI, 1, 0.5)) for p in (outer[j], outer[k], inner[k], inner[j])]
        mesh.quad(*ids)


# --- round parts along the grip axis (y), elliptical sections -----------------------------------------------
def build_round(mesh, ys, radii, region, around=16, cap_start=False, cap_end=False, u_range=None):
    angles = np.linspace(0, 2 * math.pi, around + 1)
    rings = []
    for y, (rx, rz) in zip(ys, radii):
        rings.append([np.array([rx * math.cos(a), y, rz * math.sin(a)]) for a in angles])
    y0, y1 = ys[0], ys[-1]
    for i in range(len(ys) - 1):
        for j in range(around):
            ids = []
            for (ii, jj) in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)):
                rx, rz = radii[ii]
                a = angles[jj]
                # smooth normal of an ellipse, tilted by the change of radius along y
                nrm = np.array([math.cos(a) / rx, 0, math.sin(a) / rz])
                nrm /= np.linalg.norm(nrm)
                i2 = min(ii + 1, len(ys) - 1)
                i1 = max(ii - 1, 0)
                dr = (np.mean(radii[i2]) - np.mean(radii[i1])) / max(1e-9, ys[i2] - ys[i1])
                nrm = nrm - np.array([0, dr, 0])
                u = (ys[ii] - y0) / (y1 - y0) if u_range is None else u_range[0] + (u_range[1] - u_range[0]) * (ys[ii] - y0) / (y1 - y0)
                ids.append(mesh.add(rings[ii][jj], nrm, uv_in(region, u, jj / around)))
            mesh.quad(*ids)
    for cap, (y, (rx, rz)), sign in ((cap_start, (ys[0], radii[0]), -1), (cap_end, (ys[-1], radii[-1]), 1)):
        if not cap:
            continue
        c = mesh.add([0, y + sign * 0.002, 0], [0, sign, 0], uv_in(region, 0.5, 0.5))
        for j in range(around):
            a = mesh.add(rings[0 if sign < 0 else -1][j], [0, sign, 0], uv_in(region, 0.5, 0.5))
            b = mesh.add(rings[0 if sign < 0 else -1][j + 1], [0, sign, 0], uv_in(region, 0.5, 0.5))
            mesh.tri(c, a, b)


def tsuba_outline(n=40):
    # a softly squared oval, the usual mokko-less round tsuba
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        k = (abs(c) ** 2.6 + abs(s) ** 2.6) ** (-1 / 2.6)
        pts.append((TSUBA_R[0] * c * k, TSUBA_R[1] * s * k))
    return pts


def build_tsuba(mesh):
    outline = tsuba_outline()
    n = len(outline)
    y0, y1 = TSUBA
    for y, sign in ((y0, -1), (y1, 1)):
        c = mesh.add([0, y, 0], [0, sign, 0], uv_in(R_TSUBA, 0.5, 0.5))
        for i in range(n):
            x0, z0 = outline[i]
            x1, z1 = outline[(i + 1) % n]
            a = mesh.add([x0, y, z0], [0, sign, 0], uv_in(R_TSUBA, 0.5 + x0 / (2 * TSUBA_R[0]), 0.5 + z0 / (2 * TSUBA_R[1])))
            b = mesh.add([x1, y, z1], [0, sign, 0], uv_in(R_TSUBA, 0.5 + x1 / (2 * TSUBA_R[0]), 0.5 + z1 / (2 * TSUBA_R[1])))
            mesh.tri(c, a, b)
    for i in range(n):
        x0, z0 = outline[i]
        x1, z1 = outline[(i + 1) % n]
        n0 = np.array([x0 / TSUBA_R[0] ** 2, 0, z0 / TSUBA_R[1] ** 2])
        n1 = np.array([x1 / TSUBA_R[0] ** 2, 0, z1 / TSUBA_R[1] ** 2])
        ids = [mesh.add([x0, y0, z0], n0, uv_in(R_RIM, i / n, 0)), mesh.add([x1, y0, z1], n1, uv_in(R_RIM, (i + 1) / n, 0)),
               mesh.add([x1, y1, z1], n1, uv_in(R_RIM, (i + 1) / n, 1)), mesh.add([x0, y1, z0], n0, uv_in(R_RIM, i / n, 1))]
        mesh.quad(*ids)


def build_grip(mesh):
    # kashira (pommel cap), tsuka (wrapped grip, slightly waisted), fuchi (collar at the guard)
    ys = np.linspace(KASHIRA[0], KASHIRA[1], 3)
    build_round(mesh, ys, [(0.0150, 0.0118), (0.0186, 0.0146), (0.0186, 0.0146)], R_FITTING, cap_start=True)
    ys = np.linspace(TSUKA[0], TSUKA[1], 13)
    t = (ys - TSUKA[0]) / (TSUKA[1] - TSUKA[0])
    waist = 1 - 0.07 * np.sin(math.pi * t)
    build_round(mesh, ys, [(TSUKA_R[0] * w, TSUKA_R[1] * w) for w in waist], R_TSUKA)
    ys = np.linspace(FUCHI[0], FUCHI[1], 3)
    build_round(mesh, ys, [(0.0186, 0.0146), (0.0190, 0.0150), (0.0190, 0.0150)], R_FITTING, cap_end=True)


def build():
    mesh = Mesh()
    build_grip(mesh)
    build_tsuba(mesh)
    build_habaki(mesh)
    build_blade(mesh)
    return mesh


# --- saya (scabbard) ------------------------------------------------------------------------------------------
# built along the same blade curve in the same frame, so where the stance holds the sword the blade lies inside it
SAYA_CLEAR = 0.006          # room around the blade
SAYA_MOUTH = 0.035          # gold collar at the mouth (koiguchi)
SAYA_CORD = (0.10, 0.17)    # cord wrap near the mouth (sageo)
SAYA_END = 0.05             # cap at the end (kojiri)


def saya_section(s):
    # the blade's width and thickness, kept from shrinking in the point so the scabbard ends round, not sharp
    w, t = blade_section(min(s, BLADE_LEN - KISSAKI))
    grow = 0.0042 if s < HABAKI_LEN + 0.01 else 0.0
    return max(w, SAKIHABA) + 2 * (SAYA_CLEAR + grow), max(t, KASANE[1]) + 2 * (SAYA_CLEAR + grow)


def build_saya():
    mesh = Mesh()
    around = 18
    end = BLADE_LEN + 0.03
    ss = sorted(set(list(np.linspace(0.0, end, 48)) + [SAYA_MOUTH, SAYA_CORD[0], SAYA_CORD[1], end - SAYA_END]))
    def region(s):
        if s < SAYA_MOUTH or s > end - SAYA_END:
            return R_HABAKI
        if SAYA_CORD[0] <= s <= SAYA_CORD[1]:
            return R_TSUKA
        return R_FITTING
    rings = []
    for s in ss:
        p, edge, along = blade_frame(min(s, BLADE_LEN))
        if s > BLADE_LEN:
            p = p + along * (s - BLADE_LEN)
        w, t = saya_section(s)
        centre = p + edge * (blade_section(min(s, BLADE_LEN - KISSAKI))[0] / 2)
        ring = []
        for j in range(around + 1):
            a = 2 * math.pi * j / around
            pos = centre + edge * (math.cos(a) * w / 2) + np.array([0, 0, math.sin(a) * t / 2])
            nrm = edge * (math.cos(a) / (w / 2)) + np.array([0, 0, math.sin(a) / (t / 2)])
            ring.append((pos, nrm))
        rings.append((s, ring))
    # each band between two rings takes the texture region of its middle
    for (s0, r0), (s1, r1) in zip(rings, rings[1:]):
        reg = region((s0 + s1) / 2)
        for j in range(around):
            ids = []
            for (s, ring, jj) in ((s0, r0, j), (s1, r1, j), (s1, r1, j + 1), (s0, r0, j + 1)):
                u = (s - s0) / max(1e-6, s1 - s0) if reg != R_TSUKA else (s - SAYA_CORD[0]) / (SAYA_CORD[1] - SAYA_CORD[0])
                ids.append(mesh.add(ring[jj][0], ring[jj][1], uv_in(reg, u, jj / around)))
            mesh.quad(*ids)
    # closed at both ends: a flat mouth against the guard, a rounded cap at the end
    for (s, ring), outward in ((rings[0], -1), (rings[-1], 1)):
        _, _, along = blade_frame(min(s, BLADE_LEN))
        centre = np.mean([pos for pos, _ in ring[:-1]], axis=0) + along * (0.012 * outward if outward > 0 else 0)
        c = mesh.add(centre, along * outward, uv_in(R_HABAKI, 0.5, 0.5))
        for j in range(around):
            a = mesh.add(ring[j][0], along * outward + ring[j][1] * 0.3, uv_in(R_HABAKI, 0.5, 0.5))
            b = mesh.add(ring[j + 1][0], along * outward + ring[j + 1][1] * 0.3, uv_in(R_HABAKI, 0.5, 0.5))
            mesh.tri(c, a, b)
    return mesh


# --- textures ----------------------------------------------------------------------------------------------
PALETTES = {
    "steel": dict(ji=(0.50, 0.52, 0.56), hamon=(0.86, 0.87, 0.90), nioi=(0.96, 0.96, 0.98), shinogiji=(0.34, 0.36, 0.40),
                  ito=(0.07, 0.07, 0.09), same=(0.86, 0.84, 0.78), tsuba=(0.13, 0.12, 0.12), rim=(0.22, 0.20, 0.18),
                  habaki=(0.86, 0.63, 0.27), fitting=(0.10, 0.09, 0.09), speck=(0.75, 0.58, 0.25), glow=None),
    "flametal": dict(ji=(0.16, 0.17, 0.21), hamon=(0.52, 0.72, 1.00), nioi=(0.80, 0.92, 1.00), shinogiji=(0.10, 0.10, 0.13),
                     ito=(0.05, 0.08, 0.22), same=(0.70, 0.72, 0.78), tsuba=(0.24, 0.15, 0.09), rim=(0.36, 0.22, 0.12),
                     habaki=(0.80, 0.82, 0.86), fitting=(0.08, 0.07, 0.10), speck=(0.55, 0.70, 0.95), glow=(0.45, 0.70, 1.00)),
}
# metal mask and gloss (the game's shader reads the metal mask from rgb and the gloss from alpha)
METAL = dict(ji=(1.0, 0.86), hamon=(0.9, 0.62), nioi=(0.9, 0.7), shinogiji=(1.0, 0.92), ito=(0.0, 0.25), same=(0.0, 0.35),
             tsuba=(0.6, 0.35), rim=(0.7, 0.45), habaki=(1.0, 0.72), fitting=(0.8, 0.5), speck=(1.0, 0.7))


def hamon_height(u):
    # temper line height above the edge, share of the flat, along the blade (u 0 at the guard, 1 at the tip)
    s = u * BLADE_LEN
    h = 0.36 + 0.10 * np.sin(2 * math.pi * s / 0.075) + 0.05 * np.sin(2 * math.pi * s / 0.027 + 1.3)
    h = np.where(s < HABAKI_LEN + 0.03, 0.30, h)  # straight just above the collar
    return np.clip(h, 0.18, 0.62)


def make_textures(name, seed):
    pal = PALETTES[name]
    rng = np.random.default_rng(seed)
    col = np.zeros((TEX, TEX, 3))
    met = np.zeros((TEX, TEX, 2))
    glow = np.zeros((TEX, TEX, 3))

    def fill(region, key, noise=0.03):
        x0, y0, x1, y1 = region
        c = np.array(pal[key])
        jitter = 1 + rng.uniform(-noise, noise, (y1 - y0, x1 - x0, 1))
        col[y0:y1, x0:x1] = np.clip(c * jitter, 0, 1)
        met[y0:y1, x0:x1] = METAL[key]

    fill((0, 0, TEX, TEX), "fitting", 0.0)
    # flat of the blade: steel above the temper line, the frosty hardened edge below it, a bright line between
    x0, y0, x1, y1 = R_HIRA
    for x in range(x0, x1):
        u = (x - x0 + 0.5) / (x1 - x0)
        h = hamon_height(u)
        for y in range(y0, y1):
            v = (y - y0 + 0.5) / (y1 - y0)
            key = "hamon" if v < h - 0.06 else "nioi" if v < h + 0.04 else "ji"
            col[y, x] = np.clip(np.array(pal[key]) * (1 + rng.uniform(-0.03, 0.03)), 0, 1)
            met[y, x] = METAL[key]
            if pal["glow"] is not None and key != "ji":
                glow[y, x] = np.array(pal["glow"]) * (1.0 if key == "nioi" else 0.45)
    fill(R_SHINOGIJI, "shinogiji", 0.02)
    fill(R_MUNE, "shinogiji", 0.02)
    fill(R_HABAKI, "habaki", 0.04)
    # grip: silk cord crossing in diamonds over white rayskin
    x0, y0, x1, y1 = R_TSUKA
    length = TSUKA[1] - TSUKA[0]
    for x in range(x0, x1):
        along = (x - x0 + 0.5) / (x1 - x0) * length
        phase = (along / 0.05) % 1.0
        for y in range(y0, y1):
            around = (y - y0 + 0.5) / (y1 - y0)
            theta = (around * 2) % 1.0  # windows centred on both flat faces (a quarter and three quarters around)
            window = abs(phase - 0.5) * 2 + abs(theta - 0.5) * 2 < 0.78
            key = "same" if window else "ito"
            c = np.array(pal[key])
            if key == "same":
                c = c * (1 + rng.uniform(-0.06, 0.04))  # rayskin grain
            else:
                c = c * (1.25 if (x + y) % 3 == 0 else 1.0)  # twist of the silk
            col[y, x] = np.clip(c, 0, 1)
            met[y, x] = METAL[key]
    fill(R_FITTING, "fitting", 0.05)
    for _ in range(6):
        x = rng.integers(R_FITTING[0], R_FITTING[2])
        y = rng.integers(R_FITTING[1], R_FITTING[3])
        col[y, x] = pal["speck"]
        met[y, x] = METAL["speck"]
    # guard: dark iron, a lighter worn rim
    x0, y0, x1, y1 = R_TSUBA
    for x in range(x0, x1):
        for y in range(y0, y1):
            r = math.hypot((x - x0 + 0.5) / (x1 - x0) - 0.5, (y - y0 + 0.5) / (y1 - y0) - 0.5) * 2
            key = "rim" if r > 0.84 else "tsuba"
            col[y, x] = np.clip(np.array(pal[key]) * (1 + rng.uniform(-0.08, 0.08)), 0, 1)
            met[y, x] = METAL[key]
    fill(R_RIM, "rim", 0.06)
    return col, met, glow if pal["glow"] is not None else None


# --- output ------------------------------------------------------------------------------------------------
def write_obj(mesh, path):
    # the model is built with the edge toward +x, the game's swing animations need it on -x (user's test of
    # 10 oct): the export mirrors x, and the mirror turns the triangles around, so their order flips too
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write("# Iaido_Katana katana mesh, unity frame of krom's mesh node, edge toward -x, one vertex per v/vt/vn\n")
        for p in mesh.v:
            f.write(f"v {p[0] * EDGE_SIDE:.5f} {p[1]:.5f} {p[2]:.5f}\n")
        for uv in mesh.uv:
            f.write(f"vt {uv[0]:.5f} {uv[1]:.5f}\n")
        for n in mesh.n:
            f.write(f"vn {n[0] * EDGE_SIDE:.4f} {n[1]:.4f} {n[2]:.4f}\n")
        for a, b, c in mesh.tris:
            f.write(f"f {a + 1} {b + 1} {c + 1}\n" if EDGE_SIDE > 0 else f"f {a + 1} {c + 1} {b + 1}\n")


def save_png(path, rgb, alpha=None):
    import bpy
    h, w = rgb.shape[:2]
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = rgb
    if alpha is not None:
        px[..., 3] = alpha
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels = px.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    mesh = build()
    write_obj(mesh, os.path.join(OUT, "katana.obj"))
    v = np.array(mesh.v)
    print(f"MESH {len(mesh.v)} vertices, {len(mesh.tris)} triangles, bounds {v.min(0).round(4)} .. {v.max(0).round(4)}")
    saya = build_saya()
    write_obj(saya, os.path.join(OUT, "saya.obj"))
    sv = np.array(saya.v)
    print(f"SAYA {len(saya.v)} vertices, {len(saya.tris)} triangles, bounds {sv.min(0).round(4)} .. {sv.max(0).round(4)}")
    for i, name in enumerate(PALETTES):
        col, met, glow = make_textures(name, 7 + i)
        save_png(os.path.join(OUT, f"katana_{name}_d.png"), col)
        save_png(os.path.join(OUT, f"katana_{name}_m.png"), np.repeat(met[..., :1], 3, axis=2), met[..., 1])
        if glow is not None:
            save_png(os.path.join(OUT, f"katana_{name}_e.png"), glow)
    print("TEXTURES written")


if __name__ == "__main__":
    main()

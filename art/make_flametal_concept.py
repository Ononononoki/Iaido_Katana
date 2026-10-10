# concept renders of a more distinct flametal katana, not used by the game: a wider blade with more curve and a big
# point (o-kissaki), a four-lobed guard (mokko) with an iolite inlay, asksvin hide under ashwood coloured grip wraps,
# a black-red lacquer scabbard with a lightning zigzag (inazuma) and a celestial feather at its cord, and lightning
# crackling along the drawn blade. writes everything to art/out/concept, next to the steel katana for comparison.
# run: blender -b --factory-startup --python make_flametal_concept.py
import math
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_katana as mk  # noqa: E402
import render_katana as rk  # noqa: E402

CONCEPT = os.path.join(mk.OUT, "concept")
R_SAYA = (0, 56, 128, 96)       # the scabbard's own lacquer, along its whole length (free in the atlas)
R_FEATHER = (32, 40, 64, 56)    # the feather at the cord
FEATHER_AT = 0.135              # along the scabbard, in the cord wrap
FEATHER_LEN = 0.14


# --- shape ---------------------------------------------------------------------------------------------------
def mokko_outline(n=72):
    # four lobes with notches on the diagonals, the classic mokko guard
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        r = 1.0 - 0.17 * abs(math.sin(2 * a)) ** 0.55
        pts.append((mk.TSUBA_R[0] * r * math.cos(a), mk.TSUBA_R[1] * r * math.sin(a)))
    return pts


def flametal_shape():
    mk.MOTOHABA = 0.055
    mk.SAKIHABA = 0.044
    mk.KISSAKI = 0.14
    mk.SORI = 0.05
    mk.RADIUS = mk.BLADE_LEN ** 2 / (8 * mk.SORI) + mk.SORI / 2
    mk.TSUBA_R = (0.064, 0.058)
    mk.tsuba_outline = mokko_outline


def build_saya():
    # like make_katana.build_saya, with the body on its own lacquer region along the whole length, and the feather
    mesh = mk.Mesh()
    around = 18
    end = mk.BLADE_LEN + 0.03
    ss = sorted(set(list(np.linspace(0.0, end, 56)) + [mk.SAYA_MOUTH, mk.SAYA_CORD[0], mk.SAYA_CORD[1], end - mk.SAYA_END]))

    def region(s):
        if s < mk.SAYA_MOUTH or s > end - mk.SAYA_END:
            return mk.R_HABAKI
        if mk.SAYA_CORD[0] <= s <= mk.SAYA_CORD[1]:
            return mk.R_TSUKA
        return R_SAYA

    rings = []
    for s in ss:
        p, edge, along = mk.blade_frame(min(s, mk.BLADE_LEN))
        if s > mk.BLADE_LEN:
            p = p + along * (s - mk.BLADE_LEN)
        w, t = mk.saya_section(s)
        centre = p + edge * (mk.blade_section(min(s, mk.BLADE_LEN - mk.KISSAKI))[0] / 2)
        ring = []
        for j in range(around + 1):
            a = 2 * math.pi * j / around
            pos = centre + edge * (math.cos(a) * w / 2) + np.array([0, 0, math.sin(a) * t / 2])
            nrm = edge * (math.cos(a) / (w / 2)) + np.array([0, 0, math.sin(a) / (t / 2)])
            ring.append((pos, nrm))
        rings.append((s, ring))
    for (s0, r0), (s1, r1) in zip(rings, rings[1:]):
        reg = region((s0 + s1) / 2)
        for j in range(around):
            ids = []
            for (s, ring, jj) in ((s0, r0, j), (s1, r1, j), (s1, r1, j + 1), (s0, r0, j + 1)):
                if reg == mk.R_TSUKA:
                    u = (s - mk.SAYA_CORD[0]) / (mk.SAYA_CORD[1] - mk.SAYA_CORD[0])
                elif reg == R_SAYA:
                    u = s / end
                else:
                    u = (s - s0) / max(1e-6, s1 - s0)
                ids.append(mesh.add(ring[jj][0], ring[jj][1], mk.uv_in(reg, u, jj / around)))
            mesh.quad(*ids)
    for (s, ring), outward in ((rings[0], -1), (rings[-1], 1)):
        _, _, along = mk.blade_frame(min(s, mk.BLADE_LEN))
        centre = np.mean([pos for pos, _ in ring[:-1]], axis=0) + along * (0.012 * outward if outward > 0 else 0)
        c = mesh.add(centre, along * outward, mk.uv_in(mk.R_HABAKI, 0.5, 0.5))
        for j in range(around):
            a = mesh.add(ring[j][0], along * outward + ring[j][1] * 0.3, mk.uv_in(mk.R_HABAKI, 0.5, 0.5))
            b = mesh.add(ring[j + 1][0], along * outward + ring[j + 1][1] * 0.3, mk.uv_in(mk.R_HABAKI, 0.5, 0.5))
            mesh.tri(c, a, b)
    build_feather(mesh)
    return mesh


def build_feather(mesh):
    # a flat leaf shaped feather hanging from the cord on the spine side, drawn on both faces
    p, edge, along = mk.blade_frame(FEATHER_AT)
    w, _ = mk.saya_section(FEATHER_AT)
    centre = p + edge * (mk.blade_section(FEATHER_AT)[0] / 2)
    root = centre - edge * (w / 2 + 0.004)
    d = -edge * 0.82 + along * 0.57
    d /= np.linalg.norm(d)
    side = np.cross(np.array([0, 0, 1.0]), d)
    steps = 10
    for face in (1, -1):
        nrm = np.array([0, 0, face * 1.0])
        left, right = [], []
        for i in range(steps + 1):
            u = i / steps
            half = 0.013 * math.sin(math.pi * min(1.0, u * 1.15)) ** 0.8
            c = root + d * FEATHER_LEN * u + np.array([0, 0, face * 0.0015])
            left.append(mesh.add(c + side * half, nrm, mk.uv_in(R_FEATHER, u, 0.0)))
            right.append(mesh.add(c - side * half, nrm, mk.uv_in(R_FEATHER, u, 1.0)))
        for i in range(steps):
            mesh.quad(left[i], left[i + 1], right[i + 1], right[i])


# --- textures ------------------------------------------------------------------------------------------------
# full: every metal part in heat tinted flametal, a black lacquer scabbard with fine flametal flakes. midnight: a
# near black midnight blue scabbard, dark gunmetal fittings tinted only toward their edges
VARIANT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1 else "full"

PALETTE = dict(ji=(0.15, 0.16, 0.20), hamon=(0.55, 0.75, 1.00), nioi=(0.85, 0.94, 1.00), shinogiji=(0.09, 0.09, 0.12),
               ito=(0.20, 0.21, 0.25), same=(0.80, 0.81, 0.83), tsuba=(0.16, 0.17, 0.21), rim=(0.20, 0.21, 0.25),
               habaki=(0.18, 0.19, 0.23), fitting=(0.15, 0.16, 0.20), speck=(0.55, 0.70, 0.95), glow=(0.45, 0.72, 1.00))

# flametal's heat tint, as on the game's flametal ingot, shield and armor, kept to its cool end (the user wants
# nothing brown): deep blue, indigo, violet, rose
HEAT = [(0.0, (0.13, 0.26, 0.70)), (0.4, (0.30, 0.22, 0.62)), (0.75, (0.52, 0.24, 0.58)), (1.0, (0.72, 0.36, 0.62))]
GUNMETAL = np.array([0.15, 0.16, 0.20])


def heat(t):
    return np.array([np.interp(t, [k for k, _ in HEAT], [c[i] for _, c in HEAT]) for i in range(3)])


def flametal(t, strength):
    return GUNMETAL * (1 - strength) + heat(t) * strength


def make_textures():
    mk.PALETTES["flametal_concept"] = PALETTE
    col, met, glow = mk.make_textures("flametal_concept", 11)
    rng = np.random.default_rng(23)
    # the grip: the cord in dark flametal with its heat tint (deep blue at the guard like the blade's glow, through
    # violet, to rose at the pommel, as on the game's flametal shield and armor), over pale asksvin hide with
    # darker scales
    x0, y0, x1, y1 = mk.R_TSUKA
    tint = [(0.0, np.array([0.62, 0.30, 0.55])), (0.5, np.array([0.36, 0.22, 0.58])), (1.0, np.array([0.16, 0.28, 0.68]))]
    length = mk.TSUKA[1] - mk.TSUKA[0]
    for x in range(x0, x1):
        u = (x - x0 + 0.5) / (x1 - x0)  # 0 at the pommel, 1 at the guard
        cord = np.array([np.interp(u, [t for t, _ in tint], [c[i] for _, c in tint]) for i in range(3)])
        phase = (u * length / 0.05) % 1.0
        for y in range(y0, y1):
            around = (y - y0 + 0.5) / (y1 - y0)
            theta = (around * 2) % 1.0
            if abs(phase - 0.5) * 2 + abs(theta - 0.5) * 2 < 0.78:
                c = np.array(PALETTE["same"]) * (0.82 if (x // 2 + (y // 2) % 2) % 2 == 0 else 1.0)
                col[y, x] = c
                met[y, x] = (0.0, 0.35)
            else:
                gunmetal = np.array([0.20, 0.21, 0.25])
                c = gunmetal * 0.45 + cord * 0.55
                col[y, x] = np.clip(c * (1.3 if (x + y) % 3 == 0 else 1.0), 0, 1)  # the cord's twist catching light
                met[y, x] = (0.85, 0.7)
    full = VARIANT == "full"

    # midnight_blade: version 2 with the blade's own steel in flametal too, heat tinted from the temper line (blue)
    # up to the ridge (rose) and mirror shiny. the glowing temper line along the edge stays
    if VARIANT == "midnight_blade":
        x0, y0, x1, y1 = mk.R_HIRA
        for x in range(x0, x1):
            u = (x - x0 + 0.5) / (x1 - x0)
            h = float(mk.hamon_height(u))
            for y in range(y0, y1):
                v = (y - y0 + 0.5) / (y1 - y0)
                if v >= h + 0.04:
                    t = (v - h - 0.04) / max(0.05, 1 - h - 0.04)
                    c = flametal(min(1.0, 0.1 + 0.8 * t + 0.08 * math.sin(u * 23.0)), 0.8) * 1.35
                    col[y, x] = np.clip(c * (1 + rng.uniform(-0.03, 0.03)), 0, 1)
                    met[y, x] = (1.0, 0.96)
        for region, t in ((mk.R_SHINOGIJI, 0.85), (mk.R_MUNE, 0.95)):
            x0, y0, x1, y1 = region
            col[y0:y1, x0:x1] = np.clip(flametal(t, 0.75) * 1.25, 0, 1)
            met[y0:y1, x0:x1] = (1.0, 0.96)

    def paint(region, shade):
        # shade(u, v) gives the colour and the metal and gloss of each pixel of a region
        x0, y0, x1, y1 = region
        for x in range(x0, x1):
            for y in range(y0, y1):
                c, m = shade((x - x0 + 0.5) / (x1 - x0), (y - y0 + 0.5) / (y1 - y0))
                col[y, x] = np.clip(np.array(c) * (1 + rng.uniform(-0.05, 0.05)), 0, 1)
                met[y, x] = m

    # the scabbard, clean: black lacquer with very fine flakes of heat tinted flametal (like gold nashiji), or a
    # midnight blue so dark it is almost black
    def lacquer(u, v):
        if full:
            if rng.random() < 0.035:
                return heat(rng.random()) * 0.85, (0.9, 0.8)
            return (0.022, 0.022, 0.028), (0.1, 0.85)
        return (0.014, 0.02, 0.05), (0.1, 0.85)
    paint(R_SAYA, lacquer)

    # the metal fittings: heat tinted flametal all over, or dark gunmetal with the tint only toward their edges
    def guard(u, v):
        r = math.hypot(u - 0.5, v - 0.5) * 2
        if 0.5 < r < 0.62:
            return (0.35, 0.55, 1.0), (0.9, 0.85)  # the iolite ring
        return (flametal(r, 0.62) if full else flametal(max(0.0, (r - 0.78) / 0.22), 0.6 * (r > 0.78))), (0.9, 0.75)
    paint(mk.R_TSUBA, guard)
    paint(mk.R_RIM, lambda u, v: (flametal(0.9 if full else 0.5, 0.62 if full else 0.5), (0.9, 0.75)))
    paint(mk.R_HABAKI, lambda u, v: (flametal(u, 0.62) if full else flametal(abs(u - 0.5) * 2, 0.55 * (abs(u - 0.5) > 0.32)), (0.95, 0.75)))
    paint(mk.R_FITTING, lambda u, v: (flametal(0.25 + 0.5 * v, 0.55) if full else flametal(0.4, 0.35 * (abs(v - 0.5) > 0.35)), (0.9, 0.7)))
    # the guard's glowing ring
    x0, y0, x1, y1 = mk.R_TSUBA
    for x in range(x0, x1):
        for y in range(y0, y1):
            r = math.hypot((x - x0 + 0.5) / (x1 - x0) - 0.5, (y - y0 + 0.5) / (y1 - y0) - 0.5) * 2
            if 0.5 < r < 0.62:
                glow[y, x] = np.array([0.3, 0.55, 1.0]) * 0.6
    # the celestial feather: pale vane with fine barbs, the tip turning blue and glowing
    x0, y0, x1, y1 = R_FEATHER
    for x in range(x0, x1):
        u = (x - x0 + 0.5) / (x1 - x0)
        for y in range(y0, y1):
            v = (y - y0 + 0.5) / (y1 - y0)
            c = np.array([0.88, 0.90, 0.94]) * (0.9 if (x + y) % 2 else 1.0)
            if abs(v - 0.5) < 0.08:
                c = np.array([0.95, 0.95, 0.97])
            if u > 0.6:
                k = (u - 0.6) / 0.4
                c = c * (1 - k) + np.array([0.45, 0.68, 1.0]) * k
                glow[y, x] = np.array([0.35, 0.6, 1.0]) * k * 0.7
            col[y, x] = c
            met[y, x] = (0.0, 0.3)
    return col, met, glow


# --- scene ---------------------------------------------------------------------------------------------------
def concept_material(prefix, folder):
    mat = bpy.data.materials.new("mat_" + prefix)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def tex(file, non_color=False):
        node = nt.nodes.new("ShaderNodeTexImage")
        img = bpy.data.images.load(os.path.join(folder, file))
        if non_color:
            img.colorspace_settings.name = "Non-Color"
        node.image = img
        node.interpolation = "Closest"
        return node
    d = tex(f"{prefix}_d.png")
    m = tex(f"{prefix}_m.png", True)
    nt.links.new(d.outputs["Color"], bsdf.inputs["Base Color"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(m.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(m.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    if os.path.exists(os.path.join(folder, f"{prefix}_e.png")):
        e = tex(f"{prefix}_e.png")
        nt.links.new(e.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 2.4
    return mat


def obj_object(name, path, mat, location=(0, 0, 0)):
    v, vt, vn, f = rk.read_obj(path)
    me = bpy.data.meshes.new(name)
    me.from_pydata(rk.to_blender(v).tolist(), [], f.tolist())
    uv = me.uv_layers.new(name="uv")
    loops = f.reshape(-1)
    uv.data.foreach_set("uv", vt[loops].reshape(-1))
    me.normals_split_custom_set(rk.to_blender(vn[loops]).tolist())
    me.update()
    ob = bpy.data.objects.new(name, me)
    ob.location = location
    ob.data.materials.append(mat)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def lightning(parent, seed, strands=3, reach=None):
    # jagged glowing lines along the drawn blade, like the slayer's crackle (the game would use its particles)
    rng = np.random.default_rng(seed)
    mat = bpy.data.materials.new("bolt")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.55, 0.8, 1.0, 1)
    emit.inputs["Strength"].default_value = 7.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    for k in range(strands):
        curve = bpy.data.curves.new(f"bolt{k}", "CURVE")
        curve.dimensions = "3D"
        curve.bevel_depth = 0.0022
        spline = curve.splines.new("POLY")
        top = reach if reach is not None else mk.BLADE_LEN - 0.05
        start, stop = 0.04 + (top - 0.1) * 0.15 * k, min(top, (top - 0.1) * (0.45 + 0.27 * k))
        ss = np.cumsum(np.r_[start, rng.uniform(0.02, 0.06, 80)])
        ss = ss[ss < stop]
        spline.points.add(len(ss) - 1)
        for i, s in enumerate(ss):
            p, edge, _ = mk.blade_frame(s)
            w, t = mk.blade_section(s)
            off = edge * (w * rng.uniform(-0.15, 1.15)) + np.array([0, 0, rng.uniform(-1, 1) * (t / 2 + 0.008)])
            q = p + off
            q[0] *= mk.EDGE_SIDE
            spline.points[i].co = (q[1], q[2], q[0], 1)  # unity (edge, along, thick) to blender (along, thick, edge)
        ob = bpy.data.objects.new(f"bolt{k}", curve)
        ob.data.materials.append(mat)
        ob.parent = parent
        bpy.context.scene.collection.objects.link(ob)
    light = bpy.data.lights.new("glow", "POINT")
    light.energy = 25
    light.color = (0.5, 0.75, 1.0)
    lob = bpy.data.objects.new("glow", light)
    lob.location = (0.9, -0.15, 0.0)
    lob.parent = parent
    bpy.context.scene.collection.objects.link(lob)


def shot(sc, path, x, z, scale, res, ortho=True, loc=None, look=None, lens=50):
    cam = bpy.data.cameras.new("cam")
    co = bpy.data.objects.new("cam", cam)
    sc.collection.objects.link(co)
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = scale
        co.location = (x, -4, z)
        co.rotation_euler = (math.radians(90), 0, 0)
    else:
        cam.lens = lens
        co.location = loc
        target = bpy.data.objects.new("target", None)
        target.location = look
        sc.collection.objects.link(target)
        track = co.constraints.new("TRACK_TO")
        track.target = target
        track.track_axis = "TRACK_NEGATIVE_Z"
        track.up_axis = "UP_Y"
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", path)


def main():
    os.makedirs(CONCEPT, exist_ok=True)
    flametal_shape()
    mk.write_obj(mk.build(), os.path.join(CONCEPT, "katana_flametal.obj"))
    mk.write_obj(build_saya(), os.path.join(CONCEPT, "saya_flametal.obj"))
    col, met, glow = make_textures()
    tex = f"flametal_{VARIANT}"
    mk.save_png(os.path.join(CONCEPT, f"{tex}_d.png"), col)
    mk.save_png(os.path.join(CONCEPT, f"{tex}_m.png"), np.repeat(met[..., :1], 3, axis=2), met[..., 1])
    mk.save_png(os.path.join(CONCEPT, f"{tex}_e.png"), glow)

    katana_obj = os.path.join(CONCEPT, "katana_flametal.obj")
    saya_obj = os.path.join(CONCEPT, "saya_flametal.obj")

    # 1. side by side, drawn and their scabbards: steel above, the flametal concept below. edge up, as worn at the
    # hip, so the feather hangs down
    sc = rk.setup_scene()
    sc.cycles.samples = 64
    steel = concept_material("katana_steel", mk.OUT)
    flame = concept_material(tex, CONCEPT)
    for name, path, mat, z in (("steel", os.path.join(mk.OUT, "katana.obj"), steel, 0.5),
                               ("steel_saya", os.path.join(mk.OUT, "saya.obj"), steel, 0.26),
                               ("flametal", katana_obj, flame, 0.0),
                               ("flametal_saya", saya_obj, flame, -0.24)):
        ob = obj_object(name, path, mat, (0, 0, z))
        ob.rotation_euler = (math.pi, 0, 0)  # clean, the lightning is only an effect (the hero shot)
    shot(sc, os.path.join(CONCEPT, f"compare_{VARIANT}.png"), 1.1, 0.02, 2.45, (2000, 1000))
    shot(sc, os.path.join(CONCEPT, f"blade_{VARIANT}.png"), 1.05, -0.07, 0.6, (1000, 420))  # the middle of the blade, close

    # 2. the guard from the grip side at an angle: the four lobes and the iolite ring
    sc = rk.setup_scene()
    sc.cycles.samples = 64
    obj_object("flametal", katana_obj, concept_material(tex, CONCEPT))
    shot(sc, os.path.join(CONCEPT, f"guard_{VARIANT}.png"), 0, 0, 1, (900, 700), ortho=False,
         loc=(0.12, -0.2, 0.16), look=(0.43, 0, 0), lens=55)

    # 3. hero: drawn part way out of its scabbard, lightning on the blade, a dark ashlands mood with an ember glow
    sc = rk.setup_scene()
    sc.cycles.samples = 96
    sc.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.03, 0.025, 0.03, 1)
    flame = concept_material(tex, CONCEPT)
    pivot = bpy.data.objects.new("pivot", None)
    sc.collection.objects.link(pivot)
    saya = obj_object("saya", saya_obj, flame)
    saya.parent = pivot
    # the blade slid out along a straight line misses the scabbard's curve, so it is turned about the guard until its
    # cut end sits in the scabbard mouth
    def centre(s):
        p, edge, _ = mk.blade_frame(s)
        c = p + edge * (mk.blade_section(min(s, mk.BLADE_LEN - mk.KISSAKI))[0] / 2)
        return np.array([c[1], 0.0, c[0] * mk.EDGE_SIDE])  # to blender: along, thickness, edge
    drawn = 0.75
    guard_at = centre(0.0) + np.array([-drawn, 0.0, 0.0])
    end_at = centre(drawn + 0.02) + np.array([-drawn, 0.0, 0.0])
    reach = end_at - guard_at
    hinge = bpy.data.objects.new("hinge", None)
    sc.collection.objects.link(hinge)
    hinge.location = tuple(guard_at)
    hinge.parent = pivot
    hinge.rotation_euler = (0.0, math.atan2(reach[2], reach[0]), 0.0)
    blade = obj_object("blade", katana_obj, flame, tuple(np.array([-drawn, 0.0, 0.0]) - guard_at))
    blade.parent = hinge
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(blade.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(v.co.x > mk.BLADE_START + 0.75 + 0.02 for v in f.verts)], context="FACES")
    bm.to_mesh(blade.data)
    bm.free()
    lightning(blade, 9, strands=3, reach=0.72)  # only on the drawn part
    pivot.rotation_euler = (math.pi, math.radians(-12), 0)
    ember = bpy.data.lights.new("ember", "POINT")
    ember.energy = 60
    ember.color = (1.0, 0.45, 0.15)
    eob = bpy.data.objects.new("ember", ember)
    eob.location = (0.6, -0.4, -0.7)
    sc.collection.objects.link(eob)
    shot(sc, os.path.join(CONCEPT, f"hero_{VARIANT}.png"), 0, 0, 1, (1600, 900), ortho=False,
         loc=(0.1, -1.75, 0.3), look=(0.1, 0, 0.02), lens=42)


if __name__ == "__main__":
    main()

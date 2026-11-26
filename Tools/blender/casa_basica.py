"""Casa básica (lámina de referencia): vivienda de un nivel en una parcela de 10 x 10 m.
Blender: 1 unidad = 1 m, origen al centro de la planta a nivel de suelo, fachada hacia -Y.
Uso: python casa_basica.py -- Assets/Modelos/casa_basica_b.fbx [salida.blend]
(con el paquete bpy de pip, o: blender -b -P casa_basica.py -- ...)
"""
import bpy, bmesh, math, random, sys
from mathutils import Matrix, Vector

random.seed(7)
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FBX = args[0] if args else "casa_basica.fbx"
BLEND = args[1] if len(args) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------------------------------------------------------- materiales
def lin(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4 for x in c) + (1,)

MATS = {}
def mat(name, hex_, rough=.8, metal=0., emit=0.):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = lin(hex_)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if emit:
        p.inputs["Emission Color"].default_value = lin(hex_)
        p.inputs["Emission Strength"].default_value = emit
    MATS[name] = m
    return m

M = dict(
    estuco=mat("Estuco marfil", "#dcd5ca", .85),
    rojo=mat("Estuco rojo óxido", "#a9372c", .85),
    remate=mat("Remates claros", "#ebe7e0", .7),
    zoclo=mat("Zócalo hormigón", "#8d8982", .9),
    marquesina=mat("Marquesina grafito", "#3b3d40", .6),
    pilastra=mat("Pilastra grafito", "#4a4b4d", .7),
    piedra1=mat("Piedra arena", "#b3a58c", .9),
    piedra2=mat("Piedra tostada", "#9d8f78", .9),
    piedra3=mat("Piedra paja", "#c4b79e", .9),
    piedra4=mat("Piedra gris", "#8f877a", .9),
    junta=mat("Junta mortero", "#6f6a62", .95),
    membrana=mat("Membrana azotea", "#8e8b86", .9),
    marco=mat("Marco grafito", "#2a2b2d", .5),
    vidrio=mat("Vidrio oscuro", "#2c3238", .08),
    luz=mat("Luz interior", "#f2b765", .6, emit=3.),
    lama_luz=mat("Lama interior", "#a8774a", .7),
    lama=mat("Persiana gris", "#4c4e51", .6),
    puerta=mat("Puerta grafito", "#2f3033", .5),
    herraje=mat("Herrajes acero", "#b9bbbd", .3, .9),
    lampara=mat("Lámpara cálida", "#ffd08a", .4, emit=8.),
    tinaco=mat("Tinaco plástico negro", "#1f2123", .45),
    adoquin=mat("Adoquín arena", "#b6ad9f", .9),
    adoquin2=mat("Adoquín gris", "#a39b8e", .9),
    guarnicion=mat("Guarnición hormigón", "#8c867d", .9),
    losa=mat("Losa sendero", "#c7c2b8", .85),
    cesped=mat("Césped", "#5d8f34", .95),
    cesped2=mat("Césped sombra", "#4d7d2b", .95),
    tierra=mat("Tierra mantillo", "#3e2e22", 1.),
    block=mat("Block jardinera", "#9c978e", .9),
    corteza=mat("Corteza", "#5a4535", .9),
    follaje1=mat("Follaje laurel", "#3f6b26", .8),
    follaje2=mat("Follaje olivo", "#5a8431", .8),
    follaje3=mat("Follaje claro", "#78a03b", .8),
    hoja=mat("Hoja helecho", "#3f7a2c", .8),
    flor_roja=mat("Flor roja", "#d33a22", .7),
    flor_naranja=mat("Flor naranja", "#ee7d1f", .7),
    flor_amarilla=mat("Flor amarilla", "#f0c234", .7),
    medidor=mat("Caja medidor gris", "#9fa2a3", .5, .4),
)

# ---------------------------------------------------------------- geometría por grupos
class Group:
    def __init__(self, name):
        self.name, self.bm, self.mats = name, bmesh.new(), []

    def mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)

    def _paint(self, verts, m, smooth=False):
        i = self.mi(m)
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            f.material_index = i
            f.smooth = smooth
        return faces

    def box(self, x0, y0, z0, x1, y1, z1, m, bevel=.012):
        x0, x1 = min(x0, x1), max(x0, x1)
        y0, y1 = min(y0, y1), max(y0, y1)
        z0, z1 = min(z0, z1), max(z0, z1)
        mtx = Matrix.Translation(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)) @ Matrix.Diagonal(
            (x1 - x0, y1 - y0, z1 - z0, 1))
        verts = bmesh.ops.create_cube(self.bm, size=1, matrix=mtx)["verts"]
        self._paint(verts, m)
        b = min(bevel, (x1 - x0) / 3, (y1 - y0) / 3, (z1 - z0) / 3)
        if b > .002:
            edges = list({e for v in verts for e in v.link_edges})
            res = bmesh.ops.bevel(self.bm, geom=edges, offset=b, segments=1, affect="EDGES", profile=.5)
            i = self.mi(m)
            for f in res["faces"]:      # el bisel no hereda el material: se lo damos
                f.material_index = i
                f.smooth = False
        return verts

    def cyl(self, x, y, z0, z1, r0, r1, m, seg=12, smooth=True, rot=None):
        mtx = Matrix.Translation((x, y, (z0 + z1) / 2))
        if rot: mtx = mtx @ rot
        verts = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r0, radius2=r1,
                                      depth=z1 - z0, matrix=mtx)["verts"]
        faces = self._paint(verts, m, smooth)
        for f in faces:
            if len(f.verts) > 4: f.smooth = False
        return verts

    def ico(self, c, r, m, sub=2, jitter=.18, squash=1., smooth=False):
        mtx = Matrix.Translation(c) @ Matrix.Diagonal((r, r, r * squash, 1))
        verts = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1, matrix=mtx)["verts"]
        for v in verts:
            v.co += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * r * jitter
        self._paint(verts, m, smooth)
        return verts

    def blade(self, base, length, width, yaw, tilt, m, droop=.25):
        """Hoja alargada con quiebre: dos tramos triangulados, se ve como hoja de agave o helecho."""
        d = Vector((math.cos(yaw), math.sin(yaw), 0))
        side = Vector((-d.y, d.x, 0)) * width / 2
        up = math.cos(tilt)
        mid = base + (d * math.sin(tilt) + Vector((0, 0, up))) * length * .55
        tip = mid + (d * (math.sin(tilt) + droop) + Vector((0, 0, up - droop))).normalized() * length * .45
        b = self.bm
        vs = [b.verts.new(base - side * .6), b.verts.new(base + side * .6), b.verts.new(mid + side),
              b.verts.new(mid - side), b.verts.new(tip)]
        # leve canal central para que la luz marque la hoja
        vs[2].co.z -= width * .15; vs[3].co.z -= width * .15
        b.faces.new((vs[0], vs[1], vs[2], vs[3]))
        b.faces.new((vs[3], vs[2], vs[4]))
        self._paint(vs, m)

    def leaf(self, c, size, m):
        """Hoja triangular orientada al azar."""
        n = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-.2, 1))).normalized()
        a = n.orthogonal().normalized(); b2 = n.cross(a)
        rot = random.uniform(0, math.tau)
        u = a * math.cos(rot) + b2 * math.sin(rot); v = n.cross(u)
        vs = [self.bm.verts.new(c - u * size * .5), self.bm.verts.new(c + v * size * .35), self.bm.verts.new(c + u * size * .6)]
        self.bm.faces.new(vs)
        self._paint(vs, m)

    def finish(self, parent=None):
        me = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats: me.materials.append(m)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        if parent: ob.parent = parent
        return ob

G = {k: Group(n) for k, n in [
    ("terreno", "01 · Terreno y césped"), ("banqueta", "02 · Adoquín perimetral y sendero"),
    ("jardinera", "03 · Jardinera y arriates"), ("envolvente", "04 · Envolvente"),
    ("piedra", "05 · Muro de piedra"), ("cubierta", "06 · Azotea, pretiles y tinaco"),
    ("ventanas", "07 · Ventanas y persianas"), ("acceso", "08 · Acceso, puerta y marquesina"),
    ("lamparas", "09 · Lámparas y herrajes"), ("plantas", "10 · Plantación baja"),
    ("arboles", "11 · Árboles")]}

# ---------------------------------------------------------------- medidas generales
X0, X1 = -3.3, 3.3          # ancho de la casa (6.6 m)
YF, YB = -2.6, 4.0          # frente y fondo (6.6 m)
YR = -1.8                   # plano de la puerta (nicho de acceso)
FL = .25                    # nivel de piso terminado
ROOF = 3.15                 # losa de azotea
PAR = 3.52                  # corona del pretil
DX0, DX1 = .2, 1.85         # nicho de acceso
GR = .1                     # nivel del césped

# ---------------------------------------------------------------- terreno
t = G["terreno"]
t.box(-5, -5, 0, 5, 5, .06, M["guarnicion"], bevel=0)
t.box(-4.5, -4.5, .06, 4.5, 4.5, GR, M["cesped"], bevel=0)
# manchas de pasto más oscuro, apenas en relieve
for _ in range(14):
    cx, cy = random.uniform(-4.2, 4.2), random.uniform(-4.2, 4.2)
    if X0 - .3 < cx < X1 + .3 and YF - .3 < cy < YB + .3: continue
    s = random.uniform(.4, .9)
    t.ico((cx, cy, GR - .012), s, M["cesped2"], sub=1, jitter=.0, squash=.025)

# adoquín perimetral 0.5 m, piezas de 0.5 x 0.25 con juntas
b = G["banqueta"]
b.box(-5, -5, .06, 5, 5, .1, M["junta"], bevel=0) if False else None
def pavers(xa, ya, xb, yb, along_x):
    L, W, gap = .5, .25, .018
    if along_x:
        rows = int(round((yb - ya) / W))
        for r in range(rows):
            off = (r % 2) * L / 2
            x = xa - off
            while x < xb - .01:
                p0, p1 = max(x, xa), min(x + L, xb)
                if p1 - p0 > .05:
                    m = M["adoquin"] if random.random() > .25 else M["adoquin2"]
                    h = .13 + random.uniform(-.004, .004)
                    b.box(p0 + gap / 2, ya + r * W + gap / 2, .06, p1 - gap / 2, ya + (r + 1) * W - gap / 2, h, m, .01)
                x += L
    else:
        cols = int(round((xb - xa) / W))
        for c in range(cols):
            off = (c % 2) * L / 2
            y = ya - off
            while y < yb - .01:
                p0, p1 = max(y, ya), min(y + L, yb)
                if p1 - p0 > .05:
                    m = M["adoquin"] if random.random() > .25 else M["adoquin2"]
                    h = .13 + random.uniform(-.004, .004)
                    b.box(xa + c * W + gap / 2, p0 + gap / 2, .06, xa + (c + 1) * W - gap / 2, p1 - gap / 2, h, m, .01)
                y += L
pavers(-5, -5, 5, -4.5, True)      # frente
pavers(-5, 4.5, 5, 5, True)        # fondo
pavers(-5, -4.5, -4.5, 4.5, False) # izquierda
pavers(4.5, -4.5, 5, 4.5, False)   # derecha
# guarnición interior que separa adoquín y césped
for (xa, ya, xb, yb) in [(-4.5, -4.5, 4.5, -4.44), (-4.5, 4.44, 4.5, 4.5), (-4.5, -4.44, -4.44, 4.44), (4.44, -4.44, 4.5, 4.44)]:
    b.box(xa, ya, .06, xb, yb, .15, M["guarnicion"], .008)

# sendero de losas desde la banqueta al acceso
door_cx = (DX0 + DX1) / 2
for i, y in enumerate((-4.15, -3.6, -3.05)):
    dx = (.06, -.04, .03)[i]
    b.box(door_cx - .5 + dx, y - .2, GR - .02, door_cx + .5 + dx, y + .2, GR + .045, M["losa"], .015)

# ---------------------------------------------------------------- envolvente
e = G["envolvente"]
# zócalo y losa de desplante
e.box(X0 - .04, YF - .04, GR - .02, X1 + .04, YB + .04, FL - .005, M["zoclo"], .01)
# cuerpo principal (frente en el plano del nicho) y volúmenes frontales
e.box(X0, YR, FL, X1, YB, ROOF, M["estuco"], .015)
e.box(X0, YF, FL, -.05, YR + .01, ROOF, M["estuco"], .015)      # volumen rojo (núcleo)
e.box(DX1, YF, FL, X1, YR + .01, 2.6, M["junta"], .01)          # núcleo del muro de piedra
e.box(DX1, YF, 2.6, X1, YR + .01, ROOF, M["estuco"], .015)
# dintel sobre el acceso
e.box(-.05, YF, 2.6, DX1, YR + .01, ROOF, M["estuco"], .015)
# recubrimiento rojo en la fachada (ligeramente saliente) que dobla un poco en la esquina
e.box(X0 - .02, YF - .025, FL, -.05, YF + .02, ROOF + .37, M["rojo"], .012)
e.box(X0 - .025, YF - .025, FL, X0 + .02, YF + .55, ROOF + .37, M["rojo"], .012)
# pilastra grafito entre el rojo y la puerta
e.box(-.05, YF - .03, FL, DX0 + .05, YR + .02, 2.6, M["pilastra"], .012)
# bajante pluvial y tubería
e.box(X0 - .1, YB - .25, FL, X0 - .02, YB - .17, ROOF + .2, M["remate"], .02)
e.box(X1 + .02, YB - .9, 2.3, X1 + .08, YB - .84, ROOF, M["zoclo"], .01)
# caja del medidor y registro en el muro posterior
e.box(2.35, YB, 1.3, 2.75, YB + .12, 1.8, M["medidor"], .015)
e.box(2.48, YB + .12, 1.48, 2.62, YB + .14, 1.62, M["vidrio"], .005)
e.box(2.53, YB, .3, 2.57, YB + .07, 1.3, M["herraje"], .01)
e.box(-1.0, YB, .25, -.6, YB + .1, .65, M["medidor"], .01)  # registro de agua

# ---------------------------------------------------------------- muro de piedra (sillares)
s = G["piedra"]
stones = [M["piedra1"], M["piedra2"], M["piedra3"], M["piedra4"]]
def ashlar(u0, u1, z0, z1, place):
    """Sillares en hiladas; place(u0,u1,za,zb,proud) crea cada pieza sobre la cara."""
    rows = int(round((z1 - z0) / .27))
    h = (z1 - z0) / rows
    for r in range(rows):
        u = u0 - random.uniform(0, .25) if r % 2 else u0
        while u < u1 - .02:
            w = random.uniform(.32, .62)
            a, c = max(u, u0), min(u + w, u1)
            if c - a > .08:
                place(a + .012, c - .012, z0 + r * h + .012, z0 + (r + 1) * h - .012, random.uniform(.025, .045),
                      random.choice(stones))
            u += w
# cara frontal (y = YF), cara lateral derecha (x = X1) y cara hacia la puerta (x = DX1)
ashlar(DX1, X1, FL, 2.6, lambda a, c, za, zb, p, m: s.box(a, YF - p, za, c, YF + .01, zb, m, .018))
ashlar(YF, YR + .2, FL, 2.6, lambda a, c, za, zb, p, m: s.box(X1 - .01, a, za, X1 + p, c, zb, m, .018))
ashlar(YF, YR, FL, 2.6, lambda a, c, za, zb, p, m: s.box(DX1 - p, a, za, DX1 + .01, c, zb, m, .018))

# ---------------------------------------------------------------- azotea
c = G["cubierta"]
T = .22
c.box(X0 + T, YF + T, ROOF - .1, X1 - T, YB - T, ROOF + .02, M["membrana"], 0)
# pretiles y remates sin traslapes en las esquinas (las caras coincidentes se ven negras)
for (xa, ya, xb, yb) in [(X0, YF, X1, YF + T), (X0, YB - T, X1, YB), (X0, YF + T, X0 + T, YB - T), (X1 - T, YF + T, X1, YB - T)]:
    c.box(xa, ya, ROOF, xb, yb, PAR, M["estuco"], .01)
O = .04
for (xa, ya, xb, yb) in [(X0 - O, YF - O, X1 + O, YF + T), (X0 - O, YB - T, X1 + O, YB + O),
                         (X0 - O, YF + T + .003, X0 + T, YB - T - .003), (X1 - T, YF + T + .003, X1 + O, YB - T - .003)]:
    c.box(xa, ya, PAR, xb, yb, PAR + .06, M["remate"], .012)
# el remate rojo de la fachada sigue con su pretil
c.box(X0 - .06, YF - .065, PAR + .002, -.05, YF + T, PAR + .064, M["remate"], .012)
# chaflán de membrana contra el pretil
for (xa, ya, xb, yb) in [(X0 + T, YF + T, X1 - T, YF + T + .08), (X0 + T, YB - T - .08, X1 - T, YB - T),
                         (X0 + T, YF + T, X0 + T + .08, YB - T), (X1 - T - .08, YF + T, X1 - T, YB - T)]:
    c.box(xa, ya, ROOF, xb, yb, ROOF + .09, M["membrana"], .02)
# volumen sobre el acceso (cajón de remate encima de la marquesina)
c.box(-.3, YF - .1, 2.86, .95, YF + .35, PAR + .1, M["remate"], .02)
# tinaco en la esquina posterior derecha sobre su base
tx, ty = 2.45, 3.1
c.box(tx - .6, ty - .6, ROOF, tx + .6, ty + .6, ROOF + .2, M["remate"], .02)
c.cyl(tx, ty, ROOF + .2, ROOF + 1.2, .46, .46, M["tinaco"], 24)
for zz in (ROOF + .45, ROOF + .72, ROOF + .98):
    c.cyl(tx, ty, zz - .03, zz + .03, .48, .48, M["tinaco"], 24)
c.cyl(tx, ty, ROOF + 1.2, ROOF + 1.32, .46, .3, M["tinaco"], 24)
c.cyl(tx, ty, ROOF + 1.32, ROOF + 1.4, .18, .18, M["tinaco"], 16)
c.cyl(tx, ty, ROOF + 1.4, ROOF + 1.43, .2, .2, M["tinaco"], 16)
# tubería del tinaco hacia la bajada
pipe = Matrix.Rotation(math.pi / 2, 4, "Y")
c.cyl(tx - 1.0, ty - .3, ROOF + .08, ROOF + .08, .03, .03, M["herraje"], 8) if False else None
c.cyl(tx - 1.3, ty - .32, ROOF + .02, ROOF + .02, .03, .03, M["remate"], 8) if False else None
c.box(tx - 1.6, ty - .34, ROOF + .02, tx - .45, ty - .3, ROOF + .06, M["remate"], .015)
# respiraderos y coladeras
for (vx, vy) in [(-2.2, 2.6), (-1.5, 2.9)]:
    c.cyl(vx, vy, ROOF, ROOF + .32, .05, .05, M["zoclo"], 10)
    c.cyl(vx, vy, ROOF + .32, ROOF + .38, .09, .09, M["zoclo"], 10)
for (vx, vy) in [(-2.9, 3.6), (2.9, -2.2)]:
    c.cyl(vx, vy, ROOF, ROOF + .03, .1, .1, M["marquesina"], 12)

# ---------------------------------------------------------------- ventanas
w = G["ventanas"]
def window(axis, plane, u0, u1, z0, z1, out, lit=False, mullions=1):
    """Ventana sobre una cara. axis 'x': cara normal a Y (u = x); 'y': cara normal a X (u = y). out = +1/-1 hacia afuera."""
    def B(ua, ub, za, zb, d0, d1, m, bev=.008):
        if axis == "x":
            w.box(ua, plane + out * d0, za, ub, plane + out * d1, zb, m, bev)
        else:
            w.box(plane + out * d0, ua, za, plane + out * d1, ub, zb, m, bev)
    f = .07
    # fondo: luz interior o vidrio oscuro
    B(u0 + f, u1 - f, z0 + f, z1 - f, -.01, .005, M["luz"] if lit else M["vidrio"], 0)
    # persiana horizontal
    n = int((z1 - z0 - 2 * f) / .055)
    for k in range(n):
        za = z0 + f + .02 + k * .055
        if lit and k > n * .55: break      # persiana a medio subir en la ventana encendida
        B(u0 + f + .02, u1 - f - .02, za, za + .032, .006, .014, M["lama_luz"] if lit else M["lama"], 0)
    # vidrio reflejante en el tercio superior
    # marco perimetral y parteluz
    B(u0, u1, z1 - f, z1, -.01, .06, M["marco"])
    B(u0, u1, z0, z0 + f, -.01, .06, M["marco"])
    B(u0, u0 + f, z0, z1, -.01, .06, M["marco"])
    B(u1 - f, u1, z0, z1, -.01, .06, M["marco"])
    for i in range(1, mullions + 1):
        um = u0 + (u1 - u0) * i / (mullions + 1)
        B(um - .025, um + .025, z0, z1, -.01, .05, M["marco"])
    # repisa de hormigón
    B(u0 - .06, u1 + .06, z0 - .06, z0, -.01, .1, M["remate"], .01)

window("x", YF - .025, -2.85, -.55, 1.0, 2.35, -1, lit=True, mullions=1)   # ventana principal (rojo)
window("y", X1, -.2, .9, 1.05, 2.15, +1)                                      # lateral derecha
window("y", X1, 2.0, 2.9, 1.25, 2.15, +1)
window("x", YB, -2.2, -.6, 1.0, 2.15, +1, mullions=1)                         # posterior grande
window("x", YB, .6, 1.5, 1.55, 2.15, +1, mullions=0)                          # posterior baño
window("y", X0, -.9, .2, 1.05, 2.15, -1)                                      # lateral izquierda
window("y", X0, 2.1, 2.9, 1.55, 2.15, -1, mullions=0)

# ---------------------------------------------------------------- acceso
a = G["acceso"]
# escalón y descanso
a.box(DX0 - .2, YR - .02, GR, DX1, YF - .02, FL + .015, M["remate"], .01)
a.box(DX0 + .05, YF - .02, GR, DX1 - .1, YF - .38, FL - .08, M["remate"], .01)
# puerta de tableros con marco
dx0, dx1 = .52, 1.5
a.box(dx0 - .08, YR - .05, FL, dx1 + .08, YR + .01, 2.38, M["marco"], .01)
a.box(dx0, YR - .07, FL, dx1, YR - .02, 2.3, M["puerta"], .006)
for zz in (.55, 1.15, 1.75):
    a.box(dx0 + .12, YR - .085, zz, dx1 - .12, YR - .07, zz + .45, M["puerta"], .004)
a.box(dx1 - .14, YR - .12, 1.0, dx1 - .11, YR - .07, 1.42, M["herraje"], .006)   # jaladera
# luz lateral (vidrio esmerilado) junto a la puerta
a.box(dx1 + .1, YR - .04, 1.0, DX1 - .04, YR + .01, 2.3, M["luz"], 0)
for zz in (1.0, 1.65, 2.25):
    a.box(dx1 + .08, YR - .06, zz, DX1 - .02, YR - .01, zz + .05, M["marco"], .004)
# marquesina grafito en voladizo, más larga que la fachada
a.box(-.4, YF - .85, 2.6, X1 + .5, YF + .3, 2.8, M["marquesina"], .02)
a.box(-.4, YF - .85, 2.8, X1 + .5, YF - .78, 2.86, M["marquesina"], .01)   # gotero
a.box(-.4, YF - .85, 2.8, -.34, YF + .3, 2.86, M["marquesina"], .01)
a.box(X1 + .44, YF - .85, 2.8, X1 + .5, YF + .3, 2.86, M["marquesina"], .01)
# número de casa
a.box(2.0, YF - .06, 2.2, 2.35, YF - .04, 2.36, M["herraje"], .004)

# ---------------------------------------------------------------- lámparas
l = G["lamparas"]
def sconce_y(x, y, z, out):
    l.box(x - .06, y, z - .14, x + .06, y + out * .05, z + .14, M["marquesina"], .01)
    l.box(x - .045, y + out * .05, z - .1, x + .045, y + out * .1, z + .1, M["lampara"], .006)
    l.box(x - .06, y + out * .1, z + .1, x + .06, y + out * .02, z + .14, M["marquesina"], .006)
def sconce_x(x, y, z, out):
    l.box(x, y - .06, z - .14, x + out * .05, y + .06, z + .14, M["marquesina"], .01)
    l.box(x + out * .05, y - .045, z - .1, x + out * .1, y + .045, z + .1, M["lampara"], .006)
    l.box(x + out * .02, y - .06, z + .1, x + out * .1, y + .06, z + .14, M["marquesina"], .006)
sconce_x(DX1 - .045, YR - .35, 2.0, -1)      # junto a la puerta, sobre la piedra
sconce_x(X1 + .04, -1.3, 2.1, +1)            # lateral derecha
sconce_y(-1.2, YB, 2.2, +1)                  # posterior
# luminarias empotradas en la marquesina
for xx in (.9, 2.6):
    l.cyl(xx, YF - .3, 2.585, 2.6, .07, .07, M["lampara"], 12, smooth=False)
# arbotante de jardín junto al sendero
bx, by = door_cx + .9, -3.65
l.box(bx - .07, by - .07, GR, bx + .07, by + .07, .62, M["marquesina"], .01)
l.box(bx - .055, by - .075, .42, bx + .055, by + .075, .56, M["lampara"], .004)
l.box(bx - .085, by - .085, .62, bx + .085, by + .085, .66, M["marquesina"], .008)
l.box(bx - .1, by - .1, GR - .02, bx + .1, by + .1, GR + .03, M["guarnicion"], .006)

# ---------------------------------------------------------------- jardinera frontal y arriates
j = G["jardinera"]
pz = .52
jx0, jx1, jy0, jy1 = -3.15, -.35, YF - .62, YF - .03
# muros de block con hiladas
def block_wall(xa, ya, xb, yb):
    along_x = (xb - xa) > (yb - ya)
    L, H = .4, .14
    rows = int(round((pz - GR) / H))
    for r in range(rows):
        za, zb = GR + r * H, GR + (r + 1) * H - .012
        if along_x:
            u = xa - (r % 2) * L / 2
            while u < xb - .01:
                p0, p1 = max(u, xa), min(u + L, xb)
                if p1 - p0 > .04: j.box(p0 + .006, ya, za, p1 - .006, yb, zb, M["block"], .012)
                u += L
        else:
            u = ya - (r % 2) * L / 2
            while u < yb - .01:
                p0, p1 = max(u, ya), min(u + L, yb)
                if p1 - p0 > .04: j.box(xa, p0 + .006, za, xb, p1 - .006, zb, M["block"], .012)
                u += L
j.box(jx0 + .05, jy0 + .05, GR, jx1 - .05, jy1, pz - .03, M["junta"], 0)
block_wall(jx0, jy0, jx1, jy0 + .14)
block_wall(jx0, jy0 + .14, jx0 + .14, jy1)
block_wall(jx1 - .14, jy0 + .14, jx1, jy1)
j.box(jx0, jy0, pz - .012, jx1, jy0 + .15, pz + .03, M["remate"], .012)   # albardilla
j.box(jx0, jy0, pz - .012, jx0 + .15, jy1, pz + .03, M["remate"], .012)
j.box(jx1 - .15, jy0, pz - .012, jx1, jy1, pz + .03, M["remate"], .012)
j.box(jx0 + .14, jy0 + .14, pz - .1, jx1 - .14, jy1, pz - .06, M["tierra"], 0)
# arriates de tierra al pie de los muros
beds = [(X0 - .55, YF + .55, X0 - .04, YB + .4), (X1 + .1, YR + .3, X1 + .6, YB + .4),
        (X0 - .55, YB + .04, X1 + .6, YB + .5), (DX1 + .05, YF - .55, X1 + .3, YF - .04)]
for (xa, ya, xb, yb) in beds:
    j.box(xa, ya, GR - .01, xb, yb, GR + .025, M["tierra"], 0)

# ---------------------------------------------------------------- plantas
p = G["plantas"]
def shrub(x, y, z, r, mats=(M["follaje1"], M["follaje2"])):
    for k in range(random.randint(2, 4)):
        p.ico((x + random.uniform(-r, r) * .5, y + random.uniform(-r, r) * .5, z + r * .5 + random.uniform(0, r * .3)),
              r * random.uniform(.55, .85), random.choice(mats), sub=1, jitter=.2, squash=.85)
def fern(x, y, z, size, m=M["hoja"], n=9):
    for k in range(n):
        yaw = k / n * math.tau + random.uniform(-.25, .25)
        p.blade(Vector((x, y, z)), size * random.uniform(.75, 1.1), size * .16, yaw, random.uniform(.5, 1.0), m)
def agave(x, y, z, size):
    for k in range(11):
        yaw = k * 2.39996
        p.blade(Vector((x, y, z)), size * random.uniform(.7, 1.0), size * .22, yaw, .25 + (k % 3) * .3,
                random.choice((M["follaje2"], M["follaje3"])), droop=.05)
def flowers(x, y, z, r, n, mats):
    for k in range(n):
        ang, rr = random.uniform(0, math.tau), r * math.sqrt(random.random())
        cx, cy = x + math.cos(ang) * rr, y + math.sin(ang) * rr
        cz = z + random.uniform(.12, .32)
        p.cyl(cx, cy, z, cz, .008, .008, M["hoja"], 4, smooth=False)
        p.ico((cx, cy, cz), random.uniform(.035, .055), random.choice(mats), sub=1, jitter=.25)

# jardinera: flores rojas, naranjas y algo de follaje
hot = (M["flor_roja"], M["flor_roja"], M["flor_naranja"], M["flor_amarilla"])
for xx in [-2.9, -2.35, -1.8, -1.25, -.7]:
    shrub(xx + random.uniform(-.08, .08), jy0 + .32, pz - .1, .2)
    flowers(xx, jy0 + .32, pz - .08, .22, 9, hot)
fern(-3.05, jy0 + .3, pz - .08, .42)
# arriate frontal derecho bajo la piedra
for xx in (2.1, 2.75, 3.35):
    fern(xx, YF - .3, GR + .02, .5)
    flowers(xx + .25, YF - .3, GR + .02, .15, 5, hot)
agave(DX1 + .12, YF - .25, GR + .02, .45)
# laterales y fondo: helechos, agaves y arbustos
for yy in [-1.6, -.4, .9, 2.2, 3.5]:
    fern(X0 - .28, yy + random.uniform(-.2, .2), GR + .02, random.uniform(.45, .6))
for yy in [-1.0, .3, 1.6, 3.0]:
    shrub(X0 - .3, yy + .5, GR, .26, (M["follaje2"], M["follaje3"]))
    flowers(X0 - .25, yy + .1, GR + .02, .15, 4, (M["flor_naranja"], M["flor_roja"]))
for yy in [-1.2, -.2, 1.0, 2.0, 3.0]:
    fern(X1 + .35, yy + random.uniform(-.2, .2), GR + .02, random.uniform(.45, .6))
for yy in [-.7, .5, 2.6, 3.7]:
    agave(X1 + .33, yy, GR + .02, .35)
for xx in [-2.6, -1.4, -.2, 1.0, 2.0, 3.0]:
    (fern if random.random() > .4 else agave)(xx + random.uniform(-.2, .2), YB + .28, GR + .02, .42)
    shrub(xx + .5, YB + .3, GR, .2)
# matas sueltas en el césped
for (xx, yy) in [(-4.0, -3.9), (3.9, -3.95), (-4.0, 3.9), (4.0, 4.0), (-1.2, -4.05)]:
    agave(xx, yy, GR, .3)

# ---------------------------------------------------------------- árboles
tr = G["arboles"]
def tree(x, y, h, crown, seed):
    rnd = random.Random(seed)
    tr.cyl(x, y, GR, h * .55, .11, .07, M["corteza"], 8)
    # ramas principales
    for k in range(4):
        yaw = k * math.tau / 4 + rnd.uniform(-.4, .4)
        L = rnd.uniform(.7, 1.0)
        rot = Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(rnd.uniform(.5, .8), 4, "Y")
        base = Vector((x, y, h * rnd.uniform(.42, .55)))
        tip_dir = rot @ Vector((0, 0, 1))
        cen = base + tip_dir * L / 2
        m = Matrix.Translation(cen) @ rot
        v = bmesh.ops.create_cone(tr.bm, cap_ends=True, segments=6, radius1=.05, radius2=.025, depth=L, matrix=m)["verts"]
        tr._paint(v, M["corteza"], True)
    # copa: racimos de follaje en tres tonos
    mats = (M["follaje1"], M["follaje2"], M["follaje3"])
    for k in range(42):
        ang = rnd.uniform(0, math.tau)
        rr = crown * math.sqrt(rnd.random()) * .8
        zz = h * .6 + rnd.uniform(0, h * .34) - rr * .3
        r = rnd.uniform(.2, .34) * crown
        cx, cy = x + math.cos(ang) * rr, y + math.sin(ang) * rr
        cx = max(-4.95 + r, min(4.95 - r, cx)); cy = max(-4.95 + r, min(4.95 - r, cy))
        shade = mats[0] if zz < h * .72 or rnd.random() < .3 else rnd.choice(mats[1:])
        tr.ico((cx, cy, zz), r * .72, shade, sub=1, jitter=.12, squash=.8, smooth=True)
        for q in range(70):
            d = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, .8))).normalized() * r * rnd.uniform(.7, 1.05)
            pc = Vector((cx, cy, zz)) + d
            pc.x = max(-4.97, min(4.97, pc.x)); pc.y = max(-4.97, min(4.97, pc.y))
            lm = shade if rnd.random() < .6 else rnd.choice(mats)
            tr.leaf(pc, rnd.uniform(.13, .2), lm)
tree(-4.0, -1.9, 4.3, 1.1, 1)
tree(4.0, 1.4, 4.6, 1.15, 2)

# ---------------------------------------------------------------- objetos y exportación
root = bpy.data.objects.new("Vivienda 01 · Casa básica", None)
bpy.context.scene.collection.objects.link(root)
objs = [g.finish(root) for g in G.values()]
tris = sum(sum(len(f.vertices) - 2 for f in o.data.polygons) for o in objs)
print(f"CASA: {len(objs)} objetos, {tris} triángulos, {len(MATS)} materiales")

if BLEND: bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=False, object_types={"EMPTY", "MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)

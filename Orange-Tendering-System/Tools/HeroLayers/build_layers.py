"""Cut the approved Orange HQ reference image into 2.5D layers for the homepage hero.

Input : reference.webp (the approved design, 1672x941).
Output: ../../wwwroot/assets/hero/  plate@{1,2}x.webp, <layer>@{1,2}x.webp (RGBA), manifest.json

What it does (all reproducible, no generative fill):
  1. Upscales the reference 2x (Lanczos + unsharp mask) for sharper large screens.
  2. Removes the UI text that is baked into the picture (logo, nav, headline, buttons) so the real,
     accessible HTML can sit in exactly the same place (OpenCV Telea inpainting on dark background).
  3. Removes the baked orange connector lines (the page redraws them so they follow moving floors).
  4. Cuts the building into layers (roof, floors, side glass modules) using polygons in reference pixels.
  5. Builds a clean background plate behind the moving parts (row interpolation + inpainting).
Run:  python build_layers.py           (needs pillow, opencv-python-headless, numpy)
      python build_layers.py --debug   (also writes debug_polygons.png next to this script)
"""
import json, os, sys
import cv2
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "wwwroot", "assets", "hero"))
SCALE = 2

# ---- polygons in reference (1x) pixels ----
LAYERS = {
    # name: (polygon, luminance-feather the edges?)
    "roof":  ([(648, 186), (745, 127), (1131, 58), (1152, 64), (1302, 168), (1182, 210), (1046, 231), (958, 231), (958, 199), (866, 168), (740, 221), (660, 196)], True),
    "l2":    ([(738, 222), (866, 167), (946, 196), (946, 232), (1046, 233), (1100, 219), (1176, 250), (1176, 266), (1100, 274), (1000, 284), (930, 300), (870, 303), (742, 295)], True),
    "l3":    ([(740, 322), (778, 314), (915, 290), (930, 300), (1000, 284), (1100, 274), (1176, 266), (1200, 300), (1248, 318), (1248, 348), (1212, 402), (1100, 424), (940, 430), (850, 434), (742, 430)], True),
    "card":  ([(936, 400), (1172, 396), (1175, 547), (938, 551)], False),
    "l4":    ([(746, 430), (850, 434), (940, 430), (1100, 424), (1212, 402), (1254, 428), (1254, 522), (1240, 534), (1000, 562), (850, 562), (746, 550)], True),
    "l5":    ([(628, 640), (722, 600), (722, 556), (800, 560), (905, 552), (1070, 549), (1182, 552), (1240, 558), (1242, 600), (1176, 594), (1000, 614), (800, 642), (700, 674), (630, 678)], True),
    "leftbox":   ([(486, 662), (590, 632), (608, 642), (608, 712), (522, 742), (486, 738)], False),
    "rightcube": ([(1506, 300), (1630, 290), (1644, 300), (1642, 396), (1520, 406), (1506, 396)], False),
    "ulpanels":  ([(568, 222), (645, 230), (732, 260), (734, 328), (690, 326), (640, 304), (568, 296)], False),
    "lslab":     ([(464, 402), (526, 400), (528, 504), (466, 506)], False),
    "chip":      ([(1244, 526), (1268, 526), (1268, 562), (1244, 562)], False),
    # the stream of documents (bright pixels only, see PAPERS_LUMA)
    "papers":    ([(0, 488), (300, 500), (528, 504), (528, 612), (420, 640), (300, 690), (240, 745), (0, 745)], False),
    # static: the Suppliers panel is drawn above the right glass module so the module can slide behind it
    "supcard":   ([(1297, 338), (1478, 313), (1490, 322), (1490, 463), (1306, 480), (1297, 470)], False),
}
STATIC = {"supcard"}
# Assembly (reference pixels). Stage k closes gap GAPS[k-1]; a floor moves by the sum of the gaps of
# stages up to its own, so the whole stack above settles together and gaps only ever close.
GAPS = [6, 24, 34, 30, 34]
STACK = {"l5": 1, "l4": 2, "card": 2, "l3": 3, "l2": 4, "roof": 5}
# side pieces: stage, final offset and scale (scale around the piece centre)
# Every loose piece ends fully inside the building (hidden behind the floor it docks into).
SIDE = {"leftbox": (1, 153, -37, .55), "papers": (1, 535, -16, .15), "lslab": (2, 268, 42, .5), "chip": (2, -27, 1, .6),
        "rightcube": (2, -375, 122, .6), "ulpanels": (4, 149, 15, .5)}


def final_transform(name, w, h):
    if name in STACK:
        return 0, sum(GAPS[:STACK[name]]), 1.0
    if name in SIDE:
        _, dx, dy, sc = SIDE[name]
        return dx, dy, sc
    return 0, 0, 1.0
# Areas of baked UI text/buttons to remove (x0, y0, x1, y1) and whether the whole box is a solid element.
TEXT_BOXES = [
    ((44, 4, 140, 96), True),         # logo square
    ((515, 22, 1150, 54), False),     # nav items
    ((1445, 22, 1505, 54), False),    # EN
    ((1528, 16, 1632, 58), True),     # Log in pill
    ((48, 150, 440, 178), False),     # eyebrow
    ((48, 186, 520, 340), False),     # headline
    ((48, 350, 470, 424), False),     # description
    ((48, 446, 254, 494), True),      # Explore the Platform pill
    ((272, 448, 424, 490), False),    # See It in Action
]
CONNECTOR_BOXES = [(666, 386, 724, 596), (724, 386, 746, 520), (1198, 236, 1312, 309), (1170, 296, 1200, 309),
                   (1178, 392, 1300, 452), (1200, 556, 1380, 606)]


def coons_fill(img, X0, Y0, X1, Y1, rng, pad=3):
    """Transfinite (Coons) interpolation of a rectangle from the pixels just outside it, plus faint grain."""
    H, W = img.shape[:2]
    X0, Y0, X1, Y1 = max(pad, X0), max(pad, Y0), min(W - pad - 1, X1), min(H - pad - 1, Y1)
    top = img[Y0 - pad:Y0, X0:X1].astype(np.float32).mean(0)
    bot = img[Y1:Y1 + pad, X0:X1].astype(np.float32).mean(0)
    lef = img[Y0:Y1, X0 - pad:X0].astype(np.float32).mean(1)
    rig = img[Y0:Y1, X1:X1 + pad].astype(np.float32).mean(1)
    top, bot = cv2.GaussianBlur(top[None], (0, 0), 6)[0], cv2.GaussianBlur(bot[None], (0, 0), 6)[0]
    lef, rig = cv2.GaussianBlur(lef[:, None], (0, 0), 6)[:, 0], cv2.GaussianBlur(rig[:, None], (0, 0), 6)[:, 0]
    h, w = Y1 - Y0, X1 - X0
    v = np.linspace(0, 1, h)[:, None, None]; u = np.linspace(0, 1, w)[None, :, None]
    c00, c01 = img[Y0 - 1, X0 - 1].astype(np.float32), img[Y0 - 1, X1].astype(np.float32)
    c10, c11 = img[Y1, X0 - 1].astype(np.float32), img[Y1, X1].astype(np.float32)
    patch = ((1 - v) * top[None] + v * bot[None] + (1 - u) * lef[:, None] + u * rig[:, None]
             - ((1 - u) * (1 - v) * c00 + u * (1 - v) * c01 + (1 - u) * v * c10 + u * v * c11))
    patch += rng.normal(0, 1.2, patch.shape)
    img[Y0:Y1, X0:X1] = np.clip(patch, 0, 255).astype(np.uint8)


def poly_mask(shape, poly, s=SCALE):
    m = np.zeros(shape[:2], np.uint8)
    cv2.fillPoly(m, [np.array([(int(x * s), int(y * s)) for x, y in poly], np.int32)], 255, lineType=cv2.LINE_AA)
    return m


def main(debug=False):
    os.makedirs(OUT, exist_ok=True)
    ref = Image.open(os.path.join(HERE, "reference.webp")).convert("RGB")
    W, H = ref.size
    up = ref.resize((W * SCALE, H * SCALE), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.6, percent=70, threshold=2))
    img = cv2.cvtColor(np.array(up), cv2.COLOR_RGB2BGR)
    lum = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY).astype(np.float32)
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)

    # 1) baked UI text -> smooth fill of the whole box from its border (the background there is a soft gradient)
    clean = img.copy()
    rng = np.random.default_rng(1)
    for (x0, y0, x1, y1), _solid in TEXT_BOXES:
        X0, Y0, X1, Y1 = x0 * SCALE, y0 * SCALE, x1 * SCALE, y1 * SCALE
        coons_fill(clean, X0, Y0, X1, Y1, rng)

    # 2) baked connector lines and their glow (orange in the gap corridors) -> inpaint
    b, g, r = [clean[..., i].astype(np.int32) for i in range(3)]
    lum2 = cv2.cvtColor(clean, cv2.COLOR_BGR2GRAY).astype(np.int32)
    orange = ((r - b > 38) & (lum2 > 42)).astype(np.uint8) * 255
    cmask = np.zeros(lum.shape, np.uint8)
    for x0, y0, x1, y1 in CONNECTOR_BOXES:
        cmask[y0 * SCALE:y1 * SCALE, x0 * SCALE:x1 * SCALE] = orange[y0 * SCALE:y1 * SCALE, x0 * SCALE:x1 * SCALE]
    cmask = cv2.dilate(cmask, np.ones((13, 13), np.uint8))
    clean = cv2.inpaint(clean, cmask, 11, cv2.INPAINT_TELEA)
    clean = np.where(cmask[..., None] > 0, cv2.GaussianBlur(clean, (0, 0), 3), clean)

    # 3) layers from the cleaned image (so connector remnants are gone from the floors' edges too)
    manifest = {"width": W, "height": H, "gaps": GAPS, "layers": []}
    union_floor = np.zeros(lum.shape, np.uint8)
    union_small = np.zeros(lum.shape, np.uint8)
    lum_c = cv2.cvtColor(clean, cv2.COLOR_BGR2GRAY).astype(np.float32)
    taken = np.zeros(lum.shape, np.uint8)
    masks = {}
    order = ["card", "roof", "l2", "l3", "l4", "l5", "leftbox", "rightcube", "ulpanels", "lslab", "chip", "papers", "supcard"]
    for name in order:
        poly, feather = LAYERS[name]
        m = poly_mask(img.shape, poly)
        m = cv2.subtract(m, taken)          # each pixel belongs to one layer only
        taken = cv2.bitwise_or(taken, m)
        a = cv2.GaussianBlur(m, (3, 3), 0).astype(np.float32) / 255
        if name == "papers":
            bb, gg, rr = [clean[..., i].astype(np.int32) for i in range(3)]
            keep = ((lum_c > 72) | ((rr - bb > 60) & (lum_c > 60))).astype(np.uint8) * 255
            keep = cv2.dilate(keep, np.ones((9, 9), np.uint8))
            a = a * cv2.GaussianBlur(keep, (5, 5), 0).astype(np.float32) / 255
        if feather:
            # inside a band near the cut line, dark "gap" pixels become transparent
            inner = cv2.erode(m, np.ones((19, 19), np.uint8))
            band = (m > 0) & (inner == 0)
            k = np.clip((lum_c - 30) / 45, 0, 1)
            a = np.where(band, a * k, a)
        if name not in STATIC:
            (union_small if name in SIDE else union_floor)[m > 0] = 255
        ys, xs = np.where(a > 0.01)
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        x0 -= x0 % SCALE; y0 -= y0 % SCALE
        x1 += (-x1) % SCALE; y1 += (-y1) % SCALE
        rgba = np.dstack([cv2.cvtColor(clean[y0:y1, x0:x1], cv2.COLOR_BGR2RGB), (a[y0:y1, x0:x1] * 255).astype(np.uint8)])
        im2 = Image.fromarray(rgba, "RGBA")
        im2.save(os.path.join(OUT, f"{name}@2x.webp"), quality=88, method=6)
        im2.resize(((x1 - x0) // SCALE, (y1 - y0) // SCALE), Image.LANCZOS).save(os.path.join(OUT, f"{name}@1x.webp"), quality=86, method=6)
        entry = {"name": name, "x": int(x0) // SCALE, "y": int(y0) // SCALE, "w": int(x1 - x0) // SCALE, "h": int(y1 - y0) // SCALE}
        if name in STACK: entry["stack"] = STACK[name]
        if name in SIDE: entry.update({"stage": SIDE[name][0], "dx": SIDE[name][1], "dy": SIDE[name][2], "scale": SIDE[name][3]})
        manifest["layers"].append(entry)
        masks[name] = (a > (0.15 if name == "papers" else 0.5)).astype(np.uint8) * 255
        print(name, manifest["layers"][-1])

    # 4) background plate: the clean image, except the areas that are uncovered once the parts move
    cover = np.zeros(lum.shape, np.uint8)
    for name, m in masks.items():
        dx, dy, sc = final_transform(name, 0, 0)
        ys, xs = np.where(m > 0)
        cx, cy = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
        M = np.float32([[sc, 0, cx - sc * cx + dx * SCALE], [0, sc, cy - sc * cy + dy * SCALE]])
        cover = cv2.bitwise_or(cover, cv2.warpAffine(m, M, (m.shape[1], m.shape[0])))
    moving_floor = np.zeros(lum.shape, np.uint8); moving_side = np.zeros(lum.shape, np.uint8)
    for name, m in masks.items():
        if name in STACK: moving_floor |= m
        elif name in SIDE: moving_side |= m
    k = np.ones((13, 13), np.uint8)
    fill = cv2.dilate(cv2.bitwise_and(moving_floor, cv2.bitwise_not(cv2.erode(cover, k))), k)
    # only real uncovered areas (the band above the settled roof), not hairline holes inside the building
    n, lab, stats, _ = cv2.connectedComponentsWithStats(fill)
    fill = np.isin(lab, [i for i in range(1, n) if stats[i, cv2.CC_STAT_AREA] > 6000]).astype(np.uint8) * 255
    moving_any = cv2.dilate(moving_floor | moving_side, np.ones((7, 7), np.uint8)) > 0
    plate = clean.copy()
    for y in range(plate.shape[0]):
        xs = np.where(fill[y] > 0)[0]
        if len(xs) == 0:
            continue
        # fill each run of the row from the pixels just left and right of it
        runs = np.split(xs, np.where(np.diff(xs) > 1)[0] + 1)
        for run in runs:
            # sample true background: walk outwards past every moving part
            L, R = run[0], run[-1]
            l = L
            while l > 8 and (moving_any[y, l - 1] or fill[y, l - 1]): l -= 1
            r = R
            while r < plate.shape[1] - 9 and (moving_any[y, r + 1] or fill[y, r + 1]): r += 1
            lc = clean[y, max(0, l - 70):max(1, l - 50)].reshape(-1, 3).astype(np.float32).mean(0)  # clear of edge glow
            rc = clean[y, min(plate.shape[1] - 2, r + 50):min(plate.shape[1] - 1, r + 70)].reshape(-1, 3).astype(np.float32).mean(0)
            # never pull colour from glowing neighbours (panel edges, signs): keep the darker, plain sky side
            if abs(rc.mean() - lc.mean()) > 10 or rc[2] - rc[0] > 12 or lc[2] - lc[0] > 12:
                lc = rc = np.minimum(lc, rc) if lc.mean() > 0 and rc.mean() > 0 else lc
            t = np.linspace(0, 1, r - l + 1)[:, None]
            row = lc * (1 - t) + rc * t
            plate[y, L:R + 1] = row[L - l:R - l + 1]
    # smooth only with filled pixels (blurring the whole plate would bleed the sign's orange into the sky)
    fm = (fill > 0).astype(np.float32)[..., None]
    num = cv2.GaussianBlur(plate.astype(np.float32) * fm, (0, 0), 22); den = cv2.GaussianBlur(fm, (0, 0), 22); den = den[..., None] if den.ndim == 2 else den
    plate = np.where(fill[..., None] > 0, (num / np.maximum(den, 1e-3)).clip(0, 255).astype(np.uint8), plate)
    side_fill = np.zeros(lum.shape, np.uint8)
    MIRROR = {"leftbox": "L", "ulpanels": "L", "lslab": "L", "rightcube": "R", "chip": "R"}
    for name, m in masks.items():
        if name not in SIDE:
            continue
        reg = cv2.dilate(m, k)  # a loose piece's original spot is always cleared
        side_fill |= reg
        if name not in MIRROR:
            continue
        # refill by mirroring the neighbouring background (sky / skyline), not by smearing the building
        for y in np.where(reg.any(1))[0]:
            xs = np.where(reg[y] > 0)[0]
            L, R = xs.min(), xs.max()
            n = R - L + 1
            W2 = plate.shape[1]
            if MIRROR[name] == "L":
                src = [max(0, L - 1 - (i % max(1, L))) for i in range(n)]
            else:
                src = [min(W2 - 1, R + 1 + (i % max(1, W2 - R - 1))) for i in range(n)][::-1]
            plate[y, L:R + 1] = plate[y, src]
    papers_reg = cv2.dilate(masks["papers"], k)
    plate = cv2.inpaint(plate, papers_reg, 9, cv2.INPAINT_TELEA)
    soft = cv2.GaussianBlur(plate, (0, 0), 10)
    pm = cv2.GaussianBlur(papers_reg, (0, 0), 6).astype(np.float32)[..., None] / 255
    plate = (plate * (1 - pm) + soft * pm).astype(np.uint8)
    if debug:
        cv2.imwrite(os.path.join(HERE, "debug_fill.png"), cv2.resize(cv2.merge([fill, side_fill, cover // 3]), (W, H)))
    pim = Image.fromarray(cv2.cvtColor(plate, cv2.COLOR_BGR2RGB))
    pim.save(os.path.join(OUT, "plate@2x.webp"), quality=88, method=6)
    pim.resize((W, H), Image.LANCZOS).save(os.path.join(OUT, "plate@1x.webp"), quality=86, method=6)
    # the cleaned full frame (no baked UI, no connectors) for reduced-motion / no-JS / comparison
    cim = Image.fromarray(cv2.cvtColor(clean, cv2.COLOR_BGR2RGB))
    if debug:
        cim.resize((W, H), Image.LANCZOS).save(os.path.join(HERE, "debug_frame.webp"), quality=90)
    with open(os.path.join(OUT, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=1)
    if debug:
        dbg = cv2.cvtColor(np.array(ref), cv2.COLOR_RGB2BGR)
        colors = [(0, 255, 255), (255, 0, 255), (0, 255, 0), (255, 128, 0), (0, 128, 255), (255, 255, 0), (128, 255, 128), (255, 128, 255), (128, 128, 255)]
        for i, (n, (poly, _)) in enumerate(LAYERS.items()):
            cv2.polylines(dbg, [np.array(poly, np.int32)], True, colors[i % len(colors)], 1)
            cv2.putText(dbg, n, poly[0], cv2.FONT_HERSHEY_SIMPLEX, .45, colors[i % len(colors)], 1)
        for x0, y0, x1, y1 in CONNECTOR_BOXES:
            cv2.rectangle(dbg, (x0, y0), (x1, y1), (0, 0, 255), 1)
        cv2.imwrite(os.path.join(HERE, "debug_polygons.png"), dbg)
        cv2.imwrite(os.path.join(HERE, "debug_plate.png"), cv2.resize(plate, (W, H)))
        cv2.imwrite(os.path.join(HERE, "debug_clean.png"), cv2.resize(clean, (W, H)))


if __name__ == "__main__":
    main("--debug" in sys.argv)

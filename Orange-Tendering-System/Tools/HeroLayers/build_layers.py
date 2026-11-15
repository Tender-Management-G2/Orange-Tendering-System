"""Cut the approved Orange HQ image (reference.webp, 1448x1086, assembled building) into scroll layers.

The page starts with the floors lifted apart (exploded) and lowers them into exactly this image.
Output (../../wwwroot/assets/hero/): plate@{1,2}x.webp, <floor>@{1,2}x.webp (RGBA), backdrop.webp, manifest.json
  1. 2x Lanczos + unsharp mask.
  2. The orange connector lines are removed (the page redraws them live, attached to the floors).
  3. Floors are cut with polygons along the slab lines; the podium, ground, trees and panels stay on the plate.
  4. Behind the floors the plate gets the night sky (row interpolation from the clear sky either side),
     which is what shows between floors while they are apart.
Run: python build_layers.py [--debug]     (pillow, opencv-python-headless, numpy)
"""
import json, os, sys
import cv2
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "wwwroot", "assets", "hero"))
SCALE = 2

# Floors from the top down. Each polygon runs along the slab line under the floor above.
FLOORS = {
    "roof": [(240, 296), (300, 256), (420, 212), (880, 86), (944, 82), (984, 104), (1148, 268), (1148, 310), (1124, 392), (930, 318), (326, 340), (292, 320)],
    "f2":   [(326, 340), (930, 318), (1124, 392), (1124, 474), (965, 442), (326, 432)],
    "f3":   [(326, 432), (965, 442), (1124, 474), (1124, 564), (980, 542), (326, 527)],
    "f4":   [(326, 527), (980, 542), (1124, 564), (1124, 644), (980, 617), (326, 617)],
    "card": [(652, 468), (978, 452), (986, 602), (656, 620)],
    "f5":   [(326, 617), (980, 617), (1124, 644), (1118, 702), (960, 656), (450, 690), (326, 704)],
}
# stack index: stage that locks the floor (bottom-up); the card rides on f4
STACK = {"f5": 1, "f4": 2, "card": 2, "f3": 3, "f2": 4, "roof": 5}
# vertical gap (reference px) that stage k closes; a floor is lifted by the sum of the gaps of its stage and below
GAPS = [42, 48, 52, 58, 74]
CONNECTOR_BOXES = [(250, 352, 368, 625), (1066, 172, 1160, 248), (1066, 308, 1178, 452), (1066, 522, 1224, 612)]


def poly_mask(shape, poly):
    m = np.zeros(shape[:2], np.uint8)
    cv2.fillPoly(m, [np.array([(int(x * SCALE), int(y * SCALE)) for x, y in poly], np.int32)], 255, lineType=cv2.LINE_AA)
    return m


def main(debug=False):
    os.makedirs(OUT, exist_ok=True)
    ref = Image.open(os.path.join(HERE, "reference.webp")).convert("RGB")
    W, H = ref.size
    up = ref.resize((W * SCALE, H * SCALE), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.6, percent=70, threshold=2))
    img = cv2.cvtColor(np.array(up), cv2.COLOR_RGB2BGR)
    lum = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY).astype(np.int32)
    b, g, r = [img[..., i].astype(np.int32) for i in range(3)]

    # 1) remove baked connector lines + their glow
    orange = ((r - b > 38) & (lum > 42)).astype(np.uint8) * 255
    cmask = np.zeros(lum.shape, np.uint8)
    for x0, y0, x1, y1 in CONNECTOR_BOXES:
        cmask[y0 * SCALE:y1 * SCALE, x0 * SCALE:x1 * SCALE] = orange[y0 * SCALE:y1 * SCALE, x0 * SCALE:x1 * SCALE]
    cmask = cv2.dilate(cmask, np.ones((13, 13), np.uint8))
    clean = cv2.inpaint(img, cmask, 11, cv2.INPAINT_TELEA)
    clean = np.where(cmask[..., None] > 0, cv2.GaussianBlur(clean, (0, 0), 3), clean)

    # 2) floors
    manifest = {"width": W, "height": H, "gaps": GAPS, "layers": []}
    taken = np.zeros(lum.shape, np.uint8)
    union = np.zeros(lum.shape, np.uint8)
    for name in ["card", "roof", "f2", "f3", "f4", "f5"]:
        m = cv2.subtract(poly_mask(img.shape, FLOORS[name]), taken)
        taken |= m
        union |= m
        a = cv2.GaussianBlur(m, (3, 3), 0)
        ys, xs = np.where(a > 2)
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        x0 -= x0 % SCALE; y0 -= y0 % SCALE; x1 += (-x1) % SCALE; y1 += (-y1) % SCALE
        rgba = np.dstack([cv2.cvtColor(clean[y0:y1, x0:x1], cv2.COLOR_BGR2RGB), a[y0:y1, x0:x1]])
        im2 = Image.fromarray(rgba, "RGBA")
        im2.save(os.path.join(OUT, f"{name}@2x.webp"), quality=88, method=6)
        im2.resize(((x1 - x0) // SCALE, (y1 - y0) // SCALE), Image.LANCZOS).save(os.path.join(OUT, f"{name}@1x.webp"), quality=86, method=6)
        manifest["layers"].append({"name": name, "x": int(x0) // SCALE, "y": int(y0) // SCALE, "w": int(x1 - x0) // SCALE, "h": int(y1 - y0) // SCALE, "stack": STACK[name]})
        print(name, manifest["layers"][-1])

    # 3) plate: night sky behind the floors (shown between floors while they are apart)
    plate = clean.copy()
    fill = cv2.dilate(union, np.ones((5, 5), np.uint8))
    fill[(int(690 * SCALE)):, :] = 0  # never touch the podium
    for y in range(plate.shape[0]):
        xs = np.where(fill[y] > 0)[0]
        if len(xs) == 0:
            continue
        L, R = xs.min(), xs.max()
        lc = clean[y, max(0, L - 60 * SCALE):max(1, L - 46 * SCALE)].reshape(-1, 3).astype(np.float32)
        rc = clean[y, min(plate.shape[1] - 2, R + 30 * SCALE):min(plate.shape[1] - 1, R + 40 * SCALE)].reshape(-1, 3).astype(np.float32)
        lc = np.median(lc, 0); rc = np.median(rc, 0)
        sky = np.minimum(lc, rc) if abs(lc.mean() - rc.mean()) > 8 else (lc + rc) / 2
        plate[y, L:R + 1] = np.where(fill[y, L:R + 1, None] > 0, sky, plate[y, L:R + 1])
    fm = (fill > 0).astype(np.float32)
    num = cv2.GaussianBlur(plate.astype(np.float32) * fm[..., None], (0, 0), 24)
    den = cv2.GaussianBlur(fm, (0, 0), 24)[..., None]
    plate = np.where(fill[..., None] > 0, (num / np.maximum(den, 1e-3)).clip(0, 255).astype(np.uint8), plate)
    pim = Image.fromarray(cv2.cvtColor(plate, cv2.COLOR_BGR2RGB))
    pim.save(os.path.join(OUT, "plate@2x.webp"), quality=86, method=6)
    pim.resize((W, H), Image.LANCZOS).save(os.path.join(OUT, "plate@1x.webp"), quality=86, method=6)
    pim.resize((W // 8, H // 8), Image.LANCZOS).filter(ImageFilter.GaussianBlur(6)).save(os.path.join(OUT, "backdrop.webp"), quality=80)
    with open(os.path.join(OUT, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=1)
    if debug:
        dbg = cv2.cvtColor(np.array(ref), cv2.COLOR_RGB2BGR)
        for i, (n, poly) in enumerate(FLOORS.items()):
            cv2.polylines(dbg, [np.array(poly, np.int32)], True, [(0, 255, 255), (255, 0, 255), (0, 255, 0), (255, 128, 0), (0, 128, 255), (255, 255, 0)][i], 1)
        for x0, y0, x1, y1 in CONNECTOR_BOXES:
            cv2.rectangle(dbg, (x0, y0), (x1, y1), (0, 0, 255), 1)
        cv2.imwrite(os.path.join(HERE, "debug_polygons.png"), dbg)
        cv2.imwrite(os.path.join(HERE, "debug_plate.png"), cv2.resize(plate, (W, H)))
        cv2.imwrite(os.path.join(HERE, "debug_clean.png"), cv2.resize(clean, (W, H)))


if __name__ == "__main__":
    main("--debug" in sys.argv)

"""Final hero angle: the approved image of the completed headquarters (final_reference.webp).
Writes ../../wwwroot/assets/hero/final@1x.webp, final@2x.webp (2x Lanczos + unsharp) and final-bg.webp (blurred backdrop)."""
import os
from PIL import Image, ImageFilter
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "wwwroot", "assets", "hero"))
im = Image.open(os.path.join(HERE, "final_reference.webp")).convert("RGB")
W, H = im.size
print("final reference", W, H)
im.save(os.path.join(OUT, "final@1x.webp"), quality=88, method=6)
im.resize((W * 2, H * 2), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.6, percent=70, threshold=2)).save(os.path.join(OUT, "final@2x.webp"), quality=86, method=6)
im.resize((W // 8, H // 8), Image.LANCZOS).filter(ImageFilter.GaussianBlur(6)).save(os.path.join(OUT, "final-bg.webp"), quality=80)

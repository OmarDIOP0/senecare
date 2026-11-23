"""A demo photo « fuite sous l'évier » drawn from scratch (no stock photo, no rights to clear): dark cabinet,
white PVC pipe with a joint, water drops and a puddle, soft phone-camera light and grain."""
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1200, 900
rng = np.random.default_rng(7)

# Cabinet interior: warm dark wood, lit from the top left.
y, x = np.mgrid[0:H, 0:W]
light = 1.0 - 0.55 * np.clip(np.hypot(x - 300, y - 150) / 1100, 0, 1)
grain = (np.sin(x / 9.0 + np.sin(y / 60.0) * 3) * 0.04 + 1)
base = np.stack([88 * light * grain, 62 * light * grain, 44 * light * grain], axis=-1)
img = Image.fromarray(np.clip(base, 0, 255).astype("uint8"))
d = ImageDraw.Draw(img)

# Back panel and floor of the cabinet.
d.rectangle([0, 640, W, H], fill=(118, 92, 70))
d.line([0, 640, W, 640], fill=(70, 50, 36), width=6)

# Siphon: vertical pipe, U bend, horizontal pipe to the wall.
pipe, shade = (232, 232, 226), (190, 190, 184)
d.rectangle([520, 0, 600, 360], fill=pipe)
d.rectangle([584, 0, 600, 360], fill=shade)
d.ellipse([480, 300, 760, 520], fill=pipe)
d.ellipse([540, 330, 700, 470], fill=(80, 58, 42))
d.rectangle([480, 300, 760, 410], fill=(80, 58, 42))
d.rectangle([680, 320, 760, 420], fill=pipe)
d.rectangle([680, 300, 1200, 380], fill=pipe)
d.rectangle([680, 364, 1200, 380], fill=shade)
# Joint nut where it leaks.
d.rectangle([500, 250, 620, 300], fill=(210, 210, 204))
d.rectangle([500, 290, 620, 300], fill=(160, 160, 154))

# Water: running streak, drops, puddle with highlights.
water = Image.new("RGBA", (W, H), (0, 0, 0, 0))
w = ImageDraw.Draw(water)
w.rectangle([548, 300, 560, 520], fill=(170, 205, 230, 150))
for cx, cy, r in [(554, 560, 10), (552, 600, 8), (556, 620, 6)]:
    w.ellipse([cx - r, cy - r * 1.4, cx + r, cy + r], fill=(185, 215, 240, 200))
w.ellipse([360, 660, 820, 760], fill=(120, 150, 175, 150))
w.ellipse([430, 680, 700, 735], fill=(200, 225, 245, 90))
img = Image.alpha_composite(img.convert("RGBA"), water.filter(ImageFilter.GaussianBlur(2))).convert("RGB")

# Phone camera look: slight blur, vignette, noise.
img = img.filter(ImageFilter.GaussianBlur(1.2))
arr = np.asarray(img).astype("float32")
vignette = 1 - 0.35 * np.clip((np.hypot(x - W / 2, y - H / 2) / (W * 0.7)) ** 2, 0, 1)
arr = arr * vignette[..., None] + rng.normal(0, 5, arr.shape)
Image.fromarray(np.clip(arr, 0, 255).astype("uint8")).save(sys.argv[1], quality=86)
print("photo:", sys.argv[1])

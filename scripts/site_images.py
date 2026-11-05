"""Makes the site's derived images from the originals in site/. Needs Pillow (pip install pillow).
Run: python3 scripts/site_images.py

- img/*.jpg -> the same picture as WebP at 1280 and 640 px, plus a 640 px JPEG for browsers without WebP
- icon.png (1 MB-class, 1024 px) -> icon-32.png, icon-192.png and apple-touch-icon.png for the tab and home screen
- og-os.png: the share picture for os.html, a real Lumi OS screenshot with the title on top
"""
import pathlib
from PIL import Image, ImageDraw, ImageFilter, ImageFont

site = pathlib.Path(__file__).resolve().parent.parent / "site"

for jpg in sorted((site / "img").glob("*.jpg")):
    if jpg.stem.endswith("-640"):
        continue
    im = Image.open(jpg).convert("RGB")
    im.save(jpg.with_suffix(".webp"), "WEBP", quality=80, method=6)
    small = im.resize((640, round(im.height * 640 / im.width)), Image.LANCZOS)
    small.save(jpg.with_name(jpg.stem + "-640.webp"), "WEBP", quality=80, method=6)
    small.save(jpg.with_name(jpg.stem + "-640.jpg"), "JPEG", quality=82, optimize=True, progressive=True)

icon = Image.open(site / "icon.png").convert("RGBA")
for name, px in [("icon-32.png", 32), ("icon-192.png", 192), ("apple-touch-icon.png", 180)]:
    icon.resize((px, px), Image.LANCZOS).save(site / name, optimize=True)


def font(size, bold=True):
    for path, index in [("/System/Library/Fonts/Avenir Next.ttc", 0 if bold else 7),
                        ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 0)]:
        try:
            return ImageFont.truetype(path, size, index=index)
        except OSError:
            continue
    return ImageFont.load_default()


# og-os.png, 1280×640 like og.png: the text on the left, a real screenshot running off the right edge.
W, H = 1280, 640
canvas = Image.new("RGBA", (W, H), (6, 8, 8, 255))
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse((560, -60, 1400, 760), fill=(47, 164, 131, 90))
canvas = Image.alpha_composite(canvas, glow.filter(ImageFilter.GaussianBlur(140)))
shot = Image.open(site / "img" / "os-bar-empty.jpg").convert("RGB")
sw = 720
shot = shot.resize((sw, round(shot.height * sw / shot.width)), Image.LANCZOS)
mask = Image.new("L", shot.size, 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, shot.width - 1, shot.height - 1), radius=22, fill=255)
x0, y0 = 680, (H - shot.height) // 2
canvas.paste(shot, (x0, y0), mask)
ImageDraw.Draw(canvas).rounded_rectangle((x0, y0, x0 + shot.width - 1, y0 + shot.height - 1), radius=22, outline=(52, 64, 61, 255), width=2)
d = ImageDraw.Draw(canvas)
d.text((72, 150), "Lumi OS", font=font(118), fill=(242, 245, 244))
d.text((78, 306), "Linux with Lumi", font=font(40), fill=(95, 211, 173))
d.text((78, 356), "from minute one", font=font(40), fill=(95, 211, 173))
d.text((78, 432), "Ctrl+Space opens the AI bar.", font=font(28, False), fill=(214, 220, 218))
d.text((78, 472), "Offline AI with one button.", font=font(28, False), fill=(214, 220, 218))
d.text((78, 540), "Fedora Silverblue · GNOME", font=font(26, False), fill=(138, 150, 147))
canvas.convert("RGB").save(site / "og-os.png", optimize=True)
print("done")

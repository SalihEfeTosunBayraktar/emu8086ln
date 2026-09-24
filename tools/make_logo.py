"""Generates the emu8086ln icon: a simplified, upright DIP chip with a bold "LN",
drawn at every size with a level of detail that stays legible (16 px .. 1024 px)."""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Emu8086.App', 'Assets')
SCRATCH = os.path.dirname(os.path.abspath(__file__))  # preview sheet goes next to this script

BG_TOP = (126, 184, 247)      # light sky blue (like the step 5 artwork)
BG_BOTTOM = (61, 125, 226)    # deeper blue
BODY = (30, 32, 38)
BODY_EDGE = (58, 62, 72)
PIN = (240, 196, 84)
PIN_SHADE = (196, 150, 50)
TEXT = (255, 255, 255)

FONT_CANDIDATES = [r'C:\Windows\Fonts\segoeuib.ttf', r'C:\Windows\Fonts\arialbd.ttf']


def font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def render(size):
    # Draw at 4x and downsample for clean anti-aliasing.
    s = size * 4
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))

    # Rounded-square background with a vertical gradient.
    grad = Image.new('RGBA', (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / (s - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (s, y)], fill=c)
    mask = Image.new('L', (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    small = size <= 32
    tiny = size <= 24
    pins_per_side = 2 if tiny else 4 if size <= 48 else 6

    # Chip body (upright, wider than tall so "LN" is large).
    bw, bh = (s * 0.70, s * 0.70) if tiny else (s * 0.56, s * 0.62)
    x0, y0 = (s - bw) / 2, (s - bh) / 2 + s * 0.01
    x1, y1 = x0 + bw, y0 + bh

    # Soft shadow for larger sizes.
    if not small:
        shadow = Image.new('RGBA', (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle([x0 + s * 0.02, y0 + s * 0.04, x1 + s * 0.02, y1 + s * 0.04],
                                                 radius=s * 0.05, fill=(10, 30, 70, 110))
        shadow = shadow.filter(ImageFilter.GaussianBlur(s * 0.03))
        img.alpha_composite(shadow)
        d = ImageDraw.Draw(img)

    # Pins on the left and right edges.
    pin_len = s * (0.11 if tiny else 0.09 if small else 0.08)
    pin_h = bh / (pins_per_side * 2 + 1) * (1.15 if small else 1.0)
    gap = bh / (pins_per_side * 2 + 1)
    for i in range(pins_per_side):
        py = y0 + gap * (2 * i + 1) + (gap - pin_h) / 2
        for left in (True, False):
            px0 = x0 - pin_len if left else x1 - s * 0.01
            px1 = x0 + s * 0.01 if left else x1 + pin_len
            d.rounded_rectangle([px0, py, px1, py + pin_h], radius=pin_h * 0.3, fill=PIN)
            if not small:
                d.rectangle([px0, py + pin_h * 0.62, px1, py + pin_h], fill=PIN_SHADE)

    # Body with a subtle lighter edge.
    d.rounded_rectangle([x0, y0, x1, y1], radius=s * 0.05, fill=BODY_EDGE)
    inset = s * (0.012 if small else 0.018)
    d.rounded_rectangle([x0 + inset, y0 + inset, x1 - inset, y1 - inset], radius=s * 0.04, fill=BODY)

    # Orientation notch and pin-1 dot (skipped at tiny sizes).
    if size >= 32:
        nr = bw * 0.1
        d.pieslice([s / 2 - nr, y0 - nr, s / 2 + nr, y0 + nr], 0, 180, fill=BODY_EDGE)
        d.pieslice([s / 2 - nr + inset, y0 - nr + inset, s / 2 + nr - inset, y0 + nr - inset], 0, 180, fill=(12, 13, 16))
    if size >= 48:
        r = bw * 0.035
        cx, cy = x0 + bw * 0.14, y0 + bh * 0.12
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(12, 13, 16))

    # "LN" as large as the body allows.
    text = 'LN'
    fsize = int(bh * (0.60 if tiny else 0.52 if small else 0.44))
    f = font(fsize)
    box = d.textbbox((0, 0), text, font=f)
    tw, th = box[2] - box[0], box[3] - box[1]
    while tw > bw * (0.92 if tiny else 0.82) and fsize > 6:
        fsize -= 2
        f = font(fsize)
        box = d.textbbox((0, 0), text, font=f)
        tw, th = box[2] - box[0], box[3] - box[1]
    tx = (s - tw) / 2 - box[0]
    ty = y0 + (bh - th) / 2 - box[1] + bh * 0.03
    d.text((tx, ty), text, font=f, fill=TEXT)

    return img.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [render(n) for n in sizes]
images[-1].save(os.path.join(OUT, 'app.ico'), sizes=[(n, n) for n in sizes], append_images=images[:-1])
render(128).save(os.path.join(OUT, 'logo.png'))
render(1024).save(os.path.join(OUT, 'logo_1024.png'))

# Preview sheet: every size on light and dark backgrounds.
sheet = Image.new('RGBA', (900, 300), (255, 255, 255, 255))
dark = Image.new('RGBA', (900, 150), (30, 31, 34, 255))
sheet.paste(dark, (0, 150))
x = 20
for n, im in zip(sizes, images):
    sheet.alpha_composite(im, (x, 75 - n // 2))
    sheet.alpha_composite(im, (x, 225 - n // 2))
    x += n + 30
sheet.save(os.path.join(SCRATCH, 'logo_preview.png'))
print('ok')

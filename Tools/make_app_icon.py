"""Draws the app icon: the two cards (greeting + business) inside the home screen's white scan corners.

Usage: python -I make_app_icon.py <output folder>
Writes AppIcon.png (1024, legacy/default), AppIcon_Round.png (1024, circle), and the Android adaptive layers
AppIcon_Back.png / AppIcon_Fore.png (432; the foreground keeps inside the 66/108 safe circle). Needs Pillow.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

SS = 4  # supersampling

INK_TOP, INK_BOTTOM = (28, 38, 58), (11, 14, 20)  # home screen ink 0B0E14
GOLD, RED, RED_DARK, CREAM, NAVY = (255, 199, 64), (200, 50, 60), (150, 30, 42), (243, 235, 221), (30, 48, 84)


def background(n):
    img = Image.new("RGBA", (n, n))
    d = ImageDraw.Draw(img)
    for y in range(n):
        t = y / (n - 1)
        d.line([(0, y), (n, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(INK_TOP, INK_BOTTOM)) + (255,))
    glow = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    r = n * 0.32
    ImageDraw.Draw(glow).ellipse([n / 2 - r, n * 0.47 - r, n / 2 + r, n * 0.47 + r], fill=GOLD + (70,))
    return Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(n * 0.12)))


def card(n, w, h, fill, paint):
    """A rounded card drawn upright on its own layer; paint(draw, x0, y0, w, h) adds the details."""
    layer = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x0, y0 = (n - w) / 2, (n - h) / 2
    d.rounded_rectangle([x0, y0, x0 + w, y0 + h], radius=w * 0.08, fill=fill)
    paint(d, x0, y0, w, h)
    return layer


def star(d, cx, cy, r, points=5, inner=0.45, fill=GOLD):
    pts = []
    for i in range(points * 2):
        a = -math.pi / 2 + i * math.pi / points
        k = r if i % 2 == 0 else r * inner
        pts.append((cx + k * math.cos(a), cy + k * math.sin(a)))
    d.polygon(pts, fill=fill)


def greeting(d, x0, y0, w, h):
    d.rounded_rectangle([x0 + w * 0.08, y0 + w * 0.08, x0 + w * 0.92, y0 + h - w * 0.08], radius=w * 0.05,
                        outline=GOLD, width=max(1, round(w * 0.025)))
    # a little tree topped by the star
    cx, base = x0 + w / 2, y0 + h * 0.80
    for i, (top, half) in enumerate([(0.36, 0.20), (0.48, 0.26), (0.60, 0.32)]):
        d.polygon([(cx, y0 + h * top), (cx - w * half, base - h * (0.24 - i * 0.12)), (cx + w * half, base - h * (0.24 - i * 0.12))],
                  fill=(34, 120, 70))
    d.rectangle([cx - w * 0.05, base, cx + w * 0.05, base + h * 0.07], fill=RED_DARK)
    star(d, cx, y0 + h * 0.33, w * 0.14)


def business(d, x0, y0, w, h):
    r = w * 0.13
    d.ellipse([x0 + w * 0.12, y0 + h * 0.14, x0 + w * 0.12 + 2 * r, y0 + h * 0.14 + 2 * r], fill=NAVY)
    lw = max(1, round(h * 0.045))
    for i, length in enumerate([0.55, 0.40]):
        y = y0 + h * (0.20 + i * 0.13)
        d.rounded_rectangle([x0 + w * 0.47, y, x0 + w * (0.47 + length * 0.8), y + lw], radius=lw / 2, fill=NAVY)
    for i, length in enumerate([0.70, 0.55, 0.62]):
        y = y0 + h * (0.60 + i * 0.11)
        d.rounded_rectangle([x0 + w * 0.12, y, x0 + w * (0.12 + length), y + lw * 0.8], radius=lw / 2, fill=NAVY + (150,))


def corners(d, n, half, arm, width):
    c = n / 2
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = c + sx * half, c + sy * half
            d.line([(x, y - sy * arm), (x, y), (x - sx * arm, y)], fill=(255, 255, 255, 242), width=width, joint="curve")
            for px, py in [(x, y - sy * arm), (x - sx * arm, y)]:
                d.ellipse([px - width / 2, py - width / 2, px + width / 2, py + width / 2], fill=(255, 255, 255, 242))


def sparkle(d, cx, cy, r):
    star(d, cx, cy, r, points=4, inner=0.28, fill=(255, 236, 170))


def foreground(n, s):
    """Cards, corners and sparkles; s scales the artwork about the centre (1 = full icon)."""
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    cw, ch = n * 0.30 * s, n * 0.42 * s
    for paint, fill, angle, dx, dy in [(business, CREAM, 14, -0.085, 0.02), (greeting, RED, -10, 0.085, -0.01)]:
        layer = card(n, cw, ch, fill + (255,), paint)
        shadow = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        shadow.putalpha(layer.getchannel("A").point(lambda a: a * 0.45))
        shadow = shadow.filter(ImageFilter.GaussianBlur(n * 0.015 * s))
        for part, off in [(shadow, n * 0.012 * s), (layer, 0)]:
            moved = part.rotate(angle, resample=Image.BICUBIC, center=(n / 2, n / 2),
                                translate=(n * dx * s, n * dy * s + off))
            img = Image.alpha_composite(img, moved)
    d = ImageDraw.Draw(img)
    corners(d, n, n * 0.34 * s, n * 0.11 * s, round(n * 0.032 * s))
    sparkle(d, n / 2 + n * 0.25 * s, n / 2 - n * 0.24 * s, n * 0.06 * s)
    sparkle(d, n / 2 - n * 0.27 * s, n / 2 + n * 0.20 * s, n * 0.04 * s)
    return img


def mask(n, shape):
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    if shape == "circle":
        d.ellipse([0, 0, n - 1, n - 1], fill=255)
    else:
        d.rounded_rectangle([0, 0, n - 1, n - 1], radius=n * 0.22, fill=255)
    return m


def render(size, draw):
    return draw(size * SS).resize((size, size), Image.LANCZOS)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    def full(shape, s):
        def draw(n):
            img = Image.alpha_composite(background(n), foreground(n, s))
            img.putalpha(mask(n, shape))
            return img
        return draw

    render(1024, full("square", 1.0)).save(os.path.join(out, "AppIcon.png"))
    render(1024, full("circle", 0.92)).save(os.path.join(out, "AppIcon_Round.png"))
    render(432, background).save(os.path.join(out, "AppIcon_Back.png"))
    fore = render(432, lambda n: foreground(n, 0.58))
    fore.save(os.path.join(out, "AppIcon_Fore.png"))

    # self-check: adaptive foreground stays inside the 66/108 safe circle
    a = fore.getchannel("A").load()
    c, safe = 216, 432 * 33 / 108
    outside = [(x, y) for y in range(432) for x in range(432) if a[x, y] > 8 and math.hypot(x - c, y - c) > safe]
    assert not outside, f"{len(outside)} foreground pixels outside the safe zone, e.g. {outside[0]}"


if __name__ == "__main__":
    main()

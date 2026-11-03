"""Draws placeholder portfolio pictures in the business card's style: a navy network of nodes with orange diamonds.
Replace them with real screenshots of your work (see Assets/Resources/business_card.json).

Usage: python -I make_placeholders.py <output folder>
"""
import math
import os
import random
import struct
import sys
import zlib

W, H = 960, 540
NAVY, NAVY_LIGHT, SLATE, CREAM, ORANGE = (0x14, 0x21, 0x3D), (0x24, 0x38, 0x66), (0x5C, 0x6B, 0x8F), (0xF7, 0xF5, 0xF0), (0xF2, 0x6A, 0x1B)


def blank():
    px = bytearray()
    for y in range(H):
        t = y / (H - 1)
        row = bytes(int(NAVY[c] + (NAVY_LIGHT[c] - NAVY[c]) * t) for c in range(3))
        px += row * W
    return px


def stamp(px, cx, cy, r, colour):
    r2 = r * r
    for y in range(max(0, int(cy - r) - 1), min(H, int(cy + r) + 2)):
        for x in range(max(0, int(cx - r) - 1), min(W, int(cx + r) + 2)):
            d2 = (x - cx) ** 2 + (y - cy) ** 2
            if d2 <= r2:
                o = (y * W + x) * 3
                px[o:o + 3] = bytes(colour)


def draw_line(px, a, b, width, colour):
    steps = int(math.hypot(b[0] - a[0], b[1] - a[1]))
    for i in range(steps + 1):
        t = i / max(steps, 1)
        stamp(px, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, width / 2, colour)


def diamond(px, cx, cy, r, colour):
    for y in range(int(cy - r), int(cy + r) + 1):
        half = r - abs(y - cy)
        for x in range(int(cx - half), int(cx + half) + 1):
            if 0 <= x < W and 0 <= y < H:
                o = (y * W + x) * 3
                px[o:o + 3] = bytes(colour)


def picture(seed):
    rnd = random.Random(seed)
    px = blank()
    nodes = [(rnd.randint(60, W - 60), rnd.randint(50, H - 50)) for _ in range(22)]
    for i, a in enumerate(nodes):  # each node links to its two nearest neighbours
        near = sorted((n for n in nodes if n is not a), key=lambda n: (n[0] - a[0]) ** 2 + (n[1] - a[1]) ** 2)[:2]
        for b in near:
            draw_line(px, a, b, 3, SLATE)
    for i, (x, y) in enumerate(nodes):
        if i % 6 == 0:
            diamond(px, x, y, 15, ORANGE)
        elif i % 2 == 0:
            stamp(px, x, y, 9, CREAM)
            stamp(px, x, y, 5, NAVY)
        else:
            stamp(px, x, y, 5, CREAM)
    return px


def chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def write_png(path, px):
    raw = b"".join(b"\x00" + bytes(px[y * W * 3:(y + 1) * W * 3]) for y in range(H))
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


if __name__ == "__main__":
    folder = sys.argv[1]
    os.makedirs(folder, exist_ok=True)
    for n in (1, 2, 3):
        write_png(os.path.join(folder, f"placeholder_{n}.png"), picture(n))
        print(f"placeholder_{n}.png")

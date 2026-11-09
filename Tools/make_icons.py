"""Draws the business card's UI icons as white-on-transparent 128 px PNGs (stdlib only).

Usage: python -I make_icons.py <output folder> [contact-sheet.png]
Shapes are signed-distance functions in a -0.5..0.5 square (y up); the edge is anti-aliased from the distance.
"""
import math
import os
import struct
import sys
import zlib

N = 128


# ------------------------------------------------------------------ distance helpers (negative = inside)

def seg(x, y, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(x - (ax + t * dx), y - (ay + t * dy))


def line(x, y, pts, w):
    """A polyline stroke of width w (round caps)."""
    return min(seg(x, y, *pts[i], *pts[i + 1]) for i in range(len(pts) - 1)) - w / 2


def circle(x, y, cx, cy, r):
    return math.hypot(x - cx, y - cy) - r


def rbox(x, y, cx, cy, hx, hy, r):
    qx, qy = abs(x - cx) - (hx - r), abs(y - cy) - (hy - r)
    return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r


def ring(d, w):
    return abs(d) - w / 2


def polygon(x, y, pts):
    d = min(seg(x, y, *pts[i], *pts[(i + 1) % len(pts)]) for i in range(len(pts)))
    inside = False
    for i in range(len(pts)):
        (x1, y1), (x2, y2) = pts[i], pts[(i + 1) % len(pts)]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            inside = not inside
    return -d if inside else d


# ------------------------------------------------------------------ icons

def mail(x, y):
    body = ring(rbox(x, y, 0, 0, 0.38, 0.27, 0.05), 0.07)
    flap = line(x, y, [(-0.35, 0.21), (0, -0.05), (0.35, 0.21)], 0.07)
    return min(body, flap)


def phone(x, y):
    body = ring(rbox(x, y, 0, 0, 0.21, 0.37, 0.07), 0.07)
    speaker = line(x, y, [(-0.07, 0.27), (0.07, 0.27)], 0.04)
    home = circle(x, y, 0, -0.27, 0.035)
    return min(body, speaker, home)


def globe(x, y):
    outer = ring(circle(x, y, 0, 0, 0.35), 0.065)
    inside = circle(x, y, 0, 0, 0.35)
    meridian = max(ring((math.hypot(x / 0.16, y / 0.35) - 1) * 0.16, 0.06), inside)  # clipped: no bumps past the ring
    bars = min(line(x, y, [(-0.35, 0), (0.35, 0)], 0.06),
               line(x, y, [(-0.31, 0.17), (0.31, 0.17)], 0.055),
               line(x, y, [(-0.31, -0.17), (0.31, -0.17)], 0.055))
    return min(outer, meridian, max(bars, inside))


def close(x, y):
    return min(line(x, y, [(-0.24, -0.24), (0.24, 0.24)], 0.09), line(x, y, [(-0.24, 0.24), (0.24, -0.24)], 0.09))


def about(x, y):
    head = circle(x, y, 0, 0.15, 0.14)
    body = max(circle(x, y, 0, -0.31, 0.28), -0.39 - y)
    return min(head, body)


def skills(x, y):
    bolt = [(0.07, 0.42), (-0.21, -0.04), (-0.02, -0.04), (-0.09, -0.42), (0.21, 0.07), (0.02, 0.07)]
    return polygon(x, y, bolt)


def portfolio(x, y):
    return min(rbox(x, y, sx * 0.175, sy * 0.175, 0.135, 0.135, 0.045) for sx in (-1, 1) for sy in (-1, 1))


def prev(x, y):
    return line(x, y, [(0.1, 0.27), (-0.14, 0), (0.1, -0.27)], 0.1)


def next_(x, y):
    return line(x, y, [(-0.1, 0.27), (0.14, 0), (-0.1, -0.27)], 0.1)


def trophy(x, y):
    """The achievement popup's cup (written to Assets/Resources/Icons, loaded at runtime)."""
    cup = rbox(x, y, 0, 0.14, 0.2, 0.2, 0.12)
    handles = min(ring(circle(x, y, -0.2, 0.17, 0.09), 0.05), ring(circle(x, y, 0.2, 0.17, 0.09), 0.05))
    stem = rbox(x, y, 0, -0.12, 0.045, 0.08, 0.02)
    base = rbox(x, y, 0, -0.26, 0.17, 0.055, 0.03)
    return min(cup, handles, stem, base)


ICONS = {"icon_mail": mail, "icon_phone": phone, "icon_globe": globe, "icon_close": close, "icon_about": about,
         "icon_skills": skills, "icon_portfolio": portfolio, "icon_prev": prev, "icon_next": next_}


# ------------------------------------------------------------------ output

def alpha_rows(shape):
    rows = []
    for j in range(N):
        y = 0.5 - (j + 0.5) / N
        rows.append([max(0.0, min(1.0, 0.5 - shape((i + 0.5) / N - 0.5, y) * N)) for i in range(N)])
    return rows


def chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def write_png(path, width, height, pixels, channels):
    raw = b"".join(b"\x00" + bytes(pixels[r * width * channels:(r + 1) * width * channels]) for r in range(height))
    colour_type = 6 if channels == 4 else 2
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    folder = sys.argv[1]
    os.makedirs(folder, exist_ok=True)
    rendered = {}
    for name, shape in ICONS.items():
        rows = alpha_rows(shape)
        rendered[name] = rows
        pixels = bytearray()
        for row in rows:
            for a in row:
                pixels += bytes((255, 255, 255, int(round(a * 255))))
        write_png(os.path.join(folder, name + ".png"), N, N, pixels, 4)
        print(name)

    if len(sys.argv) > 2:  # a preview sheet: cream icons on navy
        cols, cell = 5, 150
        rows_n = (len(ICONS) + cols - 1) // cols
        w, h = cols * cell, rows_n * cell
        bg, fg = (0x14, 0x21, 0x3D), (0xF7, 0xF5, 0xF0)
        sheet = bytearray(bytes(bg) * (w * h))
        for k, rows in enumerate(rendered.values()):
            ox, oy = (k % cols) * cell + (cell - N) // 2, (k // cols) * cell + (cell - N) // 2
            for j in range(N):
                for i in range(N):
                    a = rows[j][i]
                    o = ((oy + j) * w + ox + i) * 3
                    for c in range(3):
                        sheet[o + c] = int(fg[c] * a + bg[c] * (1 - a))
        write_png(sys.argv[2], w, h, sheet, 3)


if __name__ == "__main__":
    main()

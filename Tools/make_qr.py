"""Makes the Portfolio wall's QR code (Assets/Resources/Icons/qr_website.png) for the website in business_card.json.

A small QR encoder (byte mode, error correction M, versions 1-6), standard library only. Run it again after
changing the website. Check: python -I Tools/make_qr.py (prints the decoded text when OpenCV is installed).
"""
import json
import os
import struct
import sys
import zlib

# version: (total codewords, ec codewords per block, blocks) for error correction level M
TABLE = {1: (26, 10, 1), 2: (44, 16, 1), 3: (70, 26, 1), 4: (100, 18, 2), 5: (134, 24, 2), 6: (172, 16, 4)}
ALIGN = {1: [], 2: [6, 18], 3: [6, 22], 4: [6, 26], 5: [6, 30], 6: [6, 34]}

# GF(256) with the QR polynomial 0x11d
EXP, LOG = [0] * 512, [0] * 256
x = 1
for i in range(255):
    EXP[i], LOG[x] = x, i
    x <<= 1
    if x & 0x100:
        x ^= 0x11D
for i in range(255, 512):
    EXP[i] = EXP[i - 255]


def gf_mul(a, b):
    return 0 if a == 0 or b == 0 else EXP[LOG[a] + LOG[b]]


def rs_ec(data, n):
    gen = [1]
    for i in range(n):
        gen = [a ^ b for a, b in zip(gen + [0], [0] + [gf_mul(c, EXP[i]) for c in gen])]
    rem = list(data) + [0] * n
    for i in range(len(data)):
        coef = rem[i]
        if coef:
            for j in range(1, len(gen)):
                rem[i + j] ^= gf_mul(gen[j], coef)
    return rem[len(data):]


def encode(text):
    raw = text.encode("utf-8")
    for version, (total, ec, blocks) in TABLE.items():
        data_cw = total - ec * blocks
        if 4 + 8 + 8 * len(raw) <= 8 * data_cw:
            break
    else:
        raise ValueError("too long for versions 1-6")
    bits = "0100" + format(len(raw), "08b") + "".join(format(b, "08b") for b in raw)
    bits += "0" * min(4, 8 * data_cw - len(bits))
    bits += "0" * (-len(bits) % 8)
    cw = [int(bits[i:i + 8], 2) for i in range(0, len(bits), 8)]
    for i in range(data_cw - len(cw)):  # pad bytes alternate 0xEC, 0x11
        cw.append(0xEC if i % 2 == 0 else 0x11)
    # split into blocks (all the same size for these versions), interleave data, then ec
    size = data_cw // blocks
    dblocks = [cw[i * size:(i + 1) * size] for i in range(blocks)]
    eblocks = [rs_ec(b, ec) for b in dblocks]
    final = [b[i] for i in range(size) for b in dblocks] + [b[i] for i in range(ec) for b in eblocks]
    return version, final


def build(text):
    version, codewords = encode(text)
    n = 17 + 4 * version
    m = [[None] * n for _ in range(n)]
    reserved = [[False] * n for _ in range(n)]

    def put(r, c, v, res=True):
        m[r][c] = v
        reserved[r][c] = res

    def finder(r0, c0):
        for r in range(-1, 8):
            for c in range(-1, 8):
                rr, cc = r0 + r, c0 + c
                if 0 <= rr < n and 0 <= cc < n:
                    on = 0 <= r <= 6 and 0 <= c <= 6 and (r in (0, 6) or c in (0, 6) or (2 <= r <= 4 and 2 <= c <= 4))
                    put(rr, cc, on)

    finder(0, 0)
    finder(0, n - 7)
    finder(n - 7, 0)
    for i in range(8, n - 8):  # timing patterns
        put(6, i, i % 2 == 0)
        put(i, 6, i % 2 == 0)
    centres = ALIGN[version]
    for r in centres:
        for c in centres:
            if reserved[r][c]:
                continue
            for dr in range(-2, 3):
                for dc in range(-2, 3):
                    put(r + dr, c + dc, max(abs(dr), abs(dc)) != 1)
    put(n - 8, 8, True)  # the dark module
    for i in range(9):  # format areas (filled later)
        if not reserved[8][i]:
            reserved[8][i] = True
        if not reserved[i][8]:
            reserved[i][8] = True
    for i in range(8):
        reserved[8][n - 1 - i] = True
        reserved[n - 1 - i][8] = True

    # data, zig-zag from the bottom right, two columns at a time
    bits = "".join(format(b, "08b") for b in codewords)
    k, upward, col = 0, True, n - 1
    while col > 0:
        if col == 6:
            col -= 1
        rows = range(n - 1, -1, -1) if upward else range(n)
        for r in rows:
            for c in (col, col - 1):
                if not reserved[r][c]:
                    m[r][c] = k < len(bits) and bits[k] == "1"
                    k += 1
        upward = not upward
        col -= 2

    def masked(mask):
        f = [[False if v is None else v for v in row] for row in m]
        for r in range(n):
            for c in range(n):
                if not reserved[r][c]:
                    flip = [(r + c) % 2 == 0, r % 2 == 0, c % 3 == 0, (r + c) % 3 == 0,
                            (r // 2 + c // 3) % 2 == 0, (r * c) % 2 + (r * c) % 3 == 0,
                            ((r * c) % 2 + (r * c) % 3) % 2 == 0, ((r + c) % 2 + (r * c) % 3) % 2 == 0][mask]
                    if flip:
                        f[r][c] = not f[r][c]
        # format info: level M = 00, then the mask, BCH(15,5), XOR 101010000010010
        d = (0b00 << 3) | mask
        v = d << 10
        g = 0b10100110111
        for i in range(14, 9, -1):
            if v & (1 << i):
                v ^= g << (i - 10)
        fmt = ((d << 10) | v) ^ 0b101010000010010
        fb = [(fmt >> i) & 1 == 1 for i in range(15)]  # bit 0 = least significant
        for i in range(6):            # first copy, round the top-left finder
            f[i][8] = fb[i]
        f[7][8], f[8][8], f[8][7] = fb[6], fb[7], fb[8]
        for i in range(9, 15):
            f[8][14 - i] = fb[i]
        for i in range(8):            # second copy, split between the other two finders
            f[8][n - 1 - i] = fb[i]
        for i in range(8, 15):
            f[n - 15 + i][8] = fb[i]
        f[n - 8][8] = True            # the dark module
        return f

    def penalty(f):
        p = 0
        for grid in (f, list(map(list, zip(*f)))):
            for row in grid:
                run = 1
                for i in range(1, n):
                    if row[i] == row[i - 1]:
                        run += 1
                    else:
                        if run >= 5:
                            p += run - 2
                        run = 1
                if run >= 5:
                    p += run - 2
        for r in range(n - 1):
            for c in range(n - 1):
                if f[r][c] == f[r + 1][c] == f[r][c + 1] == f[r + 1][c + 1]:
                    p += 3
        dark = sum(v for row in f for v in row)
        p += abs(dark * 20 // (n * n) - 10) * 10
        return p

    best = min((masked(k) for k in range(8)), key=penalty)
    return best


def write_png(path, f, scale=16, border=4):
    n = len(f)
    size = (n + 2 * border) * scale
    rows = []
    for y in range(size):
        r = y // scale - border
        line = bytearray([0])
        for xx in range(size):
            c = xx // scale - border
            dark = 0 <= r < n and 0 <= c < n and f[r][c]
            line += b"\x14\x21\x3d\xff" if dark else b"\xf7\xf5\xf0\xff"  # the card's navy on cream
        rows.append(bytes(line))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as fh:
        fh.write(png)


if __name__ == "__main__":
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    with open(os.path.join(root, "Assets", "Resources", "business_card.json"), encoding="utf-8") as fh:
        site = json.load(fh).get("website", "howardw.web.app")
    url = site if site.startswith("http") else "https://" + site
    out = os.path.join(root, "Assets", "Resources", "Icons", "qr_website.png")
    write_png(out, build(url))
    print("wrote", out, "for", url)
    try:
        import cv2
        img = cv2.imread(out)
        text, _, _ = cv2.QRCodeDetector().detectAndDecode(img)
        print("decoded:", repr(text), "OK" if text == url else "MISMATCH")
        sys.exit(0 if text == url else 1)
    except ImportError:
        pass

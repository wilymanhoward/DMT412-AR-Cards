"""Draws a title card for each game on howardw.web.app that has no screenshot to show, in the business card's
navy and orange: the game's name, a one-line genre, and the tools and year.

Usage: python make_game_cards.py <font .ttf> <output folder>
"""
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFont

W, H = 960, 540
NAVY, NAVY_LIGHT, SLATE, CREAM, ORANGE = (0x14, 0x21, 0x3D), (0x24, 0x38, 0x66), (0xAE, 0xB7, 0xCC), (0xF7, 0xF5, 0xF0), (0xF2, 0x6A, 0x1B)

# AquaStrike isn't here: it uses the real gameplay screenshot from the site (projects/aquastrike/gameplay.webp).
GAMES = [
    ("game_we_were_mummies", "We Were Mummies", "2-player online co-op escape", "Unity  ·  Photon  ·  2026"),
    ("game_sea_vagabond", "Sea Vagabond", "Third-person action-adventure", "Unity  ·  Team of 6  ·  2025"),
    ("game_mr_detective", "MR Detective", "Co-located mixed reality mystery", "Unity  ·  Meta Quest 3  ·  2026"),
]


def card(font_path, title, genre, tools, seed):
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    for y in range(H):  # navy gradient
        t = y / (H - 1)
        d.line([(0, y), (W, y)], fill=tuple(int(NAVY[c] + (NAVY_LIGHT[c] - NAVY[c]) * t) for c in range(3)))

    rng = random.Random(seed)  # a faint node network behind the words, like the card
    nodes = [(rng.randint(0, W), rng.randint(0, H)) for _ in range(26)]
    for i, a in enumerate(nodes):
        for b in nodes[i + 1:]:
            if (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 < 190 ** 2:
                d.line([a, b], fill=(0x34, 0x48, 0x78), width=2)
    for x, y in nodes:
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(0x4A, 0x5E, 0x90))

    d.rectangle([0, 0, W, 14], fill=ORANGE)
    big = ImageFont.truetype(font_path, 92)
    while d.textlength(title, font=big) > W - 140:  # long names shrink to fit
        big = ImageFont.truetype(font_path, big.size - 4)
    mid = ImageFont.truetype(font_path, 40)
    small = ImageFont.truetype(font_path, 30)
    d.text((W / 2, H / 2 - 40), title, font=big, fill=CREAM, anchor="mm", stroke_width=1, stroke_fill=CREAM)
    d.text((W / 2, H / 2 + 50), genre, font=mid, fill=ORANGE, anchor="mm")
    d.text((W / 2, H - 70), tools, font=small, fill=SLATE, anchor="mm")
    s = 12  # an orange diamond above the title
    cx, cy = W / 2, H / 2 - 130
    d.polygon([(cx, cy - s), (cx + s, cy), (cx, cy + s), (cx - s, cy)], fill=ORANGE)
    return img


def main():
    font_path, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for i, (name, title, genre, tools) in enumerate(GAMES):
        img = card(font_path, title, genre, tools, i)
        assert img.size == (W, H)
        img.save(os.path.join(out, name + ".png"))
        print("wrote", name + ".png")


if __name__ == "__main__":
    main()

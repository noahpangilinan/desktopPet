"""Draw the built-in pet sprite sheets.

Each animation is a block of rows in pets/<name>/sheet.png: one row of frames
for a side-on pet (drawn facing right, mirrored by the app), or one row per
heading for a top-down pet (heading 0 faces right, then clockwise). pet.ini
says where each animation starts.

Shapes are rasterized straight onto the pixel grid with no antialiasing, then
given a one-pixel dark outline, so the result stays crisp when scaled up.

Run from the repo root:  py tools/draw_sprites.py
Pass --preview to also write an enlarged copy next to each sheet.
"""

import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
CLEAR = (0, 0, 0, 0)


def rgba(hex_color):
    hex_color = hex_color.lstrip("#")
    return tuple(int(hex_color[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def outline(img, color):
    """Paint every clear pixel that touches a filled pixel (4-neighbour)."""
    w, h = img.size
    src = img.load()
    out = img.copy()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            if src[x, y][3]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and src[nx, ny][3]:
                    dst[x, y] = color
                    break
    return out


# --- Koi: top-down, any heading ------------------------------------------

KOI = {
    "size": 32,
    "frames": 8,
    "headings": 16,
    # name: (tail sway in pixels, pectoral fin reach, fin flap)
    "anims": {"swim": (2.4, 2.6, 0.7), "glide": (0.8, 1.8, 0.3)},
    "white": rgba("#f4f1ea"),
    "shade": rgba("#d9d3c7"),
    "red": rgba("#e0472b"),
    "fin": rgba("#e9e4da"),
    "eye": rgba("#1d1f24"),
    "line": rgba("#2b2a2e"),
}


def koi_frame(phase, angle, sway, fin_reach, fin_flap):
    """The koi at one point in its tail cycle, its head pointing along angle.

    Each pixel centre is turned back into the fish's own coordinates (u runs
    tail to head, v across the body) and tested against the body shape there,
    so every heading is drawn fresh instead of rotating finished pixels.
    """
    size = KOI["size"]
    img = Image.new("RGBA", (size, size), CLEAR)
    px = img.load()
    centre = size / 2
    tail_u, head_u = -13.0, 13.0
    length = head_u - tail_u
    cos_a, sin_a = math.cos(angle), math.sin(angle)

    def spine(t):
        # Sideways wave that travels toward the tail and grows there.
        return sway * math.sin(phase - 3.2 * t) * (1 - t) ** 1.6

    def half_width(t):
        if t < 0.16:  # forked tail fin
            return 1.2 + (0.16 - t) * 22
        if t < 0.24:  # tail stem
            return 1.0
        body = math.sin(min(1.0, (t - 0.24) / 0.76) * math.pi * 0.92)
        return 1.0 + 3.0 * body

    def to_pixel(u, v):
        x = centre + u * cos_a - v * sin_a
        y = centre + u * sin_a + v * cos_a
        return int(math.floor(x)), int(math.floor(y))

    reach = fin_reach + fin_flap * math.sin(phase)
    for y in range(size):
        for x in range(size):
            dx, dy = x + 0.5 - centre, y + 0.5 - centre
            u = dx * cos_a + dy * sin_a
            v = -dx * sin_a + dy * cos_a
            t = (u - tail_u) / length
            if not 0 <= t <= 1:
                continue
            cy, hw = spine(t), half_width(t)
            off = abs(v - cy)
            if off > hw:
                # Pectoral fins: swept-back paddles just behind the head.
                d = off - hw
                if d <= reach and 0.66 - 0.035 * d <= t <= 0.76 - 0.035 * d:
                    px[x, y] = KOI["fin"]
                continue
            s = (v - cy) / hw
            if t < 0.16:
                if not (abs(s) < 0.35 and t < 0.1):  # notch the fork
                    px[x, y] = KOI["fin"]
                continue
            color = KOI["white"]
            if abs(s) > 0.75:
                color = KOI["shade"]
            # Kohaku patches, placed in (t, s) so they sway with the body.
            if ((t - 0.42) / 0.11) ** 2 + ((s + 0.25) / 0.85) ** 2 < 1:
                color = KOI["red"]
            if ((t - 0.66) / 0.09) ** 2 + ((s - 0.3) / 0.8) ** 2 < 1:
                color = KOI["red"]
            if ((t - 0.88) / 0.05) ** 2 + (s / 0.55) ** 2 < 1:
                color = KOI["red"]  # tancho spot on the head
            px[x, y] = color

    # Eyes near the front of the head, only where they land on the body.
    eye_t = 0.94
    for side in (-1, 1):
        ex, ey = to_pixel(tail_u + eye_t * length,
                          spine(eye_t) + side * (half_width(eye_t) - 0.6))
        if 0 <= ex < size and 0 <= ey < size and px[ex, ey][3]:
            px[ex, ey] = KOI["eye"]

    return outline(img, KOI["line"])


def koi_sheet():
    size, n, headings = KOI["size"], KOI["frames"], KOI["headings"]
    rows = []
    for sway, fin_reach, fin_flap in KOI["anims"].values():
        for h in range(headings):
            angle = 2 * math.pi * h / headings
            rows.append([koi_frame(2 * math.pi * i / n, angle, sway, fin_reach, fin_flap)
                         for i in range(n)])
    ini = [f"frameWidth={size}", f"frameHeight={size}", f"headings={headings}",
           "speed=30        ; pixels per second at 1x", ""]
    for i, name in enumerate(KOI["anims"]):
        ini += [f"[{name}]", f"row={i * headings}", f"frames={n}", "fps=10", ""]
    return rows, ini


# --- Heron: side view, flying right --------------------------------------

HERON = {
    "size": (32, 32),
    "frames": 6,
    "body": rgba("#93a3b6"),
    "belly": rgba("#c9d1db"),
    "wing": rgba("#7f90a6"),
    "primary": rgba("#3c4655"),
    "neck": rgba("#e6e9ee"),
    "crest": rgba("#22262d"),
    "bill": rgba("#e9b544"),
    "leg": rgba("#7a6a48"),
    "eye": rgba("#f2d24a"),
    "line": rgba("#262a31"),
}


def heron_frame(phase):
    w, h = HERON["size"]
    img = Image.new("RGBA", (w, h), CLEAR)
    d = ImageDraw.Draw(img)
    b = round(-1.2 * math.sin(phase))  # body lifts on the downstroke

    # Long legs trailing well past the tail, toes dropped.
    d.line([(11, 18 + b), (0, 20 + b)], fill=HERON["leg"])
    d.point((0, 21 + b), fill=HERON["leg"])

    # Slim body.
    d.ellipse([9, 14 + b, 19, 18 + b], fill=HERON["body"])
    d.line([(11, 18 + b), (18, 17 + b)], fill=HERON["belly"])

    # Neck folded into an S: a thin loop sagging below the shoulders, then
    # rising to a small head held forward.
    d.line([(18, 15 + b), (19, 18 + b)], fill=HERON["neck"])
    d.line([(19, 19 + b), (21, 19 + b)], fill=HERON["neck"])
    d.line([(20, 18 + b), (21, 15 + b)], fill=HERON["neck"])
    d.line([(21, 14 + b), (22, 13 + b)], fill=HERON["neck"])
    d.rectangle([22, 12 + b, 24, 14 + b], fill=HERON["neck"])
    d.line([(21, 12 + b), (24, 12 + b)], fill=HERON["crest"])  # crown stripe
    d.line([(18, 13 + b), (20, 12 + b)], fill=HERON["crest"])  # plume
    d.point((23, 13 + b), fill=HERON["eye"])

    # Long dagger bill.
    d.line([(25, 13 + b), (31, 14 + b)], fill=HERON["bill"])
    d.point((25, 14 + b), fill=HERON["bill"])

    # Broad near wing: shoulder fixed on the body, wrist and tip sweeping
    # from fully raised to fully lowered.
    lift = math.cos(phase)  # 1 = wings up, -1 = wings down
    sy = 14 + b
    front, back = (18, sy), (10, sy + 1)
    wrist = (15, sy - 7 * lift)
    tip = (5 - abs(lift), sy - 13 * lift)
    trail = (8, sy - 5 * lift + 1)
    d.polygon([front, wrist, tip, trail, back], fill=HERON["wing"])
    # Dark flight feathers on the outer half.
    d.polygon([wrist, tip, trail], fill=HERON["primary"])

    return outline(img, HERON["line"])


def heron_sheet():
    (w, h), n = HERON["size"], HERON["frames"]
    rows = [[heron_frame(2 * math.pi * i / n) for i in range(n)]]
    ini = [f"frameWidth={w}", f"frameHeight={h}",
           "speed=60        ; pixels per second at 1x", "",
           "[fly]", "row=0", f"frames={n}", "fps=7", ""]
    return rows, ini


# --- Output --------------------------------------------------------------

def write_pet(name, rows, ini):
    fw, fh = rows[0][0].size
    cols = max(len(r) for r in rows)
    sheet = Image.new("RGBA", (fw * cols, fh * len(rows)), CLEAR)
    for y, row in enumerate(rows):
        for x, frame in enumerate(row):
            sheet.paste(frame, (x * fw, y * fh))
    folder = ROOT / "pets" / name
    folder.mkdir(parents=True, exist_ok=True)
    sheet.save(folder / "sheet.png")
    (folder / "pet.ini").write_text("\n".join(ini), encoding="ascii")
    if "--preview" in sys.argv:
        zoom = 8 if sheet.height <= 64 else 3
        big = sheet.resize((sheet.width * zoom, sheet.height * zoom), Image.NEAREST)
        backdrop = Image.new("RGBA", big.size, (90, 120, 150, 255))
        Image.alpha_composite(backdrop, big).save(folder / "preview.png")
    print(f"wrote pets/{name}: {len(rows)} rows of {cols} frames, {fw}x{fh}")


if __name__ == "__main__":
    write_pet("koi", *koi_sheet())
    write_pet("heron", *heron_sheet())

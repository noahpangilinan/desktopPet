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


def top_down(size, angle, paint, eyes, line):
    """Draw a top-down creature with its head pointing along angle.

    Each pixel centre is turned back into the creature's own coordinates (u
    runs tail to head, v across the body) and paint(u, v) picks its colour, or
    None for empty. That draws every heading fresh instead of rotating finished
    pixels. eyes are (u, v, colour) points placed exactly, only where they land
    on the body.
    """
    img = Image.new("RGBA", (size, size), CLEAR)
    px = img.load()
    centre = size / 2
    cos_a, sin_a = math.cos(angle), math.sin(angle)
    for y in range(size):
        for x in range(size):
            dx, dy = x + 0.5 - centre, y + 0.5 - centre
            color = paint(dx * cos_a + dy * sin_a, -dx * sin_a + dy * cos_a)
            if color:
                px[x, y] = color
    for u, v, color in eyes:
        ex = int(math.floor(centre + u * cos_a - v * sin_a))
        ey = int(math.floor(centre + u * sin_a + v * cos_a))
        if 0 <= ex < size and 0 <= ey < size and px[ex, ey][3]:
            px[ex, ey] = color
    return outline(img, line)


def top_down_sheet(spec, frame, speed):
    """Rows for every animation at every heading, plus the pet.ini lines."""
    size, n, headings = spec["size"], spec["frames"], spec["headings"]
    rows = []
    for params in spec["anims"].values():
        for h in range(headings):
            angle = 2 * math.pi * h / headings
            rows.append([frame(2 * math.pi * i / n, angle, *params) for i in range(n)])
    ini = [f"frameWidth={size}", f"frameHeight={size}", f"headings={headings}",
           f"speed={speed}        ; pixels per second at 1x", ""]
    for i, name in enumerate(spec["anims"]):
        ini += [f"[{name}]", f"row={i * headings}", f"frames={n}", f"fps={spec['fps'][name]}", ""]
    return rows, ini


# --- Koi: top-down, any heading ------------------------------------------

KOI = {
    "size": 32,
    "frames": 8,
    "headings": 16,
    # name: (tail sway in pixels, pectoral fin reach, fin flap)
    "anims": {"swim": (2.4, 2.6, 0.7), "glide": (0.8, 1.8, 0.3)},
    "fps": {"swim": 10, "glide": 10},
    "white": rgba("#f4f1ea"),
    "shade": rgba("#d9d3c7"),
    "red": rgba("#e0472b"),
    "fin": rgba("#e9e4da"),
    "eye": rgba("#1d1f24"),
    "line": rgba("#2b2a2e"),
}


def koi_frame(phase, angle, sway, fin_reach, fin_flap):
    """The koi at one point in its tail cycle, its head pointing along angle."""
    tail_u, head_u = -13.0, 13.0
    length = head_u - tail_u

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

    reach = fin_reach + fin_flap * math.sin(phase)

    def paint(u, v):
        t = (u - tail_u) / length
        if not 0 <= t <= 1:
            return None
        cy, hw = spine(t), half_width(t)
        off = abs(v - cy)
        if off > hw:
            # Pectoral fins: swept-back paddles just behind the head.
            d = off - hw
            if d <= reach and 0.66 - 0.035 * d <= t <= 0.76 - 0.035 * d:
                return KOI["fin"]
            return None
        s = (v - cy) / hw
        if t < 0.16:
            return None if abs(s) < 0.35 and t < 0.1 else KOI["fin"]  # forked tail
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
        return color

    eye_t = 0.94
    eyes = [(tail_u + eye_t * length, spine(eye_t) + side * (half_width(eye_t) - 0.6), KOI["eye"])
            for side in (-1, 1)]
    return top_down(KOI["size"], angle, paint, eyes, KOI["line"])


# --- Flatfish: top-down flounder, any heading ----------------------------

FLATFISH = {
    "size": 32,
    "frames": 8,
    "headings": 16,
    # name: (fin ripple height,)
    "anims": {"swim": (1.6,), "glide": (0.5,)},
    "fps": {"swim": 9, "glide": 6},
    "body": rgba("#b8946a"),
    "spot": rgba("#8c6b45"),
    "speck": rgba("#dcc297"),
    "fin": rgba("#a3825a"),
    "ray": rgba("#876741"),
    "ring": rgba("#efe2c0"),
    "eye": rgba("#1c1712"),
    "line": rgba("#3b2d1f"),
}

# Dark blotches and pale specks, as (t, s, t radius, s radius) on the body.
FLATFISH_SPOTS = [(0.36, 0.42, 0.06, 0.24), (0.5, -0.45, 0.05, 0.22), (0.63, 0.3, 0.05, 0.2),
                  (0.43, -0.04, 0.04, 0.17), (0.73, -0.32, 0.04, 0.17), (0.28, -0.3, 0.035, 0.2)]
FLATFISH_SPECKS = [(0.31, 0.05, 0.025, 0.1), (0.56, 0.08, 0.025, 0.1), (0.68, 0.58, 0.025, 0.12),
                   (0.45, 0.6, 0.025, 0.1), (0.6, -0.68, 0.025, 0.1)]


def flatfish_frame(phase, angle, ripple):
    """A flounder: flat oval body, a fin skirt all round that ripples as it
    swims, a fan tail, and both eyes on one side of the head."""
    tail_u, head_u = -12.0, 11.0
    length = head_u - tail_u

    def sway(t):
        return 0.4 * math.sin(phase - 3 * t) * (1 - t) ** 2

    def half_width(t):
        if t < 0.1:  # fan tail
            return 1.0 + (0.1 - t) * 30
        if t < 0.16:  # tail stem
            return 1.2
        x = (t - 0.16) / 0.84
        return 0.6 + 6.0 * math.sin(math.pi * x) ** 0.7 * (1 - 0.25 * x)

    def skirt(t, side):
        # Fin fringe along both edges; waves run back along it.
        if not 0.2 < t < 0.9:
            return 0
        taper = math.sin(math.pi * (t - 0.2) / 0.7) ** 0.5
        wave = 0.5 + 0.5 * math.sin(phase - 10 * t + side * 0.6)
        return taper * (1.3 + ripple * wave)

    def in_blobs(t, s, blobs):
        return any(((t - bt) / rt) ** 2 + ((s - bs) / rs) ** 2 < 1 for bt, bs, rt, rs in blobs)

    eye_spots = [(0.87, -0.42), (0.81, -0.1)]  # (t, s), both on the upper side

    def paint(u, v):
        t = (u - tail_u) / length
        if not 0 <= t <= 1:
            return None
        vv = v - sway(t)
        hw = half_width(t)
        off = abs(vv)
        if t < 0.16:
            return FLATFISH["fin"] if off <= hw else None
        if off > hw:
            if off - hw <= skirt(t, 1 if vv > 0 else -1):
                return FLATFISH["ray"] if int(t * 22) % 2 == 0 else FLATFISH["fin"]
            return None
        s = vv / hw
        for et, es in eye_spots:
            if math.hypot(u - (tail_u + et * length), vv - es * half_width(et)) < 1.1:
                return FLATFISH["ring"]
        if in_blobs(t, s, FLATFISH_SPOTS):
            return FLATFISH["spot"]
        if in_blobs(t, s, FLATFISH_SPECKS):
            return FLATFISH["speck"]
        return FLATFISH["body"]

    eyes = [(tail_u + et * length, sway(et) + es * half_width(et), FLATFISH["eye"])
            for et, es in eye_spots]
    return top_down(FLATFISH["size"], angle, paint, eyes, FLATFISH["line"])


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
    write_pet("koi", *top_down_sheet(KOI, koi_frame, speed=30))
    write_pet("flatfish", *top_down_sheet(FLATFISH, flatfish_frame, speed=22))
    write_pet("heron", *heron_sheet())

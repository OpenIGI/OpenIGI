#!/usr/bin/env python3
"""Generates the remastered storm's sprite sheets in apps/OpenIGI.Composition/Assets/weather.

    python tools/make-weather-sprites.py            # writes the five PNGs
    python tools/make-weather-sprites.py --check    # also prints the measurements the renderer relies on

Every sheet here is painted procedurally from a fixed seed, so the same script always writes byte-identical
files and a change to the look is a reviewable change to this file rather than to an opaque image. Needs
numpy and Pillow and nothing else.

What StormRenderer expects (see StormRenderer.cs and its tests):

  rain_streaks.png            256 x 512, 8-bit grey. Ten sharp falling-drop streaks side by side, one per
                              cell of 25.6 texels. Each cell's first and last column, and the image's first
                              and last row, are exactly zero. The renderer stacks this strip (stretched 2x,
                              nearest) over the defocused one, so it must be half that file's width and the
                              same height.
  rain_streaks_defocused.png  512 x 512, 8-bit grey. The same ten drops, out of focus: wide soft columns
                              carrying a lump or two of light where the sharp streak has its beads.
  rain_splashes.png           512 x 256, RGB. Three eight-frame crown-splash flipbooks, one per colour
                              channel, frames four across and two down in reading order, 128 x 128 cells,
                              the ground on the bottom edge of every cell.
  rain_droplet.png            256 x 256, 8-bit grey. The soft rounded blob the small thrown droplets use.
  rain_curtain.png            512 x 512, 8-bit grey. One soft cloudy blob for the far drizzle sheets.

The mask sheets are read as coverage (the renderer moves the grey value into alpha and draws white times
the vertex colour), and everything is drawn additively, so ink here is light on screen.

THE TWO FILLS THE RENDERER DIVIDES BY. StormRenderer sizes a drop's quad as the streak it wants divided by
how much of a cell the painted streak covers, FocusedStreakFill = 0.39 across and StreakLengthFill = 0.9
along. Those come from this file:
  * across: STREAK_CORE_WIDTH (a core falling off as exp(-(x/w)^3), about 5 texels at half maximum, a
    fifth of a cell) together with STREAK_HALO (a faint skirt) and the sideways wander give a visible
    streak about 10 texels wide, 0.39 of a cell;
  * along: STREAK_TOP and STREAK_BOTTOM put the streak between 4% and 94% of the strip's height, 0.9 of it.
Change either and those two constants must change with them; --check prints both.
"""

from __future__ import annotations

import argparse
import math
import os

import numpy as np
from PIL import Image

SEED = 0x0DE1_5EED

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "apps", "OpenIGI.Composition", "Assets", "weather")

CELLS = 10

# The sharp streak's shape, in texels of the 256-wide strip and fractions of its height.
STREAK_CORE_WIDTH = 2.8      # core falloff exp(-(x/w)^3); FWHM = 1.77 * w, about 5 texels
STREAK_HALO = 0.05           # a faint skirt this bright, relative to the core
STREAK_HALO_SIGMA = 3.0
STREAK_TOP = 0.04
STREAK_BOTTOM = 0.955

STREAK_LIFT = 0.45           # the power the along-the-length brightness is lifted by

# The defocused streak: a broad soft column. Sigma in texels of the 512-wide strip.
SOFT_SIGMA = 14.0


# ----------------------------------------------------------------------------------------------- helpers


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def to_u8(values):
    return np.clip(np.rint(np.asarray(values) * 255.0), 0, 255).astype(np.uint8)


def wobble(rng, n, rms, waves=4, longest=1.0, shortest=0.12):
    """A smooth random curve over n samples with the given RMS: a few sines of random length and phase."""
    t = np.linspace(0.0, 1.0, n)
    curve = np.zeros(n)
    for _ in range(waves):
        period = math.exp(rng.uniform(math.log(shortest), math.log(longest)))
        curve += rng.uniform(0.5, 1.0) * np.sin((2.0 * math.pi * t / period) + rng.uniform(0.0, 2.0 * math.pi))
    curve -= curve.mean()
    spread = float(np.sqrt(np.mean(curve * curve)))
    return curve * (rms / spread) if spread > 1e-9 else curve


def value_noise(rng, size, cells):
    """Smoothly interpolated value noise on a size x size image with `cells` lattice cells across."""
    grid = rng.random((cells + 1, cells + 1))
    coords = np.linspace(0.0, cells, size, endpoint=False) + (cells / size) * 0.5
    i = np.floor(coords).astype(int)
    f = coords - i
    f = f * f * (3.0 - 2.0 * f)
    i1 = np.minimum(i + 1, cells)
    a = grid[np.ix_(i, i)]
    b = grid[np.ix_(i, i1)]
    c = grid[np.ix_(i1, i)]
    d = grid[np.ix_(i1, i1)]
    fy = f[:, None]
    fx = f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def cell_bounds(width, cell):
    return (cell * width) // CELLS, ((cell + 1) * width) // CELLS


# ------------------------------------------------------------------------------------------ the streaks


def streak_traits(rng):
    """What makes each of the ten drops its own: thickness, length, wobble and where its beads catch."""
    traits = []
    for cell in range(CELLS):
        traits.append({
            "thickness": rng.uniform(0.85, 1.15),
            "short": rng.uniform(0.05, 0.12) if rng.random() < 0.5 else 0.0,   # some stop short of the bottom
            "wander_rms": rng.uniform(0.3, 0.8),
            "head_ratio": rng.uniform(3.5, 9.0),                               # top-quarter vs bottom-quarter
            "beads": [(rng.uniform(0.15, 0.85), rng.uniform(0.12, 0.35), rng.uniform(0.008, 0.02))
                      for _ in range(rng.integers(2, 5))],
            "head_at": rng.uniform(0.02, 0.06),
            "soft_blobs": [(rng.uniform(0.1, 0.8), rng.uniform(0.4, 1.0), rng.uniform(0.06, 0.16))
                           for _ in range(rng.integers(0, 3))],
            "soft_wander": rng.uniform(0.6, 3.0),
            "seed": int(rng.integers(0, 2**31 - 1)),
        })
    return traits


def along_profile(n, top, bottom, trait, sharp):
    """Brightness along a streak, top (the leading end) to bottom (the tail), zero outside [top, bottom]."""
    y = (np.arange(n) + 0.5) / n
    span = bottom - top
    t = np.clip((y - top) / span, 0.0, 1.0)          # 0 at the head, 1 at the end of the tail
    inside = (y >= top) & (y <= bottom)

    # Rounded ends: a quick rise at the head and a slower thinning at the tail.
    ends = smoothstep(0.0, 0.025, t) * smoothstep(1.0, 0.9 if sharp else 0.8, t)

    if sharp:
        # The tail decays so that, after the lift below, the top quarter carries two to five times the bottom
    # quarter's light.
        decay = math.log(trait["head_ratio"]) / 0.75
        body = np.exp(-decay * t)
        head = 0.3 * np.exp(-0.5 * ((t - trait["head_at"]) / 0.03) ** 2)
        beads = sum(a * np.exp(-0.5 * ((t - c) / w) ** 2) for c, a, w in trait["beads"])
        profile = (body + head) * (1.0 + beads)

        # Lifted towards its peak: a drop is a line of light along its whole path, not a bead with a
        # rumour of a tail, and the tail's share of the ink is what makes it read at a distance.
        profile = (profile / profile.max()) ** STREAK_LIFT
    else:
        # Out of focus the head and the beads melt into a few broad lumps over an even column.
        profile = 0.55 + 0.25 * (1.0 - t)
        for c, a, w in trait["soft_blobs"]:
            profile = profile + a * np.exp(-0.5 * ((t - c) / w) ** 2)
    return np.where(inside, profile * ends, 0.0)


def paint_sharp(traits, width=256, height=512):
    image = np.zeros((height, width))
    for cell, trait in enumerate(traits):
        rng = np.random.default_rng(trait["seed"])
        left, right = cell_bounds(width, cell)
        x = np.arange(left, right) + 0.5
        centre = (left + right) * 0.5
        bottom = STREAK_BOTTOM - trait["short"]
        along = along_profile(height, STREAK_TOP, bottom, trait, sharp=True)
        drift = wobble(rng, height, trait["wander_rms"])

        # Fine, uneven glint along the length so the line is never a ruler stroke.
        sparkle = 1.0 + 0.12 * wobble(rng, height, 1.0, waves=5, longest=0.3, shortest=0.06)

        offset = x[None, :] - (centre + drift[:, None])
        core = np.exp(-(np.abs(offset) / (STREAK_CORE_WIDTH * trait["thickness"])) ** 3)
        halo = STREAK_HALO * np.exp(-0.5 * (offset / (STREAK_HALO_SIGMA * trait["thickness"])) ** 2)
        streak = (core + halo) * (along * sparkle)[:, None]

        # The cell's own side columns stay black, and nothing is allowed to reach into them.
        streak[:, 0] = 0.0
        streak[:, -1] = 0.0
        image[:, left:right] = streak / max(streak.max(), 1e-9)

    # Anything below the 8-bit floor is written as an exact zero, never as a stray 1.
    image[image < 0.5 / 255.0] = 0.0
    image[0, :] = 0.0
    image[-1, :] = 0.0
    return image


def paint_soft(traits, width=512, height=512):
    image = np.zeros((height, width))
    for cell, trait in enumerate(traits):
        rng = np.random.default_rng(trait["seed"] ^ 0x5F5F)
        left, right = cell_bounds(width, cell)
        x = np.arange(left, right) + 0.5
        centre = (left + right) * 0.5
        bottom = 0.95 - trait["short"] * 0.6
        along = along_profile(height, 0.03, bottom, trait, sharp=False)
        drift = wobble(rng, height, trait["soft_wander"], waves=3, longest=1.5, shortest=0.3)

        sigma = SOFT_SIGMA * (0.9 + 0.2 * trait["thickness"])
        offset = x[None, :] - (centre + drift[:, None])

        # A blurred disc rather than a plain Gaussian: flatter across the middle, softer at the sides.
        across = 0.5 * (1.0 - np.tanh((np.abs(offset) - sigma * 0.95) / (sigma * 0.55)))
        streak = across * along[:, None]
        peak = rng.uniform(0.62, 0.82)
        image[:, left:right] = streak / max(streak.max(), 1e-9) * peak

    image[image < 0.5 / 255.0] = 0.0
    image[0, :] = 0.0
    image[-1, :] = 0.0
    return image


# ------------------------------------------------------------------------------------------ the splashes

CELL = 128


class Canvas:
    """One 128 x 128 frame, painted in soft primitives. h is height above the ground in cell units (0 at
    the bottom edge, 1 at the top) and u runs from -1 at the left edge to 1 at the right."""

    def __init__(self):
        px = (np.arange(CELL) + 0.5) / CELL
        self.u = (px * 2.0 - 1.0)[None, :]
        self.h = (1.0 - px)[:, None]
        self.ink = np.zeros((CELL, CELL))

    def dot(self, u, h, radius, amount):
        d2 = (self.u - u) ** 2 + (self.h - h) ** 2
        self.ink += amount * np.exp(-0.5 * d2 / (radius * radius))

    def ellipse(self, u, h, ru, rh, thickness, amount):
        """An elliptical ring seen slightly from above: the rim of a crown."""
        q = np.sqrt(((self.u - u) / ru) ** 2 + ((self.h - h) / rh) ** 2)
        self.ink += amount * np.exp(-0.5 * ((q - 1.0) * min(ru, rh) / thickness) ** 2)

    def segment(self, u0, h0, u1, h1, thickness, amount0, amount1=None):
        amount1 = amount0 if amount1 is None else amount1
        du, dh = u1 - u0, h1 - h0
        length2 = max(du * du + dh * dh, 1e-9)
        t = np.clip(((self.u - u0) * du + (self.h - h0) * dh) / length2, 0.0, 1.0)
        d2 = (self.u - (u0 + t * du)) ** 2 + (self.h - (h0 + t * dh)) ** 2
        self.ink += (amount0 + (amount1 - amount0) * t) * np.exp(-0.5 * d2 / (thickness * thickness))

    def sheet(self, half_width, top, amount, softness=0.05):
        """A low skirt of water lying on the ground, highest in the middle."""
        shape = np.clip(1.0 - (self.u / half_width) ** 2, 0.0, 1.0)
        surface = top * np.sqrt(shape)
        self.ink += amount * smoothstep(-softness, softness, surface - self.h) * (0.4 + 0.6 * shape)

    def finish(self):
        # Soft saturation, so overlapping strokes brighten without clipping into flat white blocks, and a
        # window that holds the cell's side columns at zero so neighbouring frames never bleed together.
        ink = 1.0 - np.exp(-1.6 * self.ink)
        border = np.minimum(np.arange(CELL), CELL - 1 - np.arange(CELL)).astype(float)
        window = smoothstep(0.5, 7.0, border)[None, :]
        return ink * window


VARIANTS = [
    # Red: a narrow, tall crown with a few long jets.
    {"width": 0.34, "height": 0.62, "jets": 5, "jet_len": 0.30, "spray": 0.9, "pool": 0.0, "lean": 0.12},
    # Green: a wide, spiky crown with many short jets.
    {"width": 0.62, "height": 0.42, "jets": 9, "jet_len": 0.20, "spray": 1.0, "pool": 0.0, "lean": 0.30},
    # Blue: a low, broad splash that leaves a sheet of water lying at its base.
    {"width": 0.70, "height": 0.26, "jets": 7, "jet_len": 0.13, "spray": 0.6, "pool": 1.0, "lean": 0.45},
]


def paint_splash(variant, rng):
    v = VARIANTS[variant]
    jets = []
    for j in range(v["jets"]):
        side = -1.0 + 2.0 * (j + rng.uniform(0.2, 0.8)) / v["jets"]
        jets.append({
            "side": side,
            "len": v["jet_len"] * rng.uniform(0.6, 1.35),
            "bead": rng.uniform(0.6, 1.0),
            "kick": rng.uniform(0.7, 1.3),
            "drops": int(rng.integers(1, 3)),
        })

    frames = []
    for f in range(8):
        c = Canvas()
        tau = (f + 0.5) / 8.0
        width = v["width"]
        height = v["height"]

        if f <= 1:
            # Impact: a bright lump at the base swelling into the first wall of the crown.
            grow = (f + 1) / 2.0
            c.dot(0.0, 0.03 + 0.05 * grow, 0.05 + 0.05 * grow, 1.4)
            c.ellipse(0.0, 0.04 + 0.15 * grow * height, width * (0.35 + 0.45 * grow), 0.05 + 0.05 * grow,
                      0.012, 1.1)
            for side in (-1.0, 1.0):
                c.segment(side * width * 0.2, 0.0, side * width * (0.35 + 0.5 * grow), 0.25 * grow * height + 0.06,
                          0.022, 0.9, 0.7)
            if f == 1:
                for jet in jets:
                    base_u = jet["side"] * width * 0.8
                    c.segment(base_u, 0.25 * height, base_u * (1 + v["lean"] * 0.5), 0.25 * height + jet["len"] * 0.5,
                              0.010, 0.8, 1.0)
        elif f <= 3:
            # The crown at its height: thin walls, a rim and jets breaking into beads at their tips.
            k = 1.0 if f == 2 else 0.92
            rim_h = height * k
            c.ellipse(0.0, rim_h, width, 0.06, 0.010, 0.9)
            for side in (-1.0, 1.0):
                c.segment(side * width * 0.35, 0.0, side * width * 1.0, rim_h, 0.020, 0.55, 0.85)
            c.sheet(width * 0.9, rim_h * 0.55, 0.35)
            for jet in jets:
                base_u = jet["side"] * width
                tip_u = base_u * (1.0 + v["lean"] * jet["kick"])
                tip_h = rim_h + jet["len"] * (1.0 if f == 2 else 1.1)
                c.segment(base_u, rim_h, tip_u, tip_h - 0.03, 0.009, 0.9, 0.6)
                c.dot(tip_u, tip_h, 0.020 * jet["bead"] + 0.008, 1.3)
                if f == 3:
                    c.dot(tip_u * 1.05, tip_h + 0.06, 0.016, 0.9 * jet["bead"])
        else:
            # Collapse: the crown sinks and its water flies off as droplets falling back.
            s = (f - 3) / 4.0                                   # 0.25 .. 1
            fade = (1.0 - s) ** 1.6
            rim_h = height * (1.0 - 0.75 * s)
            if s < 0.8:
                c.ellipse(0.0, rim_h, width * (1.0 + 0.2 * s), 0.05, 0.010, 0.6 * fade)
                for side in (-1.0, 1.0):
                    c.segment(side * width * 0.4, 0.0, side * width * (1.0 + 0.2 * s), rim_h, 0.018, 0.4 * fade)
            for jet in jets:
                for d in range(jet["drops"]):
                    out = jet["side"] * width * (1.0 + v["lean"] * jet["kick"]) * (1.0 + 0.6 * s * v["spray"])
                    launch = height + jet["len"]
                    rise = 0.12 * jet["kick"] * s
                    fall = 1.4 * s * s
                    h = max(launch + rise - fall - d * 0.07, 0.015)
                    u = out * (1.0 + d * 0.12)
                    c.dot(u, h, 0.016 * jet["bead"] + 0.006, 1.1 * fade + 0.15 * (1.0 - s))
            if v["pool"] > 0.0:
                # The lingering skirt of the low broad splash.
                c.sheet(width * (1.05 + 0.25 * s), 0.10 * (1.0 - 0.6 * s), 0.55 * v["pool"] * (1.0 - 0.55 * s))
                c.ellipse(0.0, 0.07 * (1.0 - 0.5 * s), width * (1.0 + 0.3 * s), 0.03, 0.010, 0.5 * (1.0 - 0.6 * s))
        frames.append(c.finish())

    # Peaks reach full scale in the early frames.
    top = max(fr.max() for fr in frames[:4])
    return [np.clip(fr / top, 0.0, 1.0) for fr in frames]


def paint_splash_sheet(rng):
    sheet = np.zeros((2 * CELL, 4 * CELL, 3))
    for variant in range(3):
        frames = paint_splash(variant, np.random.default_rng(int(rng.integers(0, 2**31 - 1))))
        for f, frame in enumerate(frames):
            col, row = f % 4, f // 4
            sheet[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL, variant] = frame
    sheet[sheet < 0.5 / 255.0] = 0.0
    return sheet


# ------------------------------------------------------------------------------------- droplet, curtain


def paint_droplet(size=256):
    p = (np.arange(size) + 0.5) / size * 2.0 - 1.0
    u, v = np.meshgrid(p, p)
    # A superellipse norm just short of a circle: the diagonals fall off a little faster than the axes,
    # which keeps the blob from reading as a perfect disc while its corners stay soft.
    r = (np.abs(u) ** 1.85 + np.abs(v) ** 1.85) ** (1.0 / 1.85)
    body = np.exp(-((np.maximum(r - 0.15, 0.0) / 0.42) ** 2.2))
    blob = body * smoothstep(1.0, 0.85, r)
    blob = blob / blob.max()
    blob[blob < 0.5 / 255.0] = 0.0
    blob[0, :] = blob[-1, :] = 0.0
    blob[:, 0] = blob[:, -1] = 0.0
    return blob


def paint_curtain(rng, size=512):
    p = (np.arange(size) + 0.5) / size * 2.0 - 1.0
    u, v = np.meshgrid(p, p)
    r = np.sqrt(u * u + v * v)
    body = 0.63 * np.exp(-((r / 0.5) ** 1.6)) * smoothstep(1.12, 0.7, r)

    cloud = np.zeros((size, size))
    amplitude, total = 1.0, 0.0
    for cells in (4, 8, 16, 32):
        cloud += amplitude * value_noise(rng, size, cells)
        total += amplitude
        amplitude *= 0.55
    cloud = cloud / total
    cloud = (cloud - cloud.mean()) / cloud.std()

    image = body * np.clip(1.0 + 0.12 * cloud, 0.0, None)
    image[image < 0.5 / 255.0] = 0.0
    image[0, :] = image[-1, :] = 0.0
    image[:, 0] = image[:, -1] = 0.0
    return image


# -------------------------------------------------------------------------------------------- reporting


def report(sharp, soft, splash, droplet, curtain):
    def fill(img, w, lo, hi):
        shares = []
        for cell in range(CELLS):
            left, right = cell_bounds(w, cell)
            cols = (img[:, left:right] > 8).any(axis=0)
            shares.append(cols.mean())
        return float(np.mean(shares))

    s8, d8 = to_u8(sharp).astype(int), to_u8(soft).astype(int)
    print(f"sharp:     width fill {fill(s8, 256, 0, 512):.3f} (renderer assumes 0.39), mean ink {sharp.mean():.3f}")
    rows = np.nonzero(s8.max(axis=1) > 8)[0]
    print(f"           length fill {(rows.max() - rows.min() + 1) / 512:.3f} (renderer assumes 0.9), rows {rows.min()}..{rows.max()}")
    ratios = []
    for cell in range(CELLS):
        left, right = cell_bounds(256, cell)
        c = sharp[:, left:right]
        ratios.append(c[20:135].mean() / max(c[365:480].mean(), 1e-9))
    print(f"           head/tail quarter ratio {min(ratios):.1f}..{max(ratios):.1f}")
    print(f"defocused: width fill {fill(d8, 512, 0, 512):.3f}, peak {soft.max():.2f}, mean {soft.mean():.3f}")
    for ch, name in enumerate("RGB"):
        means = [splash[(f // 4) * CELL:(f // 4 + 1) * CELL, (f % 4) * CELL:(f % 4 + 1) * CELL, ch].mean() for f in range(8)]
        print(f"splash {name}:  frame means " + " ".join(f"{m:.3f}" for m in means)
              + f"  border max {to_u8(splash[:, ::CELL, ch]).max()}")
    print(f"droplet:   mean {droplet.mean():.3f}, at 0.25 {droplet[128, 128 + 32]:.2f}, at 0.5 {droplet[128, 128 + 64]:.2f}, "
          f"at 0.75 {droplet[128, 128 + 96]:.2f}, diagonal 0.5 {droplet[128 + 45, 128 + 45]:.2f}")
    p = (np.arange(512) + 0.5) / 512 * 2.0 - 1.0
    r = np.sqrt(p[None, :] ** 2 + p[:, None] ** 2)
    bands = [(0.0, 0.1), (0.2, 0.3), (0.4, 0.5), (0.6, 0.7), (0.8, 0.9), (1.2, 2.0)]
    print(f"curtain:   peak {curtain.max():.2f}, mean {curtain.mean():.3f}, std r<0.3 {curtain[r < 0.3].std():.3f}, radial "
          + " ".join(f"{curtain[(r >= a) & (r < b)].mean():.2f}" for a, b in bands))


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--check", action="store_true", help="print the measurements the renderer relies on")
    parser.add_argument("--out", default=OUT)
    args = parser.parse_args()

    rng = np.random.default_rng(SEED)
    traits = streak_traits(rng)
    sharp = paint_sharp(traits)
    soft = paint_soft(traits)
    splash = paint_splash_sheet(rng)
    droplet = paint_droplet()
    curtain = paint_curtain(rng)

    os.makedirs(args.out, exist_ok=True)

    def save(name, pixels, mode):
        # No timestamps or text chunks, so a re-run writes the same bytes.
        Image.fromarray(pixels).save(os.path.join(args.out, name), optimize=False, compress_level=9)

    save("rain_streaks.png", to_u8(sharp), "L")
    save("rain_streaks_defocused.png", to_u8(soft), "L")
    save("rain_splashes.png", to_u8(splash), "RGB")
    save("rain_droplet.png", to_u8(droplet), "L")
    save("rain_curtain.png", to_u8(curtain), "L")

    if args.check:
        report(sharp, soft, splash, droplet, curtain)


if __name__ == "__main__":
    main()

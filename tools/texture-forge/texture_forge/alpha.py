"""Keeps a cut-out's edge clean across an upscale.

THE PROBLEM, WHICH IS INVISIBLE UNTIL IT IS SHIPPED.

I.G.I.'s textures are Palette8 (index zero is the hole) or A1R5G5B5 (one bit of alpha). Either way the
colour stored UNDER a transparent texel is arbitrary -- usually black, sometimes the palette's magenta, and
in a few cases whatever the artist left there. Nothing ever sampled it, so nothing ever had to be right.

An upscaler samples it. A super-resolution network is hundreds of stacked convolutions with a receptive
field far wider than the hole, so it averages that arbitrary colour into the neighbouring opaque pixels and
it comes back out as a dark or magenta fringe around every leaf, every strand of barbed wire, every railing.
The alpha channel is still correct; the colour behind it is not, and alpha blending at the mip levels then
smears the fringe across the whole silhouette.

The fix is older than the problem and is what every asset pipeline that handles cut-outs does: flood the
nearest opaque colour outwards into the transparent region BEFORE the upscale, so there is no wrong colour
left to average in. Then do it AGAIN afterwards, because the network writes its own colour back into the
region it had no reason to leave alone.

Do NOT reach for texconv's --color-key for this. Its tolerance is a fixed 0.2 and it zeroes RGBA together --
so it replaces a magenta fringe with a black one, which is not an improvement, only a different colour of
the same bug.
"""

from __future__ import annotations

import numpy as np

__all__ = ["CUTOUT_CUTOFF", "split_key", "bleed", "rethreshold", "is_cutout"]


CUTOUT_CUTOFF = 0.5
"""
The alpha the shaders discard below.

Matches `WorldRenderer.AlphaTestCutoff` on the C# side. It has to: the renderer widens a cut-out's alpha
against this value and then corrects every mip level's coverage against it, so a tool that thresholds
somewhere else hands the engine a silhouette it will immediately alter.
"""


def is_cutout(alpha: np.ndarray) -> bool:
    """Reports whether an alpha channel is a cut-out mask rather than a real transparency.

    The same distinction `AlphaCoverage.IsCutout` draws on the C# side, and for the same reason: a cut-out is
    binary and wants its silhouette preserved, where window glass is a genuine ramp and wants leaving alone.
    Here it decides whether the alpha is re-thresholded after the upscale.

    A texture that is entirely opaque or entirely hole is not a cut-out -- there is no silhouette to keep.
    """
    if alpha.size == 0:
        return False

    holes = alpha < CUTOUT_CUTOFF
    hole_count = int(np.count_nonzero(holes))

    if hole_count == 0 or hole_count == alpha.size:
        return False

    # Binary, within the slack that 1-bit alpha promoted to 8-bit leaves. A real transparency spends most of
    # its pixels in the middle; a cut-out spends almost none there.
    midrange = np.count_nonzero((alpha > 0.03) & (alpha < 0.97))
    return midrange <= alpha.size * 0.02


def split_key(rgb: np.ndarray, alpha: np.ndarray | None, key: tuple[int, int, int] | None) -> np.ndarray:
    """Turns a colour key into straight alpha, leaving an existing alpha channel alone where it has one.

    `ResExtract images` already decodes Palette8's index zero and A1R5G5B5's bit into a real alpha channel,
    so this is normally a no-op. It exists for PNGs that came from somewhere else -- a hand-edited texture,
    or one exported by a tool that wrote the key as a colour instead.
    """
    if alpha is None:
        alpha = np.ones(rgb.shape[:2], dtype=np.float32)

    if key is None:
        return alpha

    keyed = np.array(key, dtype=np.float32) / 255.0

    # Exact match only. A tolerance here would eat art that happens to be near the key colour, and the key
    # in these textures is an exact palette entry rather than a compressed approximation of one.
    matches = np.all(np.abs(rgb - keyed) < (0.5 / 255.0), axis=-1)
    return np.where(matches, 0.0, alpha).astype(np.float32)


def bleed(rgb: np.ndarray, alpha: np.ndarray, *, wrap: bool = True) -> np.ndarray:
    """Floods the nearest opaque colour outwards into every transparent texel.

    A jump-flood-style propagation rather than a per-pixel nearest search: each pass dilates the known region
    by one texel in eight directions, so a hole of radius r closes in r passes over the whole image at once.
    At these sizes that is milliseconds, and the alternative -- a true nearest-opaque transform -- would buy
    accuracy that the next stage's convolutions immediately blur away.

    Wraps by default, because these textures tile. A leaf touching the right edge of the image continues on
    the left, and clamping instead would bleed the wrong colour inwards along all four borders -- the same
    failure mode that makes a derived normal map show a seam grid over the whole level.
    """
    height, width = alpha.shape
    known = alpha >= (1.0 / 255.0)

    if not known.any():
        # Every texel is a hole. There is no colour to spread, and returning the input unchanged is the only
        # honest answer; the upscaler's output here is discarded by the alpha anyway.
        return rgb

    filled = np.where(known[..., None], rgb, 0.0).astype(np.float32)
    mask = known.astype(np.float32)

    mode = "wrap" if wrap else "edge"
    passes = max(height, width)

    for _ in range(passes):
        if mask.all():
            break

        # One ring at a time. Accumulating the eight neighbours and dividing by how many of them were known
        # averages the border colour rather than picking one arbitrarily, which keeps the flood from
        # streaking a single texel's colour down a long corridor.
        padded_colour = np.pad(filled, ((1, 1), (1, 1), (0, 0)), mode=mode)
        padded_mask = np.pad(mask, ((1, 1), (1, 1)), mode=mode)

        colour_sum = np.zeros_like(filled)
        weight_sum = np.zeros_like(mask)

        for dy in (0, 1, 2):
            for dx in (0, 1, 2):
                if dy == 1 and dx == 1:
                    continue

                colour_sum += padded_colour[dy : dy + height, dx : dx + width]
                weight_sum += padded_mask[dy : dy + height, dx : dx + width]

        spreadable = (mask < 0.5) & (weight_sum > 0.0)

        if not spreadable.any():
            break

        averaged = colour_sum / np.maximum(weight_sum, 1e-6)[..., None]
        filled = np.where(spreadable[..., None], averaged, filled)
        mask = np.where(spreadable, 1.0, mask)

    return filled.astype(np.float32)


def rethreshold(alpha: np.ndarray, coverage: float) -> np.ndarray:
    """Snaps an upscaled alpha channel back to binary, keeping the silhouette the same weight.

    The source alpha was one bit. The upscaler returns a ramp, and a naive threshold at the cutoff makes the
    silhouette thinner or fatter than the artist drew -- which is exactly the fault the renderer already
    fights on the other side, where thin fences fade out with distance because a box-filtered mip chain loses
    their coverage.

    So the cut is chosen to PRESERVE COVERAGE: the fraction of texels above the cutoff after is the fraction
    that was above it before. The same idea as `TextureImage.PreserveAlphaCoverage`, and deliberately so --
    both ends of the pipeline should agree about how thick a wire is.
    """
    if alpha.size == 0:
        return alpha

    coverage = float(np.clip(coverage, 0.0, 1.0))

    if coverage <= 0.0:
        return np.zeros_like(alpha)

    if coverage >= 1.0:
        return np.ones_like(alpha)

    # The value with exactly that fraction of texels above it. Quantile rather than a bisection search: it is
    # the same answer, exactly, in one pass.
    cut = float(np.quantile(alpha, 1.0 - coverage))
    return (alpha >= cut).astype(np.float32)

"""Works out whether a texture actually tiles, because roughly a third of this game's do not.

WHY THIS EXISTS, AND WHAT IT COST TO LEARN.

Wrapping is the right thing to do to a tiling texture and it matters a great deal: a wall, a road, a crate
lid all continue across their own edges, so a network given clamped or black context invents a border that
does not match the one it will be tiled against, and the level gets a seam grid over every surface.

So the first version of this tool wrapped everything. That was wrong, and not subtly: I.G.I.'s art also
contains faces, sprites, muzzle flashes, HUD elements and skybox panels, none of which tile. Wrapping a
FACE hands the network an image whose left cheek continues into its right ear, and because these networks
have receptive fields of hundreds of pixels the damage is not confined to the border -- the whole image
comes back blocky and posterised, with the features smeared sideways. Judged on a wall texture the wrapping
looked like a clear win; judged on a portrait it destroyed the image. Both were true at once.

The test is cheap and needs no metadata: a texture that tiles is one whose opposite edges CONTINUE into each
other about as smoothly as its interior continues into itself. Measured per axis, because plenty of textures
tile one way and not the other -- a wall strip repeats along its length and is capped top and bottom.
"""

from __future__ import annotations

import numpy as np

__all__ = ["Tiling", "detect"]


_TOLERANCE = 2.0
"""
How much rougher a seam may be than the interior before the texture is judged not to tile.

A seam in a genuinely tiling texture is an ordinary pair of adjacent columns and scores about 1. A seam in a
face or a sprite is a hard cut between unrelated content and scores five to fifty. Two is deliberately
generous: the cost of wrongly wrapping a non-tiling texture is a ruined image, while the cost of wrongly
NOT wrapping a tiling one is a slightly weaker border -- so when the measurement is ambiguous, the safe
answer is the one that cannot destroy anything.
"""

_FLAT = 1e-4
"""Below this much interior variation a texture is a flat colour, and every edge test is meaningless."""


class Tiling:
    """Whether a texture repeats along each axis."""

    __slots__ = ("x", "y")

    def __init__(self, x: bool, y: bool) -> None:
        self.x = x
        self.y = y

    def __repr__(self) -> str:  # pragma: no cover - diagnostics only
        return f"Tiling(x={self.x}, y={self.y})"

    @property
    def any(self) -> bool:
        """Whether the texture repeats along either axis, and so wants wrapping at all."""
        return self.x or self.y


def _continues(image: np.ndarray, axis: int) -> bool:
    """Whether the image's two opposite edges along `axis` continue into each other.

    Compared against the image's OWN mean adjacent-element difference rather than against a fixed
    threshold, because "smooth" means nothing in the abstract: a noisy gravel texture has a large difference
    between any two adjacent columns and a smooth sky has almost none, and both of them tile.
    """
    extent = image.shape[axis]

    if extent < 4:
        # Too small for the interior to mean anything. These exist -- there are 8x8 textures in this game --
        # and they are almost all flat colours used as tiling fills, so wrapping them is both safe and right.
        return True

    first = np.take(image, 0, axis=axis)
    last = np.take(image, extent - 1, axis=axis)
    seam = float(np.abs(last - first).mean())

    leading = np.take(image, np.arange(1, extent), axis=axis)
    trailing = np.take(image, np.arange(0, extent - 1), axis=axis)
    interior = float(np.abs(leading - trailing).mean())

    if interior < _FLAT:
        # A flat colour. Its seam is flat too, so it tiles trivially -- and if the seam is NOT flat, the one
        # edge that differs is a border the artist drew, which does not tile.
        return seam < _FLAT

    return seam <= interior * _TOLERANCE


def detect(rgb: np.ndarray, alpha: np.ndarray | None = None) -> Tiling:
    """Reports whether a texture repeats along each axis.

    Alpha is folded in when present, because a cut-out's silhouette is part of whether it tiles: a chain
    link fence whose colour is uniform still has a mesh that must line up, and a decal that is opaque in the
    middle and transparent at its border is exactly the case where the colour says "tiles" and the shape
    says the opposite.
    """
    field = rgb if alpha is None else np.concatenate([rgb, alpha[..., None]], axis=-1)

    # Axis 0 is rows, so continuity ALONG axis 0 is whether the top meets the bottom -- which is tiling in
    # y. Naming them after the axis they repeat along rather than the axis they are indexed by is what keeps
    # the caller's padding arguments the right way round.
    return Tiling(x=_continues(field, axis=1), y=_continues(field, axis=0))

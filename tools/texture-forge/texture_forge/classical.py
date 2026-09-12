"""A numpy port of `MaterialMapGenerator`, so the tool produces a complete result with no models at all.

WHY THIS EXISTS WHEN THE C# VERSION ALREADY DOES.

The renderer derives these maps at runtime from the player's own installation, and it has to: the game's art
with a filter over it is still Innerloop's, so a pack cannot be shipped. `MaterialMapGenerator` is that
derivation and it is good -- frequency-separated height, two-octave Scharr normals, a horizon search for
occlusion, roughness from local variance, every one of them wrapping because these textures tile.

What it cannot do is run on an image the AI models produced, inside this process, without a round trip
through a second executable. So the algorithms are here as well, and the duplication is deliberate and
bounded:

  * The ambient occlusion is used ALWAYS, on whichever height field won -- the AI's or this one's. There is
    no AI occlusion model at any licence, so the horizon search is the only thing that can produce it.
  * The rest is the FALLBACK, for the run with `--no-ai`, for a texture a model refused, and for anyone who
    wants to see what the AI actually bought before trusting it.

The constants are copied rather than re-derived, and they are commented there rather than here -- the
reasoning behind each one lives in MaterialMapGenerator.cs and should not be forked. What must not drift is
the OUTPUT: `tests/OpenIGI.Rendering.Tests` compares a map packed by this file against one packed by the C#
generator, byte for byte, and that test is the thing that keeps this honest.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .tiling import Tiling

ALL = Tiling(True, True)
"""What to assume when a caller does not say: that the texture repeats, which most walls and floors do."""

__all__ = [
    "Options",
    "Maps",
    "generate",
    "elevation_from",
    "normal_from",
    "occlusion_from",
    "roughness_from",
    "slope_of",
    "match_relief",
]


_RELIEF = 20.0
"""How much height one unit of the normalised field stands for. MaterialMapGenerator.Relief."""


@dataclass(frozen=True)
class Options:
    """How the maps are derived. Mirrors MaterialMapGenerator.Options, defaults included."""

    normal_strength: float = 1.0
    relief: float = 0.12
    detail_balance: float = 0.6
    roughness_range: float = 0.4
    occlusion_strength: float = 1.0
    occlusion_directions: int = 8


@dataclass(frozen=True)
class Maps:
    """The derived maps, each one value per texel. Mirrors MaterialMapGenerator.Maps."""

    elevation: np.ndarray
    normal_x: np.ndarray
    normal_y: np.ndarray
    """In [-1, 1]. Positive is UP: the OpenGL convention, which is what the shader reconstructs Z against."""

    roughness: np.ndarray
    """A multiplier around one for whatever roughness materials.json gave the surface."""

    occlusion: np.ndarray


def _shift(field: np.ndarray, rows: int, columns: int, tiles: Tiling) -> np.ndarray:
    """Shifts a field, wrapping only along the axes that repeat and clamping along the ones that do not."""
    result = field

    if rows:
        if tiles.y:
            result = np.roll(result, -rows, axis=0)
        else:
            padded = np.pad(result, ((abs(rows), abs(rows)), (0, 0)), mode="edge")
            start = abs(rows) + rows
            result = padded[start : start + field.shape[0], :]

    if columns:
        if tiles.x:
            result = np.roll(result, -columns, axis=1)
        else:
            padded = np.pad(result, ((0, 0), (abs(columns), abs(columns))), mode="edge")
            start = abs(columns) + columns
            result = padded[:, start : start + field.shape[1]]

    return result


def _box_blur(field: np.ndarray, radius: int, passes: int, tiles: Tiling = None) -> np.ndarray:
    """A separable box blur, wrapping, run `passes` times to approximate a Gaussian.

    Three box passes is the standard approximation and is indistinguishable from a true Gaussian at this
    scale. Implemented with a cumulative sum so the cost is independent of the radius, which matters because
    the radius is a fraction of the texture rather than a small constant.
    """
    if radius <= 0 or passes <= 0:
        return field.astype(np.float32, copy=True)

    tiles = tiles or ALL
    result = field.astype(np.float32, copy=True)
    window = (2 * radius) + 1

    for _ in range(passes):
        for axis in (0, 1):
            extent = result.shape[axis]
            wraps = tiles.y if axis == 0 else tiles.x

            if wraps:
                # Enough copies that a radius wider than the texture still fills. Nothing asks for that, but
                # the arithmetic permits it and a silent IndexError would be a poor way to find out.
                repeats = (radius // extent) + 2
                padded = np.concatenate([result] * (2 * repeats + 1), axis=axis)
                offset = repeats * extent
            else:
                # Padded by exactly the radius, so real element i sits at padded index i + radius and its
                # window spans padded[i .. i + 2 * radius]. The offset is therefore ZERO, not the radius:
                # the padding has already done the shifting, and adding it again walks off the end.
                pad = ((radius, radius), (0, 0)) if axis == 0 else ((0, 0), (radius, radius))
                padded = np.pad(result, pad, mode="edge")
                offset = 0

            cumulative = np.cumsum(padded, axis=axis, dtype=np.float64)
            zero_shape = list(cumulative.shape)
            zero_shape[axis] = 1
            cumulative = np.concatenate(
                [np.zeros(zero_shape, dtype=np.float64), cumulative], axis=axis
            )

            upper = np.take(cumulative, np.arange(offset + window, offset + window + extent), axis=axis)
            lower = np.take(cumulative, np.arange(offset, offset + extent), axis=axis)
            result = ((upper - lower) / window).astype(np.float32)

    return result


def elevation_from(
    rgb: np.ndarray, alpha: np.ndarray | None, options: Options, tiles: Tiling | None = None
) -> np.ndarray:
    """The height field: luminance, high-passed against a wide blur, normalised, denoised.

    Taking luminance as height directly is the obvious approach and it is wrong in one very visible way --
    it is right wherever a texture is dark because it is recessed, and wrong wherever a texture is dark
    because somebody painted it that way. A black stripe on flat concrete becomes a trench. Hence the split.
    """
    # Rec. 601, matching the C# generator exactly. The round-trip test compares bytes, so this is not a
    # place to prefer Rec. 709 on aesthetic grounds.
    luminance = (0.299 * rgb[..., 0]) + (0.587 * rgb[..., 1]) + (0.114 * rgb[..., 2])
    luminance = luminance.astype(np.float32)

    height, width = luminance.shape
    relief = float(np.clip(options.relief, 0.01, 1.0))
    relief_radius = max(int(round(min(width, height) * relief)), 1)

    tiles = tiles or ALL
    high_pass = luminance - _box_blur(luminance, relief_radius, 3, tiles)

    # Normalised to a fixed contrast, which is what makes every constant downstream mean something: what a
    # high-pass leaves behind differs by a factor of thirty between a photograph of brick and a flat painted
    # panel. Scaled by the standard deviation rather than the extremes, so one bright speck cannot set the
    # scale for the whole texture.
    mean = float(high_pass.mean())
    deviation = float(np.sqrt(((high_pass - mean) ** 2).mean()))
    contrast = (0.2 / deviation) if deviation > 1e-4 else 0.0

    centred = ((high_pass - mean) * contrast) + 0.5

    # One pass at one pixel. These textures are photographs resampled to 128 pixels and quantised, so much
    # of what survives a high-pass is grain rather than relief -- and the normalisation above cannot tell
    # them apart. Three passes at this radius would erase the mortar line along with the grain.
    elevation = _box_blur(centred, 1, 1, tiles)

    if alpha is not None:
        # No relief where there is no surface. The alpha textures here are foliage, wire fences and glass,
        # and the shape painted into their alpha is a hole rather than a feature.
        elevation = np.where(alpha < (128.0 / 255.0), 0.5, elevation).astype(np.float32)

    return elevation


def _scharr(field: np.ndarray, tiles: Tiling) -> tuple[np.ndarray, np.ndarray]:
    """Scharr gradients. Scharr rather than Sobel: its weights are the rotation-symmetric ones."""
    scale = 1.0 / 32.0

    top = _shift(field, -1, 0, tiles)
    bottom = _shift(field, 1, 0, tiles)

    top_left = _shift(top, 0, -1, tiles)
    top_right = _shift(top, 0, 1, tiles)
    bottom_left = _shift(bottom, 0, -1, tiles)
    bottom_right = _shift(bottom, 0, 1, tiles)
    middle_left = _shift(field, 0, -1, tiles)
    middle_right = _shift(field, 0, 1, tiles)

    dx = (
        (3.0 * ((top_right - top_left) + (bottom_right - bottom_left)))
        + (10.0 * (middle_right - middle_left))
    ) * scale

    dy = (
        (3.0 * ((top_left + top_right) - (bottom_left + bottom_right)))
        + (10.0 * (top - bottom))
    ) * scale

    return dx.astype(np.float32), dy.astype(np.float32)


def normal_from(
    elevation: np.ndarray, options: Options, tiles: Tiling | None = None
) -> tuple[np.ndarray, np.ndarray]:
    """The tangent normal, blended over two octaves of the height field.

    The gradient of a height field is the normal's tilt, negated: ground rising to the right tilts its
    normal to the left. Two octaves because one is either crisp and noisy or smooth and soft, and a surface
    wants both.
    """
    tiles = tiles or ALL
    coarse_field = _box_blur(elevation, 2, 3, tiles)

    fine_x, fine_y = _scharr(elevation, tiles)
    coarse_x, coarse_y = _scharr(coarse_field, tiles)

    balance = float(np.clip(options.detail_balance, 0.0, 1.0))
    strength = max(options.normal_strength, 0.0)

    fine_weight = -balance * strength * _RELIEF
    coarse_weight = -(1.0 - balance) * strength * _RELIEF

    slope_x = (fine_x * fine_weight) + (coarse_x * coarse_weight)
    slope_y = (fine_y * fine_weight) + (coarse_y * coarse_weight)

    inverse = 1.0 / np.sqrt((slope_x * slope_x) + (slope_y * slope_y) + 1.0)
    return (slope_x * inverse).astype(np.float32), (slope_y * inverse).astype(np.float32)


def slope_of(normal_x: np.ndarray, normal_y: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The height-field slope a unit tangent normal came from.

    The inverse of the last two lines of `normal_from`. A normal is a direction and a slope is a rate, and
    scaling is only meaningful on the rate: doubling a unit normal's x and y and renormalising is not
    "twice the relief", it is a rotation towards the tangent plane that saturates at 90 degrees.
    """
    z = np.sqrt(np.maximum(1.0 - (normal_x * normal_x) - (normal_y * normal_y), 1e-8))
    return normal_x / z, normal_y / z


def match_relief(
    normal_x: np.ndarray,
    normal_y: np.ndarray,
    reference_x: np.ndarray,
    reference_y: np.ndarray,
    limit: float = 12.0,
) -> tuple[np.ndarray, np.ndarray]:
    """Rescales a normal map to carry the same amount of relief as a reference one.

    WHY THIS EXISTS. A neural normal model estimates the SHAPE of a surface and has no absolute scale to
    report it in -- nothing in its training ties the depth of a brick's mortar line to a number of
    millimetres, and nothing in its loss would notice if every one of its outputs were half as deep. The
    classical derivation does have a scale: it is a Scharr gradient of a height field normalised to a fixed
    contrast, calibrated once against this game's art.

    Measured over 24 textures, the model's normals tilt a median of 3.83 degrees where the classical
    derivation on the same upscaled image tilts 13.47, and on the original-size texture 21.49. That is why
    turning `r_material_detail` up did nothing visible: the map being multiplied was already flat, so the
    renderer had a working strength control over almost no signal.

    So the model supplies the shape and the classical derivation supplies the magnitude. A single gain per
    texture, on the SLOPE rather than on the normal. Per texture and not per corpus, because how much
    relief a surface should have is a property of that surface: one constant would flatten a brick wall to
    make a painted door reasonable.

    MATCHED ON THE MEDIAN, and that is not a detail. The first version of this matched root-mean-square
    slope and made the problem worse -- median tilt 3.83 degrees fell to 0.32 rather than rising to 13.
    The two maps do not merely differ in scale, they differ in SHAPE: the model's output is spiky, mostly
    flat with occasional very steep texels, where the classical gradient is broadly rough. An RMS is
    carried by the tail, so matching it set the gain from the steep minority and shrank everything else.
    The median is carried by the bulk of the surface, which is the part the eye reads.

    The limit is a guard, not a tuning knob. A texture the model reports as perfectly flat has a gain of
    infinity, and a few of this game's textures genuinely are flat.
    """
    slope_x, slope_y = slope_of(normal_x, normal_y)
    want_x, want_y = slope_of(reference_x, reference_y)

    have = float(np.median(np.sqrt((slope_x * slope_x) + (slope_y * slope_y))))
    want = float(np.median(np.sqrt((want_x * want_x) + (want_y * want_y))))

    if have <= 1e-6 or want <= 1e-6:
        return normal_x, normal_y

    gain = min(want / have, limit)
    slope_x = slope_x * gain
    slope_y = slope_y * gain

    inverse = 1.0 / np.sqrt((slope_x * slope_x) + (slope_y * slope_y) + 1.0)
    return (slope_x * inverse).astype(np.float32), (slope_y * inverse).astype(np.float32)


def occlusion_from(
    elevation: np.ndarray, options: Options, tiles: Tiling | None = None
) -> np.ndarray:
    """Ambient occlusion, from a horizon search over the height field.

    The same question the screen-space pass asks of the depth buffer, asked here of the texture's own
    relief. It resolves what a screen-space search never can, because the occluder is smaller than a pixel:
    the mortar between bricks, the gap between planks.

    THE REACH IS SHORT ON PURPOSE. This is cavity occlusion, not a horizon over a landscape, and what it
    exists to find is two or three texels wide. Sweeping a tenth of the texture averages those grooves away
    against the flat face around them -- which is how the C# version's first run came out at a mean of 251
    out of 255, an occlusion map that occluded nothing.
    """
    tiles = tiles or ALL
    strength = max(options.occlusion_strength, 0.0)

    if strength <= 0.0:
        return np.ones_like(elevation)

    height, width = elevation.shape
    directions = int(np.clip(options.occlusion_directions, 1, 32))
    reach = max(min(width, height) // 16, 3)

    # Deeper than the normal's relief, and for a reason rather than to taste: the normal is a gradient
    # between ADJACENT texels, while this compares texels several apart, over which the same groove presents
    # a much shallower average slope. The factor puts the two maps back on the same footing.
    relief = _RELIEF * 3.0

    openness = np.zeros_like(elevation)

    for direction in range(directions):
        angle = (2.0 * np.pi * direction) / directions
        step_x = np.cos(angle)
        step_y = np.sin(angle)

        highest = np.zeros_like(elevation)

        for step in range(1, reach + 1):
            offset_x = int(round(step_x * step))
            offset_y = int(round(step_y * step))

            # The shift IS the neighbourhood walk. It wraps on the axes this texture repeats along and
            # clamps on the ones it does not, so a face does not get a band of invented occlusion around its
            # border from the far side of the image reaching round.
            neighbour = _shift(elevation, offset_y, offset_x, tiles)

            # The tangent of the angle this neighbour subtends per unit of height difference: the same
            # height twice as far away hides half as much sky.
            subtended = (neighbour - elevation) * (relief / step)
            highest = np.maximum(highest, subtended)

        # The visible fraction of this direction's quarter-hemisphere. cos(atan(t)) is 1/sqrt(1+t^2), which
        # is the same answer without the trigonometry.
        openness += 1.0 / np.sqrt(1.0 + (highest * highest))

    share = 1.0 / directions
    return np.clip(1.0 - ((1.0 - (openness * share)) * strength), 0.0, 1.0).astype(np.float32)


def roughness_from(
    elevation: np.ndarray, options: Options, tiles: Tiling | None = None
) -> np.ndarray:
    """Roughness, from the spread of the height field about its own local mean.

    A surface whose neighbourhood varies has structure finer than the geometry can show, and that is what
    roughness means physically. The WINDOW is what makes it meaningful: measured against the four immediate
    neighbours the only thing that varies is single-texel grain, so every texture in the game comes back at
    almost the same value. A neighbourhood wide enough to hold a brick and its mortar is what distinguishes
    brick from painted steel.

    TWO-SIDED, about one. A neighbourhood quieter than the corpus average asks for a smoother surface and a
    busier one asks for a rougher, so `roughness_range` is a half-width: 0.4 spans [0.6, 1.4].

    It used to return a multiplier at or above one and never below, on the reading that a texture being
    smooth is not evidence the SURFACE is polished. That reading cost the channel its entire lower half:
    measured over 400 of the derived maps, not one texel in the whole corpus asked for a surface smoother
    than its material said, while 14% sat pinned at the ceiling -- and on materials.json's base roughness
    every surface in the game ended up inside a band where a GGX lobe is a hemisphere and the channel could
    not be seen at all. The base is what stands for "what is authored"; this says how the grain moves it,
    and it has to be able to move it both ways. The model path in `pipeline.py` was always two-sided; this
    is the classical path catching up with it.
    """
    tiles = tiles or ALL
    height, width = elevation.shape
    span = float(np.clip(options.roughness_range, 0.0, 1.0))
    radius = int(np.clip(min(width, height) // 12, 2, 8))

    # Standard deviation is the square root of the mean of the squares less the square of the mean, and both
    # means are box filters -- so the cost is independent of the radius.
    local_mean = _box_blur(elevation, radius, 3, tiles)
    local_mean_of_squares = _box_blur(elevation * elevation, radius, 3, tiles)

    variance = np.maximum(local_mean_of_squares - (local_mean * local_mean), 0.0)

    # The height field is normalised to a standard deviation of a fifth globally, so a neighbourhood at that
    # spread is an ordinarily detailed one and saturates the range.
    deviation = np.sqrt(variance)
    busyness = np.minimum(deviation * 5.0, 1.0)
    return (1.0 + (((busyness * 2.0) - 1.0) * span)).astype(np.float32)


def generate(
    rgb: np.ndarray,
    alpha: np.ndarray | None,
    options: Options | None = None,
    tiles: Tiling | None = None,
) -> Maps:
    """Derives every map from a diffuse texture, the same pipeline and order as the C# generator."""
    settings = options or Options()
    repeats = tiles or ALL

    elevation = elevation_from(rgb, alpha, settings, repeats)
    normal_x, normal_y = normal_from(elevation, settings, repeats)
    occlusion = occlusion_from(elevation, settings, repeats)
    roughness = roughness_from(elevation, settings, repeats)

    return Maps(
        elevation=elevation,
        normal_x=normal_x,
        normal_y=normal_y,
        roughness=roughness,
        occlusion=occlusion,
    )

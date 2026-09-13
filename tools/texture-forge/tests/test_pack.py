"""The other half of the packing contract.

The identical table is asserted in C# by `tests/OpenIGI.Core.Tests/MaterialMapPackingContractTests.cs`. Two
implementations write the same PNG into the same directory for the same sampler to read, so the layout is a
contract between two languages rather than an implementation detail of either -- and a literal table on both
sides is what makes it impossible for one to move without failing.

If you change a row here, change it there. If a row here disagrees with a row there, one of the two packers
is writing a texture that looks like a plausible picture and is wrong, which is the failure this exists for.

    uv run --project tools/texture-forge pytest tools/texture-forge/tests
"""

from __future__ import annotations

import numpy as np
import pytest

from texture_forge import alpha as alpha_ops
from texture_forge import classical, images, pack, tiling

# (normal_x, normal_y, roughness, occlusion) -> (r, g, b, a), exactly as the sampler reads them.
#
#   r = (normal.x * 0.5) + 0.5
#   g = (normal.y * 0.5) + 0.5      OpenGL handedness: positive is up
#   b = roughness * 0.5             a MULTIPLIER stored halved, so 0.5 is the identity
#   a = occlusion                   white for open, dark for a crease
#
# Alpha used to carry `1 - metallic`. Metal moved to materials.json -- it is a property of a MATERIAL and
# not of a texel, and neither packer could ever derive it, so the channel was 255 everywhere and the shader
# line reading it was a no-op. The expected bytes below did not change when occlusion took the channel over,
# because the two meanings agree at both ends; that is what lets every pack already on disk stay correct.
CONTRACT = [
    # The identity of the whole system: flat, ordinary, unoccluded. Every channel is the value that means
    # "change nothing", which makes this the one row whose bytes need no arithmetic to check.
    ((0.0, 0.0, 1.0, 1.0), (128, 128, 128, 255)),
    # The normal's extremes, which is where a swapped channel shows.
    ((1.0, 0.0, 1.0, 1.0), (255, 128, 128, 255)),
    ((-1.0, 0.0, 1.0, 1.0), (0, 128, 128, 255)),
    ((0.0, 1.0, 1.0, 1.0), (128, 255, 128, 255)),
    ((0.0, -1.0, 1.0, 1.0), (128, 0, 128, 255)),
    # Roughness at the ends of the range the generator may produce.
    ((0.0, 0.0, 0.0, 1.0), (128, 128, 0, 255)),
    ((0.0, 0.0, 2.0, 1.0), (128, 128, 255, 255)),
    # Occlusion, fully closed.
    ((0.0, 0.0, 1.0, 0.0), (128, 128, 128, 0)),
    # An arbitrary interior point, to catch a rounding rule that only agrees at the ends.
    ((0.5, -0.25, 1.5, 0.75), (191, 96, 191, 191)),
]


@pytest.mark.parametrize(("inputs", "expected"), CONTRACT)
def test_detail_matches_the_contract(inputs, expected):
    normal_x, normal_y, roughness, occlusion = inputs

    rgb, alpha = pack.detail(
        np.array([[normal_x]], dtype=np.float32),
        np.array([[normal_y]], dtype=np.float32),
        np.array([[roughness]], dtype=np.float32),
        np.array([[occlusion]], dtype=np.float32),
    )

    actual = (
        int(images.quantise(rgb)[0, 0, 0]),
        int(images.quantise(rgb)[0, 0, 1]),
        int(images.quantise(rgb)[0, 0, 2]),
        int(images.quantise(alpha)[0, 0]),
    )

    assert actual == expected


def test_neutral_map_matches_the_device_fallback():
    """A neutral map must be the texture the device substitutes when there is no pack at all.

    `VulkanGraphicsDevice.CreateNeutralDetailTexture` uploads one texel of (128, 128, 128, 255) for every
    surface with no map, and the shader multiplies rather than branches -- so "no pack" and "a pack that
    says change nothing" have to be the same four bytes, or turning a pack on would shift every surface in
    the level slightly with nothing to say why.
    """
    zero = np.zeros((1, 1), dtype=np.float32)
    one = np.ones((1, 1), dtype=np.float32)
    rgb, alpha = pack.detail(zero, zero, one, one)

    assert tuple(images.quantise(rgb)[0, 0]) == (128, 128, 128)
    assert int(images.quantise(alpha)[0, 0]) == 255


def test_flat_texture_derives_a_flat_normal():
    """A texture with no relief must derive no relief, rather than amplifying its own rounding error.

    The height field is normalised by its own standard deviation, and a single flat colour has none. The C#
    generator guards that with `deviation > 1e-4`, and this is the same guard: without it the contrast
    scale is a division by almost zero and a blank wall comes back as confetti.
    """
    flat = np.full((32, 32, 3), 0.5, dtype=np.float32)
    maps = classical.generate(flat, None)

    assert np.allclose(maps.normal_x, 0.0, atol=1e-6)
    assert np.allclose(maps.normal_y, 0.0, atol=1e-6)


def test_derivation_wraps():
    """Every derivation must treat the texture as tiling, because every one of these textures does.

    Clamping instead flattens all four edges, which shows up as a seam grid over the whole level and is the
    single failure that makes derived maps look homemade. Tested by rolling the input and checking the
    output rolls with it: a wrapping operator commutes with a shift, and a clamping one does not.
    """
    rng = np.random.default_rng(20260911)
    texture = rng.random((32, 32, 3)).astype(np.float32)

    direct = classical.generate(texture, None)
    shifted = classical.generate(np.roll(texture, (7, 11), axis=(0, 1)), None)

    for name in ("normal_x", "normal_y", "roughness", "occlusion"):
        rolled = np.roll(getattr(direct, name), (7, 11), axis=(0, 1))
        assert np.allclose(rolled, getattr(shifted, name), atol=1e-5), name


def test_cutout_alpha_keeps_its_coverage():
    """Re-thresholding must keep a silhouette the same weight, not merely binary.

    The source alpha is one bit and the upscaler returns a ramp. Cutting at the midpoint makes a wire fence
    thinner or fatter than the artist drew -- the same fault the renderer already fights on the other side,
    where a box-filtered mip chain loses a thin thing's coverage and it fades out with distance.
    """
    rng = np.random.default_rng(7)
    ramp = rng.random((64, 64)).astype(np.float32)

    for coverage in (0.05, 0.25, 0.5, 0.9):
        result = alpha_ops.rethreshold(ramp, coverage)

        assert set(np.unique(result)).issubset({0.0, 1.0})
        assert np.count_nonzero(result) == pytest.approx(coverage * result.size, abs=2)


def test_bleed_fills_every_hole_and_leaves_opaque_pixels_alone():
    """The bleed must not touch a texel the artist painted, and must leave none of the arbitrary ones.

    What is stored under a transparent texel in these formats is arbitrary -- often black, sometimes the
    palette's magenta. An upscaler averages it into the neighbouring opaque pixels and it comes back as a
    fringe. The test is that nothing arbitrary survives, and that nothing real is disturbed.
    """
    rgb = np.zeros((16, 16, 3), dtype=np.float32)
    rgb[..., 0] = 1.0  # Magenta-ish junk everywhere, to stand in for whatever is under the holes.
    rgb[..., 2] = 1.0

    alpha = np.zeros((16, 16), dtype=np.float32)
    rgb[4:8, 4:8] = np.array([0.25, 0.75, 0.125], dtype=np.float32)
    alpha[4:8, 4:8] = 1.0

    filled = alpha_ops.bleed(rgb, alpha)

    assert np.allclose(filled[4:8, 4:8], np.array([0.25, 0.75, 0.125]), atol=1e-6)

    # Nothing outside the painted region may still be the junk that was there.
    outside = filled[alpha < 0.5]
    assert not np.any(np.all(np.isclose(outside, np.array([1.0, 0.0, 1.0])), axis=-1))


def test_bleed_survives_a_fully_transparent_texture():
    """A texture that is entirely hole has no colour to spread, and must not raise trying."""
    rgb = np.full((8, 8, 3), 0.5, dtype=np.float32)
    alpha = np.zeros((8, 8), dtype=np.float32)

    assert alpha_ops.bleed(rgb, alpha).shape == rgb.shape


def test_cutout_classification():
    """Binary alpha is a cut-out; a ramp is glass; all-or-nothing is neither.

    The same distinction `AlphaCoverage.IsCutout` draws on the C# side. It decides whether the silhouette is
    restored after the upscale, and getting it wrong either softens a fence or hard-edges a window.
    """
    solid = np.ones((16, 16), dtype=np.float32)
    empty = np.zeros((16, 16), dtype=np.float32)

    binary = np.zeros((16, 16), dtype=np.float32)
    binary[::2] = 1.0

    ramp = np.linspace(0.0, 1.0, 256, dtype=np.float32).reshape(16, 16)

    assert alpha_ops.is_cutout(binary)
    assert not alpha_ops.is_cutout(ramp)
    assert not alpha_ops.is_cutout(solid)
    assert not alpha_ops.is_cutout(empty)


def test_tiling_detects_a_repeating_texture():
    """A texture built by tiling a pattern must be recognised as tiling on both axes.

    Wrapping is right for these and matters: a network given clamped context invents a border that will not
    match the one it gets tiled against, and the level ends up with a seam grid over every wall.
    """
    rng = np.random.default_rng(3)
    cell = rng.random((16, 16, 3)).astype(np.float32)
    repeating = np.tile(cell, (4, 4, 1))

    result = tiling.detect(repeating, None)

    assert result.x
    assert result.y


def test_tiling_rejects_a_picture_with_unrelated_edges():
    """A texture whose opposite edges are unrelated must NOT be wrapped.

    This is the case that was wrong first time round and produced a visibly ruined image. Wrapping a
    portrait hands the network a picture whose left cheek continues into its right ear, and because the
    receptive field is hundreds of pixels wide the damage reaches the middle -- measured at 46% of adjacent
    pixels exactly equal, against 26% for the same model and image unwrapped.
    """
    ramp = np.linspace(0.0, 1.0, 64, dtype=np.float32)
    picture = np.repeat(np.repeat(ramp[None, :, None], 64, axis=0), 3, axis=2)

    # A horizontal ramp: the right edge is white, the left is black, so it cannot tile in x. It is constant
    # down every column, so it tiles perfectly in y.
    result = tiling.detect(picture, None)

    assert not result.x
    assert result.y


def test_tiling_handles_a_flat_colour():
    """A single flat colour tiles trivially, and must not divide by its own zero variation."""
    flat = np.full((32, 32, 3), 0.25, dtype=np.float32)
    result = tiling.detect(flat, None)

    assert result.x
    assert result.y


def test_tiling_reads_the_alpha_too():
    """Whether a texture tiles can live entirely in its alpha, so the shape has to be measured as well.

    Both textures here are ONE FLAT COLOUR: judged on their colour channels alone they are identical, and
    both trivially tile. The difference is the silhouette. The first is a decal floating inside a clear
    border, and it really does tile -- repeat it and the transparent margins meet transparent margins, with
    nothing discontinuous anywhere. The second runs its opaque region off the left edge only, so tiling it
    in x butts solid against clear.

    That distinction is the whole reason this reads the alpha: the colour says "tiles" for both.
    """
    rgb = np.full((32, 32, 3), 0.5, dtype=np.float32)

    floating = np.zeros((32, 32), dtype=np.float32)
    floating[8:24, 8:24] = 1.0

    result = tiling.detect(rgb, floating)
    assert result.x, "a decal inside a clear border repeats cleanly and should be wrapped"
    assert result.y

    running_off = np.zeros((32, 32), dtype=np.float32)
    running_off[8:24, :20] = 1.0

    result = tiling.detect(rgb, running_off)
    assert not result.x, "opaque at the left edge and clear at the right cannot tile in x"
    assert result.y


def test_derivation_clamps_when_a_texture_does_not_tile():
    """A texture that does not repeat must not have its far side folded into its near side's gradient.

    The wrapping in `test_derivation_wraps` is right for a wall and wrong for a face: wrapped, the horizon
    search reaches off the left edge and finds whatever is on the right, so a portrait gets a band of
    invented occlusion around its border and the normal gets a rim of relief that is not in the art.

    Tested by construction: a field with a hard step between its left and right halves has a discontinuity
    at its wrap seam and none in its interior. Wrapped, the edge column sees that step and reports relief.
    Clamped, it sees only itself and reports none.
    """
    field = np.zeros((32, 32), dtype=np.float32)
    field[:, 16:] = 1.0

    options = classical.Options()
    wrapped = classical.normal_from(field, options, tiling.Tiling(True, True))[0]
    clamped = classical.normal_from(field, options, tiling.Tiling(False, False))[0]

    # Column zero sits against the seam. Wrapped it is a cliff; clamped it is flat.
    assert np.abs(wrapped[:, 0]).max() > 0.1
    assert np.abs(clamped[:, 0]).max() < 1e-6

    # The genuine step in the middle of the image is found either way -- clamping must not cost real detail.
    assert np.abs(wrapped[:, 16]).max() > 0.1
    assert np.abs(clamped[:, 16]).max() > 0.1

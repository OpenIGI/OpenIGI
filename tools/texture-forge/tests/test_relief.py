"""The calibration that gives a model's normal map the classical derivation's depth.

A neural normal model estimates the SHAPE of a surface and has no absolute scale to report it in: nothing
in its training ties the depth of a mortar line to a number, and nothing in its loss would notice if every
output were half as deep. Measured on this game's art, the model's normals tilt a median of 3.83 degrees
where the classical derivation on the same image tilts 13.47 -- which is why a pack built from it left
`r_material_detail` as a working strength control over almost no signal.

`classical.match_relief` takes the model's shape and the derivation's magnitude. These tests pin the two
mistakes made writing it, both of which produced a function that ran, returned plausible normal maps, and
did not work:

  1. Matching ROOT-MEAN-SQUARE slope rather than the median. The two maps differ in shape as well as in
     scale -- the model's output is spiky, mostly flat with occasional very steep texels -- so an RMS is
     carried by the tail and matching it set the gain from the steep minority. Median tilt fell from 3.83
     degrees to 0.32 rather than rising.
  2. Taking the reference from the height MODEL's output rather than from the classical elevation. That
     measures one neural estimate against another and inherits the flatness the calibration exists to
     remove; it undershot by about five times, every texture moving the right way and none arriving.
"""

from __future__ import annotations

import numpy as np

from texture_forge import classical


def _median_slope(normal_x: np.ndarray, normal_y: np.ndarray) -> float:
    slope_x, slope_y = classical.slope_of(normal_x, normal_y)
    return float(np.median(np.sqrt((slope_x * slope_x) + (slope_y * slope_y))))


def _flatten(normal_x: np.ndarray, normal_y: np.ndarray, factor: float):
    """The same surface with its relief scaled, which is what an uncalibrated model reports."""
    slope_x, slope_y = classical.slope_of(normal_x, normal_y)
    slope_x = slope_x * factor
    slope_y = slope_y * factor
    inverse = 1.0 / np.sqrt((slope_x * slope_x) + (slope_y * slope_y) + 1.0)
    return (slope_x * inverse).astype(np.float32), (slope_y * inverse).astype(np.float32)


def _relief_field(size: int = 64) -> np.ndarray:
    """A height field with broad structure and fine grain, like a brick and its mortar."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    coarse = 0.5 + (0.3 * np.sin(x / 7.0) * np.cos(y / 5.0))
    fine = 0.05 * np.sin(x * 1.7) * np.sin(y * 2.3)
    return (coarse + fine).astype(np.float32)


def test_restores_the_reference_depth():
    """A flattened map must come back to the depth of the map it is matched against."""
    options = classical.Options()
    reference_x, reference_y = classical.normal_from(_relief_field(), options, None)
    flat_x, flat_y = _flatten(reference_x, reference_y, 0.15)

    assert _median_slope(flat_x, flat_y) < _median_slope(reference_x, reference_y) / 3

    fixed_x, fixed_y = classical.match_relief(flat_x, flat_y, reference_x, reference_y)

    assert _median_slope(fixed_x, fixed_y) == pytest_approx(
        _median_slope(reference_x, reference_y)
    )


def test_a_map_already_at_depth_is_left_where_it_is():
    """Matching a map against itself must be a no-op, not a slow drift."""
    options = classical.Options()
    reference_x, reference_y = classical.normal_from(_relief_field(), options, None)

    fixed_x, fixed_y = classical.match_relief(
        reference_x, reference_y, reference_x, reference_y
    )

    assert np.allclose(fixed_x, reference_x, atol=1e-5)
    assert np.allclose(fixed_y, reference_y, atol=1e-5)


def _normal_of(slope_x: np.ndarray, slope_y: np.ndarray):
    inverse = 1.0 / np.sqrt((slope_x * slope_x) + (slope_y * slope_y) + 1.0)
    return (slope_x * inverse).astype(np.float32), (slope_y * inverse).astype(np.float32)


def test_a_spiky_map_is_matched_on_its_bulk_not_its_tail():
    """The mistake that made this worse: an RMS follows the steep minority.

    Built in slope space so the failure is exact rather than approximate. The reference is a uniform slope
    of 0.2. The "model" is 99% of texels at 0.02 -- a tenth of the reference -- and 1% at 1.99, chosen so
    the two have the SAME root-mean-square slope. An RMS match therefore computes a gain of 1.0 and leaves
    the bulk of the surface ten times too flat, while the median match has to raise it by ten.

    This is the shape the real model produces: measured over 24 of this game's textures its output is
    mostly flat with occasional very steep texels, which is why the first version of `match_relief` drove
    median tilt from 3.83 degrees down to 0.32 instead of up to 13.
    """
    size = 100
    reference_x, reference_y = _normal_of(
        np.full((size, size), 0.2, dtype=np.float32), np.zeros((size, size), dtype=np.float32)
    )

    slope = np.full((size, size), 0.02, dtype=np.float32)
    spikes = np.zeros(size * size, dtype=bool)
    spikes[: (size * size) // 100] = True
    slope.reshape(-1)[spikes] = 1.99

    model_x, model_y = _normal_of(slope, np.zeros_like(slope))

    # The premise: the two agree on RMS and disagree by ten on the median. If this ever stops holding the
    # fixture has drifted and the test below is no longer testing anything.
    def rms(nx, ny):
        sx, sy = classical.slope_of(nx, ny)
        return float(np.sqrt(np.mean((sx * sx) + (sy * sy))))

    assert rms(model_x, model_y) == pytest_approx(rms(reference_x, reference_y), 0.05)
    assert _median_slope(model_x, model_y) < _median_slope(reference_x, reference_y) / 5

    fixed_x, fixed_y = classical.match_relief(model_x, model_y, reference_x, reference_y)

    assert _median_slope(fixed_x, fixed_y) == pytest_approx(
        _median_slope(reference_x, reference_y)
    )


def test_a_flat_map_is_left_alone_rather_than_multiplied_by_infinity():
    """A genuinely flat texture has a gain of infinity, and a few of this game's textures are flat."""
    options = classical.Options()
    reference_x, reference_y = classical.normal_from(_relief_field(), options, None)

    flat = np.zeros((64, 64), dtype=np.float32)

    fixed_x, fixed_y = classical.match_relief(flat, flat, reference_x, reference_y)

    assert np.all(np.isfinite(fixed_x))
    assert np.all(np.isfinite(fixed_y))
    assert np.abs(fixed_x).max() == 0.0
    assert np.abs(fixed_y).max() == 0.0


def test_the_result_is_always_a_valid_tangent_normal():
    """x squared plus y squared must never reach one, or the shader's rebuilt z collapses."""
    options = classical.Options()
    reference_x, reference_y = classical.normal_from(_relief_field(), options, None)
    flat_x, flat_y = _flatten(reference_x, reference_y, 0.02)

    fixed_x, fixed_y = classical.match_relief(flat_x, flat_y, reference_x, reference_y)

    assert np.all(((fixed_x * fixed_x) + (fixed_y * fixed_y)) < 1.0)


def pytest_approx(value: float, tolerance: float = 0.03):
    """A tolerance in RELATIVE terms; the gain is a ratio of medians and lands within a percent or two."""

    class _Approx:
        def __eq__(self, other: object) -> bool:
            return abs(float(other) - value) <= tolerance * max(abs(value), 1e-9)

        def __repr__(self) -> str:
            return f"{value} +/- {tolerance * 100:g}%"

    return _Approx()

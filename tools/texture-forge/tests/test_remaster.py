"""The pass that is allowed to invent, and the three things it still may not do.

Upscaling resolves what is already in a texture. It cannot modernise one, because what dates a 2001
texture is not its size -- it is 5 bits of colour per channel, lighting painted into the albedo, and no
detail finer than the brush that made it. The remaster pass is a second network asked what the surface
would look like if it had been authored now, and it is the only stage in this tool permitted to put detail
on a surface that was never drawn.

Being allowed to invent makes the rails worth testing. It must not change the size, because the texture is
already at the size the pack needs; it must not touch alpha, because invention along a silhouette moves
the silhouette and a wire fence grows or loses wire; and it must not run at all unless asked.
"""

from __future__ import annotations

import numpy as np
import pytest

from texture_forge import pipeline, tiling


class _Doubler:
    """A stand-in for a 4x network: enlarges by nearest neighbour and tints, so its touch is visible."""

    scale = 4

    def __init__(self) -> None:
        self.calls: list[int] = []
        self.wraps: list[object] = []

    def run(self, items, wrap=True):
        self.calls.append(len(items))
        self.wraps.append(wrap)

        return [np.clip(np.repeat(np.repeat(item, 4, axis=0), 4, axis=1) + 0.25, 0.0, 1.0) for item in items]


class _Session:
    """A session that hands back whatever model it was given, without loading anything."""

    def __init__(self, model=None) -> None:
        self._model = model

    def get(self, key: str):
        return self._model


def _texture(width: int = 8, height: int = 4, *, alpha: bool = False) -> pipeline.Texture:
    rgb = np.linspace(0.0, 0.5, width * height * 3, dtype=np.float32).reshape(height, width, 3)
    mask = np.full((height, width), 0.5, dtype=np.float32) if alpha else None

    texture = pipeline.Texture(name="wall", path=None, rgb=rgb, alpha=mask)  # type: ignore[arg-type]
    texture.tiles = tiling.Tiling(True, True)
    return texture


def test_the_size_is_unchanged():
    """A 4x model runs and the result comes straight back down: the pass reinterprets, it does not enlarge.

    The enlargement is not the product. It is how the invention is obtained -- the network only synthesises
    detail when it believes it is upscaling -- so the texture is handed back at exactly the size it arrived.
    """
    texture = _texture()
    before = texture.rgb.shape

    pipeline.remaster_all(
        [texture], pipeline.Settings(remaster="texturedat2"), _Session(_Doubler())
    )

    assert texture.rgb.shape == before


def test_it_actually_changes_the_colour():
    """Guards the no-op: a pass that quietly did nothing would pass every other test here."""
    texture = _texture()
    original = texture.rgb.copy()

    pipeline.remaster_all(
        [texture], pipeline.Settings(remaster="texturedat2"), _Session(_Doubler())
    )

    assert not np.allclose(texture.rgb, original)


def test_alpha_is_left_exactly_alone():
    """The upscale pass puts alpha through the model; this one must not.

    That difference is deliberate. Up there, a cut-out's edge is real high-frequency detail a resize would
    smear. Here the stage exists to INVENT, and invention along a silhouette moves the silhouette -- the
    coverage restore downstream can put back how much wire there is, not where it went.
    """
    texture = _texture(alpha=True)
    original = texture.alpha.copy()

    pipeline.remaster_all(
        [texture], pipeline.Settings(remaster="texturedat2"), _Session(_Doubler())
    )

    assert np.array_equal(texture.alpha, original)


def test_it_does_not_run_unless_asked():
    """Off by default, and off means the model is never even asked for."""
    texture = _texture()
    original = texture.rgb.copy()
    model = _Doubler()

    pipeline.remaster_all([texture], pipeline.Settings(), _Session(model))

    assert model.calls == []
    assert np.array_equal(texture.rgb, original)


def test_a_model_that_will_not_load_leaves_the_texture_finished():
    """The pack is complete without this stage, so a missing weight must not throw the upscale away."""
    texture = _texture()
    original = texture.rgb.copy()

    pipeline.remaster_all(
        [texture], pipeline.Settings(remaster="texturedat2"), _Session(None)
    )

    assert np.array_equal(texture.rgb, original)


def test_the_wrap_flags_reach_the_model():
    """What keeps a tiling texture tiling through a stage that is inventing.

    Measured on real art: for textures that do tile, the seam across the wrap boundary is as continuous
    after this pass as before it -- 0.89 to 0.89, 0.66 to 0.66. That only holds because the network is
    given wrapped context, per axis, exactly as the upscaler is.
    """
    tiling_texture = _texture()
    tiling_texture.tiles = tiling.Tiling(True, False)
    model = _Doubler()

    pipeline.remaster_all(
        [tiling_texture], pipeline.Settings(remaster="texturedat2"), _Session(model)
    )

    assert model.wraps == [[(True, False)]]

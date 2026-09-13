"""What the bars are allowed to say, which is one tick per image and no more.

The callback the pipeline is handed ADVANCES a bar by what it is given. That makes two mistakes possible
and both were present:

Handing it a running total. `derive_all`'s upscale-only branch passed `written` -- 1, then 2, then 3 -- so
a chunk of 96 advanced the bar by 4,656, and an upscale-only run's writing bar reached the end during the
first chunk and sat there. The derive branch beside it always passed 1, which is why nobody saw it until a
run without --pbr.

Reporting once at the end. A stage that ticks when its whole chunk returns says nothing for the minutes in
between: CHUNK is 96, so the bars stood still and then jumped by 96, and the remaster stage -- which is
most of the wall clock, four times the pixels the upscaler saw -- read 0 for the entire first chunk.

So a stage is tested for both halves: the total it reports must equal the images it was given, and it must
arrive in more than one instalment.
"""

from __future__ import annotations

import numpy as np
import pytest

from texture_forge import pipeline, runner, tiling


class _Ticks:
    """Records every instalment a stage reports."""

    def __init__(self) -> None:
        self.steps: list[float] = []

    def __call__(self, step: float) -> None:
        self.steps.append(step)

    @property
    def total(self) -> float:
        return sum(self.steps)


class _Model:
    """A 4x stand-in that reports each image as it finishes, as the real runner does."""

    scale = 4

    def run(self, items, wrap=True, progress=None):
        items = list(items)

        if progress is not None:
            for _ in items:
                progress(1)

        return [np.repeat(np.repeat(item, 4, axis=0), 4, axis=1) for item in items]


class _Session:
    def __init__(self, model=None) -> None:
        self._model = model

    def get(self, key: str):
        return self._model


def _textures(count: int, *, alpha: bool = False) -> list[pipeline.Texture]:
    made = []

    for index in range(count):
        rgb = np.full((8, 8, 3), index / max(count, 1), dtype=np.float32)
        mask = np.full((8, 8), 0.5, dtype=np.float32) if alpha else None
        texture = pipeline.Texture(name=f"t{index}", path=None, rgb=rgb, alpha=mask)  # type: ignore[arg-type]
        texture.tiles = tiling.Tiling(True, True)
        made.append(texture)

    return made


def _settings(**kwargs):
    return pipeline.Settings(**kwargs)


@pytest.mark.parametrize("alpha", [False, True])
def test_the_upscale_reports_one_tick_per_texture(alpha: bool):
    """Exactly one, whatever the chain did -- and the alpha pass must not add a second."""
    ticks = _Ticks()
    textures = _textures(6, alpha=alpha)

    pipeline.upscale_all(
        textures,
        _settings(scale=4),
        _Session(_Model()),
        progress=ticks,
    )

    assert ticks.total == pytest.approx(len(textures))


def test_the_upscale_reports_as_it_goes():
    ticks = _Ticks()

    pipeline.upscale_all(_textures(6), _settings(scale=4), _Session(_Model()), progress=ticks)

    assert len(ticks.steps) > 1, "the whole chunk was reported in one instalment"


def test_the_remaster_reports_one_tick_per_texture_as_it_goes():
    """The stage that is most of the run, and the one that read 0 for a whole chunk."""
    ticks = _Ticks()
    textures = _textures(6)

    pipeline.upscale_all(textures, _settings(scale=4), _Session(None), progress=_Ticks())
    pipeline.remaster_all(
        textures,
        _settings(scale=4, remaster="texturedat2"),
        _Session(_Model()),
        progress=ticks,
    )

    assert ticks.total == pytest.approx(len(textures))
    assert len(ticks.steps) > 1


def test_a_skipped_remaster_still_accounts_for_its_chunk():
    """No model, no pass -- but the bar still has to reach the end of the corpus."""
    ticks = _Ticks()
    textures = _textures(6)

    pipeline.remaster_all(
        textures,
        _settings(scale=4, remaster="missing"),
        _Session(None),
        progress=ticks,
    )

    assert ticks.total == pytest.approx(len(textures))


def test_writing_reports_one_tick_per_file_and_not_a_running_total(tmp_path):
    """The fault a run without --pbr showed: the bar was advanced BY the total so far, not by one.

    Ninety-six files reported 1 + 2 + ... + 96 = 4,656 ticks against a corpus of 1,751, so the writing bar
    of an upscale-only run finished during the first chunk and stayed there for the rest of the hour.
    """
    ticks = _Ticks()
    textures = _textures(8)
    outputs = pipeline.Outputs(tmp_path / "out")

    written = pipeline.derive_all(
        textures,
        _settings(scale=1, derive_pbr=False),
        outputs,
        _Session(None),
        progress=ticks,
    )

    assert written == len(textures)
    assert ticks.total == pytest.approx(len(textures))
    assert ticks.steps == [1] * len(textures)


def test_the_runner_reports_every_image_exactly_once():
    """The count is only known inside `run`, so this is where it has to come from."""
    import torch

    made = object.__new__(runner.Runner)
    made._torch = torch
    made._device = torch.device("cpu")
    made.dtype = torch.float32
    made.scale = 1
    made.architecture = "DAT"
    made._descriptor = lambda tensor: tensor
    made.budget = 250_000

    ticks = _Ticks()
    images = [np.zeros((64, 64, 3), dtype=np.float32) for _ in range(5)]
    # One that has to be tiled, which is a different branch and reported from a different place.
    images.append(np.zeros((1024, 1024, 3), dtype=np.float32))

    out = made.run(images, wrap=False, progress=ticks)

    assert len(out) == len(images)
    assert ticks.total == len(images)

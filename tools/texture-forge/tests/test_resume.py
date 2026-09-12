"""What counts as a finished texture, which is the whole of whether a resumed run is safe.

A resume that is too eager ships a truncated or wrongly-sized map into the pack, and nothing downstream can
notice: a short PNG still decodes into a plausible picture, and a 4x diffuse in an 8x pack is just a soft
one. So the interesting cases here are all the ones where a file EXISTS and the texture is still not done.
"""

from __future__ import annotations

import numpy as np
import pytest
from PIL import Image

from texture_forge import images, pipeline, resume


def _source(directory, name: str, size: int = 8):
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"{name}.png"
    pixels = np.linspace(0.0, 1.0, size * size * 3, dtype=np.float32).reshape(size, size, 3)
    images.write_rgb(path, pixels)
    return path


def _finish(outputs, name: str, *, scale: int, size: int = 8, derive: bool, inspection: bool):
    """Writes every output a completed texture has, at the right size."""
    side = size * scale

    for path in resume.expected(outputs, name, derive=derive, inspection=inspection):
        images.write_grey(path, np.zeros((side, side), dtype=np.float32))


def test_a_texture_with_every_output_is_done(tmp_path):
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=True, inspection=True)

    assert resume.completed(source, outputs, scale=4, derive=True, inspection=True)


def test_a_missing_map_is_not_done(tmp_path):
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=True, inspection=True)
    (outputs.inspection("occlusion") / "wall.png").unlink()

    assert not resume.completed(source, outputs, scale=4, derive=True, inspection=True)


def test_a_truncated_png_is_not_done(tmp_path):
    """The case the existence test gets wrong: a file that is there and is not a PNG."""
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=True, inspection=True)

    detail = outputs.detail / "wall.png"
    detail.write_bytes(detail.read_bytes()[:-40])

    assert not resume.finished(detail)
    assert not resume.completed(source, outputs, scale=4, derive=True, inspection=True)


def test_an_empty_file_is_not_done(tmp_path):
    """Shorter than the marker it is looked for in -- a seek past the start, not a missing file."""
    outputs = pipeline.Outputs(tmp_path / "out")
    outputs.hd.mkdir(parents=True)
    (outputs.hd / "wall.png").write_bytes(b"")

    assert not resume.finished(outputs.hd / "wall.png")


def test_the_wrong_scale_is_not_done(tmp_path):
    """A finished 4x pack must not satisfy an 8x run, however complete it looks."""
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=True, inspection=True)

    assert resume.completed(source, outputs, scale=4, derive=True, inspection=True)
    assert not resume.completed(source, outputs, scale=8, derive=True, inspection=True)


def test_what_is_expected_follows_the_command(tmp_path):
    """`upscale` writes the diffuse alone, so a diffuse-only output is done for it and not for `all`."""
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=False, inspection=False)

    assert resume.completed(source, outputs, scale=4, derive=False, inspection=False)
    assert not resume.completed(source, outputs, scale=4, derive=True, inspection=False)


def test_inspection_maps_are_only_required_when_they_are_written(tmp_path):
    source = _source(tmp_path / "png", "wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=4, derive=True, inspection=False)

    assert resume.completed(source, outputs, scale=4, derive=True, inspection=False)
    assert not resume.completed(source, outputs, scale=4, derive=True, inspection=True)


def test_pending_keeps_the_given_order(tmp_path):
    png = tmp_path / "png"
    sources = [_source(png, name) for name in ("a", "b", "c")]
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "b", scale=2, derive=True, inspection=True)

    todo = resume.pending(sources, outputs, scale=2, derive=True, inspection=True)

    assert [path.stem for path in todo] == ["a", "c"]


def test_sweep_removes_only_the_temporaries(tmp_path):
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, "wall", scale=2, derive=True, inspection=True)
    stray = outputs.detail / f"half.png{images.PARTIAL_SUFFIX}"
    stray.write_bytes(b"not a png")

    assert resume.sweep(outputs) == 1
    assert not stray.exists()
    assert (outputs.detail / "wall.png").exists()


def test_an_interrupted_write_leaves_nothing_behind(tmp_path, monkeypatch):
    """The other half of the defence: the destination only ever names a complete file."""
    destination = tmp_path / "hd" / "wall.png"

    class Exploding:
        def save(self, *_args, **_kwargs):
            raise KeyboardInterrupt

    with pytest.raises(KeyboardInterrupt):
        images._save(Exploding(), destination)

    assert not destination.exists()
    assert list(tmp_path.rglob(f"*{images.PARTIAL_SUFFIX}")) == []


def test_source_paths_are_sorted_and_are_not_decoded(tmp_path):
    png = tmp_path / "png"

    for name in ("Zulu", "alpha", "Mike"):
        _source(png, name)

    assert [path.stem for path in pipeline.source_paths(png)] == ["alpha", "Mike", "Zulu"]


def test_the_five_inspection_kinds_are_the_ones_the_run_writes(tmp_path):
    """Stops the two lists drifting: a map added to the run and not to the check is never required."""
    source = _source(tmp_path / "png", "wall", size=4)
    outputs = pipeline.Outputs(tmp_path / "out")

    texture = pipeline.read_texture(source)
    pipeline.prepare(texture, pipeline.Settings(scale=1, use_ai=False))
    pipeline.derive_all(
        [texture], pipeline.Settings(scale=1, use_ai=False), outputs, pipeline.Session()
    )

    written = {
        path.parent.name
        for path in outputs.root.rglob("wall.png")
    }

    assert written == {"hd", "detail", *resume.INSPECTION_KINDS}
    assert resume.completed(source, outputs, scale=1, derive=True, inspection=True)

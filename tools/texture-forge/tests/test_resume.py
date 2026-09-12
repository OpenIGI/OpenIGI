"""What counts as a finished texture, which is the whole of whether a resumed run is safe.

A resume that is too eager ships a truncated or wrongly-sized asset into the pack, and nothing downstream
can notice: a short container still decodes into a plausible picture, and a 4x diffuse in an 8x pack is
just a soft one. So the interesting cases here are all the ones where a file EXISTS and the texture is
still not done.

Two things changed the shape of this. The output is now the game's own container rather than a pair of
PNGs in parallel directories -- so "finished" is a question about what is INSIDE a file, including how many
layers it holds, which is what tells an upscale-only pack from one carrying maps. And one image is now
written to every path that addresses it, because the same art is packed into every level that draws it, so
a run interrupted between those writes has to come back for the rest.
"""

from __future__ import annotations

import numpy as np
import pytest

from texture_forge import images, pipeline, resume, tex


def _source(directory, name: str, size: int = 8):
    """Writes a source PNG at the given relative name, creating its directories."""
    path = directory / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    pixels = np.linspace(0.0, 1.0, size * size * 3, dtype=np.float32).reshape(size, size, 3)
    images.write_rgb(path, pixels)
    return path


def _finish(outputs, names, *, scale: int, size: int = 8, derive: bool, inspection: bool):
    """Writes every output a completed texture has, at the right size."""
    side = size * scale
    rgb = np.zeros((side, side, 3), dtype=np.float32)
    alpha = np.ones((side, side), dtype=np.float32)
    detail = (rgb, alpha) if derive else None

    for name in names:
        tex.write(outputs.asset(name), rgb, alpha, detail)

    for path in resume.expected(outputs, names, derive=derive, inspection=inspection):
        images.write_grey(path, np.zeros((side, side), dtype=np.float32))


def test_a_texture_with_every_output_is_done(tmp_path):
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=True, inspection=True)

    assert resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=True, inspection=True
    )


def test_a_missing_inspection_map_is_not_done(tmp_path):
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=True, inspection=True)
    (outputs.inspection("occlusion") / "common/textures/wall.png").unlink()

    assert not resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=True, inspection=True
    )


def test_a_truncated_asset_is_not_done(tmp_path):
    """The case the existence test gets wrong: a file that is there and is short."""
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=True, inspection=True)

    asset = outputs.asset("common/textures/wall")
    asset.write_bytes(asset.read_bytes()[:-40])

    assert tex.describe(asset) is None
    assert not resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=True, inspection=True
    )


def test_an_empty_file_is_not_done(tmp_path):
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=True, inspection=True)

    outputs.asset("common/textures/wall").write_bytes(b"")

    assert not resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=True, inspection=True
    )


def test_the_wrong_scale_is_not_done(tmp_path):
    """A 4x pack asked for at 8x has to be redone, not accepted as present."""
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=True, inspection=True)

    assert not resume.completed(
        source, ["common/textures/wall"], outputs, scale=8, derive=True, inspection=True
    )


def test_a_pack_without_maps_is_unfinished_for_a_run_that_derives_them(tmp_path):
    """The layer count is the distinction, and it is inside the file.

    `--no-pbr` writes a complete, correct asset with one layer. Asked for maps afterwards, that asset is
    not finished -- and nothing about its name or size says so, which is why the check opens it.
    """
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["common/textures/wall"], scale=4, derive=False, inspection=False)

    assert resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=False, inspection=False
    )
    assert not resume.completed(
        source, ["common/textures/wall"], outputs, scale=4, derive=True, inspection=False
    )


def test_every_path_the_image_is_addressed_at_must_be_there(tmp_path):
    """One image, several paths: a run interrupted between the writes is not finished.

    This is the failure the dedup introduces. The same art is packed into every level that draws it, so it
    is upscaled once and written many times; checking only the first path would call the job done and
    leave the other levels showing the original texture for good.
    """
    source = _source(tmp_path / "png", "common/textures/wall")
    outputs = pipeline.Outputs(tmp_path / "out")
    names = ["common/textures/wall", "missions/location0/level3/textures/wall"]

    _finish(outputs, names, scale=4, derive=True, inspection=True)
    assert resume.completed(source, names, outputs, scale=4, derive=True, inspection=True)

    outputs.asset(names[1]).unlink()
    assert not resume.completed(source, names, outputs, scale=4, derive=True, inspection=True)


def test_what_is_expected_follows_the_command(tmp_path):
    """`upscale` writes no inspection maps, so it must not wait for them."""
    outputs = pipeline.Outputs(tmp_path / "out")

    assert resume.expected(outputs, ["wall"], derive=False, inspection=False) == []
    assert resume.expected(outputs, ["wall"], derive=False, inspection=True) == []
    assert resume.expected(outputs, ["wall"], derive=True, inspection=False) == []
    assert len(resume.expected(outputs, ["wall"], derive=True, inspection=True)) == len(
        resume.INSPECTION_KINDS
    )


def test_pending_keeps_the_given_order(tmp_path):
    outputs = pipeline.Outputs(tmp_path / "out")
    first = _source(tmp_path / "png", "a")
    second = _source(tmp_path / "png", "b")
    third = _source(tmp_path / "png", "c")

    _finish(outputs, ["b"], scale=4, derive=False, inspection=False)

    work = [(first, ["a"]), (second, ["b"]), (third, ["c"])]
    todo = resume.pending(work, outputs, scale=4, derive=False, inspection=False)

    assert [item[0] for item in todo] == [first, third]


def test_sweep_removes_only_the_temporaries(tmp_path):
    outputs = pipeline.Outputs(tmp_path / "out")
    _finish(outputs, ["wall"], scale=4, derive=False, inspection=False)

    partial = outputs.asset("wall").with_name(f"wall.tex{images.PARTIAL_SUFFIX}")
    partial.write_bytes(b"half a file")

    assert resume.sweep(outputs) == 1
    assert not partial.exists()
    assert outputs.asset("wall").exists()


def test_the_five_inspection_kinds_are_the_ones_the_run_writes():
    assert set(resume.INSPECTION_KINDS) == {
        "normal",
        "orm",
        "roughness",
        "occlusion",
        "height",
    }

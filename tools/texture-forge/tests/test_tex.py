"""The byte layout of the container this tool writes, which the game reads.

The other half of this contract is `tests/OpenIGI.Formats.Tests/SurfaceAssetTests.cs`, which asserts the
same table from the reader's side. Two programs have to agree on it and they never run together, so the
layout is written out as literal bytes on both sides rather than as a comparison between them.

If you change a number here, change it there. A file that is wrong in a way both sides agree on still
loads -- it just draws the wrong thing -- which is why the expectations are spelled out rather than
round-tripped through the writer's own reader.
"""

from __future__ import annotations

import struct

import numpy as np
import pytest

from texture_forge import tex


def _solid(width: int, height: int, colour: tuple[float, float, float], alpha: float = 1.0):
    rgb = np.zeros((height, width, 3), dtype=np.float32)
    rgb[..., 0], rgb[..., 1], rgb[..., 2] = colour
    return rgb, np.full((height, width), alpha, dtype=np.float32)


def test_the_header_is_the_layout_the_reader_parses():
    rgb, alpha = _solid(8, 4, (1.0, 0.0, 0.0))
    data = tex.encode([tex._as_bgra(rgb, alpha)])  # noqa: SLF001 - the layout is the point

    assert data[0:4] == b"LOOP"
    assert struct.unpack_from("<I", data, 0x04)[0] == 9, "version 9, the multi-image container"
    assert struct.unpack_from("<I", data, 0x20)[0] == 1, "layer count"
    assert struct.unpack_from("<I", data, 0x28)[0] == 8, "tile width"
    assert struct.unpack_from("<I", data, 0x2C)[0] == 4, "tile height"


def test_a_layer_descriptor_carries_its_own_offset_format_and_size():
    rgb, alpha = _solid(8, 4, (1.0, 0.0, 0.0))
    data = tex.encode([tex._as_bgra(rgb, alpha)])  # noqa: SLF001

    base = tex.HEADER_SIZE

    assert struct.unpack_from("<I", data, base + 0x00)[0] == tex.HEADER_SIZE + tex.LAYER_SIZE
    assert struct.unpack_from("<I", data, base + 0x04)[0] == 3, "Bgra8"
    assert struct.unpack_from("<H", data, base + 0x08)[0] == 8 * 4, "pitch is bytes, not pixels"
    assert struct.unpack_from("<H", data, base + 0x0A)[0] == 8
    assert struct.unpack_from("<H", data, base + 0x0C)[0] == 4


def test_the_payload_is_blue_first():
    """Memory order, not the order a sampler names the channels.

    Getting this backwards produces a picture -- a plausible one, with red and blue swapped -- which is
    exactly the kind of wrong that ships.
    """
    rgb, alpha = _solid(2, 1, (1.0, 0.0, 0.0))
    data = tex.encode([tex._as_bgra(rgb, alpha)])  # noqa: SLF001

    first = tex.HEADER_SIZE + tex.LAYER_SIZE

    assert data[first + 0] == 0, "blue"
    assert data[first + 1] == 0, "green"
    assert data[first + 2] == 255, "red"
    assert data[first + 3] == 255, "alpha"


def test_two_layers_sit_back_to_back_and_are_declared_as_two():
    rgb, alpha = _solid(4, 2, (0.0, 1.0, 0.0))
    detail = _solid(4, 2, (0.0, 0.0, 1.0), alpha=0.25)
    data = tex.encode([tex._as_bgra(rgb, alpha), tex._as_bgra(*detail)])  # noqa: SLF001

    assert struct.unpack_from("<I", data, 0x20)[0] == 2

    payload = 4 * 4 * 2
    first = tex.HEADER_SIZE + (tex.LAYER_SIZE * 2)
    second = struct.unpack_from("<I", data, tex.HEADER_SIZE + tex.LAYER_SIZE + 0x00)[0]

    assert second == first + payload
    assert len(data) == first + (payload * 2)


def test_layers_of_different_sizes_are_refused():
    """The header declares ONE size for the file, so a mismatched pair is not representable."""
    rgb, alpha = _solid(4, 2, (1.0, 1.0, 1.0))
    other, other_alpha = _solid(8, 2, (1.0, 1.0, 1.0))

    with pytest.raises(ValueError, match="declares"):
        tex.encode([tex._as_bgra(rgb, alpha), tex._as_bgra(other, other_alpha)])  # noqa: SLF001


def test_a_layer_too_wide_for_a_16_bit_pitch_is_refused():
    """Refused rather than truncated: the field would wrap and the file would decode as garbage."""
    rgb, alpha = _solid(tex.MAX_WIDTH + 1, 1, (0.0, 0.0, 0.0))

    with pytest.raises(ValueError, match="16 bits"):
        tex.encode([tex._as_bgra(rgb, alpha)])  # noqa: SLF001


def test_describe_reports_the_shape_and_rejects_a_short_file(tmp_path):
    rgb, alpha = _solid(8, 4, (0.5, 0.5, 0.5))
    path = tmp_path / "a.tex"

    tex.write(path, rgb, alpha, None)
    assert tex.describe(path) == (8, 4, 1)

    tex.write(path, rgb, alpha, (rgb, alpha))
    assert tex.describe(path) == (8, 4, 2)

    path.write_bytes(path.read_bytes()[:-1])
    assert tex.describe(path) is None, "a file one byte short is not finished"


def test_a_write_leaves_no_partial_behind(tmp_path):
    """Written through a temporary, so an interrupted run never leaves a short file in place."""
    rgb, alpha = _solid(4, 4, (0.0, 0.0, 0.0))
    path = tmp_path / "nested" / "a.tex"

    tex.write(path, rgb, alpha, None)

    assert path.exists()
    assert list(path.parent.glob("*.partial")) == []

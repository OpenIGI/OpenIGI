"""Writes the game's own texture container, so a pack is assets rather than a side-car format.

WHY THIS EXISTS. The renderer used to take upscaled textures from a directory of PNGs pointed at by a
setting of its own. It does not any more: replacement art goes in `<game>/overrides`, laid out the way the
installation lays its assets out, and arrives through the ordinary resource path. That only works if what
this tool writes is a file the game can already read.

THE CONTAINER. `LOOP` version 9 -- the multi-image form a level's terrain atlas uses -- rather than the
single-image versions 2 and 11. One reason: a version 9 file holds SEVERAL layers of the same size, so the
upscaled diffuse and the derived surface map travel together as one asset. The pair cannot then be
half-installed, half-regenerated, or pointed at by two settings that disagree.

    layer 0   the upscaled diffuse, BGRA8
    layer 1   the packed detail map, BGRA8, and only when maps were derived

THE LAYOUT, WHICH THE READER PINS AND THIS MUST MATCH. Little-endian throughout.

    header, 52 bytes
      0x00  'LOOP'
      0x04  version, 9
      0x12  secondary format, u16
      0x20  layer count, u32
      0x28  tile width, u32
      0x2C  tile height, u32
      0x30  shared format, u32          (version 7 reads this; version 9 reads each layer's own)

    layer descriptor, 32 bytes each, immediately after the header
      0x00  payload offset from the start of the file, u32
      0x04  format code, u32            (3 = BGRA8)
      0x08  pitch in bytes, u16
      0x0A  width in pixels, u16
      0x0C  height in pixels, u16

    payloads, in layer order, each `pitch * height` bytes

PITCH IS SIXTEEN BITS, which is the one real limit here: at four bytes a pixel a layer cannot be wider
than 16383. An 8x upscale of this game's largest texture is 2048, so there is room, but the writer refuses
rather than silently truncating a field.
"""

from __future__ import annotations

import os
import struct
from pathlib import Path

import numpy as np

from . import images

__all__ = [
    "HEADER_SIZE",
    "LAYER_SIZE",
    "VERSION",
    "FORMAT_BGRA8",
    "MAX_WIDTH",
    "encode",
    "write",
    "describe",
]

HEADER_SIZE = 52
LAYER_SIZE = 32
VERSION = 9

#: `TextureFormatCode.Bgra8`. Thirty-two bits in memory order: blue, green, red, alpha.
FORMAT_BGRA8 = 3

#: The widest layer a 16-bit pitch can describe at four bytes a pixel.
MAX_WIDTH = 0xFFFF // 4


def _as_bgra(rgb: np.ndarray, alpha: np.ndarray | None) -> np.ndarray:
    """Packs float RGB in [0, 1] and optional alpha into the byte order the container stores."""
    if rgb.ndim != 3 or rgb.shape[2] != 3:
        raise ValueError(f"expected an (h, w, 3) image, got {rgb.shape}")

    height, width, _ = rgb.shape

    if alpha is None:
        alpha = np.ones((height, width), dtype=np.float32)

    if alpha.shape != (height, width):
        raise ValueError(f"alpha {alpha.shape} does not match the image {(height, width)}")

    out = np.empty((height, width, 4), dtype=np.uint8)
    quantised = np.clip(np.rint(rgb * 255.0), 0, 255).astype(np.uint8)

    # Memory order, which is why this is not simply the array as it stands: the file stores blue first.
    out[..., 0] = quantised[..., 2]
    out[..., 1] = quantised[..., 1]
    out[..., 2] = quantised[..., 0]
    out[..., 3] = np.clip(np.rint(alpha * 255.0), 0, 255).astype(np.uint8)

    return out


def encode(layers: list[np.ndarray]) -> bytes:
    """Builds the file bytes from one or more BGRA8 layers.

    Every layer must be the same size: the header declares one tile size for the file, which is what makes
    a diffuse and the map derived from it a legal pair and anything else not.
    """
    if not layers:
        raise ValueError("a texture needs at least one layer")

    height, width, channels = layers[0].shape

    if channels != 4:
        raise ValueError(f"expected BGRA8 layers, got {channels} channels")

    if width > MAX_WIDTH or height > 0xFFFF:
        raise ValueError(
            f"{width}x{height} cannot be described: the layer descriptor's pitch is 16 bits, "
            f"so a BGRA8 layer is limited to {MAX_WIDTH} wide"
        )

    for index, layer in enumerate(layers):
        if layer.shape != (height, width, 4):
            raise ValueError(
                f"layer {index} is {layer.shape[1]}x{layer.shape[0]}, but the file declares {width}x{height}"
            )

    pitch = width * 4
    payload = pitch * height
    first = HEADER_SIZE + (LAYER_SIZE * len(layers))

    header = bytearray(HEADER_SIZE)
    header[0:4] = b"LOOP"
    struct.pack_into("<I", header, 0x04, VERSION)
    struct.pack_into("<H", header, 0x12, 0)
    struct.pack_into("<I", header, 0x20, len(layers))
    struct.pack_into("<I", header, 0x28, width)
    struct.pack_into("<I", header, 0x2C, height)
    struct.pack_into("<I", header, 0x30, FORMAT_BGRA8)

    descriptors = bytearray()
    for index in range(len(layers)):
        descriptor = bytearray(LAYER_SIZE)
        struct.pack_into("<I", descriptor, 0x00, first + (index * payload))
        struct.pack_into("<I", descriptor, 0x04, FORMAT_BGRA8)
        struct.pack_into("<H", descriptor, 0x08, pitch)
        struct.pack_into("<H", descriptor, 0x0A, width)
        struct.pack_into("<H", descriptor, 0x0C, height)
        descriptors += descriptor

    body = b"".join(np.ascontiguousarray(layer).tobytes() for layer in layers)

    return bytes(header) + bytes(descriptors) + body


def write(
    path: Path,
    rgb: np.ndarray,
    alpha: np.ndarray | None,
    detail: tuple[np.ndarray, np.ndarray] | None = None,
) -> None:
    """Writes one asset: the upscaled diffuse, and the derived map beside it when there is one.

    `detail` is the (rgb, alpha) pair `pack.detail` produces. Absent, the file carries one layer and the
    renderer finds no map in it — which is what `--no-pbr` produces, and is a complete, correct asset.
    """
    layers = [_as_bgra(rgb, alpha)]

    if detail is not None:
        layers.append(_as_bgra(detail[0], detail[1]))

    path.parent.mkdir(parents=True, exist_ok=True)

    # Through a neighbouring temporary and an atomic replace, for the reason `images._save` does it: a run
    # interrupted mid-write would otherwise leave a file that EXISTS and is short, and `resume` decides
    # what to skip by looking at what is on disk. The suffix is the same one, so one sweep clears both.
    temporary = path.with_name(f"{path.name}{images.PARTIAL_SUFFIX}")

    try:
        temporary.write_bytes(encode(layers))
        os.replace(temporary, path)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def describe(path: Path) -> tuple[int, int, int] | None:
    """A container's width, height and layer count, or None when it is absent or incomplete.

    Reads the header and checks the file is as long as the layers it declares. That is what makes it
    usable as a resume test: a truncated file is reported absent rather than accepted as finished, and a
    caller can tell an upscale-only pack (one layer) from one carrying maps (two) without decoding
    anything.
    """
    try:
        with path.open("rb") as handle:
            header = handle.read(HEADER_SIZE)

            if len(header) < HEADER_SIZE or header[0:4] != b"LOOP":
                return None

            (version,) = struct.unpack_from("<I", header, 0x04)
            (layers,) = struct.unpack_from("<I", header, 0x20)
            (width,) = struct.unpack_from("<I", header, 0x28)
            (height,) = struct.unpack_from("<I", header, 0x2C)

            if version != VERSION or layers <= 0 or width <= 0 or height <= 0:
                return None

            expected = HEADER_SIZE + (LAYER_SIZE * layers) + (width * 4 * height * layers)

            return (width, height, layers) if path.stat().st_size == expected else None
    except OSError:
        return None

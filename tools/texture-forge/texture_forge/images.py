"""Reading and writing the PNGs, in the one layout everything else here assumes.

Everything in this tool passes images around as float32 in [0, 1], shaped (height, width, channels), with
alpha kept as its own (height, width) array rather than a fourth channel. That split is not tidiness: RGB
and alpha go through the upscaler as two separate passes, and carrying them together would mean splitting
and rejoining them at every stage.

8-bit PNG in and 8-bit PNG out, because that is what `ResExtract images` writes and what `MaterialDetailPack`
reads. There is no case here for 16-bit: the source is a quantised palette or a 5-bit channel, so the extra
precision would describe rounding error rather than art.
"""

from __future__ import annotations

import os
from pathlib import Path

import numpy as np
from PIL import Image

__all__ = ["read", "write_rgb", "write_rgba", "write_grey", "quantise", "PARTIAL_SUFFIX"]


PARTIAL_SUFFIX = ".partial"
"""What a PNG is called while it is being written. See `_save`, and `resume.sweep`."""


def _save(image: Image.Image, path: Path) -> None:
    """Writes a PNG so that it is either wholly there or not there at all.

    Saving straight to the destination is what would make a resumed run untrustworthy. Interrupt a run
    mid-save and the file EXISTS, is short, and is not a PNG -- and `resume`, which decides what to skip by
    looking at what is on disk, would skip a texture that was never finished. Both halves of the defence
    live here: the pixels go to a neighbouring temporary and are moved into place with `os.replace`, which
    is atomic on Windows as well as on POSIX, so the destination only ever names a complete file.

    An interrupt therefore leaves a stray temporary rather than a broken output, and `resume.sweep` deletes
    those at the start of the next run.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f"{path.name}{PARTIAL_SUFFIX}")

    try:
        image.save(temporary, format="PNG", optimize=True)
        os.replace(temporary, path)
    except BaseException:
        # BaseException, not Exception: a Ctrl-C during the save is the commonest way to get here, and it
        # is the one case where leaving the temporary behind would be worst.
        temporary.unlink(missing_ok=True)
        raise


def read(path: Path) -> tuple[np.ndarray, np.ndarray | None]:
    """Reads a PNG as (rgb, alpha), or (rgb, None) when it has no alpha channel.

    Returns alpha as None rather than as ones, so the caller can tell "opaque by construction" from "has an
    alpha channel that happens to be full" -- the first needs no cut-out handling at all.
    """
    with Image.open(path) as handle:
        image = handle.convert("RGBA") if handle.mode in ("RGBA", "LA", "P") else handle.convert("RGB")
        data = np.asarray(image, dtype=np.uint8)

    pixels = data.astype(np.float32) / 255.0

    if pixels.shape[-1] == 4:
        return np.ascontiguousarray(pixels[..., :3]), np.ascontiguousarray(pixels[..., 3])

    return pixels, None


def quantise(values: np.ndarray) -> np.ndarray:
    """Rounds float [0, 1] to uint8 the same way the C# side does.

    `MaterialMapGenerator.Quantise` is `(byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255)`, and the
    round-trip test between the two implementations compares bytes, so round-half-away-from-zero here has to
    match MathF.Round's round-half-to-even... except that it does not, and the difference only ever shows on
    an exact .5, which `value * 255f` produces for a vanishingly small set of inputs. numpy's rint is
    round-half-to-even, which is what MathF.Round does by default. They agree.
    """
    return np.clip(np.rint(values * 255.0), 0, 255).astype(np.uint8)


def write_rgb(path: Path, rgb: np.ndarray) -> None:
    """Writes a three-channel PNG."""
    _save(Image.fromarray(quantise(rgb), mode="RGB"), path)


def write_rgba(path: Path, rgb: np.ndarray, alpha: np.ndarray) -> None:
    """Writes a four-channel PNG from separate colour and alpha."""
    merged = np.concatenate([rgb, alpha[..., None]], axis=-1)
    _save(Image.fromarray(quantise(merged), mode="RGBA"), path)


def write_grey(path: Path, values: np.ndarray) -> None:
    """Writes a single-channel map as an ordinary greyscale PNG, for looking at."""
    _save(Image.fromarray(quantise(values), mode="L"), path)

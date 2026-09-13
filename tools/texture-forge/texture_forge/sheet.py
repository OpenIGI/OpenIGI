"""A contact sheet, because these maps have to be judged by eye and cannot be judged one at a time.

Every map this tool writes is INVENTED DATA. There is no ground truth to diff against and no metric that
says whether a normal map is right -- the only test that counts is whether a wall reads as a wall when the
sun crosses it. What a contact sheet buys is the ability to see a systematic fault: a delight setting that
flattens every metal surface, an upscaler that turns all the foliage to mush, a normal map whose green
channel is the wrong way round. One texture at a time, each of those looks like a plausible picture.

Rendered at the SOURCE resolution rather than the upscaled one, deliberately. A sheet of 4x tiles is a
100-megapixel PNG that no viewer opens comfortably, and the fault being looked for is at the scale of the
whole texture rather than the texel.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

__all__ = ["write"]


_TILE = 96
"""Each cell's side in the sheet. Large enough to read a texture's character, small enough that 64 fit."""

_GAP = 4
_BACKGROUND = 24


def _tile(path: Path, size: int) -> np.ndarray:
    """Loads one texture as an opaque RGB tile, compositing any alpha over a checkerboard.

    A checkerboard rather than black: a cut-out over black is indistinguishable from a texture that is
    genuinely black there, and telling those apart is half of what the sheet is for.
    """
    from PIL import Image

    with Image.open(path) as handle:
        image = handle.convert("RGBA").resize((size, size), Image.Resampling.NEAREST)
        data = np.asarray(image, dtype=np.float32) / 255.0

    rgb = data[..., :3]
    alpha = data[..., 3:4]

    axis = np.arange(size)
    checker = (((axis[:, None] // 8) + (axis[None, :] // 8)) % 2).astype(np.float32)
    board = (0.35 + (checker * 0.25))[..., None]

    return (rgb * alpha) + (board * (1.0 - alpha))


def write(
    source: Path,
    destination: Path,
    compare: Path | None = None,
    *,
    limit: int = 64,
    width: int = 8,
) -> int:
    """Writes a contact sheet. With `compare`, each texture appears twice side by side, source then compare.

    Matched by filename, and a texture missing from the comparison directory is skipped rather than shown
    against a blank -- a half-empty sheet reads as a broken run when it is usually just a partial one.
    """
    from PIL import Image

    paths = sorted(source.glob("*.png"), key=lambda item: item.name.lower())

    if compare is not None:
        paths = [path for path in paths if (compare / path.name).exists()]

    paths = paths[:limit]

    if not paths:
        return 0

    per_texture = 2 if compare is not None else 1
    cells = len(paths) * per_texture
    columns = max(width * per_texture, per_texture)
    rows = (cells + columns - 1) // columns

    sheet_width = (columns * _TILE) + ((columns + 1) * _GAP)
    sheet_height = (rows * _TILE) + ((rows + 1) * _GAP)

    sheet = np.full((sheet_height, sheet_width, 3), _BACKGROUND / 255.0, dtype=np.float32)

    for index, path in enumerate(paths):
        tiles = [_tile(path, _TILE)]

        if compare is not None:
            tiles.append(_tile(compare / path.name, _TILE))

        for offset, tile in enumerate(tiles):
            cell = (index * per_texture) + offset
            row, column = divmod(cell, columns)

            top = _GAP + (row * (_TILE + _GAP))
            left = _GAP + (column * (_TILE + _GAP))
            sheet[top : top + _TILE, left : left + _TILE] = tile

    destination.parent.mkdir(parents=True, exist_ok=True)
    data = np.clip(np.rint(sheet * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(data, mode="RGB").save(destination, optimize=True)

    return len(paths)

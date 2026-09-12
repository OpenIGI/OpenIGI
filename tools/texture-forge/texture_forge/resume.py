"""What a finished texture looks like on disk, so a second run can leave it alone.

A whole-corpus run at 8x is hours of GPU time, and there are only so many ways it ends early: a Ctrl-C, a
driver reset, a machine that reboots for an update overnight. Before this existed, every one of those meant
starting from the first texture again, re-deriving a thousand maps that were already sitting in the output
directory -- so the tool was, in practice, unusable at the setting it was most worth using.

THE OUTPUT DIRECTORY IS THE RECORD. There is no manifest, no journal and no state file, deliberately: the
pack itself is a flat directory whose filename is the key (`MaterialDetailPack.FindMapPath` is a plain
`Path.Combine`), and any second record of what was done is a thing that can disagree with the files. A
texture is done when every output the CURRENT command would write for it is present and whole. Delete a
map by hand and the next run rebuilds exactly that texture, which is also how you correct one.

WHOLE, NOT MERELY PRESENT, and the distinction is the whole difficulty. Existence is the obvious test and
it is wrong: a run interrupted during a save leaves a file that exists and is truncated, and skipping on
existence alone would bake that corruption into the pack permanently -- the worst kind of failure here,
because a short PNG usually still decodes into a plausible-looking picture. Two defences, and both are
needed:

  * `images._save` writes to a temporary and renames, so nothing half-written is ever AT the destination.
  * `finished` below still checks each file for its IEND marker, because a pack may have been written by
    an older build of this tool, before the writes were atomic.

The IEND check is eight bytes from the end of the file rather than a decode. A decode of nine thousand PNGs
would cost a minute of every run to answer a question the last twelve bytes already answer: PNG's final
chunk is fixed and terminal, so a file carrying it was written to completion.

THE SIZE CHECK is the other half, and it catches the mistake nobody expects: re-running an 8x job over a
directory holding a finished 4x one. Every filename matches, every file is whole, and skipping on those
grounds would silently leave three quarters of the corpus at the wrong resolution. So the diffuse's header
-- the header alone; PIL does not decode until you ask it to -- is measured against the source's, and a
texture whose output is not the size THIS command would produce is not done.
"""

from __future__ import annotations

from collections.abc import Iterable, Sequence
from pathlib import Path

from PIL import Image

from . import images

__all__ = ["INSPECTION_KINDS", "finished", "expected", "completed", "pending", "sweep"]


INSPECTION_KINDS = ("normal", "orm", "roughness", "occlusion", "height")
"""The five maps written for looking at, exactly as `pipeline.derive_all` writes them.

Listed here as well as there because this file has to know what a finished texture consists of, and a run
that skipped a texture whose occlusion map had never been written would be wrong in a way nothing later
could notice. If a map is added there, add it here.
"""

_IEND = b"IEND\xaeB`\x82"
"""PNG's last twelve bytes are a zero-length IEND chunk; these are the final eight of them."""


def finished(path: Path) -> bool:
    """Whether a PNG is present and was written to completion."""
    try:
        with path.open("rb") as handle:
            handle.seek(-len(_IEND), 2)
            return handle.read(len(_IEND)) == _IEND
    except OSError:
        # Absent, or shorter than the marker -- a seek past the start of a file is an OSError on Windows.
        return False


def expected(outputs, names: Sequence[str], *, derive: bool, inspection: bool) -> list[Path]:
    """Every inspection file the current command writes for one texture.

    The pictures themselves are checked by `completed`, which asks for their size and their last twelve
    bytes rather than for their presence. This covers the inspection maps written beside them.

    Follows the command, not the directory: `upscale` writes no inspection maps, so a pack that has only
    assets is complete for `upscale` and unfinished for `all`. That is what lets the two be resumed
    independently over the same output directory.
    """
    if not derive or not inspection:
        return []

    return [outputs.inspection(kind) / f"{names[0]}.png" for kind in INSPECTION_KINDS]


def _dimensions(path: Path) -> tuple[int, int] | None:
    """A PNG's size from its header, without decoding a pixel."""
    try:
        with Image.open(path) as handle:
            return handle.size
    except (OSError, ValueError):
        return None


def completed(
    source: Path, names: Sequence[str], outputs, *, scale: int, derive: bool, inspection: bool
) -> bool:
    """Whether this texture is already done, at every path it is addressed at, to this run's settings.

    EVERY path, not just the first. One image can be addressed at eight of them -- the same art packed
    into eight levels -- and a run interrupted between writes leaves some present and some not. Checking
    only the representative would call that finished and leave the rest missing for good.
    """
    source_size = _dimensions(source)

    if source_size is None:
        return False

    width, height = source_size
    wanted = (max(width * scale, 1), max(height * scale, 1))

    for name in names:
        # PRESENT, WHOLE, AND THE RIGHT SIZE, and all three are load-bearing. A file that exists proves
        # nothing: one truncated by an interrupted run still answers with the size it was going to be,
        # because a PNG records its dimensions in the first chunk; and one an earlier run wrote at a
        # different `--scale` is complete and decodes perfectly into a picture of the wrong size. Skipping
        # either leaves a pack that is quietly torn, or quietly two scales at once.
        picture = outputs.asset(name)

        if _dimensions(picture) != wanted or not finished(picture):
            return False

        if derive:
            surface = outputs.surface(name)

            if _dimensions(surface) != wanted or not finished(surface):
                return False

    return all(finished(path) for path in expected(outputs, names, derive=derive, inspection=inspection))


def pending(
    work: Iterable[tuple[Path, Sequence[str]]],
    outputs,
    *,
    scale: int,
    derive: bool,
    inspection: bool,
) -> list[tuple[Path, Sequence[str]]]:
    """The jobs still to do, in the order they were given."""
    return [
        item
        for item in work
        if not completed(item[0], item[1], outputs, scale=scale, derive=derive, inspection=inspection)
    ]


def sweep(outputs) -> int:
    """Deletes the temporaries an interrupted run left behind, returning how many.

    They are harmless -- nothing reads them and the next write overwrites its own -- but an output
    directory carrying a dozen `*.png.partial` files from last night's Ctrl-C looks like damage, and
    somebody will eventually try to load one.
    """
    if not outputs.root.is_dir():
        return 0

    removed = 0

    for path in outputs.root.rglob(f"*{images.PARTIAL_SUFFIX}"):
        try:
            path.unlink()
            removed += 1
        except OSError:
            continue

    return removed

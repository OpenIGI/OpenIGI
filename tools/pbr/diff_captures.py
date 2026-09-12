"""Reports what changed between two captures of the same viewpoint.

Run through `uv run --with pillow --with numpy`, which is how `compare-pbr.ps1` calls it: the
repository's Python is a bare interpreter and this is the only thing here that needs an array
library.

The numbers are deliberately plain. A render change that cannot move a mean, a maximum or a count
of altered pixels is a change nobody will see in the game either, and the whole reason this exists
is that four conclusions were drawn last session from screenshots that were identical.
"""

from __future__ import annotations

import argparse
import json
import sys

import numpy as np
from PIL import Image


def load(path: str) -> np.ndarray:
    """Reads a capture as float luminance-carrying RGB in [0, 255]."""
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.float32)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--label", default="")
    # Eight is a code value, not a fraction: below it a difference is inside the dither and the
    # display encode, and counting those makes every pair of captures look different.
    parser.add_argument("--threshold", type=float, default=8.0)
    parser.add_argument("--json", action="store_true")
    arguments = parser.parse_args()

    before = load(arguments.before)
    after = load(arguments.after)

    if before.shape != after.shape:
        print(f"{arguments.label}: sizes differ, {before.shape} vs {after.shape}", file=sys.stderr)
        return 2

    difference = np.abs(after - before)
    # Per pixel rather than per channel: a change that moves only the blue of a pixel is still one
    # changed pixel, and the largest channel is what the eye picks up.
    pixel = difference.max(axis=2)
    changed = float((pixel >= arguments.threshold).mean() * 100.0)

    result = {
        "label": arguments.label,
        "mean": float(difference.mean()),
        "p99": float(np.percentile(pixel, 99.0)),
        "max": float(pixel.max()),
        "changed_percent": changed,
        "mean_before": float(before.mean()),
        "mean_after": float(after.mean()),
    }

    if arguments.json:
        print(json.dumps(result))
    else:
        print(
            f"{result['label']:<24} mean {result['mean']:7.3f}  p99 {result['p99']:6.1f}  "
            f"max {result['max']:5.1f}  changed {result['changed_percent']:6.2f}%  "
            f"brightness {result['mean_before']:6.2f} -> {result['mean_after']:6.2f}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

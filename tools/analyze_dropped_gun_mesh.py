#!/usr/bin/env python3
"""Inspect a JSON dump of pickup-space weapon vertices.

Pass a JSON array of [x, y, z] engine-unit points emitted from a weapon model.  This shows why an
enclosing capsule can visually float: its radius must cover the greatest cross-axis extent, while the
mesh's actual ground support comes from only the lowest vertices.  The runtime now uses those vertices
for terrain contact and reserves the narrow core capsule for broad-phase rigid-body motion.
"""

import json
import math
import sys
from pathlib import Path


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: analyze_dropped_gun_mesh.py vertices.json")
    points = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    if not points:
        raise SystemExit("the vertex list is empty")
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    extent = [high[i] - low[i] for i in range(3)]
    axis = max(range(3), key=extent.__getitem__)
    radius = max(extent[i] for i in range(3) if i != axis) / 2.0
    lowest = sorted(points, key=lambda p: p[2])[:8]
    print(json.dumps({
        "vertex_count": len(points),
        "minimum": low,
        "maximum": high,
        "extent": extent,
        "long_axis": "xyz"[axis],
        "old_enclosing_capsule_radius": radius,
        "lowest_mesh_vertices": lowest,
        "note": "A large enclosing radius is expected to float. Runtime terrain contacts use the mesh vertices."
    }, indent=2))


if __name__ == "__main__":
    main()

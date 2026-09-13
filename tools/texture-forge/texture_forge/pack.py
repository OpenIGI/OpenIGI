"""Writes the one texture the shader actually samples, in the exact arrangement it expects.

THE LAYOUT, WHICH IS NOT NEGOTIABLE AND IS NOT OBVIOUS.

The deferred G-buffer pass binds three samplers per surface: the diffuse, the lightmap, and ONE more called
`uDetail` that carries the entire physically-based companion set packed into four channels:

    r = normal.x, remapped from [-1, 1] to [0, 1]
    g = normal.y, likewise, OpenGL handedness -- positive is up
    b = roughness MULTIPLIER, stored HALVED, so 0.5 means "leave the material alone"
    a = OCCLUSION, white for open and dark for a crease

and the shader undoes it as

    tangentNormal = vec3((detail.xy * 2) - 1, 0)
    tangentNormal.z = sqrt(max(1 - dot(tangentNormal.xy, tangentNormal.xy), 0))
    normal = normalize(mix(normal, CotangentFrame(...) * tangentNormal, r_material_detail))
    roughness = clamp(roughness * (detail.z * 2), 0.02, 1)
    occlusion = detail.w

The strength BLENDS towards the mapped normal rather than scaling the stored pair. It used to scale, and
could not survive a strength above one: what is stored is the xy of an already-unit normal, so scaling it
made the pair longer than one and the rebuilt z collapsed, leaving the world normal lying flat along the
wall. Nothing about this file changes either way -- the stored bytes are the same -- but the docstring
quoted the shader, so it has to stay true to it.

Three consequences worth stating, because each one has already caused a bug somewhere:

**Alpha is occlusion, and used to be the complement of metallic.** Metal is a property of a MATERIAL and
not of a texel -- a fuel tank is steel over all of it and a wall is steel over none of it -- so it lives in
materials.json where somebody can look at a texture and say so. Nothing here could ever derive it, which is
why in practice the channel was 255 in every texel ever written and the shader line reading it was
`max(metallic, 0)`. The stored bytes for the ordinary case are unchanged, so no pack on disk needs rewriting.

**Z is not stored.** A unit vector's third component is not independent information, and the shader rebuilds
it. Writing a conventional blue-dominant normal map here would put the normal's Z where roughness belongs.

**Green is +Y.** PBRify's models emit the other convention -- their own chain names the output `_normal_dx`
-- and a normal map read under the wrong handedness lights every bump from the opposite side. That reads as
the surface being inverted rather than as an error, which is exactly why it ships. `pbr.py` flips it; this
file assumes the flip already happened.

**Channel order is red-first here and blue-first in C#.** `Bgra32`'s constructor takes its channels in
MEMORY order, so its first positional argument is blue, while a sampler and every function here calls the
first channel red. Writing that positionally once put roughness where the normal's x belonged, and the
result still looked like a plausible picture -- the worst way for it to be wrong. The C# side defends
against it with named arguments; this side is red-first throughout because PNG is, and
`tests/OpenIGI.Rendering.Tests` compares the two byte for byte so neither can drift.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

from . import images

__all__ = ["detail", "write_detail", "normal_image", "orm_image", "roughness_display"]


def detail(
    normal_x: np.ndarray,
    normal_y: np.ndarray,
    roughness: np.ndarray,
    occlusion: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    """Packs the four maps into the (rgb, alpha) pair the shader samples.

    Mirrors `MaterialMapGenerator.PackDetail`. Occlusion is a map rather than the scalar metallic that used
    to live in this channel, and that is the point of the change: metallic could never be derived from a
    diffuse texture -- no photograph distinguishes painted steel from grey paint -- so it was always zero
    and the channel was always 255, while a real horizon search was being computed and thrown away.
    """
    rgb = np.stack(
        [
            (normal_x * 0.5) + 0.5,
            (normal_y * 0.5) + 0.5,
            roughness * 0.5,
        ],
        axis=-1,
    ).astype(np.float32)

    return rgb, np.broadcast_to(occlusion, normal_x.shape).astype(np.float32)


def write_detail(
    path: Path,
    normal_x: np.ndarray,
    normal_y: np.ndarray,
    roughness: np.ndarray,
    occlusion: np.ndarray,
) -> None:
    """Writes the detail map where `MaterialDetailPack` will find it.

    The pack is a flat directory and the FILENAME IS THE KEY -- there is no manifest, and
    `MaterialDetailPack.FindMapPath` is a plain `Path.Combine(root, name + ".png")`. So the caller must pass
    the texture's own name, exactly as `ResExtract images` wrote it.
    """
    rgb, alpha = detail(normal_x, normal_y, roughness, occlusion)
    images.write_rgba(path, rgb, alpha)


def normal_image(normal_x: np.ndarray, normal_y: np.ndarray) -> np.ndarray:
    """The normal as the usual blue-dominant tangent-space image, for looking at.

    Not what the renderer reads -- `detail` is. This exists because a packed texture is unreadable at a
    glance, and a wrong normal map has to be judged by eye. OpenGL convention: green is +Y.
    """
    z = np.sqrt(np.maximum(1.0 - (normal_x * normal_x) - (normal_y * normal_y), 0.0))

    return np.stack(
        [(normal_x * 0.5) + 0.5, (normal_y * 0.5) + 0.5, (z * 0.5) + 0.5],
        axis=-1,
    ).astype(np.float32)


def orm_image(
    occlusion: np.ndarray, roughness: np.ndarray, metallic: float = 0.0
) -> np.ndarray:
    """Occlusion, roughness and metallic in the industry's usual three channels, for interchange.

    Mirrors `MaterialMapGenerator.PackOcclusionRoughnessMetallic`. Also not what the renderer reads: it is
    written so a texture somebody wants to correct by hand can be taken out, edited in any tool in the
    world, and put back.
    """
    return np.stack(
        [
            occlusion,
            roughness * 0.5,
            np.full(occlusion.shape, float(np.clip(metallic, 0.0, 1.0)), dtype=np.float32),
        ],
        axis=-1,
    ).astype(np.float32)


def roughness_display(roughness: np.ndarray) -> np.ndarray:
    """Roughness as a greyscale image.

    Halved, as the packed channel is: roughness here is a multiplier that runs past one and an image cannot
    show that. Mid-grey means "leave the material alone".
    """
    return (roughness * 0.5).astype(np.float32)

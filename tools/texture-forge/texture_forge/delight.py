"""Takes the painted lighting out of a texture, as far as anything can.

THE PROBLEM, AND WHY THERE IS NO MODEL FOR IT.

A physically-based renderer wants ALBEDO: what fraction of each wavelength a surface reflects, with no
lighting in it at all. I.G.I.'s textures are not that. They were painted in 1998 for a renderer that
multiplied a texture by a lightmap and stopped, so the artists painted the light in -- the shadow under the
window ledge, the gradient down the wall, the highlight along the pipe. Relighting them double-shades: the
engine adds its own shadow on top of the one already in the picture, and the result reads as flat and
plastic however good the normal map is.

Removing it is the single highest-leverage step in this whole pipeline, and there is nothing to do it with.
An exhaustive sweep of OpenModelDB's 671 models found no delighting model at any licence. PBRify's own notes
say they trained one and it "does not perform well enough to be worthwhile" -- and that they disabled height
generation by default because of the same problem. compphoto/Intrinsic, the best published work, is
academic-use-only with a patent warning attached.

So this is classical, and it is OFF BY DEFAULT. What it does is a homomorphic separation: painted lighting
is overwhelmingly LOW frequency and multiplicative, so taking the log turns the multiply into a sum, a wide
blur isolates the low-frequency part, subtracting it removes the light, and exponentiating returns a
reflectance. That is a real and old technique and it works well on a wall with a gradient down it. It
CANNOT tell a painted shadow from a genuinely dark material -- a black stripe on white concrete is
low-frequency too -- so it will also flatten art that was deliberately painted dark.

Which is why `texture-forge sheet` exists: judge it per texture class, on your own art, before switching it
on for a whole run.
"""

from __future__ import annotations

import numpy as np

from .classical import _box_blur

__all__ = ["delight"]


def delight(rgb: np.ndarray, *, strength: float = 0.7, radius: float = 0.25) -> np.ndarray:
    """Removes the low-frequency lighting from a texture and returns an approximate albedo.

    `strength` is how much of the estimated illumination is taken out, from 0 (nothing) to 1 (all of it).
    Full removal is almost never right: some of what the blur calls illumination really is the material
    changing colour across the texture, and taking all of it leaves a wall with no variation at all.

    `radius` is the blur, as a fraction of the shorter side. Wide, because painted light is broad -- the
    gradient down a wall spans the whole texture. Narrow enough and this stops being a delight and becomes
    a local-contrast filter, which is a different and less useful thing.
    """
    strength = float(np.clip(strength, 0.0, 1.0))

    if strength <= 0.0:
        return rgb

    height, width = rgb.shape[:2]
    blur_radius = max(int(round(min(width, height) * float(np.clip(radius, 0.05, 1.0)))), 1)

    # The epsilon keeps log finite on a texel that is exactly black, of which these textures have plenty --
    # every hole in a cut-out is one.
    epsilon = 1.0 / 512.0
    luminance = (0.299 * rgb[..., 0]) + (0.587 * rgb[..., 1]) + (0.114 * rgb[..., 2])
    log_luminance = np.log(luminance.astype(np.float32) + epsilon)

    # The illumination estimate. Blurred in LOG space, which is what makes the separation multiplicative:
    # light times reflectance becomes log-light plus log-reflectance, and a blur that would smear a shadow's
    # edge into its surroundings in linear space instead recovers the shadow's own level.
    illumination = _box_blur(log_luminance, blur_radius, 3)

    # Referenced to the texture's own mean rather than to zero, so the result keeps roughly the brightness
    # it had. Dividing by the raw illumination instead would drive every texture towards white, which is
    # technically an albedo and visually useless -- the whole level would read as chalk.
    correction = np.exp(-(illumination - float(illumination.mean())) * strength)

    # Applied per channel, so a colour cast in the painted light comes out with it. The clip matters: a
    # texel that was dark because it was shadowed can legitimately correct past one, and letting it do so
    # would blow the highlight out on the next stage.
    return np.clip(rgb * correction[..., None], 0.0, 1.0).astype(np.float32)

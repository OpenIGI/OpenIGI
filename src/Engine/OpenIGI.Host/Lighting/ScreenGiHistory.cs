using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Lighting;

/// <summary>
/// What the screen-space GI keeps from one frame to the next: its probe and resolve atlases, their ping-pong cursors,
/// the cameras they were accumulated under, and what would make their history worthless.
/// </summary>
/// <remarks>Held for as long as the frame loop runs; a cut, a zoom or a new surface cache throws the histories away.</remarks>
internal sealed class ScreenGiHistory
{
    // The live screen probe gather's atlases, sized to the frame divided by the probe step and reused
    // across frames. Ping-ponged: each frame reads one pair and writes the other, so the accumulation
    // reprojects onto its own past. Accum holds the accumulated irradiance the deferred lighting reads;
    // Position holds the world position each probe belongs to, for the reprojection's disocclusion test.
    // Grown when the window changes, which drops the history with it.
    public TextureHandle[] Accum { get; } = [default, default];
    public TextureHandle[] Position { get; } = [default, default];
    public TextureHandle Filtered { get; set; }

    // The gather's raw per-probe estimates, traced by its first pass and pooled by its second.
    public TextureHandle Raw { get; set; }

    // The one-ray-a-pixel gather DLSS ray reconstruction denoises in all of the above's place, at the
    // frame's size, and the count its white noise is keyed on.
    public TextureHandle Pixels { get; set; }
    public int PixelsWidth { get; set; }
    public int PixelsHeight { get; set; }
    public uint PixelFrame { get; set; }
    public int IndirectWidth { get; set; }
    public int IndirectHeight { get; set; }
    public int Frame { get; set; }
    public bool HasHistory { get; set; }
    public Matrix3f PrevWorldToView { get; set; } = Matrix3f.Identity;
    public Vector3f PrevCameraToCache { get; set; }
    public DepthUnprojection? PrevUnprojection { get; set; }

    // The full-resolution resolve runs EVERY frame even where the gather is amortised, so it keeps its
    // own ping-pong cursor and its own previous camera. Sharing the gather's was the trap: on a frame
    // the gather skipped, the resolve would have reprojected against a camera two frames old and read
    // back the slot it was about to write.
    public int ResolveFrame { get; set; }
    public Matrix3f ResolvePrevWorldToView { get; set; } = Matrix3f.Identity;
    public Vector3f ResolvePrevCameraToCache { get; set; }
    public DepthUnprojection? ResolvePrevUnprojection { get; set; }

    // Which accumulation slot the last gather wrote, and the atlas the resolve should read. On a frame
    // the gather skips, both simply stand.
    public int LastWrite { get; set; }
    public int CadenceCursor { get; set; }

    // The full-resolution temporal resolve's atlases: the sparse probe atlas upsampled and accumulated
    // per pixel so the indirect is pinned to the world rather than sliding with the sparse reprojection.
    // Ping-ponged like the probe atlases, sized to the full render, and read directly by the deferred fill.
    public TextureHandle[] Resolve { get; } = [default, default];
    public TextureHandle[] ResolvePosition { get; } = [default, default];
    public int ResolveWidth { get; set; }
    public int ResolveHeight { get; set; }
    public bool ResolveHasHistory { get; set; }

    // What the screen GI's histories were accumulated under, so a cut, a zoom or a new surface cache
    // can throw them away. See where the gather begins.
    public bool ContinuityValid { get; set; }
    public Vector3d LastEye { get; set; }
    public DepthUnprojection LastUnprojection { get; set; }
    public SurfaceCacheLighting? LastCards { get; set; }
}

using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for surfaces: the derived material maps, overrides, cut-outs and texture filtering.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class MaterialCVars
{
    /// <summary>How strongly the derived surface-detail maps are applied, or 0 to ignore them.</summary>
    /// <remarks>
    /// <para>
    /// I.G.I. ships a diffuse texture per surface and nothing else — no normal map, no roughness map.
    /// A physically-based shading model has four inputs and the data answers one, so the rest are
    /// derived from the diffuse itself and registered per texture. This is how far to trust that.
    /// </para>
    /// <para>
    /// <b>Invented data, and a deliberate divergence.</b> It is off under vanilla and enhanced, because
    /// those presets exist to reproduce and to gently improve what shipped rather than to add detail
    /// nobody painted. It is what makes brick read as brick when the sun crosses it, which is a larger
    /// visible gap between this renderer and a modern one than any amount of indirect lighting.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> MaterialDetail = Presets.Define(
        CVar.Float(
            "r_material_detail",
            0f,
            0f,

            // Two, not the twenty this used to allow. The strength blends the surface normal towards the
            // mapped one, so one is the map exactly and anything above it is extrapolation past what the
            // generator derived -- useful for a mild map on a flat wall, meaningless past a doubling. The
            // old ceiling was inherited from the arrangement where the strength scaled a slope, where the
            // number had no upper meaning at all because the maths silently degenerated instead of
            // saturating; anyone who found their way to 3 there got a wall of normals lying flat along
            // the surface.
            2f,
            CVarOptions.Archive,
            "How strongly derived normal and roughness maps are applied; 0 ignores them, 1 is the map."),
        vanilla: "0",
        enhanced: "0",

        // Deliberately mild. The maps are DERIVED from tiny diffuse textures (8x8 to 128x128), and at full
        // strength their Scharr normals turn every surface into grainy, sparkling relief that catches the
        // sun, the local lights and the screen-space reflections -- the "everything is reflective and
        // blurry" fault. Measured on mission 1's interiors: relief still reads at a quarter, and the noise
        // only sets in past a third. Full strength was calibrated on one brick wall in isolation and does
        // not survive a whole room. The generator itself (MaterialMapGenerator) wants gentler normals; until
        // then this is where the strength is held.
        high: "0.25",

        // Off at ultra. The ultra look renders the level's own diffuse textures flat, with no derived normal
        // or roughness, so the derived relief -- however mild -- reads as a departure from it: a faint
        // sparkle on surfaces meant to be matte. With the relief gone the low-frequency lighting the
        // radiance cache carries is what the eye reads instead. High keeps a quarter, since it is not held
        // to that look.
        ultra: "0");

    /// <summary>How an up-facing surface's (a floor's, a desk top's) roughness differs from a wall's.</summary>
    /// <remarks>
    /// <para>
    /// A multiplier on an up-facing surface's roughness, keyed off how far the surface faces the world up.
    /// One is off, every surface as its material said. Below one smooths floors towards a sheen so they take
    /// a reflection while the walls stay matte; above one ROUGHENS them, so they shine less than the walls.
    /// Only the dynamic model reads it.
    /// </para>
    /// <para>
    /// The game ships one roughness for every surface, and a building's floor at the default 0.6 read as the
    /// shiniest thing in the room even with this off: a floor is the one surface nearly always seen at a
    /// grazing angle, where a dielectric's Fresnel term climbs towards all of the light, so the same lamp and
    /// ambient highlight a wall shows faintly lies across a floor as a polished sheen. It also sits exactly
    /// on the traced reflection's roughness ceiling (r_rt_reflection_roughness 0.6), so anything that took a
    /// texel a little smoother handed it a reflection at the 1.7 intensity.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> FloorGloss = Presets.Define(
        CVar.Float(
            "r_floor_gloss",
            1f,
            0.05f,
            2f,
            CVarOptions.Archive,
            "Roughness multiplier for up-facing surfaces: below 1 floors gloss, above 1 they go matte. 1 is off."),
        vanilla: "1",

        // A quarter rougher: a default floor at 0.75 rather than 0.6. Enough to take the polished sheen off
        // the grazing view of a building's floor and lift it clear of the traced reflection's ceiling, while
        // the floor keeps a soft lamp highlight -- concrete and lino are not chalk. Ground is untouched either
        // way: the geometry pass writes it at full roughness with no specular after this runs.
        //
        // Never below one at these presets. The screen-space reflection a glossy floor takes cannot describe
        // a thin object -- a chair's legs, a desk edge -- and smeared them across the floor as hard dark
        // streaks, worse than the matte floor it replaced.
        enhanced: "1.25",
        high: "1.25",
        ultra: "1.25");

    /// <summary>Whether the overrides folder is drawn, for comparing against the game's own assets.</summary>
    /// <remarks>
    /// <para>
    /// The successor to <c>r_hd</c>, which switched an upscaled-texture pack in and out of the frame.
    /// That pack no longer exists: replacement art goes in the installation's <c>overrides</c> folder and
    /// arrives through the ordinary resource path, which is how every other asset arrives and is why it
    /// needs no setting of its own to point at it.
    /// </para>
    /// <para>
    /// The comparison it made is worth keeping, though, and it is worth more now — the folder can replace
    /// anything, not only a texture. Off bypasses every override without deleting a file, so the two
    /// pictures are one keypress apart. Not saved: it is a switch for looking at something, and a player
    /// who put files in the folder meant them to be used.
    /// </para>
    /// <para>
    /// Toggling costs a reload of the level's textures, because what is already uploaded was decoded from
    /// the bytes this changes.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar OverridesEnabled = CVar.Bool(
        "r_overrides",
        true,
        CVarOptions.None,
        "Whether the overrides folder is used; off shows the game's own assets. Not saved.");

    /// <summary>Whether the derived surface maps are applied, for comparing against flat shading.</summary>
    /// <remarks>
    /// <para>
    /// The F9 half of the same pair, and not archived for the same reason. <see cref="MaterialDetail"/> is
    /// the authored strength and is a quality level the presets own; this multiplies it by one or zero so
    /// the normal, the roughness and the metal can be taken out of the frame and put back without
    /// disturbing what the player chose or what the preset set.
    /// </para>
    /// <para>
    /// Toggling <c>r_material_detail</c> directly would do the same thing visually and would write the
    /// player's setting to zero on the way past, which is exactly the fault this arrangement exists to
    /// avoid.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar MaterialDetailEnabled = CVar.Bool(
        "r_pbr",
        true,
        CVarOptions.None,
        "Whether derived normal/roughness/metal maps are applied. Not saved.");

    /// <summary>Whether cut-outs use a temporally-resolved hashed alpha test rather than a hard one.</summary>
    /// <remarks>
    /// A hard cut-out keeps or drops whole pixels, so a chain-link fence or a wire thinner than a pixel
    /// aliases into a shimmer. The hashed test recovers the edge's coverage from the alpha gradient and
    /// dithers it, which the temporal resolve then averages into a smooth edge. It therefore only helps
    /// where the temporal filter runs, so it follows the same presets: off where <see cref="ReconstructionCVars.TemporalFeedback"/>
    /// is one, on above it.
    /// </remarks>
    /// <para>
    /// <b>Off at every preset now, on two independent grounds.</b> The target look has no dithered opacity
    /// mask, so the look says off. And measured here it costs more than it buys: still-camera stability
    /// goes from 0.390% to 0.483% consecutive-frame RMSE and detail from 45.9 to 44.5, because the dither
    /// is noise until a temporal filter resolves it and ours does not fully resolve it even with the camera
    /// stopped. It buys a slightly better yard whole-frame figure (12.76% to 12.48%) and that is not worth
    /// a quarter more shimmer on the exact geometry the shimmer was complained about.
    /// </para>
    /// <para>
    /// It is implemented rather than removed: the value used to be one here and the Vulkan backend
    /// silently ignored it, which is the failure mode the settings sweep exists to catch. It now does
    /// what it says, and says off.
    /// </para>
    public static readonly BoolCVar HashedAlpha = Presets.Define(
        CVar.Bool(
            "r_alpha_hash",
            false,
            CVarOptions.Archive,
            "Dither cut-out edges and let the temporal resolve average them, instead of a hard alpha test."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>The anisotropic filtering ratio, 1 for none.</summary>
    /// <remarks>
    /// The single cheapest visible win in this renderer. A ground plane at a grazing angle is the
    /// worst case trilinear filtering has: a pixel's footprint is a long thin sliver, the mip level
    /// has to be chosen for its widest axis, and all the detail along the other axis is discarded.
    /// That is why terrain smears towards the horizon without it.
    /// </remarks>
    public static readonly NumericCVar<int> Anisotropy = Presets.Define(
        CVar.Int(
            "r_anisotropy",
            8,
            1,
            16,
            CVarOptions.Archive,
            "Anisotropic filtering ratio; 1 is off. Mostly decides how sharp the ground stays into the distance."),
        vanilla: "1",
        enhanced: "4",
        high: "8",
        ultra: "16");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}

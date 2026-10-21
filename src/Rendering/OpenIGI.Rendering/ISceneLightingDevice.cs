using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>What the deferred lighting lights the scene with: the sun, the lamps, the flashlight, and the indirect light the surface cache and the probe fields carry.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ISceneLightingDevice
{
    /// <summary>Hands the backend the surface cache, so a traced ray can be turned into light.</summary>
    /// <param name="binding">
    /// The cards, the grid and the lighting atlas, or <see cref="SurfaceCacheBinding.None"/> while the
    /// level has no cache — during loading, or on a mission whose cards failed to build.
    /// </param>
    /// <remarks>
    /// Called every frame rather than once per mission, because the camera offset in it moves. Everything
    /// else is a property of the level and is simply repeated; the backend stores the struct and resolves
    /// nothing until a pass that traces actually runs.
    /// </remarks>
    void SetSurfaceCache(in SurfaceCacheBinding binding);

    /// <summary>Hands over the level's distance field, for the passes that march it.</summary>
    /// <param name="binding">The clipmaps and their boxes, or <see cref="DistanceFieldBinding.None"/>.</param>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="SetSurfaceCache"/>, and handed over the same way and at the same
    /// point: every frame, because the boxes are camera-relative and move with the camera, with the
    /// backend storing the struct and resolving nothing until a pass that marches actually runs.
    /// </para>
    /// <para>
    /// This exists so a reflection can find a hit on an adapter that cannot trace. The field is the only
    /// description of the world a shader can walk without an acceleration structure, and it lived
    /// entirely on the game's side until now — which is why glass without ray tracing reflected the sky
    /// and nothing else. A no-op by default: a backend that marches nothing has nowhere to put it.
    /// </para>
    /// </remarks>
    void SetDistanceField(in DistanceFieldBinding binding)
    {
    }

    /// <summary>Copies the sky's irradiance harmonics into <paramref name="coefficients"/>.</summary>
    /// <param name="coefficients">Room for 28 floats: seven vectors of four.</param>
    /// <returns><see langword="true"/> when the backend has an atmosphere to report.</returns>
    /// <remarks>
    /// The seven coefficient vectors of the deferred sky term — third-order harmonics of the sky's light,
    /// cosine-convolved with the Lambert one-over-pi folded in, exactly as the lighting pass consumes
    /// them. A game-layer pass that traces its own rays evaluates these along each escaping ray so its
    /// sky matches the sky the lighting shows, one frame behind (the values are read back from the
    /// device). A backend with no atmosphere tables returns <see langword="false"/> and the caller keeps
    /// whatever flat approximation it has.
    /// </remarks>
    bool TryGetSkyIrradiance(Span<float> coefficients);

    /// <summary>Feeds a screen probe gather's indirect light into the deferred lighting.</summary>
    /// <param name="indirect">The probe atlas of indirect irradiance, covering the frame.</param>
    /// <param name="strength">How strongly to apply it. Zero, or an invalid handle, is a no-op.</param>
    /// <param name="fullResolution">
    /// When set, the handle is a full-resolution atlas the temporal resolve already upsampled, read straight
    /// per pixel; when clear it is the sparse probe atlas the deferred fill upsamples itself.
    /// </param>
    /// <remarks>
    /// Set while the G-buffer is live, before the geometry pass closes: the deferred lighting reads it
    /// as its diffuse fill in place of the flat ambient, where the surface's own albedo and the light
    /// scale are already applied -- so this is not a post-pass over the lit frame but part of the shade
    /// itself, in the renderer's <c>albedo * E</c> convention with metals excluded. A no-op when the
    /// target is not deferred or the strength is zero, so a preset that leaves it off pays nothing.
    /// </remarks>
    void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false);

    /// <summary>Sets the level's real-time local lights for the next deferred lighting pass.</summary>
    /// <param name="lights">
    /// The lights, in VIEW space; a set longer than <see cref="DeferredLocalLight.Maximum"/> is
    /// truncated. View space because the level sits tens of kilometres from the world origin, where a
    /// float has metres of resolution left: the camera-relative subtract has to happen in double
    /// precision on the game's side, and by the time it arrives here it has already happened.
    /// </param>
    /// <remarks>
    /// <para>
    /// Held rather than acted on, like <see cref="SetScreenIndirect"/>: this records what the next
    /// lighting pass folds in, and the pass clears it after, so a frame that sets none lights from none.
    /// Called each frame while the G-buffer is live, before the geometry pass closes.
    /// </para>
    /// <para>
    /// These are the lamps a level implies and never declares — see the game layer's lamp detection.
    /// They are what lights a windowless interior: there is no daylight to gather in a sealed room, so
    /// no amount of exposure or bounce can find light that has no source, and this is the source.
    /// </para>
    /// </remarks>
    void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights);

    /// <summary>Hands over the level's distance field, for a renderer that shadows its lamps without tracing.</summary>
    /// <param name="field">The field, or <see langword="null"/> while there is none.</param>
    /// <remarks>
    /// A no-op by default: a renderer that traces its lamp shadows never reads it. See
    /// <see cref="LampOcclusionField"/>. Called every frame, so a field rebuilt or dropped is followed.
    /// </remarks>
    void SetLampOcclusionField(LampOcclusionField? field)
    {
    }

    /// <summary>Sets the player's flashlight for the next deferred lighting pass.</summary>
    /// <param name="light">The flashlight, in VIEW space as the lamps are, or <see cref="FlashlightLight.Off"/>.</param>
    /// <remarks>
    /// A no-op by default: a renderer with no deferred lighting -- the original's direct path, which vanilla
    /// draws -- has nothing to light it with. Held like <see cref="SetLocalLights"/> and cleared as the
    /// capture ends, so it is called every frame the light is on.
    /// </remarks>
    void SetFlashlight(in FlashlightLight light)
    {
    }

    /// <summary>Hands over the level's baked probe field, or clears it.</summary>
    /// <param name="volume">The baked volume, or <see langword="null"/> to forget the one held.</param>
    /// <remarks>
    /// Separate from <see cref="SetIrradianceVolumeSettings"/> and called only when the level changes:
    /// the volume is megabytes of baked transfer that does not vary per frame, while the colours
    /// applied to it do.
    /// </remarks>
    void SetIrradianceVolume(IrradianceVolume? volume);

    /// <summary>Binds a real-time radiance-cache volume in place of the baked field, or leaves the last one.</summary>
    /// <param name="red">The red-channel L1-SH volume, four coefficients per texel.</param>
    /// <param name="green">The green-channel volume.</param>
    /// <param name="blue">The blue-channel volume.</param>
    /// <param name="distance">The distance-moment volume, for the Chebyshev leak test.</param>
    /// <param name="origin">The world position of probe (0, 0, 0), in metres.</param>
    /// <param name="spacing">The distance between neighbouring probes, in metres.</param>
    /// <param name="countX">How many probes span X.</param>
    /// <param name="countY">How many probes span Y.</param>
    /// <param name="countZ">How many probes span Z.</param>
    /// <remarks>
    /// The volumes are owned by the pass that writes them each frame, so the device only references them --
    /// it never allocates or frees them, unlike the baked field it uploads. When bound they take precedence
    /// over the baked volume at the same sampler: the layout is identical, but the radiance is live and
    /// already coloured, so the device applies no sky tint to it. Call
    /// <see cref="ClearRealtimeProbeVolume"/> to fall back to the baked field.
    /// </remarks>
    void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ);

    /// <summary>Stops sampling the real-time radiance cache, falling back to the baked field.</summary>
    void ClearRealtimeProbeVolume();

    /// <summary>Sets how this frame lights itself from the probe field.</summary>
    /// <param name="settings">
    /// The application, or <see cref="IrradianceVolumeSettings.Off"/> for none. With none, or with no
    /// volume handed over, surfaces fall back to the flat ambient term.
    /// </param>
    void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings);

    /// <summary>Sets how much of the sky each surface can see, and what its neighbours bounce onto it.</summary>
    /// <param name="settings">
    /// The search, or <see cref="AmbientOcclusionSettings.Off"/> for none. With none, the lighting pass
    /// uses full visibility and no bounce, which is the flat ambient the stage before this one had.
    /// </param>
    /// <remarks>
    /// Applied to the ambient and bounced terms only, never to the sun. Occluding direct light is what
    /// shadow maps are for, and doing it here as well would darken the same geometry twice — visibly so
    /// in a corner that is both shadowed and enclosed, which is most corners.
    /// </remarks>
    void SetAmbientOcclusion(in AmbientOcclusionSettings settings);

    /// <summary>Sets the light the scene is lit by for this frame.</summary>
    /// <param name="lighting">The lighting model, sun and ambient.</param>
    void SetLighting(in SceneLighting lighting);
}

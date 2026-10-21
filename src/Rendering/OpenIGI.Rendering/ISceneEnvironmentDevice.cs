using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>What the frame is seen through and reflects: the participating medium, the wet ground, the reflections, the colour grade and the debug view.</summary>
/// <remarks>Told once a frame, or once a mission, and read by the passes that shade it. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ISceneEnvironmentDevice
{
    /// <summary>Sets the participating medium the scene is seen through.</summary>
    /// <param name="fog">The medium, or <see cref="VolumetricFogSettings.Off"/> for none.</param>
    /// <param name="cameraPosition">
    /// The camera's world position in metres. The march works in camera-relative world space, so the
    /// medium's base height and every volume's centre are rebased against this; passing it explicitly
    /// is clearer than having the shell pre-subtract it and hand over coordinates that are no longer
    /// world coordinates.
    /// </param>
    /// <param name="volumes">
    /// Regions where the medium differs from the level's base fog — rooms, basements, smoke. Retail
    /// has no such entities, so these are authored for the port.
    /// </param>
    /// <remarks>
    /// Applied after lighting and before the forward pass, so translucent surfaces and the view model
    /// sit in front of the fog rather than being fogged twice.
    /// </remarks>
    void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes);

    /// <summary>Sets how wet the world is, which the lighting pass applies to every surface it shades.</summary>
    /// <param name="wetness">The state of the ground, or <see cref="WetnessSettings.Dry"/> for none.</param>
    /// <remarks>
    /// <para>
    /// Applied inside the deferred lighting pass, between unpacking the G-buffer and shading it: the
    /// albedo is darkened, the roughness collapsed and the normal replaced by the water's, so the sun,
    /// the lamps, the sky and the bounce all light the wet surface rather than the dry one underneath.
    /// See <see cref="WetnessSettings"/> for why it cannot be done in the G-buffer instead.
    /// </para>
    /// <para>
    /// A backend that has not implemented it does nothing, which is why this has a body: adding standing
    /// water to one renderer should not stop the other one compiling, and a device that ignores it says
    /// so through <see cref="GraphicsCapabilities.UnimplementedSettings"/> where the shell can report it.
    /// </para>
    /// </remarks>
    void SetWetness(in WetnessSettings wetness);

    /// <summary>Sets the top-down map of the level's roofs, which decides what the rain reaches.</summary>
    /// <param name="map">
    /// The raster, packed as <see cref="ShelterMapTransform"/> describes, or
    /// <see cref="TextureHandle.None"/> for a level with no map.
    /// </param>
    /// <param name="transform">
    /// How to read it, or <see cref="ShelterMapTransform.None"/> when nothing is sheltered. Set once
    /// when a mission loads: the map is a property of the level rather than of the frame.
    /// </param>
    /// <remarks>
    /// Ground under a roof stays dry however hard it is raining outside — without this the floor of
    /// every building in the level pools with water while its own roof keeps the rain off it. Both the
    /// shading and the reflection march read it, and they must read the same one: a reflection that
    /// disagreed with the shading about where the water is would land beside it.
    /// </remarks>
    void SetShelterMap(TextureHandle map, in ShelterMapTransform transform);

    /// <summary>Sets what surfaces reflect and how far the frame is searched to find it.</summary>
    /// <param name="settings">The search, or <see cref="ReflectionSettings.Off"/> for none.</param>
    /// <remarks>
    /// Applied after the lighting and before the fog: a reflection is of the lit world, and it belongs
    /// behind the air rather than in front of it.
    /// </remarks>
    void SetReflections(in ReflectionSettings settings);

    /// <summary>Hands over the authored colour grade, or clears it.</summary>
    /// <param name="table">The table, or <see langword="null"/> to forget the one held.</param>
    /// <remarks>
    /// Separate from <see cref="PostProcessSettings.ColourLookup"/>, and called only when the grade
    /// changes — which in practice is when the mission does. The table is a volume texture upload; how
    /// much of it to apply is a number per frame.
    /// </remarks>
    void SetColourLookup(ColourLookupTable? table);

    /// <summary>Hands over the cube map reflective draws sample, or clears it.</summary>
    /// <param name="cube">The cube, or <see cref="TextureHandle.None"/> for none.</param>
    /// <remarks>
    /// <para>
    /// Device state rather than a texture unit, and that is a fact about this seam rather than about
    /// cube maps: a 3D draw here takes its diffuse and its lightmap as <see cref="IMeshDrawDevice.DrawMesh"/> arguments,
    /// and the unit array <see cref="ITextureDevice.SetTexture"/> writes is read by the compute path alone. A
    /// reflection belongs with <see cref="SetShelterMap"/> and <see cref="SetReflections"/> — something
    /// the frame is told once and a run of draws then uses.
    /// </para>
    /// <para>
    /// A device with no cube reflects nothing; so does a draw made while none is set. That is the
    /// state every non-reflective draw is in, which is nearly all of them.
    /// </para>
    /// </remarks>
    void SetEnvironmentCube(TextureHandle cube);

    /// <summary>Sets which buffer the frame shows instead of the scene.</summary>
    /// <param name="view">The buffer, or <see cref="DebugView.None"/> for the scene itself.</param>
    /// <remarks>
    /// Its own member rather than a field of <see cref="PostProcessSettings"/> because the passes that
    /// can answer it are not all the resolve: the probe field's contribution exists only inside the
    /// lighting pass, and by the time the frame resolves it has been multiplied into the picture.
    /// </remarks>
    void SetDebugView(DebugView view);
}

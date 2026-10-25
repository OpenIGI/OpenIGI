using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The shadow maps: the sun's cascades and the lamps' maps, fine and coarse.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IShadowDevice
{
    /// <summary>Gets whether this frame's sun shadow will come from a ray trace rather than the cascades.</summary>
    /// <remarks>
    /// When it will, the cascaded shadow map is written and never read -- the deferred lighting samples
    /// the traced mask as the only sun shadow -- so the app can skip rasterising the cascades entirely
    /// and the picture cannot change. False by default, and false whenever the trace cannot actually run
    /// this frame (no ray support, no acceleration structure yet), in which case the cascades are the
    /// sun's only shadow and must keep rendering.
    /// </remarks>
    bool SunShadowTraced { get; }

    /// <summary>Sets the sun's shadow map for this frame.</summary>
    /// <param name="settings">How the shadows are drawn, or <see cref="ShadowSettings.Off"/> for none.</param>
    /// <param name="cascades">The fitted cascades, from <see cref="ShadowCascades.Fit"/>.</param>
    /// <param name="unprojection">
    /// What the lighting pass needs to rebuild a view-space position from the depth buffer: the
    /// projection.s x and y scales, then its near and far planes.
    /// </param>
    /// <param name="viewToWorld">
    /// The rotation taking a view-space direction back into <em>camera-relative</em> world space,
    /// which is the frame the cascades are fitted in.
    /// </param>
    void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld);

    /// <summary>Starts rendering depth into one cascade.</summary>
    /// <param name="cascade">The zero-based cascade index.</param>
    /// <returns><see langword="false"/> when there is no shadow map to render into.</returns>
    /// <remarks>
    /// Draws between this and <see cref="EndShadowCascade"/> write depth only; their textures are read
    /// solely for the cut-out test. Called outside a scene capture, before the frame is drawn.
    /// </remarks>
    bool BeginShadowCascade(int cascade);

    /// <summary>Ends the current cascade.</summary>
    void EndShadowCascade();

    /// <summary>Gets how many lamps can hold a shadow map at once.</summary>
    int LampShadowMapSlots => 0;

    /// <summary>Says which lamp map each of this frame's lamps reads.</summary>
    /// <param name="slotPerLight">
    /// One per lamp, in the order <see cref="ISceneLightingDevice.SetLocalLights"/> was given them: the slot, or -1 for none.
    /// </param>
    void SetLampShadowMapSlots(ReadOnlySpan<int> slotPerLight)
    {
        _ = slotPerLight;
    }

    /// <summary>Says which coarse cube of maps each of this frame's lamps has, for the lamps the fine maps miss.</summary>
    /// <param name="firstSlotPerLight">
    /// One per lamp, in the order <see cref="ISceneLightingDevice.SetLocalLights"/> was given them: the first of the six coarse
    /// slots (<see cref="BeginLampBounceMap"/>) once all six are drawn, or -1 for none.
    /// </param>
    void SetLampCoarseMapSlots(ReadOnlySpan<int> firstSlotPerLight)
    {
        _ = firstSlotPerLight;
    }

    /// <summary>Begins drawing the world into one lamp's shadow map, from the lamp.</summary>
    /// <param name="slot">The map, below <see cref="LampShadowMapSlots"/>.</param>
    /// <param name="position">Where the lamp's camera stands, in absolute world metres.</param>
    /// <param name="rotation">The light's rotation: its rows the right, up and forward axes.</param>
    /// <param name="projection">The perspective it looks through.</param>
    /// <returns><see langword="true"/> when the pass is open; end it with <see cref="EndShadowCascade"/>.</returns>
    /// <remarks>Like a cascade: depth only, outside a scene capture, before the frame is drawn.</remarks>
    bool BeginLampShadowMap(int slot, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        _ = slot;
        _ = position;
        _ = rotation;
        _ = projection;
        return false;
    }

    /// <summary>Says that some lamp's output has just changed: switched, flickered, faded.</summary>
    /// <remarks>
    /// The lamp shadows are a ratio averaged over frames, and a switch changes which lamps that ratio stands
    /// for; the history is held short for a moment after, rather than carrying the old answer.
    /// </remarks>
    void NoteLampLevelsChanged()
    {
    }

    /// <summary>Begins drawing the world into one coarse lamp map, from the lamp.</summary>
    /// <param name="slot">The map, below <see cref="IGraphicsDevice.LampBounceMapSlots"/>.</param>
    /// <param name="position">Where the lamp's camera stands, in absolute world metres.</param>
    /// <param name="rotation">The light's rotation: its rows the right, up and forward axes.</param>
    /// <param name="projection">The perspective it looks through.</param>
    /// <returns><see langword="true"/> when the pass is open; end it with <see cref="EndShadowCascade"/>.</returns>
    bool BeginLampBounceMap(int slot, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        _ = slot;
        _ = position;
        _ = rotation;
        _ = projection;
        return false;
    }

    /// <summary>Gets a number that changes whenever a coarse lamp map is drawn or the maps are released.</summary>
    /// <remarks>So a reader of <see cref="WriteLampBounceMaps"/> knows when what it holds is out of date.</remarks>
    int LampBounceMapVersion => 0;

    /// <summary>Writes every coarse lamp map's projection, measured from an origin of the caller's choosing.</summary>
    /// <param name="origin">The absolute world position the caller's coordinates are relative to.</param>
    /// <param name="destination">
    /// <see cref="LampMapRecordFloats"/> floats per slot: the four rows of the map's origin-relative world to
    /// clip matrix, then its projection's two depth terms (ndc depth = first + second / view depth), whether
    /// the map has been drawn (1 or 0), and a spare nought.
    /// </param>
    void WriteLampBounceMaps(Vector3d origin, Span<float> destination)
    {
        _ = origin;
        destination.Clear();
    }

    /// <summary>The floats <see cref="WriteLampBounceMaps"/> writes for each slot.</summary>
    const int LampMapRecordFloats = 20;

    /// <summary>The most coarse lamp maps a device holds: six faces each for a hundred and seventy lamps.</summary>
    const int LampBounceMapCapacity = 1020;
}

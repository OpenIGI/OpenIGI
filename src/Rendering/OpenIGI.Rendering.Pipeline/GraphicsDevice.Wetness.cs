namespace OpenIGI.Rendering.Pipeline;

/// <summary>The weather on the ground: how wet the world is, where water stands, and what keeps it off.</summary>
public sealed partial class GraphicsDevice
{
    private WetnessSettings _wetness = WetnessSettings.Dry;

    /// <inheritdoc/>
    /// <remarks>
    /// Held: it is two vectors in the shading's own block and a few in the reflection's, so it arrives with
    /// the frame's parameters and costs no binding and no pass.
    /// </remarks>
    public void SetWetness(in WetnessSettings wetness)
    {
        ThrowIfDisposed();
        _wetness = wetness;
    }

    /// <summary>Whether the level's roofs are known, so water does not stand under one.</summary>
    private bool HasShelterMap() => _shelterTransform.IsPresent && ViewOf(_shelterMap) is not null;

    /// <summary>What the shading is told about the water and the roofs this frame.</summary>
    private DeferredLightingState WithWetness(in DeferredLightingState state)
    {
        bool roofs = HasShelterMap();

        return state with
        {
            Wetness = _wetness,
            HasShelterMap = roofs,
            Shelter = roofs ? _shelterTransform : ShelterMapTransform.None,
        };
    }

    /// <summary>Points the shading at the roofs, or at a stand-in where the level has none.</summary>
    private void BindShelterMap() => _shading?.BindShelterMap(HasShelterMap() ? ViewOf(_shelterMap) : null);
}

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The cube reflective draws would sample, or the null handle.</summary>
    public TextureHandle EnvironmentCube { get; private set; }

    /// <summary>Every cube handed over or cleared, in order.</summary>
    public List<TextureHandle> EnvironmentCubes { get; } = [];

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
        EnvironmentCube = cube;
        EnvironmentCubes.Add(cube);
    }

    /// <summary>The reflection settings each frame was given, in order.</summary>
    public List<ReflectionSettings> Reflections { get; } = [];

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings) => Reflections.Add(settings);

    /// <summary>The colour tables handed over, in order; a null entry is a clear.</summary>
    public List<ColourLookupTable?> ColourLookups { get; } = [];

    /// <inheritdoc/>
    public void SetColourLookup(ColourLookupTable? table) => ColourLookups.Add(table);

    /// <summary>The debug views asked for, in order.</summary>
    public List<DebugView> DebugViews { get; } = [];

    /// <inheritdoc/>
    public void SetDebugView(DebugView view) => DebugViews.Add(view);

    /// <summary>The fog settings each frame was given, in order.</summary>
    public List<VolumetricFogSettings> Fogs { get; } = [];

    /// <summary>The wetness settings applied, in order, with the shelter count each carried.</summary>
    public List<WetnessSettings> Wetness { get; } = [];

    /// <summary>Every shelter map handed over, newest last.</summary>
    public List<(TextureHandle Map, ShelterMapTransform Transform)> ShelterMaps { get; } = [];

    /// <inheritdoc/>
    public void SetWetness(in WetnessSettings wetness) => Wetness.Add(wetness);

    /// <inheritdoc/>
    public void SetShelterMap(TextureHandle map, in ShelterMapTransform transform) =>
        ShelterMaps.Add((map, transform));

    /// <inheritdoc/>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes) => Fogs.Add(fog);
}

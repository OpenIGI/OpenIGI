using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>What the game's GI stack reads from the frame, and what the shading reads back from it.</summary>
/// <remarks>
/// <para>
/// The stack itself — the distance field, the surface cache's lighting, the radiance cache, the screen
/// gather — is the game's, and runs through the compute seam. This is the two ends of it on the device:
/// the frame's own images handed to the gather as texture handles, and what the stack produces bound
/// into the shading with the numbers that say how to read it.
/// </para>
/// <para>
/// The handles to the frame's images are ALIASES rather than textures of the table's own: the scene's
/// targets are remade on a resize and the card atlas whenever the capture wants a new size, while the
/// game keeps one handle for the life of the level. Each is resolved to whatever stands behind it when a
/// dispatch reads it.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>Every module the game's GI stack is built from, in the variants it builds here.</summary>
    /// <remarks>
    /// The software ones, because this device traces no rays. Where any of them has no form for the
    /// backend the stack cannot run at all — today that is WebGPU, where the surface cache's lighting and
    /// its radiance cache are SPIR-V alone — and the device reports no compute rather than a stack that
    /// fails halfway through building itself.
    /// </remarks>
    private static readonly (ShaderModuleId Module, ShaderVariant Variant)[] GiModules =
    [
        (ShaderModuleId.FieldClipmapBuild, default),
        (ShaderModuleId.CardsLighting, new ShaderVariant("software")),
        (ShaderModuleId.CardsRadianceCache, new ShaderVariant("software")),
        (ShaderModuleId.CardsScreenGather, new ShaderVariant("software")),
        (ShaderModuleId.CardsScreenGatherTemporal, new ShaderVariant("software")),
        (ShaderModuleId.CardsScreenResolve, default),
        (ShaderModuleId.CardsScreenFilter, default),
    ];

    private readonly Dictionary<uint, Func<IRhiTextureView?>> _aliases = [];
    private TextureHandle _sceneDepthAlias = TextureHandle.None;
    private TextureHandle _sceneNormalAlias = TextureHandle.None;
    private TextureHandle _sceneColourAlias = TextureHandle.None;
    private TextureHandle _cardAlbedoAlias = TextureHandle.None;
    private TextureHandle _cardNormalAlias = TextureHandle.None;
    private TextureHandle _cardMaterialAlias = TextureHandle.None;
    private TextureHandle _cardDepthAlias = TextureHandle.None;

    private TextureHandle _screenIndirect = TextureHandle.None;
    private float _screenIndirectStrength;
    private bool _screenIndirectFullResolution;
    private RealtimeProbeField? _realtimeProbe;
    private IrradianceVolumeSettings _volumeSettings = IrradianceVolumeSettings.Off;

    /// <summary>Gets whether a device can run the whole of the game's GI stack.</summary>
    /// <param name="device">The device.</param>
    /// <returns><see langword="true"/> when every module of it has the device's form.</returns>
    public static bool GiRunsOn(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return GiModules.All(entry => ShaderCatalog.Compiles(entry.Module, entry.Variant, device.ShaderTarget));
    }

    /// <inheritdoc/>
    public TextureHandle SceneDepth => Alias(ref _sceneDepthAlias, () => _scene?.View(SceneTargetId.Depth));

    /// <inheritdoc/>
    public TextureHandle SceneNormal => Alias(ref _sceneNormalAlias, () => _scene?.View(SceneTargetId.Normal));

    /// <inheritdoc/>
    /// <remarks>The lit image, which at the point the gather reads it still holds the frame before.</remarks>
    public TextureHandle SceneColour => Alias(ref _sceneColourAlias, () => _scene?.View(SceneTargetId.Lit));

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => Alias(ref _cardAlbedoAlias, () => _cardAtlas?.Albedo.View);

    /// <inheritdoc/>
    public TextureHandle CardNormal => Alias(ref _cardNormalAlias, () => _cardAtlas?.Normal.View);

    /// <inheritdoc/>
    public TextureHandle CardMaterial => Alias(ref _cardMaterialAlias, () => _cardAtlas?.Material.View);

    /// <inheritdoc/>
    public TextureHandle CardDepth => Alias(ref _cardDepthAlias, () => _cardAtlas?.Depth.View);

    /// <inheritdoc/>
    public void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false)
    {
        ThrowIfDisposed();
        _screenIndirect = indirect;
        _screenIndirectStrength = strength;
        _screenIndirectFullResolution = fullResolution;
    }

    /// <inheritdoc/>
    public void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ)
    {
        ThrowIfDisposed();

        // A field with any part missing is no field: the shading would read one stand-in among three
        // volumes and light every surface with a third of the answer.
        if (!red.IsValid || !green.IsValid || !blue.IsValid || !distance.IsValid
            || countX <= 0 || countY <= 0 || countZ <= 0 || spacing <= 0f)
        {
            _realtimeProbe = null;
            return;
        }

        _realtimeProbe = new RealtimeProbeField(red, green, blue, distance, origin, spacing, countX, countY, countZ);
    }

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume()
    {
        ThrowIfDisposed();
        _realtimeProbe = null;
    }

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings)
    {
        ThrowIfDisposed();
        _volumeSettings = settings;
    }

    /// <summary>What the shading is told about the GI this frame, over what it is told of everything else.</summary>
    private DeferredLightingState WithGlobalIllumination(in DeferredLightingState state)
    {
        RealtimeProbeField? field = _realtimeProbe;
        bool screen = _screenIndirectStrength > 0f && _screenIndirect.IsValid;

        return state with
        {
            ProbeEnabled = _volumeSettings.Enabled && field is not null,
            ProbeOrigin = field?.Origin ?? default,
            ProbeInverseCount = field is { } live
                ? new Vector3f(1f / live.CountX, 1f / live.CountY, 1f / live.CountZ)
                : default,
            ProbeSpacing = field?.Spacing ?? 0f,
            ProbeBlend = _volumeSettings.Blend,
            SkyColour = _volumeSettings.SkyColour,
            SkyIntensity = MathF.Max(_volumeSettings.Intensity, 0f),
            ScreenGiStrength = screen ? _screenIndirectStrength : 0f,
            ScreenGiFullResolution = _screenIndirectFullResolution,
        };
    }

    /// <summary>Points the shading at what the stack produced this frame, or at stand-ins where it did not.</summary>
    private void BindGlobalIllumination()
    {
        if (_shading is not { } shading)
        {
            return;
        }

        RealtimeProbeField? field = _realtimeProbe;

        shading.BindIndirect(
            _screenIndirectStrength > 0f ? ViewOf(_screenIndirect) : null,
            field is { } live ? ViewOf(live.Red) : null,
            field is { } green ? ViewOf(green.Green) : null,
            field is { } blue ? ViewOf(blue.Blue) : null,
            field is { } distance ? ViewOf(distance.Distance) : null);
    }

    /// <summary>The view a handle stands for: an alias's current target, or a texture of the table's own.</summary>
    private IRhiTextureView? ViewOf(TextureHandle handle)
    {
        if (!handle.IsValid)
        {
            return null;
        }

        if (_aliases.TryGetValue(handle.Id, out Func<IRhiTextureView?>? alias))
        {
            return alias();
        }

        return _textures.Find(handle) is null ? null : _textures.Resolve(handle).View;
    }

    /// <summary>What a compute slot reads for a handle that is an alias, if it is one.</summary>
    private bool TryResolveAlias(TextureHandle handle, out RhiTextureBinding binding)
    {
        if (handle.IsValid
            && _aliases.TryGetValue(handle.Id, out Func<IRhiTextureView?>? alias)
            && alias() is { } view)
        {
            binding = new RhiTextureBinding(handle.Id, view, ComputeSampler());
            return true;
        }

        binding = default;
        return false;
    }

    /// <summary>Takes a handle for an image that is not the table's own, the first time it is asked for.</summary>
    private TextureHandle Alias(ref TextureHandle handle, Func<IRhiTextureView?> resolve)
    {
        if (!handle.IsValid)
        {
            handle = _textures.Reserve();
            _aliases[handle.Id] = resolve;
        }

        return handle;
    }

    /// <summary>The radiance cache as the game handed it over: four volumes and the grid they describe.</summary>
    private readonly record struct RealtimeProbeField(
        TextureHandle Red,
        TextureHandle Green,
        TextureHandle Blue,
        TextureHandle Distance,
        Vector3f Origin,
        float Spacing,
        int CountX,
        int CountY,
        int CountZ);
}

using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The seam the screen-probe gather needs: the G-buffer offered as ordinary textures, and a place in the
/// frame where a compute pass may read them.
/// </summary>
/// <remarks>
/// <para>
/// The gather traces the surface cache along the view to work out how much indirect light reaches each
/// part of the screen, and to do that it needs three things the frame has already produced: how far away
/// each pixel is, which way it faces, and what the scene looked like last frame. On the GL path those are
/// simply the bound framebuffer's textures, readable at any point while it is bound.
/// </para>
/// <para>
/// Here they are render-pass attachments, and a render pass is the one place a dispatch cannot be
/// recorded. So the geometry pass closes when the first such dispatch asks for it — which is exactly the
/// gap the deferred path was split in two to create — and the shading pass opens afterwards as it always
/// did. Nothing is drawn between the gather and the end of the geometry stage, so closing early costs
/// nothing; a draw that arrived after would be a real mistake and says so.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    private readonly Dictionary<uint, SceneTexture> _sceneTextures = [];
    private Sampler _sceneSampler;
    private TextureHandle _sceneDepth = TextureHandle.None;
    private TextureHandle _sceneNormal = TextureHandle.None;
    private TextureHandle _sceneColour = TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle SceneDepth => _sceneDepth;

    /// <inheritdoc/>
    public TextureHandle SceneNormal => _sceneNormal;

    /// <inheritdoc/>
    public TextureHandle SceneColour => _sceneColour;

    /// <inheritdoc/>
    public SceneTextureConvention SceneConvention => SceneTextureConvention.Vulkan;

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the atmosphere's own harmonics, copied down to the host each frame. The slot read here
    /// is the one whose fence the frame loop has already waited on, so the values are a frame or two old
    /// — the sun does not move fast enough for that to show.
    /// </remarks>
    public bool TryGetSkyIrradiance(Span<float> coefficients)
    {
        ThrowIfDisposed();
        return _deferred.Sky.TryReadIrradiance(_frameLoop.CurrentFrame, coefficients);
    }

    /// <summary>One G-buffer attachment offered to compute as a sampled texture.</summary>
    /// <param name="View">The attachment's view.</param>
    /// <param name="Layout">The layout it sits in once its pass has ended.</param>
    private readonly record struct SceneTexture(ImageView View, ImageLayout Layout);

    /// <summary>Points the three scene-texture handles at the current scene target's attachments.</summary>
    /// <remarks>
    /// Called whenever the target is made or remade. The handles themselves are stable across a resize —
    /// the game holds them for the life of the level — so only what they resolve to changes.
    /// </remarks>
    private void RefreshSceneTextures()
    {
        if (_sceneTarget is not { } target)
        {
            _sceneTextures.Clear();
            return;
        }

        EnsureSceneSampler();

        if (!_sceneDepth.IsValid)
        {
            _sceneDepth = new TextureHandle(_nextTextureId++);
            _sceneNormal = new TextureHandle(_nextTextureId++);
            _sceneColour = new TextureHandle(_nextTextureId++);
        }

        // Every one of these ends its pass in the read-only layout, which is where the gather finds them.
        _sceneTextures[_sceneDepth.Id] = new SceneTexture(target.DepthView, ImageLayout.ShaderReadOnlyOptimal);
        _sceneTextures[_sceneNormal.Id] = new SceneTexture(target.NormalView, ImageLayout.ShaderReadOnlyOptimal);

        // The lit colour is the previous frame's: the shading pass that fills it for this frame has not
        // run yet when the gather reads it. That is what the screen-space tier of the trace wants — it is
        // looking for light that was already resolved somewhere on screen.
        _sceneTextures[_sceneColour.Id] = new SceneTexture(target.LitView, ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <summary>Finds a scene attachment by the handle the game holds it as.</summary>
    /// <param name="texture">The handle.</param>
    /// <param name="found">The attachment.</param>
    /// <returns><see langword="true"/> when the handle names a scene attachment.</returns>
    private bool TryResolveSceneTexture(TextureHandle texture, out SceneTexture found)
    {
        if (texture.IsValid && _sceneTextures.TryGetValue(texture.Id, out found))
        {
            return true;
        }

        found = default;
        return false;
    }

    /// <summary>Gets the sampler the scene attachments are read through.</summary>
    private Sampler SceneSampler
    {
        get
        {
            EnsureSceneSampler();
            return _sceneSampler;
        }
    }

    private unsafe void EnsureSceneSampler()
    {
        if (_sceneSampler.Handle != 0)
        {
            return;
        }

        // Linear and clamped. The gather reads at a coarser grid than the frame, so it is interpolating
        // between texels by design, and a read off the edge should repeat the edge rather than wrap to
        // the far side of the screen.
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0f,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(
            _instance.Vk.CreateSampler(_device.Device, &createInfo, null, &sampler),
            "vkCreateSampler (scene textures)");
        _sceneSampler = sampler;
    }

    /// <summary>
    /// Closes the geometry pass early because something wants to dispatch compute over its results.
    /// </summary>
    /// <returns><see langword="true"/> if the pass was open and has now been closed.</returns>
    private bool CloseGeometryForCompute()
    {
        if (!_deferredPassActive || _deferredStage != DeferredStage.Geometry)
        {
            return false;
        }

        _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
        _deferredPassActive = false;

        // The G-buffer pass has ended; close the Geometry range the BeginPass opened before the compute in
        // the gap (the sun-shadow trace) is timed as its own stage.
        EndStageTiming();

        // Everything the backend itself wants done over a finished G-buffer, so the gather that follows
        // sees the same world the shading will.
        RecordBetweenPasses();
        _deferredStage = DeferredStage.Gap;
        return true;
    }

    /// <summary>The structure a tracing compute shader binds: the world's, or an empty stand-in.</summary>
    /// <returns>The structure, or null when this adapter cannot trace at all.</returns>
    /// <remarks>
    /// The world's own is not built until the level has been drawn once and its geometry gathered, and a
    /// descriptor cannot be left unwritten in the meantime. So the frames before it exists trace against a
    /// scene with one triangle placed far outside any level, where every ray misses — which is the answer
    /// an empty world would give anyway.
    /// </remarks>
    private RayTracing.VulkanAccelerationStructure? TraceableScene()
    {
        if (_worldScene is { IsReady: true, TopLevel: { } world })
        {
            return world;
        }

        return _placeholderScene?.TopLevel;
    }

    private unsafe void DisposeSceneTextures()
    {
        if (_sceneSampler.Handle != 0)
        {
            _instance.Vk.DestroySampler(_device.Device, _sceneSampler, null);
            _sceneSampler = default;
        }

        _sceneTextures.Clear();
    }
}

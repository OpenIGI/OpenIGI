using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>What the driver says it is and offers, read once when the device is made.</summary>
/// <param name="Version">The context's version, as major times ten plus minor: 43 for 4.3.</param>
/// <param name="Renderer">The renderer string, which names the adapter.</param>
/// <param name="Vendor">The vendor string.</param>
/// <param name="ClipControl">
/// Whether <c>glClipControl</c> is there (4.5, or <c>GL_ARB_clip_control</c>), which lets the depth range be
/// [0, 1] and the origin the top-left as every other backend has them. Required: without it the shaders would need
/// their clip space rewritten, and depth would lose the precision reverse-Z exists for.
/// </param>
/// <param name="Anisotropy">Whether a sampler may filter anisotropically, and how far.</param>
/// <param name="MaxAnisotropy">The most samples an anisotropic lookup may take.</param>
/// <param name="S3tc">Whether the BC1 formats can be uploaded without decoding.</param>
/// <param name="TimerQuery">Whether timestamps can be taken (<c>GL_ARB_timer_query</c>, core since 3.3).</param>
/// <param name="Debug">Whether the context reports what it thinks is wrong (<c>KHR_debug</c>, core since 4.3).</param>
/// <param name="QueryBuffer">
/// Whether a query's result can be written into a buffer by the GPU (4.4, or <c>GL_ARB_query_buffer_object</c>);
/// without it a resolve waits for each result on the host.
/// </param>
/// <param name="TextureSubImage">
/// Whether part of a texture can be read straight into a buffer (4.5, or <c>GL_ARB_get_texture_sub_image</c>); without
/// it a read goes through a framebuffer.
/// </param>
/// <param name="UniformOffsetAlignment">What a uniform buffer range's offset must be a multiple of.</param>
/// <param name="StorageOffsetAlignment">What a storage buffer range's offset must be a multiple of.</param>
/// <param name="Limits">The sizes the device will not exceed, in the RHI's terms.</param>
internal sealed record GlCapabilities(
    int Version,
    string Renderer,
    string Vendor,
    bool ClipControl,
    bool Anisotropy,
    float MaxAnisotropy,
    bool S3tc,
    bool TimerQuery,
    bool Debug,
    bool QueryBuffer,
    bool TextureSubImage,
    int UniformOffsetAlignment,
    int StorageOffsetAlignment,
    RhiLimits Limits)
{
    /// <summary>The lowest version the backend runs on: compute, storage buffers, texture views and debug output.</summary>
    public const int MinimumVersion = 43;

    /// <summary>Gets what the RHI calls the optional things this device can do.</summary>
    public RhiFeatures Features => new(
        RayQuery: false,
        TimestampQuery: TimerQuery,
        Float32Filterable: true,
        DepthClamp: true,
        IndirectFirstInstance: true,
        TextureCompressionBc: S3tc,
        StrictTextureSampleTypes: false,
        MultiDrawIndirect: true);

    /// <summary>Reads the driver, and refuses one this backend cannot run on.</summary>
    /// <param name="gl">The entry points, loaded from a current context.</param>
    /// <returns>What it offers.</returns>
    /// <exception cref="OpenGLUnavailableException">The context is older than 4.3, or lacks clip control.</exception>
    public static GlCapabilities Read(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);

        string renderer = gl.GetStringS(StringName.Renderer) ?? "OpenGL";
        string vendor = gl.GetStringS(StringName.Vendor) ?? string.Empty;
        string versionText = gl.GetStringS(StringName.Version) ?? string.Empty;

        gl.GetInteger(GetPName.MajorVersion, out int major);
        gl.GetInteger(GetPName.MinorVersion, out int minor);
        int version = (major * 10) + minor;

        if (version < MinimumVersion)
        {
            throw new OpenGLUnavailableException(
                $"{renderer} offers OpenGL {major}.{minor} ({versionText}); the game needs OpenGL 4.3. "
                + "Updating the graphics driver may raise it; older adapters cannot run the game.");
        }

        HashSet<string> extensions = new(StringComparer.Ordinal);
        gl.GetInteger(GetPName.NumExtensions, out int count);
        for (uint i = 0; i < (uint)count; i++)
        {
            extensions.Add(gl.GetStringS(StringName.Extensions, i) ?? string.Empty);
        }

        bool clipControl = version >= 45 || extensions.Contains("GL_ARB_clip_control");
        if (!clipControl)
        {
            throw new OpenGLUnavailableException(
                $"{renderer}'s OpenGL {major}.{minor} has no clip control (GL_ARB_clip_control), which the game needs "
                + "to draw with the depth range every other renderer uses. Updating the graphics driver usually adds it.");
        }

        bool anisotropy = version >= 46
            || extensions.Contains("GL_ARB_texture_filter_anisotropic")
            || extensions.Contains("GL_EXT_texture_filter_anisotropic");
        float maxAnisotropy = 1f;
        if (anisotropy)
        {
            gl.GetFloat((GetPName)GLEnum.MaxTextureMaxAnisotropy, out maxAnisotropy);
        }

        gl.GetInteger(GetPName.UniformBufferOffsetAlignment, out int uniformAlignment);
        gl.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment, out int storageAlignment);

        return new GlCapabilities(
            version,
            renderer,
            vendor,
            clipControl,
            anisotropy,
            Math.Max(1f, maxAnisotropy),
            extensions.Contains("GL_EXT_texture_compression_s3tc"),
            TimerQuery: true,
            Debug: true,
            QueryBuffer: version >= 44 || extensions.Contains("GL_ARB_query_buffer_object"),
            TextureSubImage: version >= 45 || extensions.Contains("GL_ARB_get_texture_sub_image"),
            Math.Max(1, uniformAlignment),
            Math.Max(1, storageAlignment),
            ReadLimits(gl));
    }

    private static RhiLimits ReadLimits(GL gl)
    {
        int Get(GLEnum name)
        {
            gl.GetInteger(name, out int value);
            return value;
        }

        int Indexed(GLEnum name, uint index)
        {
            gl.GetInteger(name, index, out int value);
            return value;
        }

        long Get64(GLEnum name)
        {
            gl.GetInteger64(name, out long value);
            return value;
        }

        int textures = Math.Min(Get(GLEnum.MaxTextureImageUnits), Get(GLEnum.MaxComputeTextureImageUnits));
        int storageBuffers = Math.Min(
            Math.Min(Get(GLEnum.MaxVertexShaderStorageBlocks), Get(GLEnum.MaxFragmentShaderStorageBlocks)),
            Get(GLEnum.MaxComputeShaderStorageBlocks));
        int images = Math.Min(
            Get(GLEnum.MaxImageUnits),
            Math.Min(Get(GLEnum.MaxFragmentImageUniforms), Get(GLEnum.MaxComputeImageUniforms)));
        int groups = Math.Min(
            Indexed(GLEnum.MaxComputeWorkGroupCount, 0),
            Math.Min(Indexed(GLEnum.MaxComputeWorkGroupCount, 1), Indexed(GLEnum.MaxComputeWorkGroupCount, 2)));

        return new RhiLimits(
            MaxTextureDimension2D: Get(GLEnum.MaxTextureSize),
            MaxTextureDimension3D: Get(GLEnum.Max3DTextureSize),
            MaxTextureArrayLayers: Get(GLEnum.MaxArrayTextureLayers),

            // Groups are the RHI's, flattened into units when a pipeline is made; the push block takes the fourth.
            MaxBindGroups: 4,
            MaxSamplersPerStage: textures,
            MaxSampledTexturesPerStage: textures,
            MaxStorageBuffersPerStage: storageBuffers,
            MaxStorageTexturesPerStage: images,
            MaxUniformBufferBindingSize: (ulong)Math.Max(0, Get64(GLEnum.MaxUniformBlockSize)),
            MaxStorageBufferBindingSize: (ulong)Math.Max(0, Get64(GLEnum.MaxShaderStorageBlockSize)),

            // A slice of the uniform ring, as on WebGPU: OpenGL has no push constants either.
            MaxPushDataBytes: 256,
            MaxComputeWorkgroupStorageBytes: Get(GLEnum.MaxComputeSharedMemorySize),
            MaxComputeInvocationsPerWorkgroup: Get(GLEnum.MaxComputeWorkGroupInvocations),
            MaxComputeWorkgroupsPerDimension: groups,
            MaxColorAttachments: Math.Min(8, Get(GLEnum.MaxColorAttachments)),
            MaxSampleCount: Math.Min(Get(GLEnum.MaxColorTextureSamples), Get(GLEnum.MaxDepthTextureSamples)));
    }
}

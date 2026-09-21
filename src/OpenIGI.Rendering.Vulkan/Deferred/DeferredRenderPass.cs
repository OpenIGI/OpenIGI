using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The deferred render passes: one that fills the G-buffer, and one that shades it and draws translucency
/// over the result.
/// </summary>
/// <remarks>
/// <para>
/// Two passes rather than one, with a gap between them. That gap is the point: it is where compute runs
/// against a finished G-buffer. A sun shadow traced against the scene, and every gather the indirect
/// lighting is built from, happen there — a dispatch cannot be recorded inside a render pass, and a
/// gather needs the whole G-buffer finished before it can read anything but the pixel it is on.
/// </para>
/// <para>
/// What the split does not cost is the on-chip read. The G-buffer is still an input attachment to the
/// shading pass, which is what lets a tiled part shade without spilling it — an attachment written by an
/// earlier render pass is a perfectly good input to a later one, so long as the layouts line up and the
/// second pass loads rather than clears.
/// </para>
/// <para>
/// The attachment set follows the reference renderer's G-buffer, because the indirect-lighting passes
/// ported on top of it read exactly these channels: an sRGB base colour, a world-space normal in
/// 10-10-10-2, a packed metallic/specular/roughness/shading-model target, an HDR baked-light target the
/// reference has no equivalent of, a reverse-Z depth-stencil buffer, and the HDR scene colour.
/// </para>
/// <para>
/// The G-buffer targets are stored rather than discarded. On-tile they would be cheaper, and the
/// lighting subpass still reads them that way — but the gather passes that follow the pass sample them
/// as ordinary textures, and a transient attachment does not survive to be sampled.
/// </para>
/// </remarks>
internal sealed unsafe class DeferredRenderPass : IDisposable
{
    /// <summary>The base-colour G-buffer format: sRGB, so lighting reads linear base colour for free.</summary>
    /// <remarks>
    /// The hardware encodes on write and decodes on read, so the shader works in linear throughout and the
    /// eight bits are spent where the eye can see them rather than evenly across a linear range.
    /// </remarks>
    public const Format BaseColourFormat = Format.R8G8B8A8Srgb;

    /// <summary>The normal G-buffer format: ten bits a channel for a world-space normal, two spare.</summary>
    /// <remarks>
    /// Ten bits a channel stored directly beats the two sixteen-bit channels an octahedral encoding needs:
    /// it is smaller, it costs no encode or decode, and it avoids the octahedral seam. The two spare bits
    /// carry per-object flags, which is what the reference uses them for.
    /// </remarks>
    public const Format NormalFormat = Format.A2B10G10R10UnormPack32;

    /// <summary>The material G-buffer format: metallic, specular, roughness and the shading model.</summary>
    public const Format MaterialFormat = Format.R8G8B8A8Unorm;

    /// <summary>The baked-light G-buffer format: a compact positive-only HDR triple.</summary>
    public const Format BakedFormat = Format.B10G11R11UfloatPack32;

    /// <summary>The depth format: 32-bit float, paired with reverse-Z.</summary>
    /// <remarks>
    /// Depth only, where the reference pairs it with stencil. Stencil is how the reference masks its
    /// shading models and decals, and nothing here does either yet — the shading model travels in the
    /// material target instead. Adding the stencil half costs an aspect mask on every barrier and a second
    /// view on every sampled read, so it waits for the pass that actually reads it.
    /// </remarks>
    public const Format DepthFormat = Format.D32Sfloat;

    /// <summary>The lit-scene format: 16-bit float HDR, the one attachment that survives the pass.</summary>
    public const Format LitFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The glazing front layer: a pane's world normal, and its view depth in the alpha.</summary>
    /// <remarks>
    /// Sixteen bits a channel and not the ten the opaque normals get. A pane is a MIRROR — the reflected
    /// direction is twice the normal's error — so the coarse encoding that is invisible on a diffuse wall
    /// smears a window's reflection. The reference reaches the same conclusion and says so in as many
    /// words where it picks this format.
    /// </remarks>
    public const Format FrontLayerFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The only subpass of the geometry pass, which writes the G-buffer.</summary>
    public const uint GeometrySubpass = 0;

    /// <summary>The shading pass's first subpass, which lights the G-buffer into the scene's colour.</summary>
    public const uint LightingSubpass = 0;

    /// <summary>The shading pass's second subpass, which draws translucency over the lit colour.</summary>
    public const uint ForwardSubpass = 1;

    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="DeferredRenderPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <exception cref="GraphicsDeviceException">The render pass could not be created.</exception>
    public DeferredRenderPass(Vk vk, Device device)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        Geometry = CreateGeometryPass();
        Shading = CreateShadingPass();
        FrontLayer = CreateFrontLayerPass();
    }

    /// <summary>Gets the pass that fills the G-buffer.</summary>
    public RenderPass Geometry { get; }

    /// <summary>Gets the pass that shades the G-buffer and draws translucency.</summary>
    public RenderPass Shading { get; }

    /// <summary>Gets the pass that records which pane of glazing is nearest at each pixel.</summary>
    /// <remarks>
    /// <para>
    /// A colour target and a DEPTH target of its own. The depth is the whole point: a pane's reflection
    /// is computed from one surface per pixel, and where two windows overlap the depth unit is what
    /// decides which — nearest wins, by construction. The glazing used to answer that with an image store
    /// from the translucency subpass, and an image store is not ordered between fragments, so the answer
    /// changed every frame and the reflection fizzed.
    /// </para>
    /// <para>
    /// Separate from the translucency subpass rather than depth-writing inside it, because there the
    /// glazing shares a depth buffer with the tracers, the muzzle flame and the smoke — all drawn after
    /// it, all of which glass would then occlude.
    /// </para>
    /// </remarks>
    public RenderPass FrontLayer { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyRenderPass(_device, FrontLayer, null);
        _vk.DestroyRenderPass(_device, Shading, null);
        _vk.DestroyRenderPass(_device, Geometry, null);
    }

    private RenderPass CreateGeometryPass()
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[5];
        attachments[0] = ColourAttachment(BaseColourFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[1] = ColourAttachment(NormalFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[2] = ColourAttachment(MaterialFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[3] = ColourAttachment(BakedFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[4] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,

            // Stored, and left where both a sampled compute read and the shading pass input attachment can
            // take it.
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        AttachmentReference* colour = stackalloc AttachmentReference[4]
        {
            new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 3, Layout = ImageLayout.ColorAttachmentOptimal },
        };
        AttachmentReference depth = new() { Attachment = 4, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 4,
            PColorAttachments = colour,
            PDepthStencilAttachment = &depth,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,

            // What these images were doing before this pass overwrites them is being READ -- by the
            // shading pass that took them as input attachments, and by every compute pass that sampled
            // them in the gap. Clearing them has to wait for those readers, so the wait names the shader
            // stages rather than only the attachment ones it is about to use.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.FragmentShaderBit
                | PipelineStageFlags.ComputeShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentWriteBit
                | AccessFlags.InputAttachmentReadBit
                | AccessFlags.ShaderReadBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,

            // Everything the pass wrote has to be visible to what comes next, and what comes next is both
            // the compute that traces against it and the shading pass that reads it as input attachments.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.InputAttachmentReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 5,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &renderPass), "vkCreateRenderPass (geometry)");
        return renderPass;
    }

    private RenderPass CreateShadingPass()
    {
        // The G-buffer arrives already written and already in the layout an input attachment wants, so it
        // is loaded rather than cleared and left exactly as it was found -- later passes still read it.
        AttachmentDescription* attachments = stackalloc AttachmentDescription[6];
        attachments[0] = LoadedAttachment(BaseColourFormat, ImageLayout.ShaderReadOnlyOptimal);
        attachments[1] = LoadedAttachment(NormalFormat, ImageLayout.ShaderReadOnlyOptimal);
        attachments[2] = LoadedAttachment(MaterialFormat, ImageLayout.ShaderReadOnlyOptimal);
        attachments[3] = LoadedAttachment(BakedFormat, ImageLayout.ShaderReadOnlyOptimal);

        attachments[4] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ShaderReadOnlyOptimal,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        attachments[5] = ColourAttachment(LitFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);

        AttachmentReference* lightingInput = stackalloc AttachmentReference[5]
        {
            new() { Attachment = 0, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 3, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 4, Layout = ImageLayout.DepthStencilReadOnlyOptimal },
        };
        AttachmentReference lit = new() { Attachment = 5, Layout = ImageLayout.ColorAttachmentOptimal };
        AttachmentReference depth = new() { Attachment = 4, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        SubpassDescription* subpasses = stackalloc SubpassDescription[2];
        subpasses[0] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            InputAttachmentCount = 5,
            PInputAttachments = lightingInput,
            ColorAttachmentCount = 1,
            PColorAttachments = &lit,
        };
        subpasses[1] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &lit,
            PDepthStencilAttachment = &depth,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[3];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = LightingSubpass,

            // Whatever ran in the gap -- the shadow trace, the gathers -- wrote results this pass reads,
            // and the geometry pass wrote the G-buffer it reads as input attachments.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.LateFragmentTestsBit
                | PipelineStageFlags.ComputeShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentWriteBit
                | AccessFlags.ShaderWriteBit,

            // Both halves of what this pass does at its start, not just the shader's. Reading the
            // G-buffer as input attachments happens in the fragment stage, but LOADING five of these
            // attachments and transitioning them into the layout this pass wants happens before any
            // shader runs -- at colour output for the colour ones and at the depth test for depth. A
            // dependency naming only the shader leaves that load unordered against the geometry pass
            // that wrote them, which is a hazard the synchronisation layer names precisely and which
            // otherwise shows up as nothing at all until it does.
            //
            // Both depth stages, not just the early one: the transition into this subpass writes depth,
            // and so does storing it when the pass ends -- and a store happens at the late test. Naming
            // only the early stage leaves those two writes unordered against each other.
            DstStageMask = PipelineStageFlags.FragmentShaderBit
                | PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            DstAccessMask = AccessFlags.InputAttachmentReadBit
                | AccessFlags.ShaderReadBit
                | AccessFlags.ColorAttachmentReadBit
                | AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentReadBit
                | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = LightingSubpass,
            DstSubpass = ForwardSubpass,

            // Depth as well as colour. The depth attachment is read-only while the lighting subpass
            // samples it and written again once the forward subpass draws into it, so crossing this
            // boundary transitions it -- and that transition is a write. Left out of the dependency it
            // is ordered against nothing, including the store that writes the same depth when the pass
            // ends, which is the write-after-write the synchronisation layer reports.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentReadBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.ColorAttachmentReadBit
                | AccessFlags.DepthStencilAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };
        dependencies[2] = new SubpassDependency
        {
            SrcSubpass = ForwardSubpass,
            DstSubpass = Vk.SubpassExternal,

            // The lit colour to the resolve sample, and the depth to the occlusion and motion passes.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 6,
            PAttachments = attachments,
            SubpassCount = 2,
            PSubpasses = subpasses,
            DependencyCount = 3,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &renderPass), "vkCreateRenderPass (shading)");
        return renderPass;
    }

    private static AttachmentDescription LoadedAttachment(Format format, ImageLayout layout) => new()
    {
        Format = format,
        Samples = SampleCountFlags.Count1Bit,
        LoadOp = AttachmentLoadOp.Load,
        StoreOp = AttachmentStoreOp.Store,
        StencilLoadOp = AttachmentLoadOp.DontCare,
        StencilStoreOp = AttachmentStoreOp.DontCare,
        InitialLayout = layout,
        FinalLayout = layout,
    };

    private RenderPass CreateFrontLayerPass()
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];

        // Cleared, because a pixel no pane covers has to come out at nought -- that is what the
        // reflection pass reads as "no glazing here". Left in GENERAL rather than the read-only layout so
        // the compute pass that consumes it can keep binding it exactly as it did when this was a storage
        // image: the change of who writes it is not a change of who reads it.
        attachments[0] = ColourAttachment(FrontLayerFormat, ImageLayout.General, store: true);

        // Its own depth, cleared to the far value every frame. Nothing reads it afterwards -- it exists
        // only so the depth unit can pick the nearest pane -- so it is not stored.
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        AttachmentReference colour = new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        AttachmentReference depth = new() { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colour,
            PDepthStencilAttachment = &depth,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,

            // The last frame's reflection pass read this layer, and the clear must wait for it.
            SrcStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,

            // And what it wrote has to be visible to the compute pass that reflects in it.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &renderPass), "vkCreateRenderPass (front layer)");
        return renderPass;
    }

    private static AttachmentDescription ColourAttachment(Format format, ImageLayout finalLayout, bool store) => new()
    {
        Format = format,
        Samples = SampleCountFlags.Count1Bit,
        LoadOp = AttachmentLoadOp.Clear,
        StoreOp = store ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
        StencilLoadOp = AttachmentLoadOp.DontCare,
        StencilStoreOp = AttachmentStoreOp.DontCare,
        InitialLayout = ImageLayout.Undefined,
        FinalLayout = finalLayout,
    };
}

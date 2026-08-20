using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The deferred render pass: geometry into a G-buffer, a lighting subpass that reads it on-chip through
/// input attachments, and a forward subpass for translucency — one pass, its stages synchronised by
/// subpass dependencies rather than by hand.
/// </summary>
/// <remarks>
/// <para>
/// Keeping the whole of deferred inside one render pass is the Vulkan-native shape: the G-buffer is an
/// input attachment to the lighting subpass, so on a tiled GPU it never leaves on-chip memory, and the
/// barriers between geometry, lighting and forward are expressed as subpass dependencies the driver
/// schedules — there are no manual pipeline barriers to get wrong.
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

    /// <summary>The subpass that writes the G-buffer.</summary>
    public const uint GeometrySubpass = 0;

    /// <summary>The subpass that lights the G-buffer into the lit colour.</summary>
    public const uint LightingSubpass = 1;

    /// <summary>The subpass that draws translucency over the lit colour.</summary>
    public const uint ForwardSubpass = 2;

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
        Handle = Create();
    }

    /// <summary>Gets the render pass handle.</summary>
    public RenderPass Handle { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyRenderPass(_device, Handle, null);
    }

    private RenderPass Create()
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[6];
        attachments[0] = ColourAttachment(BaseColourFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[1] = ColourAttachment(NormalFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[2] = ColourAttachment(MaterialFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[3] = ColourAttachment(BakedFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);
        attachments[4] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,

            // Stored and left in the shader-read layout so the ambient-occlusion compute can sample it once
            // the pass closes — a sampled read wants that layout, not the depth read-only one the lighting's
            // input attachment uses inside the pass.
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        attachments[5] = ColourAttachment(LitFormat, ImageLayout.ShaderReadOnlyOptimal, store: true);

        AttachmentReference* geometryColour = stackalloc AttachmentReference[4]
        {
            new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 3, Layout = ImageLayout.ColorAttachmentOptimal },
        };
        AttachmentReference depthRef = new() { Attachment = 4, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        // The four G-buffer targets plus the depth buffer as input attachments: the lighting pass
        // reconstructs the view position from depth on-tile to sample the probe field, so depth is read
        // here rather than stored and sampled in a later pass.
        AttachmentReference* lightingInput = stackalloc AttachmentReference[5]
        {
            new() { Attachment = 0, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 3, Layout = ImageLayout.ShaderReadOnlyOptimal },
            new() { Attachment = 4, Layout = ImageLayout.DepthStencilReadOnlyOptimal },
        };
        AttachmentReference litRef = new() { Attachment = 5, Layout = ImageLayout.ColorAttachmentOptimal };

        SubpassDescription* subpasses = stackalloc SubpassDescription[3];
        subpasses[0] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 4,
            PColorAttachments = geometryColour,
            PDepthStencilAttachment = &depthRef,
        };
        subpasses[1] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            InputAttachmentCount = 5,
            PInputAttachments = lightingInput,
            ColorAttachmentCount = 1,
            PColorAttachments = &litRef,
        };
        subpasses[2] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &litRef,
            PDepthStencilAttachment = &depthRef,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[5];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = GeometrySubpass,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = GeometrySubpass,
            DstSubpass = LightingSubpass,

            // Both the colour targets and the depth buffer the geometry pass wrote must be visible to the
            // lighting pass's input-attachment reads — the four G-buffer channels and the depth it
            // reconstructs position from.
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.InputAttachmentReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };
        dependencies[2] = new SubpassDependency
        {
            SrcSubpass = LightingSubpass,
            DstSubpass = ForwardSubpass,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };
        dependencies[3] = new SubpassDependency
        {
            SrcSubpass = GeometrySubpass,
            DstSubpass = ForwardSubpass,
            SrcStageMask = PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[4] = new SubpassDependency
        {
            SrcSubpass = ForwardSubpass,
            DstSubpass = Vk.SubpassExternal,

            // The lit colour to the resolve's fragment sample, and the depth to the occlusion compute's
            // sample, are both made visible as the pass ends.
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
            SubpassCount = 3,
            PSubpasses = subpasses,
            DependencyCount = 5,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &renderPass), "vkCreateRenderPass (deferred)");
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

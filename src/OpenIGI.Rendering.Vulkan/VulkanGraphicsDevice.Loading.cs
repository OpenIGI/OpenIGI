using OpenIGI.Rendering.Vulkan.Deferred;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Rendering.Vulkan.Textures;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The loading backdrop pass for <see cref="VulkanGraphicsDevice"/>.</summary>
public sealed partial class VulkanGraphicsDevice
{
    private const uint LoadingPushFloats = 16;

    private FullscreenPipeline? _loadingBackdrop;

    /// <inheritdoc/>
    /// <remarks>
    /// Drawn into the present pass, blended over what the frame already holds, through the same
    /// one-sampler descriptor layout the 2D sprites use -- so a held frame that was captured for the
    /// dissolve is bound here without any further plumbing. Built on first use: most sessions never load
    /// a level through the panel at all.
    /// </remarks>
    public unsafe void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
        ThrowIfDisposed();
        EnsurePresentPass();

        _loadingBackdrop ??= new FullscreenPipeline(
            _instance.Vk,
            _device,
            ShaderModuleId.Loading,
            _sprite2D.DescriptorSetLayout,
            _presentTarget.RenderPass,
            0,
            alphaBlend: true);

        uint id = texture.Id;
        if (!settings.HasPicture || !_textures.TryGetValue(id, out VulkanTexture? resolved))
        {
            id = _whiteTexture.Id;
            resolved = _textures[id];
        }

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;
        DescriptorSet set = _textureDescriptors.Get(id, resolved);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _loadingBackdrop.Pipeline);
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _loadingBackdrop.PipelineLayout, 0, 1, &set, 0, null);

        float* push = stackalloc float[(int)LoadingPushFloats];
        push[0] = settings.TimeSeconds;
        push[1] = settings.Blur;
        push[2] = settings.Desaturation;
        push[3] = settings.Vignette;
        push[4] = settings.Zoom;
        push[5] = settings.Grain;
        push[6] = settings.Scanlines;
        push[7] = settings.Opacity;
        push[8] = settings.Letterbox;
        push[9] = settings.HasPicture ? 1f : 0f;
        push[10] = settings.AspectRatio;
        push[11] = settings.Sweep;
        push[12] = 0f;
        push[13] = 0f;
        push[14] = 0f;
        push[15] = 0f;
        vk.CmdPushConstants(
            cmd, _loadingBackdrop.PipelineLayout, ShaderStageFlags.FragmentBit, 0, sizeof(float) * LoadingPushFloats, push);

        Extent2D extent = _presentTarget.Extent;
        ApplyViewportAndScissor(cmd, new Viewport(0, 0, (int)extent.Width, (int)extent.Height));
        vk.CmdDraw(cmd, 3, 1, 0, 0);

        // The 2D path assumes its own pipeline and viewport are what is bound; neither is any more.
        ApplyViewportAndScissor(cmd, _viewport);
        InvalidateBoundState();
    }
}

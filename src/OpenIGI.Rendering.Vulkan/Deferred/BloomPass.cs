using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The bloom pyramid: six progressively coarser blurs of the lit scene, summed with the reference's
/// per-stage tints — the reference's sum-of-gaussians bloom.
/// </summary>
/// <remarks>
/// <para>
/// Owns its own images because they are wanted only between the shading pass and the tone map, and are
/// small: the whole pyramid at 1600x900 is under a megabyte. They are per frame in flight for the same
/// reason every other target is — two frames can be recording at once, and a shared pyramid would have
/// the second overwrite what the first has not yet read.
/// </para>
/// <para>
/// See the Bloom modules in OpenIGI.Shaders for why the sum has six terms and no threshold.
/// </para>
/// </remarks>
internal sealed unsafe class BloomPass : IDisposable
{
    /// <summary>How many levels the pyramid has, matching the reference's six bloom stages.</summary>
    public const int Levels = 6;

    // The reference's default per-stage tints (Bloom1..6 Tint, all grey), and its default intensity.
    // Their sum is well under one, which is what keeps an unthresholded bloom subtle.
    private static readonly float[] StageTints = [0.3465f, 0.138f, 0.1176f, 0.066f, 0.066f, 0.061f];

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;

    private readonly VulkanComputePass _downsample;
    private readonly VulkanComputePass _blur;
    private readonly VulkanComputePass _combine;

    private readonly Frame[] _frames;
    private int _width;
    private int _height;
    private bool _disposed;

    private sealed class Frame
    {
        public VulkanImage[] Chain = [];
        public VulkanImage[] Scratch = [];
        public VulkanImage? Result;
    }

    /// <summary>Initialises a new instance of the <see cref="BloomPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the pyramid.</param>
    /// <param name="commands">The pool the initial transitions come from.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public BloomPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int framesInFlight)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;

        _downsample = new VulkanComputePass(
            vk, device, allocator, ShaderModuleId.BloomDownsample,
            [ComputeBindingKind.SampledStorageImage, ComputeBindingKind.StorageImage],
            32, framesInFlight * Levels);

        _blur = new VulkanComputePass(
            vk, device, allocator, ShaderModuleId.BloomBlur,
            [ComputeBindingKind.SampledStorageImage, ComputeBindingKind.StorageImage],
            32, framesInFlight * Levels * 2);

        _combine = new VulkanComputePass(
            vk, device, allocator, ShaderModuleId.BloomCombine,
            [
                ComputeBindingKind.SampledStorageImage, ComputeBindingKind.SampledStorageImage,
                ComputeBindingKind.SampledStorageImage, ComputeBindingKind.SampledStorageImage,
                ComputeBindingKind.SampledStorageImage, ComputeBindingKind.SampledStorageImage,
                ComputeBindingKind.StorageImage,
            ],
            48, framesInFlight);

        _frames = new Frame[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _frames[i] = new Frame();
        }
    }

    /// <summary>Gets the summed bloom for a frame, or null before the pyramid has been built at a size.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The bloom image view, or a null handle.</returns>
    public ImageView ResultView(int frame) => _frames[frame].Result?.View ?? default;

    /// <summary>Gets the summed bloom image for a frame, for its barriers.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The bloom image, or a null handle.</returns>
    public Image ResultImage(int frame) => _frames[frame].Result?.Handle ?? default;

    /// <summary>Rebuilds the pyramid for a render size, if it is not already that size.</summary>
    /// <param name="width">The rendered frame's width.</param>
    /// <param name="height">The rendered frame's height.</param>
    public void Resize(int width, int height)
    {
        if (_width == width && _height == height && _frames[0].Result is not null)
        {
            return;
        }

        DestroyImages();
        _width = width;
        _height = height;

        const ImageUsageFlags usage = ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit;
        foreach (Frame frame in _frames)
        {
            frame.Chain = new VulkanImage[Levels];
            frame.Scratch = new VulkanImage[Levels];
            for (int level = 0; level < Levels; level++)
            {
                (int w, int h) = LevelSize(level);
                frame.Chain[level] = new VulkanImage(
                    _vk, _device, _allocator, _commands, DeferredRenderPass.LitFormat, (uint)w, (uint)h, 1, usage);
                frame.Scratch[level] = new VulkanImage(
                    _vk, _device, _allocator, _commands, DeferredRenderPass.LitFormat, (uint)w, (uint)h, 1, usage);
            }

            (int rw, int rh) = LevelSize(0);
            frame.Result = new VulkanImage(
                _vk, _device, _allocator, _commands, DeferredRenderPass.LitFormat, (uint)rw, (uint)rh, 1, usage);
        }
    }

    /// <summary>Records the whole chain: downsample, blur each level, then sum.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="litView">The lit scene the pyramid is built from.</param>
    /// <param name="threshold">
    /// The luminance a pixel must pass before it spills, or zero to spill everything. Zero is what the
    /// reference asks for and what ultra carries; see the BloomDownsample module.
    /// </param>
    /// <param name="intensity">The reference's bloom intensity.</param>
    public void Record(CommandBuffer cmd, int frame, ImageView litView, float intensity, float threshold)
    {
        Frame state = _frames[frame];
        if (state.Result is null)
        {
            return;
        }

        // Down the pyramid: each level halves the one above it, the top halving the lit scene itself.
        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);
            ImageView source = level == 0 ? litView : state.Chain[level - 1].View;
            if (level > 0)
            {
                Barrier(cmd, state.Chain[level - 1].Handle, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
            }

            // Only the top level sees the threshold: every level below it is a blur of the level above,
            // which has already been cut.
            ReadOnlySpan<float> data = [w, h, 1f / w, 1f / h, level == 0 ? threshold : 0f, 0f, 0f, 0f];
            _downsample.Record(
                cmd,
                (frame * Levels) + level,
                [new ComputeResource(source), new ComputeResource(state.Chain[level].View)],
                data,
                (uint)((w + 7) / 8),
                (uint)((h + 7) / 8));
        }

        // Then a separable gaussian over each level, in its own resolution.
        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);
            uint groupsX = (uint)((w + 7) / 8);
            uint groupsY = (uint)((h + 7) / 8);
            int slot = ((frame * Levels) + level) * 2;

            Barrier(cmd, state.Chain[level].Handle, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
            ReadOnlySpan<float> horizontal = [w, h, 1f / w, 1f / h, 1f, 0f, 0f, 0f];
            _blur.Record(
                cmd, slot,
                [new ComputeResource(state.Chain[level].View), new ComputeResource(state.Scratch[level].View)],
                horizontal, groupsX, groupsY);

            Barrier(cmd, state.Scratch[level].Handle, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
            ReadOnlySpan<float> vertical = [w, h, 1f / w, 1f / h, 0f, 1f, 0f, 0f];
            _blur.Record(
                cmd, slot + 1,
                [new ComputeResource(state.Scratch[level].View), new ComputeResource(state.Chain[level].View)],
                vertical, groupsX, groupsY);
        }

        foreach (VulkanImage level in state.Chain)
        {
            Barrier(cmd, level.Handle, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
        }

        Barrier(cmd, state.Result.Handle, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit);

        (int cw, int ch) = LevelSize(0);
        ReadOnlySpan<float> combine =
        [
            cw, ch, 1f / cw, 1f / ch,
            StageTints[0], StageTints[1], StageTints[2], StageTints[3],
            StageTints[4], StageTints[5], intensity, 0f,
        ];

        _combine.Record(
            cmd,
            frame,
            [
                new ComputeResource(state.Chain[0].View), new ComputeResource(state.Chain[1].View),
                new ComputeResource(state.Chain[2].View), new ComputeResource(state.Chain[3].View),
                new ComputeResource(state.Chain[4].View), new ComputeResource(state.Chain[5].View),
                new ComputeResource(state.Result.View),
            ],
            combine,
            (uint)((cw + 7) / 8),
            (uint)((ch + 7) / 8));

        Barrier(cmd, state.Result.Handle, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
    }

    private (int Width, int Height) LevelSize(int level)
    {
        int shift = level + 1;
        return (Math.Max(1, _width >> shift), Math.Max(1, _height >> shift));
    }

    private void Barrier(CommandBuffer cmd, Image image, AccessFlags from, AccessFlags to)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = from,
            DstAccessMask = to,
        };

        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    private void DestroyImages()
    {
        foreach (Frame frame in _frames)
        {
            foreach (VulkanImage image in frame.Chain)
            {
                image.Dispose();
            }

            foreach (VulkanImage image in frame.Scratch)
            {
                image.Dispose();
            }

            frame.Result?.Dispose();
            frame.Chain = [];
            frame.Scratch = [];
            frame.Result = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyImages();
        _downsample.Dispose();
        _blur.Dispose();
        _combine.Dispose();
    }
}

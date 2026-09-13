using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;
using VkShaderModule = Silk.NET.Vulkan.ShaderModule;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>A compiled SPIR-V shader wrapped as a Vulkan shader module.</summary>
/// <remarks>
/// A shader module is the loadable form a pipeline stage references. It holds the SPIR-V for one or
/// more entry points; this backend compiles one stage per source, so a module is one stage, named
/// <c>main</c>. The module can be destroyed as soon as every pipeline that uses it has been created, but
/// keeping it for the resource's life costs little and lets a pipeline be rebuilt without recompiling.
/// </remarks>
internal sealed unsafe class VulkanShaderModule : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanShaderModule"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to create the module on.</param>
    /// <param name="spirv">The SPIR-V words, a byte array whose length is a multiple of four.</param>
    /// <param name="stage">Which pipeline stage the module is for.</param>
    /// <exception cref="GraphicsDeviceException">The module could not be created.</exception>
    public VulkanShaderModule(Vk vk, Device device, ReadOnlySpan<byte> spirv, ShaderStage stage)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        Stage = stage;

        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };

            VkShaderModule module;
            VkCheck.Ok(_vk.CreateShaderModule(device, &createInfo, null, &module), "vkCreateShaderModule");
            Handle = module;
        }
    }

    /// <summary>Gets the shader module handle.</summary>
    public VkShaderModule Handle { get; }

    /// <summary>Gets which pipeline stage the module is for.</summary>
    public ShaderStage Stage { get; }

    /// <summary>Gets the Vulkan stage flag matching <see cref="Stage"/>.</summary>
    public ShaderStageFlags StageFlags => Stage switch
    {
        ShaderStage.Vertex => ShaderStageFlags.VertexBit,
        ShaderStage.Fragment => ShaderStageFlags.FragmentBit,
        ShaderStage.Compute => ShaderStageFlags.ComputeBit,
        _ => ShaderStageFlags.None,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyShaderModule(_device, Handle, null);
    }
}

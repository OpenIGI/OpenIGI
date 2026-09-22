using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;
using VkShaderModule = Silk.NET.Vulkan.ShaderModule;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>A compiled SPIR-V shader wrapped as a Vulkan shader module.</summary>
/// <remarks>
/// A shader module is the loadable form a pipeline stage references, and it holds the SPIR-V for one
/// or more entry points. A module from the shader library holds every entry point its Slang source
/// declares -- usually a vertex and a fragment stage -- and a pipeline stage selects one by name,
/// which is also how WebGPU binds a module. The GLSL sources this backend still compiles at load
/// produce one stage each, named <c>main</c>.
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
        : this(vk, device, spirv, stage, "main")
    {
    }

    /// <summary>Initialises a new instance of the <see cref="VulkanShaderModule"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to create the module on.</param>
    /// <param name="spirv">The SPIR-V words, a byte array whose length is a multiple of four.</param>
    /// <param name="stage">Which pipeline stage the module is for.</param>
    /// <param name="entryPoint">The entry point a pipeline stage should name.</param>
    /// <exception cref="GraphicsDeviceException">The module could not be created.</exception>
    public VulkanShaderModule(Vk vk, Device device, ReadOnlySpan<byte> spirv, ShaderStage stage, string entryPoint)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentException.ThrowIfNullOrEmpty(entryPoint);
        _vk = vk;
        _device = device;
        Stage = stage;
        EntryPoint = entryPoint;

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

    /// <summary>Gets the entry point a pipeline stage using this module should name.</summary>
    public string EntryPoint { get; }

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

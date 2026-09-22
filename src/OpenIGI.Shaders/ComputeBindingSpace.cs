namespace OpenIGI.Shaders;

/// <summary>
/// Where a compute module's resources sit in its descriptor set, as
/// <c>Source/Common/ComputeBindings.slang</c> places them.
/// </summary>
/// <remarks>
/// The engine's compute seam binds a resource by the number the shader declared it at AND by what kind it
/// is: an image at slot zero and a storage buffer at slot zero are two different resources, because the
/// dialect these shaders came from gives each kind its own numbering. Vulkan gives a set one numbering
/// for all of them, so each kind takes a block of sixteen and the backend recovers the kind and the slot
/// by division. There is no table to keep in step -- only these numbers and the macros that mirror them,
/// which <c>ComputeBindingSpaceTests</c> pins together.
/// </remarks>
public static class ComputeBindingSpace
{
    /// <summary>How many slots each kind is given.</summary>
    public const int SlotsPerKind = 16;

    /// <summary>The first binding a storage image may occupy.</summary>
    public const int ImageBase = 0;

    /// <summary>The first binding a storage buffer may occupy.</summary>
    public const int BufferBase = 16;

    /// <summary>The first binding a sampled image may occupy.</summary>
    public const int TextureBase = 32;

    /// <summary>The binding the world's acceleration structure occupies. There is only ever one.</summary>
    public const int SceneBinding = 48;

    /// <summary>The binding the gathered parameter block occupies.</summary>
    public const int GlobalsBinding = 49;

    /// <summary>The slot a binding stands for within its own kind.</summary>
    /// <param name="binding">The binding the module declared.</param>
    /// <returns>Its slot, counting from nought within its kind's block.</returns>
    public static int SlotOf(int binding) => binding % SlotsPerKind;
}

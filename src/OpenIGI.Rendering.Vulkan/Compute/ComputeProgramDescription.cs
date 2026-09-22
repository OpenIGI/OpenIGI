namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>
/// What a compute program binds: its resources, the parameters gathered into its globals block, and
/// where that block sits.
/// </summary>
/// <remarks>
/// <para>
/// A description rather than a source. The modules are compiled ahead of time by the shader library, so
/// what reaches this backend is SPIR-V and a reflection table; everything the pipeline and the dispatch
/// need is stated here, and nothing reads a shader's text.
/// </para>
/// <para>
/// GL's three binding namespaces collapse into one in Vulkan, so a resource carries both numbers: the
/// <see cref="Resource.Binding"/> the descriptor set uses, and the <see cref="Resource.Slot"/> the API
/// binds it by, which is the per-kind number the seam's callers name.
/// </para>
/// </remarks>
internal static class ComputeProgramDescription
{
    /// <summary>One resource a compute shader binds, and the descriptor it needs.</summary>
    /// <param name="Binding">The set-0 binding number, unique across every kind.</param>
    /// <param name="Slot">The number the API binds it by — the per-kind number.</param>
    /// <param name="Kind">Which kind of descriptor it is.</param>
    /// <param name="Name">The resource's name; for a sampler bound by unit, the name its unit is set by.</param>
    /// <param name="IsVolume">Whether a sampled image is a 3D volume rather than a 2D texture.</param>
    /// <param name="ByUnit">
    /// Whether the resource resolves by the texture unit assigned through <c>SetComputeUniform</c> rather
    /// than by its fixed <see cref="Slot"/>.
    /// </param>
    internal readonly record struct Resource(
        uint Binding, uint Slot, ComputeResourceKind Kind, string Name, bool IsVolume = false, bool ByUnit = false);

    /// <summary>One scalar or vector parameter, and where it sits in the globals block.</summary>
    /// <param name="Name">The parameter's name, as the seam's callers set it by.</param>
    /// <param name="Offset">Its byte offset within the block.</param>
    /// <param name="Size">How many bytes the value occupies.</param>
    /// <param name="Type">
    /// The type it was declared with. Carried rather than inferred from <paramref name="Size"/>, because
    /// a size does not identify a type — a float and an int are both four bytes, and taking one for the
    /// other silently reinterprets every value written to it.
    /// </param>
    /// <param name="ArrayLength">
    /// How many elements, for an array; zero for a plain parameter, and negative for the per-element
    /// entry that exists only so <c>name[i]</c> can be written to.
    /// </param>
    internal readonly record struct Uniform(string Name, int Offset, int Size, string Type = "", int ArrayLength = 0);

    /// <summary>Everything a program's pipeline and dispatches are built from.</summary>
    /// <param name="Resources">The storage buffers, images and samplers, by binding.</param>
    /// <param name="Uniforms">The gathered scalar parameters, by name.</param>
    /// <param name="UniformBlockBinding">The binding the globals block sits at, or -1 if there is none.</param>
    /// <param name="UniformBlockSize">The globals block's size in bytes, rounded up to 16.</param>
    internal sealed record Description(
        IReadOnlyList<Resource> Resources,
        IReadOnlyDictionary<string, Uniform> Uniforms,
        int UniformBlockBinding,
        int UniformBlockSize);
}

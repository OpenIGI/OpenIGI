namespace OpenIGI.Shaders;

/// <summary>One compiled form of a module, selected by the preprocessor defines it was built with.</summary>
/// <remarks>
/// <para>
/// Only differences the <em>preprocessor</em> makes need a variant. A value that merely changes at
/// pipeline-creation time does not: Vulkan specialization constants and their WGSL <c>override</c>
/// counterpart are both applied when the pipeline is created, from one compiled module, so the
/// alpha-cut-out threshold and the level's sun colour cost no extra build output.
/// </para>
/// <para>
/// Every module has <see cref="Default"/>. Modules that need more list them in the manifest, and the
/// build compiles each one for both targets.
/// </para>
/// </remarks>
/// <param name="Name">The variant's name, which is also part of its generated file names.</param>
public readonly record struct ShaderVariant(string Name)
{
    /// <summary>The name borne by the variant every module has.</summary>
    public const string DefaultName = "default";

    /// <summary>Gets the variant every module is compiled for, with no extra defines.</summary>
    public static ShaderVariant Default => new(DefaultName);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

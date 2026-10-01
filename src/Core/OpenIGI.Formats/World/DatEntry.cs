namespace OpenIGI.Formats.World;

/// <summary>One entry of a <c>.dat</c> pack index: a model and the textures it uses.</summary>
/// <remarks>
/// Asset ids follow the <c>GGG_VV_L</c> convention — group, variant, LOD level — so <c>000_01_1</c>,
/// <c>000_01_2</c> and <c>000_01_3</c> are the same object at decreasing detail. Non-numeric groups such
/// as <c>water</c> and <c>mapcomputer</c> also occur.
/// </remarks>
/// <param name="Model">The model id, for example <c>210_01_1</c>.</param>
/// <param name="Textures">The texture ids the model uses, in file order.</param>
public readonly record struct DatEntry(string Model, IReadOnlyList<string> Textures)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Model} ({Textures.Count} textures)";
}

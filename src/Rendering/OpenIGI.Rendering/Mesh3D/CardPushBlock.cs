using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Mesh3D;

/// <summary>The 128 bytes a card capture pushes: where the model is, and what it is made of.</summary>
/// <remarks>
/// <para>
/// A card is a flat window onto part of a model, drawn through an orthographic camera sitting on the
/// card's own plane, so the only transform there is is the one that puts the model in that window.
/// </para>
/// <para>
/// The three normal rows go unused — a page stores the model's own normal, which needs no transform —
/// but the block is shared in size and shape with the mesh paths, so they are written as nought rather
/// than left holding whatever the last draw pushed. A push block is not cleared between draws, and a
/// shader that reads a slot nobody wrote reads the previous draw's value.
/// </para>
/// </remarks>
public static class CardPushBlock
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 32;

    /// <summary>Where the material's four values start.</summary>
    public const int MaterialOffset = 28;

    /// <summary>Writes the block for one card draw.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="modelViewProjection">The transform into the card's own window.</param>
    /// <param name="material">What the surface is made of.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(
        Span<float> block,
        in Matrix4f modelViewProjection,
        in SurfaceMaterial material)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The card block is {Floats} floats, not {block.Length}.", nameof(block));
        }

        modelViewProjection.CopyColumnMajorTo(block[..16]);
        block[16..MaterialOffset].Clear();

        block[MaterialOffset] = material.Roughness;
        block[MaterialOffset + 1] = material.Metallic;
        block[MaterialOffset + 2] = material.Emissive;
        block[MaterialOffset + 3] = 0f;
    }
}

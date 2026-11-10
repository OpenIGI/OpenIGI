using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// One entry of an I.G.I. 2 model's <c>WOLG</c> (<c>GLOW</c>) chunk: a light the artists placed on the model.
/// </summary>
/// <remarks>
/// <para>
/// The sequel draws each one as a camera-facing additive sprite at the point, so a lamp's bulb blooms
/// where the model alone would only show bright paint. It casts no light there — the light a fitting
/// appears to give is still baked — but it is the one place either game says outright where a model's
/// lamps are, what colour they are and how large.
/// </para>
/// <para>
/// Project I.G.I. has no such chunk: none of its models carries one, and nothing in that game ever adds
/// to the glow list its renderer would draw.
/// </para>
/// <para>
/// The record is 32 bytes: the position, the radius, the colour and four bytes of padding, floats
/// throughout and in the model's own units.
/// </para>
/// </remarks>
/// <param name="Position">Where the glow sits, in model space and model units.</param>
/// <param name="Radius">The sprite's half-size, in model units.</param>
/// <param name="Colour">The sprite's colour, each channel between nought and one.</param>
public readonly record struct MefGlow(Vector3f Position, float Radius, Vector3f Colour)
{
    /// <summary>The size in bytes of one record.</summary>
    public const int Stride = 32;

    /// <inheritdoc/>
    public override string ToString() => $"glow at {Position} r={Radius:0.#} {Colour}";
}

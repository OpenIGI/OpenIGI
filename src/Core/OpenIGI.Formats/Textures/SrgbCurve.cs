namespace OpenIGI.Formats.Textures;

/// <summary>The sRGB transfer for a texture's bytes, by table.</summary>
/// <remarks>
/// Every authored colour in the game's textures is display-encoded. Anything that averages or weighs them
/// as light -- a lamp's tint, a bake's bounce albedo, a mip level -- has to decode each texel first: the
/// mean of encoded bytes is not the encoding of the mean, and comes out paler and less saturated than the
/// texture it was taken from.
/// </remarks>
public static class SrgbCurve
{
    // A table rather than a pow per channel per texel: the input is a byte, so there are only 256 answers.
    private static readonly float[] Table = Build();

    /// <summary>Decodes one display-encoded byte to linear light.</summary>
    /// <param name="encoded">The byte as stored.</param>
    /// <returns>Its linear value, nought to one.</returns>
    public static float ToLinear(byte encoded) => Table[encoded];

    private static float[] Build()
    {
        float[] table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float encoded = i / 255f;
            table[i] = encoded <= 0.04045f
                ? encoded / 12.92f
                : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
        }

        return table;
    }
}

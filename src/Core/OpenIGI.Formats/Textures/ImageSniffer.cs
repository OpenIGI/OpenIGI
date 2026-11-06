namespace OpenIGI.Formats.Textures;

/// <summary>Decodes a picture in whichever of the games' formats it is, recognised from its bytes.</summary>
/// <remarks>
/// The order is the one the games' own loading amounts to: the engine's format first, because it has a magic
/// number, then JPEG, then TGA, whose header is a shape rather than an identity and so is tried last.
/// </remarks>
public static class ImageSniffer
{
    /// <summary>Decodes a picture.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <param name="image">Receives the image, with whatever mip chain the file carries.</param>
    /// <returns><see langword="false"/> when the bytes are in none of the formats read here.</returns>
    /// <exception cref="TextureFormatException">The bytes claim a format but are not a valid picture in it.</exception>
    public static bool TryDecode(ReadOnlySpan<byte> data, out TextureImage? image)
    {
        image = TexFile.IsLoopAsset(data) ? TexFile.Load(data)
            : JpegImage.IsJpeg(data) ? JpegImage.Read(data)
            : TgaImage.IsTga(data) ? TgaImage.Read(data)
            : null;
        return image is not null;
    }
}

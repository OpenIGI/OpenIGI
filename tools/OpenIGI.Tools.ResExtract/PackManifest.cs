using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// What the extractor learned about each picture, so the packer can put it back where it came from.
/// </summary>
/// <remarks>
/// <para>
/// The PNG tree alone cannot say this. A picture's path tells you where its replacement goes, but not
/// which archive it came out of — <c>common/textures/</c> holds two archives — nor whether it was one
/// of 150 tiles of a loading screen, nor what size the menu lays it out at. Losing any of that means
/// the pack cannot be built without opening the game again and guessing.
/// </para>
/// <para>
/// So the manifest is written beside the PNGs and read back by <c>pack</c>. It is JSON because a person
/// should be able to look at it: when a texture comes back wrong, the first question is what the tool
/// thought that texture was, and that question should not need a debugger.
/// </para>
/// </remarks>
internal sealed record PackManifest
{
    /// <summary>The file name the extractor writes and the packer reads.</summary>
    public const string FileName = "pack.json";

    /// <summary>The layout revision, so a pack built by an older tool is refused rather than misread.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Gets the manifest layout revision.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Gets the installation this was extracted from, for diagnostics only.</summary>
    public string? Source { get; init; }

    /// <summary>Gets every picture found, in the order they were walked.</summary>
    public List<PackAsset> Assets { get; init; } = [];

    /// <summary>Reads a manifest.</summary>
    /// <param name="path">The manifest file.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidDataException">The file is not a manifest this tool understands.</exception>
    public static PackManifest Read(string path)
    {
        using FileStream file = File.OpenRead(path);

        PackManifest manifest = JsonSerializer.Deserialize<PackManifest>(file, Format)
            ?? throw new InvalidDataException($"'{path}' is empty.");

        if (manifest.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"'{path}' is a version {manifest.Version} manifest; this tool writes and reads version "
                    + $"{CurrentVersion}. Re-run `ResExtract images` to rebuild it.");
        }

        return manifest;
    }

    /// <summary>Writes this manifest.</summary>
    /// <param name="path">Where to write it.</param>
    public void Write(string path)
    {
        using FileStream file = File.Create(path);
        JsonSerializer.Serialize(file, this, Format);
    }
}

/// <summary>How a picture is put back together.</summary>
internal enum PackShape
{
    /// <summary>One picture in a single-image container.</summary>
    Texture,

    /// <summary>Several unrelated pictures addressed by index, like a level's ground tiles.</summary>
    Atlas,

    /// <summary>Several frames of one sprite, kept apart because they are not a picture between them.</summary>
    Sprite,

    /// <summary>One large picture stored as a tile grid, extracted and worked on whole.</summary>
    Picture,
}

/// <summary>One of the game's pictures, and everything needed to write a replacement for it.</summary>
/// <param name="Path">Where a replacement goes, relative to the overrides folder.</param>
/// <param name="Shape">How the frames relate to each other.</param>
/// <param name="LogicalWidth">The width anything laying this out uses, whatever the pack's pixels become.</param>
/// <param name="LogicalHeight">The height anything laying this out uses.</param>
/// <param name="Frames">The PNGs this picture was written as.</param>
/// <param name="Archive">The archive it came from, relative to the installation, or null when it was loose.</param>
/// <param name="Entry">Its name inside that archive, namespace prefix included.</param>
/// <param name="GridColumns">The tile grid's width, when the picture is one.</param>
/// <param name="GridRows">The tile grid's height, when the picture is one.</param>
internal sealed record PackAsset(
    string Path,
    PackShape Shape,
    int LogicalWidth,
    int LogicalHeight,
    List<PackFrame> Frames,
    string? Archive = null,
    string? Entry = null,
    int GridColumns = 0,
    int GridRows = 0);

/// <summary>One PNG in the extracted tree, and the sprite fields that travel with it.</summary>
/// <param name="File">The PNG's path, relative to the extraction root.</param>
/// <param name="Width">The picture's own width at extraction, which is the logical width of this frame.</param>
/// <param name="Height">The picture's own height at extraction.</param>
/// <param name="Advance">How far a glyph advances the pen; zero for anything that is not one.</param>
/// <param name="OffsetX">Where the frame draws relative to its anchor.</param>
/// <param name="OffsetY">Where the frame draws relative to its anchor.</param>
internal sealed record PackFrame(
    string File,
    int Width,
    int Height,
    int Advance = 0,
    int OffsetX = 0,
    int OffsetY = 0);

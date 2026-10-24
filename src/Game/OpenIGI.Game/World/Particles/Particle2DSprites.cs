using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Sprites;

namespace OpenIGI.Game.World.Particles;

/// <summary>
/// How many frames a particle sprite has, as a system reads its sprite's tilemap: the mist plays them over its life. Read
/// once a sprite from the title's sprite archive, and remembered.
/// </summary>
public sealed class Particle2DSprites
{
    private readonly VirtualFileSystem? _files;
    private readonly string _archive;
    private readonly Dictionary<string, int> _frames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="Particle2DSprites"/> class.</summary>
    /// <param name="files">The title's data.</param>
    /// <param name="archive">The archive its particle sprites ship in.</param>
    public Particle2DSprites(VirtualFileSystem? files, string archive = MissionLoader.CommonSpritesArchive)
    {
        _files = files;
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
    }

    /// <summary>How many frames a sprite has.</summary>
    /// <param name="sprite">The sprite's name.</param>
    /// <returns>Its frames; one when it does not resolve.</returns>
    public int FramesOf(string sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (!_frames.TryGetValue(sprite, out int frames))
        {
            frames = 1;
            try
            {
                if (_files is { } files && files.Exists(_archive) && files.ReadArchive(_archive).FindBody(sprite) is { } entry)
                {
                    frames = Math.Max(1, SpriteLoader.Load(entry.Data, sprite).Frames.Count);
                }
            }
            catch (InvalidDataException)
            {
                // A sprite that does not read shows its one frame.
            }

            _frames[sprite] = frames;
        }

        return frames;
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <summary>
/// I.G.I. 2's status art, by name, for another game's HUD drawn over an I.G.I. 2 level: the badges its action menu shows
/// (<see cref="Game.Igi2.Interaction.Igi2ActionBadge"/>), so that a player standing in is shown the level's own.
/// </summary>
public sealed class Igi2StatusArt : IHudIconSource, IDisposable
{
    private Igi2SpriteResources? _assets;
    private readonly Igi2HudIcons _icons;

    /// <summary>Initializes a new instance of the <see cref="Igi2StatusArt"/> class.</summary>
    /// <param name="device">The device the sprites are uploaded to.</param>
    /// <param name="files">I.G.I. 2's files.</param>
    /// <param name="logger">Where loading reports.</param>
    public Igi2StatusArt(IGraphicsDevice device, VirtualFileSystem files, IOpenIgiLogger logger)
    {
        _assets = new Igi2SpriteResources(device, files, logger);
        _icons = new Igi2HudIcons(() => _assets);
    }

    /// <inheritdoc/>
    public bool TryMeasure(HudIcon icon, out float width, out float height) => _icons.TryMeasure(icon, out width, out height);

    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour) =>
        _icons.Draw(batch, icon, x, y, scale, colour);

    /// <inheritdoc/>
    public SpriteResource? Resolve(HudIcon icon) => _icons.Resolve(icon);

    /// <inheritdoc/>
    public void Dispose()
    {
        _assets?.Dispose();
        _assets = null;
    }
}

using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>
/// The level's lens flares over the picture, on the original's look with its lens-flares option on: each item's sprite added at its
/// place along the line through the display's centre (<c>Lensflaretask_DrawLensflareItem</c>), texture coordinates 0.01 to 0.99.
/// </content>
public sealed partial class Igi2HudRenderer
{
    // The archive the flares' sprites are in, LOCAL:common/textures/lens00.tex to lens09.tex.
    private const string FlareArchive = "LOCAL:common/textures/textures.res";

    private Igi2TextureLoader? _flareTextures;
    private VirtualFileSystem? _files;

    private void DrawLensflares(SpriteBatch batch, Igi2TitlePlayer title, int screenWidth, int screenHeight)
    {
        if (GraphicsPresetRegistry.Default.Current != GraphicsPreset.Vanilla || title.GameConfig?.Gfx.LensFlares == false)
        {
            return;
        }

        IReadOnlyList<Igi2LensflareQuad> quads = title.Lensflares(screenWidth, screenHeight);
        if (quads.Count == 0 || FlareTextures() is not { } textures)
        {
            return;
        }

        const float near = 0.01f, far = 0.99f;
        foreach (Igi2LensflareQuad quad in quads)
        {
            TextureHandle sprite = textures.Resolve(Igi2LensflareItemTask.SpriteFile(quad.Sprite));
            if (sprite == TextureHandle.None)
            {
                continue;
            }

            Bgra32 colour = Bgra32.FromRgba(Channel(quad.Colour.X), Channel(quad.Colour.Y), Channel(quad.Colour.Z), 255);
            batch.DrawQuad(
                sprite,
                new Vertex2D(quad.X0, quad.Y0, near, near, colour),
                new Vertex2D(quad.X1, quad.Y0, far, near, colour),
                new Vertex2D(quad.X1, quad.Y1, far, far, colour),
                new Vertex2D(quad.X0, quad.Y1, near, far, colour),
                BlendMode.Additive);
        }

        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);
    }

    // The game's own textures, read through a registry that holds the archive the sprites are in.
    private Igi2TextureLoader? FlareTextures()
    {
        if (_flareTextures is null && _files is { } files && files.Exists(FlareArchive))
        {
            ResourceRegistry resources = new(files);
            resources.Register(FlareArchive, files.ReadAllBytes(FlareArchive));
            _flareTextures = new Igi2TextureLoader(_device, resources, _logger);
        }

        return _flareTextures;
    }

    private void ReleaseLensflares()
    {
        _flareTextures = null;
    }
}

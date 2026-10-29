using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Igi1.Computer;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// Draws Project I.G.I.'s computer as the original does, from the state the mission's tick leaves in <see cref="Igi1Computer"/>:
/// the map's own view of the level, the screens' layers either side of the device model, and the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The original draws the device in the player's view and the screens over it, each layer at its own depth, so the casing hides
/// whatever lies behind its plane. In painter's order that is two passes: <see cref="DrawBehindDevice"/> -- the green
/// multiply over the map (<c>0x46AE30</c>, flags <c>0xC0040000</c>, <c>ZERO, SRCCOLOR</c>) or a page's black, the marks, the
/// interference and the boot box -- before the device, and <see cref="DrawInFrontOfDevice"/> -- the controls, the pages' text
/// and the pointer, nearer the eye than the casing -- after it.
/// </para>
/// <para>
/// Everything is laid out at the display size <see cref="Igi1Computer"/> was opened with and magnified by the interface's
/// whole-number scale, the one divergence the interface makes everywhere.
/// </para>
/// </remarks>
internal sealed partial class MapComputerRenderer : IMapComputerPresenter, IComputerFonts
{
    public const string LocalArchive = "LOCAL:computer/computer.res";

    /// <summary>The level's menu archive, whose fonts the screens are written in (<c>MenuManager_FindFont</c>, <c>0x418E40</c>).</summary>
    public const string MenuArchive = "LOCAL:menusystem/ingamemenu.res";

    private readonly IOpenIgiLogger _logger;
    private readonly Igi1Gameplay? _game;
    private readonly RetailUiResources _uiResources;

    // The in-game menu's fonts: the links, the pages, the labels. Only the boot box loads the computer's own font3.
    private readonly RetailUiResources _menuResources;
    private readonly List<ComputerWord> _labelWords = [];

    // LineFilter's own stream (0x4A2CD0 draws from gFilterRandom, never the game's).
    private readonly IgiRandom _filterRandom = IgiRandom.Seeded();
    private IresArchive? _archive;
    private float _scale = 1f;
    private bool _disposed;

    public MapComputerRenderer(IGraphicsDevice device, IOpenIgiLogger logger, IMissionGameplay? gameplay)
    {
        _logger = logger;
        _game = gameplay as Igi1Gameplay;
        _uiResources = new RetailUiResources(device, new RetailUiResourceOptions { LogCategory = "Computer" }, logger);
        _menuResources = new RetailUiResources(device, new RetailUiResourceOptions { LogCategory = "Computer" }, logger);
    }

    public bool IsReady => _archive is not null && Computer is not null;

    /// <inheritdoc/>
    public bool ShowsMap => Computer is { IsOpen: true, Screen: ComputerMap };

    /// <inheritdoc/>
    public bool ShowsDevice => Computer is { IsOpen: true, ShowsDevice: true };

    /// <inheritdoc/>
    public bool MapAvailable { get; set; } = true;

    /// <inheritdoc/>
    public WorldScene? Scene { get; set; }

    private Igi1Computer? Computer => _game?.World.Computer;

    /// <inheritdoc/>
    public ComputerPointer? Pointer => Computer is { IsOpen: true } computer
        ? new ComputerPointer(computer.PointerX, computer.PointerY, computer.IsDragging, computer.Width)
        : null;

    public bool Load(VirtualFileSystem fileSystem)
    {
        try
        {
            if (!fileSystem.Exists(LocalArchive))
            {
                _logger.Warning("Computer", $"No map-computer archive at {LocalArchive}.");
                return false;
            }

            _archive = fileSystem.ReadArchive(LocalArchive);
            _uiResources.SetArchives(_archive);
            if (fileSystem.Exists(MenuArchive))
            {
                _menuResources.SetArchives(fileSystem.ReadArchive(MenuArchive));
            }
            if (_game is not null)
            {
                _game.ComputerWorld.Fonts = this;
                _game.ComputerWorld.SpriteSizes = SpriteSize;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Computer", $"Could not load the map computer: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public int StringWidth(string font, string text) => Font(font) is { } bitmap ? (int)bitmap.Measure(text).Width : text.Length * 8;

    /// <inheritdoc/>
    public int LineHeight(string font) => Font(font)?.LineHeight ?? 16;

    /// <summary>The map's own camera, as the last tick set it (<c>0x46A0D0</c>): straight down over the level, across the whole display.</summary>
    public MapSceneView SceneView(int screenWidth, int screenHeight)
    {
        ComputerMap? map = Computer?.Screen as ComputerMap;
        Vector3d at = WorldScene.ToMetres(map?.CameraPosition ?? default);
        float fov = map?.CameraFieldOfView ?? 1f;

        // The camera's aspect is one: the tangent across is the field of view, the one down the display's share of it.
        float vertical = 2f * MathF.Atan(fov * screenHeight / Math.Max(1, screenWidth));
        Camera3D camera = new(at, Camera3D.LookRotation(0f, -MathF.PI * 0.5f));
        return new MapSceneView(camera, vertical, new Viewport(0, 0, screenWidth, screenHeight));
    }

    /// <inheritdoc/>
    public void DrawBehindDevice(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Computer is not { IsOpen: true } computer)
        {
            return;
        }

        _scale = Math.Max(1f, MathF.Floor(screenWidth / Math.Max(1f, computer.Width)));
        if (computer.Screen is ComputerMap map)
        {
            // The objects' outlines are drawn in the map's own view, so the multiply over it tints them too.
            DrawOutlines(batch, computer, map);

            // 0x46AE30: the picture multiplied by (0, 127, 0) of 128 -- what was drawn of the level, in its green alone.
            batch.FillRect(new RectF(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 253, 0, 255), BlendMode.Modulate);
            DrawMarks(batch, computer, map);
            DrawNoise(batch, map.Noise, screenWidth, screenHeight);
            DrawStartup(batch, computer, map.Startup);
        }
        else
        {
            // 0x469110 and 0x468BC0: a page is black behind the device.
            batch.FillRect(new RectF(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 255));
        }
    }

    /// <inheritdoc/>
    public void DrawInFrontOfDevice(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Computer is not { IsOpen: true } computer)
        {
            return;
        }

        if (computer.Screen is ComputerPage page)
        {
            DrawPage(batch, page);
        }

        foreach (ComputerItem item in computer.Panel.Items)
        {
            DrawItem(batch, item);
        }

        // 0x46AE30: the pointer centred on its point, hidden while the sphere drags.
        if (!computer.IsDragging && Frame("pointer.spr", 0) is { } pointer)
        {
            float x = computer.PointerX - (pointer.Sprite.Width * 0.5f), y = computer.PointerY - (pointer.Sprite.Height * 0.5f);
            DrawFrame(batch, pointer, x, y, Unmodified);
        }
    }

    /// <summary>Draws the mission-failed menu reached after retail's 151-tick LevelFlow grace period.</summary>
    public void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Bgra32 red = Bgra32.FromRgba(220, 45, 35, 255), bright = Bgra32.FromRgba(64, 225, 33, 255);
        batch.FillRect(new RectF(0, 0, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 205));
        float left = (screenWidth - 440f) * 0.5f;
        float top = (screenHeight - 150f) * 0.5f;
        batch.FillRect(new RectF(left, top, 440, 150), Bgra32.FromRgba(21, 29, 15, 255));
        Font("font3.fnt")?.Draw(batch, "MISSION FAILED", left + 105, top + 28, red);
        Font("font3.fnt")?.Draw(batch, "ENTER: RESTART MISSION", left + 78, top + 82, bright, 0.65f, 0.65f);
        Font("font3.fnt")?.Draw(batch, "ESC: END MISSION", left + 110, top + 112, bright, 0.65f, 0.65f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_game is not null && ReferenceEquals(_game.ComputerWorld.Fonts, this))
        {
            _game.ComputerWorld.Fonts = null;
            _game.ComputerWorld.SpriteSizes = null;
        }

        _uiResources.Dispose();
        _menuResources.Dispose();
    }

    // 0x46B5C0 and 0x46B890.
    private void DrawItem(SpriteBatch batch, ComputerItem item)
    {
        switch (item)
        {
            case ComputerButtonItem button when Frame(button.Sprite, 0) is { } frame:
                DrawFrame(batch, frame, button.X, button.Y, Unmodified);
                break;
            case ComputerTextItem text:
                (float r, float g, float b) = text.Draw();
                DrawText(batch, text.Font, text.Text, text.X, text.Y, ItemColour(r, g, b));
                break;
        }
    }

    // The pages' words, and the log's rule under each line: a hundred pixels from (48, 96, 48) to (0, 16, 0).
    private void DrawPage(SpriteBatch batch, ComputerPage page)
    {
        foreach (ComputerPageWord word in page.Words)
        {
            (float r, float g, float b) = ComputerColors.Items[word.Color];
            DrawText(batch, word.Font, word.Text, word.X, word.Y, ItemColour(r, g, b));
        }

        foreach ((int x, int y) in page.Rules)
        {
            Line(batch, x, y, x + 100, y, Bgra32.FromRgba(95, 191, 95, 255), Bgra32.FromRgba(0, 31, 0, 255));
        }
    }

    // The original's sprite and text colours are on a scale of 128; 128 leaves the art as it is.
    private static readonly Bgra32 Unmodified = BitmapTextRenderer.NeutralColor;

    private static Bgra32 ItemColour(float r, float g, float b) => BitmapTextRenderer.FromRetail(r, g, b);

    private BitmapTextRenderer? Font(string name) => _menuResources.Font(name) ?? _uiResources.Font(name);

    // The boot box's own font (0x466240 loads LOCAL:computer/font3.fnt itself).
    private BitmapTextRenderer? ComputerFont(string name) => _uiResources.Font(name);

    private (int Width, int Height) SpriteSize(string name) =>
        _uiResources.Sprite(name) is { Asset.Frames.Count: > 0 } sprite ? (sprite.Asset.Frames[0].Width, sprite.Asset.Frames[0].Height) : (0, 0);

    private SpriteFrameRef? Frame(string name, int frame) =>
        _uiResources.Sprite(name) is { Asset.Frames.Count: > 0 } sprite
            ? new SpriteFrameRef(sprite, sprite.Asset.Frames[Math.Clamp(frame, 0, sprite.Asset.Frames.Count - 1)], sprite.Asset.Frames[0])
            : null;

    private void DrawFrame(SpriteBatch batch, SpriteFrameRef frame, float x, float y, Bgra32 colour) =>
        frame.Resource.DrawFrame(batch, frame.Frame, x * _scale, y * _scale, _scale, _scale, colour);

    private void DrawText(SpriteBatch batch, string font, string text, float x, float y, Bgra32 colour) =>
        DrawText(batch, Font(font), text, x, y, colour);

    private void DrawText(SpriteBatch batch, BitmapTextRenderer? font, string text, float x, float y, Bgra32 colour) =>
        font?.Draw(batch, text, x * _scale, y * _scale, colour, _scale, _scale);

    // A one-pixel line in display pixels, its colour running from one end to the other.
    private void Line(SpriteBatch batch, float x0, float y0, float x1, float y1, Bgra32 from, Bgra32 to)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0f)
        {
            return;
        }

        float nx = -dy / length * 0.5f * _scale, ny = dx / length * 0.5f * _scale;
        float ax = x0 * _scale, ay = y0 * _scale, bx = x1 * _scale, by = y1 * _scale;
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(ax + nx, ay + ny, 0f, 0f, from),
            new Vertex2D(bx + nx, by + ny, 0f, 0f, to),
            new Vertex2D(bx - nx, by - ny, 0f, 0f, to),
            new Vertex2D(ax - nx, ay - ny, 0f, 0f, from));
    }

    private readonly record struct SpriteFrameRef(SpriteResource Resource, SpriteFrame Frame, SpriteFrame Sprite);
}

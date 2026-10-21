using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws IGI1's C-key SIMOTECH map computer, objectives and event log.</summary>
internal sealed class MapComputerRenderer : IMapComputerPresenter
{
    public const string Archive = "COMPUTER:computer.res";
    public const string LocalArchive = "LOCAL:computer/computer.res";
    public const string ComputerStringsArchive = "LOCAL:language/english/computer.res";
    public const string ObjectiveStringsArchive = "LOCAL:language/english/objectives.res";
    public const string MessageStringsArchive = "LOCAL:language/english/messages.res";

    // 0x53F750: the four values selected by the retail map's +/- callbacks.
    private static readonly float[] MapFieldsOfView =
        [0.7853982f, 0.3272492f, 0.1396263f, 0.009599311f];

    private static readonly Bgra32 ScreenBlack = new(2, 10, 3, 255);
    private static readonly Bgra32 ScreenGreen = new(15, 150, 29, 255);
    private static readonly Bgra32 ScreenBright = new(33, 225, 64, 255);
    private static readonly Bgra32 ScreenRed = new(35, 45, 220, 255);
    private const float PageLinkScaleMultiplier = 2f;
    private const float PageLinkHitHeight = 14f;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Func<string, string> _resolveText;
    private readonly RetailUiResources _uiResources;
    private IresArchive? _archive;
    private Page _page;
    // Retail opens on its useful mission-overview scale; the widest level is reached with minus.
    private int _zoom = 1;
    private Vector3d _centreUnits;
    private Vector3d _playerUnits;
    private bool _hasCentre;
    private float _mouseX;
    private float _mouseY;
    private bool _mapDragActive;
    private bool _disposed;

    public MapComputerRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger logger,
        Func<string, string> resolveText)
    {
        _device = device;
        _resources = resources;
        _logger = logger;
        _resolveText = resolveText;
        _uiResources = new RetailUiResources(device, new RetailUiResourceOptions { LogCategory = "Computer" }, logger);
    }

    public bool IsReady => _archive is not null;

    /// <summary>Whether the live overhead world pass, rather than a text page, should be drawn.</summary>
    public bool ShowsMap => _page == Page.Map;

    /// <inheritdoc/>
    public bool MapAvailable { get; set; } = true;

    public bool Load(VirtualFileSystem fileSystem)
    {
        try
        {
            // The retail installation ships this at LOCAL:computer/computer.res. COMPUTER: is an
            // optional script alias, but probing an absent VFS device is fatal.
            string? name = fileSystem.Exists(LocalArchive) ? LocalArchive : null;
            if (name is null)
            {
                _logger.Warning("Computer", $"No map-computer archive at {Archive} or {LocalArchive}.");
                return false;
            }

            _archive = IresArchive.Read(fileSystem.ReadAllBytes(name));
            _uiResources.SetArchives(_archive);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Computer", $"Could not load the map computer: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts a new computer session centred on Jones, as 0x469740 does.</summary>
    public void Open(Vector3d playerUnits)
    {
        _playerUnits = playerUnits;
        _centreUnits = playerUnits;
        _hasCentre = true;
        _page = MapAvailable ? Page.Map : Page.Objectives;
    }

    /// <summary>Handles page links plus direct mouse pan and zoom on the map.</summary>
    public void Update(InputState input, int screenWidth, int screenHeight)
    {
        _mouseX = input.Mouse.X;
        _mouseY = input.Mouse.Y;

        bool leftPressed = input.Mouse.WasPressed(MouseButton.Left);
        bool middlePressed = input.Mouse.WasPressed(MouseButton.Middle);
        bool leftDown = input.Mouse.IsDown(MouseButton.Left);
        bool middleDown = input.Mouse.IsDown(MouseButton.Middle);
        Viewport device = DeviceViewportFor(screenWidth, screenHeight);
        float linkScale = PageLinkScale(device);
        float linkX = device.X + device.Width * 0.1f;
        float logY = LogLinkY(device);
        float objectivesY = ObjectivesLinkY(device);
        bool pageLinkPressed = false;
        if (!MapAvailable && _page == Page.Map)
        {
            _page = Page.Objectives;
        }

        if (leftPressed)
        {
            if (LinkTarget(Page.Objectives) is { } objectivesTarget
                && HitTextLink(linkX, objectivesY, 175f * linkScale, PageLinkHitHeight * linkScale))
            {
                _page = objectivesTarget;
                pageLinkPressed = true;
            }
            else if (LinkTarget(Page.Log) is { } logTarget
                && HitTextLink(linkX, logY, 100f * linkScale, PageLinkHitHeight * linkScale))
            {
                _page = logTarget;
                pageLinkPressed = true;
            }
        }

        if (_page != Page.Map)
        {
            _mapDragActive = false;
            return;
        }

        Viewport map = ScreenViewport(screenWidth, screenHeight);
        if (!leftDown && !middleDown)
        {
            _mapDragActive = false;
        }
        else if (!_mapDragActive
            && (leftPressed || middlePressed)
            && Hit(map.X, map.Y, map.Width, map.Height)
            && (!pageLinkPressed || middlePressed))
        {
            _mapDragActive = true;
        }

        if (_mapDragActive)
        {
            PanByDrag(input.Mouse.DeltaX, input.Mouse.DeltaY, map);
        }

        if (Hit(map.X, map.Y, map.Width, map.Height) && input.Mouse.WheelDelta != 0f)
        {
            _zoom = Math.Clamp(_zoom + (input.Mouse.WheelDelta > 0f ? 1 : -1), 0, 3);
        }
    }

    /// <summary>Builds the retail vertical map camera and its inset CRT viewport.</summary>
    public MapSceneView SceneView(Vector3d playerUnits, int screenWidth, int screenHeight)
    {
        _playerUnits = playerUnits;
        if (!_hasCentre)
        {
            _centreUnits = playerUnits;
            _hasCentre = true;
        }

        Viewport viewport = ScreenViewport(screenWidth, screenHeight);
        Vector3d centreMetres = ToMetres(_centreUnits);
        // The map owns a genuine second 3D view in retail. Looking straight down keeps terrain,
        // buildings and moving units live instead of replacing them with a fabricated grid.
        Camera3D camera = new(
            new Vector3d(centreMetres.X, centreMetres.Y, centreMetres.Z + 900.0),
            Camera3D.LookRotation(0f, -MathF.PI * 0.5f));
        return new MapSceneView(camera, MapFieldsOfView[_zoom], viewport);
    }

    public void Draw(
        SpriteBatch batch,
        MissionObjectiveRuntime runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(runtime);
        _playerUnits = player;

        switch (_page)
        {
            case Page.Map:
                DrawMapGlass(batch, runtime, player, guards, highlights, screenWidth, screenHeight);
                break;
            case Page.Objectives:
                DrawTextScreen(batch, screenWidth, screenHeight);
                DrawObjectives(batch, runtime, screenWidth, screenHeight);
                break;
            case Page.Log:
                DrawTextScreen(batch, screenWidth, screenHeight);
                DrawLog(batch, runtime, screenWidth, screenHeight);
                break;
        }

        DrawPageLinks(batch, screenWidth, screenHeight);
        DrawPointer(batch);
    }

    /// <summary>Draws the mission-failed menu reached after retail's 151-tick LevelFlow grace period.</summary>
    public void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        batch.FillRect(new RectF(0, 0, screenWidth, screenHeight), new Bgra32(0, 0, 0, 205));
        float left = (screenWidth - 440f) * 0.5f;
        float top = (screenHeight - 150f) * 0.5f;
        batch.FillRect(new RectF(left, top, 440, 150), new Bgra32(15, 29, 21, 255));
        DrawText(batch, "font3.fnt", "MISSION FAILED", left + 105, top + 28, 1f, ScreenRed);
        DrawText(batch, "font3.fnt", "ENTER: RESTART MISSION", left + 78, top + 82, 0.65f, ScreenBright);
        DrawText(batch, "font3.fnt", "ESC: END MISSION", left + 110, top + 112, 0.65f, ScreenBright);
    }

    private void DrawMapGlass(SpriteBatch batch, MissionObjectiveRuntime runtime, Vector3d player,
        IReadOnlyList<Vector3d> guards, IEnumerable<ComputerMapHighlight> highlights, int width, int height)
    {
        Viewport screen = ScreenViewport(width, height);
        // The live world pass is converted to green phosphor in the resolve shader. Keep only this
        // faint tube reflection here; an opaque tint makes terrain and architecture disappear.
        batch.FillRect(new RectF(screen.X, screen.Y, screen.Width, screen.Height), new Bgra32(0, 18, 0, 18));

        // The mission scripts own these labels and their linked world positions. Retail only exposes
        // them once the user has zoomed past the widest overview, where the text would overlap badly.
        if (_zoom >= 1)
        {
            float uiScale = ComputerUiScale(DeviceViewportFor(width, height));
            List<ComputerMapHighlight> candidates = highlights
                .GroupBy(highlight => highlight.TitleResource, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Count())
                .SelectMany(group => group)
                .ToList();
            List<(float X, float Y)> occupied = [];
            foreach (ComputerMapHighlight highlight in candidates)
            {
                if (!TryProject(highlight.Position, screen, out float x, out float y))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(highlight.Sprite))
                {
                    DrawIcon(batch, highlight.Sprite, x, y);
                }
                bool labelClear = occupied.All(point =>
                    MathF.Abs(point.X - x) > 115f * uiScale || MathF.Abs(point.Y - y) > 24f * uiScale);
                if (labelClear && !string.IsNullOrWhiteSpace(highlight.TitleResource))
                {
                    DrawText(batch, "font4.fnt", ComputerString(highlight.TitleResource),
                        x + 6f * uiScale, y - 8f * uiScale, uiScale, ScreenRed);
                    occupied.Add((x, y));
                }
            }
        }

        foreach (Vector3d guard in guards)
        {
            if (TryProject(guard, screen, out float x, out float y))
            {
                DrawIcon(batch, "h_guard.spr", x, y, ScreenRed);
            }
        }

        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (objective.Position is not { } position || objective.State == MissionObjectiveState.Failed)
            {
                continue;
            }
            if (TryProject(position, screen, out float x, out float y))
            {
                DrawIcon(batch, objective.State == MissionObjectiveState.Complete
                    ? "objective_2.spr" : "objective_1.spr", x, y);
            }
        }

        if (TryProject(player, screen, out float px, out float py))
        {
            DrawIcon(batch, "h_player.spr", px, py);
        }

        DrawScanlines(batch, screen);
    }

    private static void DrawTextScreen(SpriteBatch batch, int width, int height)
    {
        Viewport screen = ScreenViewport(width, height);
        batch.FillRect(new RectF(screen.X, screen.Y, screen.Width, screen.Height), ScreenBlack);
        DrawScanlines(batch, screen);
    }

    private void DrawObjectives(SpriteBatch batch, MissionObjectiveRuntime runtime, int width, int height)
    {
        Viewport device = DeviceViewportFor(width, height);
        float scale = ComputerUiScale(device);
        float x = device.X + device.Width * 0.1f;
        float y = device.Y + device.Height * 0.1f;
        for (int i = 0; i < runtime.Objectives.Count; i++)
        {
            MissionObjective objective = runtime.Objectives[i];
            string state = ComputerString(objective.State.ToString().ToUpperInvariant());
            DrawText(batch, "font3.fnt", $"{ComputerString("OBJECTIVE")} {i + 1}: {state}", x, y, scale, ScreenBright);
            y += FontLineHeight("font3.fnt") * scale;
            y += DrawWrappedText(batch, "font4.fnt", ComputerString(objective.TextResource), x, y, scale, ScreenBright, 400f * scale);
            y += 8f * scale;
        }
    }

    private void DrawLog(SpriteBatch batch, MissionObjectiveRuntime runtime, int width, int height)
    {
        Viewport device = DeviceViewportFor(width, height);
        float scale = ComputerUiScale(device);
        float x = device.X + device.Width * 0.15f;
        float y = device.Y + device.Height * 0.1f;
        foreach (MissionComputerLogEntry entry in runtime.Log.TakeLast(15).Reverse())
        {
            y += DrawWrappedText(batch, "font1.fnt", ComputerString(entry.TextResource), x, y, scale, ScreenBright, 400f * scale);
            batch.FillRect(new RectF(x, y + 5f * scale, 100f * scale, Math.Max(1f, scale)), new Bgra32(48, 96, 48, 255));
            y += 16f * scale;
        }
    }

    private void DrawPageLinks(SpriteBatch batch, int width, int height)
    {
        Viewport device = DeviceViewportFor(width, height);
        float scale = PageLinkScale(device);
        float x = device.X + device.Width * 0.1f;
        float logY = LogLinkY(device);
        float objectivesY = ObjectivesLinkY(device);
        if (LinkTarget(Page.Log) is { } first)
        {
            DrawBoldText(batch, "font3.fnt", ComputerString(first == Page.Map ? "MAP" : "LOG"), x, logY, scale,
                HitTextLink(x, logY, 100f * scale, PageLinkHitHeight * scale) ? ScreenBright : ScreenGreen);
        }

        if (LinkTarget(Page.Objectives) is { } second)
        {
            DrawBoldText(batch, "font3.fnt", ComputerString(second == Page.Map ? "MAP" : "OBJECTIVES"), x, objectivesY, scale,
                HitTextLink(x, objectivesY, 175f * scale, PageLinkHitHeight * scale) ? ScreenBright : ScreenGreen);
        }
    }

    /// <summary>Where a page's link leads from the current page: back to the map from that page itself.</summary>
    /// <remarks>Nothing when that is the map and the level has not given the player the device.</remarks>
    private Page? LinkTarget(Page link)
    {
        Page target = _page == link ? Page.Map : link;
        return target == Page.Map && !MapAvailable ? null : target;
    }

    private void DrawPointer(SpriteBatch batch) => DrawSprite(batch, "pointer.spr", _mouseX, _mouseY);

    private static void DrawScanlines(SpriteBatch batch, Viewport screen)
    {
        Bgra32 line = new(0, 0, 0, 18);
        for (int y = screen.Y + 1; y < screen.Y + screen.Height; y += 3)
        {
            batch.FillRect(new RectF(screen.X, y, screen.Width, 1), line);
        }
    }

    private void PanByDrag(float deltaX, float deltaY, Viewport viewport)
    {
        if (deltaX == 0f && deltaY == 0f)
        {
            return;
        }

        // Keep the drag conversion in the same units and projection as TryProject: moving the
        // pointer moves the map under it, rather than applying a frame-rate-dependent nudge.
        double altitudeUnits = 900.0 * 4096.0;
        double halfHeight = altitudeUnits * Math.Tan(MapFieldsOfView[_zoom] * 0.5);
        double halfWidth = halfHeight * viewport.Width / Math.Max(1.0, viewport.Height);
        double unitsPerPixelX = 2.0 * halfWidth / Math.Max(1, viewport.Width);
        double unitsPerPixelY = 2.0 * halfHeight / Math.Max(1, viewport.Height);
        _centreUnits = new Vector3d(
            _centreUnits.X - deltaX * unitsPerPixelX,
            _centreUnits.Y + deltaY * unitsPerPixelY,
            _centreUnits.Z);
    }

    private bool TryProject(Vector3d worldUnits, Viewport viewport, out float x, out float y)
    {
        double altitudeUnits = 900.0 * 4096.0;
        double halfHeight = altitudeUnits * Math.Tan(MapFieldsOfView[_zoom] * 0.5);
        double halfWidth = halfHeight * viewport.Width / Math.Max(1.0, viewport.Height);
        x = viewport.X + viewport.Width * 0.5f
            + (float)((worldUnits.X - _centreUnits.X) / (2.0 * halfWidth) * viewport.Width);
        y = viewport.Y + viewport.Height * 0.5f
            - (float)((worldUnits.Y - _centreUnits.Y) / (2.0 * halfHeight) * viewport.Height);
        return x >= viewport.X && x <= viewport.X + viewport.Width
            && y >= viewport.Y && y <= viewport.Y + viewport.Height;
    }

    private void DrawIcon(SpriteBatch batch, string name, float centreX, float centreY, Bgra32? colour = null)
    {
        if (Sprite(name) is not { } sprite || sprite.Asset.Frames.Count == 0)
        {
            return;
        }
        SpriteFrame frame = sprite.Asset.Frames[0];
        sprite.DrawFrame(batch, frame, centreX - frame.Width * 0.5f, centreY - frame.Height * 0.5f,
            1f, 1f, colour ?? BitmapTextRenderer.NeutralColor);
    }

    private void DrawSprite(SpriteBatch batch, string name, float x, float y, Bgra32? colour = null, float scale = 1f)
    {
        if (Sprite(name) is { Asset.Frames.Count: > 0 } sprite)
        {
            sprite.DrawFrame(batch, sprite.Asset.Frames[0], x, y, scale, scale,
                colour ?? BitmapTextRenderer.NeutralColor);
        }
    }

    private string ComputerString(string key)
    {
        if (_resources.TryGetEntry(key, out ResourceEntry? entry))
        {
            ReadOnlySpan<byte> bytes = entry.Data.Span;
            int terminator = bytes.IndexOf((byte)0);
            return System.Text.Encoding.Latin1.GetString(terminator < 0 ? bytes : bytes[..terminator]);
        }
        return _resolveText(key);
    }

    private float DrawWrappedText(SpriteBatch batch, string fontName, string text, float x, float y,
        float scale, Bgra32 colour, float maxWidth)
    {
        if (Font(fontName) is not { } font || string.IsNullOrWhiteSpace(text))
        {
            return 0f;
        }
        float start = y;
        string line = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.Measure(candidate, scale, scale).Width > maxWidth)
            {
                font.Draw(batch, line, x, y, colour, scale, scale);
                y += font.LineHeight * scale;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
        {
            font.Draw(batch, line, x, y, colour, scale, scale);
            y += font.LineHeight * scale;
        }
        return y - start;
    }

    private void DrawText(SpriteBatch batch, string fontName, string text, float x, float y, float scale, Bgra32 colour)
    {
        if (Font(fontName) is { } font && !string.IsNullOrEmpty(text))
        {
            font.Draw(batch, text, x, y, colour, scale, scale);
        }
    }

    private void DrawBoldText(SpriteBatch batch, string fontName, string text, float x, float y, float scale, Bgra32 colour)
    {
        DrawText(batch, fontName, text, x, y, scale, colour);
        float stroke = MathF.Max(1f, scale * 0.5f);
        DrawText(batch, fontName, text, x + stroke, y, scale, colour);
        DrawText(batch, fontName, text, x, y + stroke, scale, colour);
    }

    private float FontLineHeight(string name) => Font(name)?.LineHeight ?? 12f;

    private BitmapTextRenderer? Font(string name) => _uiResources.Font(name);

    private SpriteResource? Sprite(string name) => _uiResources.Sprite(name);

    private bool HitTextLink(float x, float y, float width, float height) => Hit(x, y, width, height);
    private bool Hit(float x, float y, float width, float height) =>
        _mouseX >= x && _mouseX <= x + width && _mouseY >= y && _mouseY <= y + height;

    /// <summary>Gets the centred 4:3 presentation occupied by the retail handheld computer.</summary>
    public Viewport DeviceViewport(int width, int height) => DeviceViewportFor(width, height);

    /// <summary>The same, without needing an instance, for this class's own layout maths.</summary>
    internal static Viewport DeviceViewportFor(int width, int height)
    {
        int displayWidth = Math.Max(1, width);
        int displayHeight = Math.Max(1, height);
        int deviceHeight = Math.Min(displayHeight, (int)MathF.Round(displayWidth * 0.75f));
        int deviceWidth = Math.Min(displayWidth, (int)MathF.Round(deviceHeight * (4f / 3f)));
        return new Viewport(
            (displayWidth - deviceWidth) / 2,
            (displayHeight - deviceHeight) / 2,
            deviceWidth,
            deviceHeight);
    }

    private static Viewport ScreenViewport(int width, int height)
    {
        Viewport device = DeviceViewportFor(width, height);
        return new Viewport(
            device.X + Math.Max(1, (int)(device.Width * 0.045f)),
            device.Y + Math.Max(1, (int)(device.Height * 0.0525f)),
            Math.Max(1, (int)(device.Width * 0.91f)),
            Math.Max(1, (int)(device.Height * 0.8525f)));
    }

    private static float ComputerUiScale(Viewport device) =>
        MathF.Max(0.5f, MathF.Min(device.Width / 1000f, device.Height / 750f));

    private static float PageLinkScale(Viewport device) => ComputerUiScale(device) * PageLinkScaleMultiplier;

    private static float LogLinkY(Viewport device) => device.Y + device.Height * 0.735f;

    private static float ObjectivesLinkY(Viewport device) => device.Y + device.Height * 0.795f;

    private static Vector3d ToMetres(Vector3d units) => new(units.X / 4096.0, units.Y / 4096.0, units.Z / 4096.0);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _uiResources.Dispose();
        _archive = null;
    }

    private enum Page { Map, Objectives, Log }
}

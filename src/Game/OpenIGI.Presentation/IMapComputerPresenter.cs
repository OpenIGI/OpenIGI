using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>Lists the places on a level that its map computer marks.</summary>
/// <remarks>
/// Title content because what is worth marking is decided by the level's own task types — the catalogue
/// walks the tree looking for the tasks one game uses to say "put a symbol here", and another game's
/// tree says it differently or not at all.
/// </remarks>
public interface IComputerHighlightCatalogue
{
    /// <summary>Gets the marks that should be showing now.</summary>
    /// <returns>The visible marks; empty when the level marks nothing.</returns>
    IEnumerable<ComputerMapHighlight> Visible();
}

/// <summary>Draws a title's handheld map computer.</summary>
/// <remarks>
/// <para>
/// Title content twice over: the device is one game's own model and screens, and its pages are laid out
/// from that game's own string archives. <c>ITitleDataLayout.StringArchives</c> is where those
/// paths live now, so the shell can preload them without naming a renderer.
/// </para>
/// <para>
/// <see cref="ITacticalComputerPresenter"/> is the modern alternative to this, chosen by a setting, the
/// same way the two HUDs are alternatives. They share <see cref="MapSceneView"/> so the host can frame
/// the world behind either without knowing which it has.
/// </para>
/// </remarks>
public interface IMapComputerPresenter : IDisposable
{
    /// <summary>Gets whether the archive loaded and the device can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Gets whether the device is currently showing its map page.</summary>
    bool ShowsMap { get; }

    /// <summary>Gets or sets whether the level lets the player have the map page at all.</summary>
    /// <remarks>
    /// The level's <c>HumanPlayerInput</c> carries a "Mapcomputer on expression", evaluated every tick
    /// (<c>0x45DCF0</c>). Without the device the computer still opens, on its objectives page
    /// (<c>0x46BB10</c> passes mode 2 to <c>0x46AFB0</c>), and neither text page offers a MAP link
    /// (<c>0x468920</c>, <c>0x468E90</c>). Missions 7 and 14 never have it, mission 8 gains it with
    /// GenericPickup 860, and mission 13 loses it inside AreaActivate 3999.
    /// </remarks>
    bool MapAvailable { get; set; }

    /// <summary>Loads the device's screens and strings.</summary>
    /// <param name="fileSystem">The file system the archives are read through.</param>
    /// <returns><see langword="false"/> when the installation ships no such device.</returns>
    bool Load(VirtualFileSystem fileSystem);

    /// <summary>Gets whether the device itself is held: only while the level grants the map.</summary>
    bool ShowsDevice { get; }

    /// <summary>Gets or sets the level's placed objects, which the map outlines.</summary>
    WorldScene? Scene { get; set; }

    /// <summary>Gets how the world behind the device is framed: the map's own camera, as the mission's tick last set it.</summary>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <returns>The view to draw the world with.</returns>
    MapSceneView SceneView(int screenWidth, int screenHeight);

    /// <summary>Draws what lies behind the device's casing: its screen's picture, tint and marks.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawBehindDevice(SpriteBatch batch, int screenWidth, int screenHeight);

    /// <summary>Draws what lies in front of the casing: the controls, the text and the pointer.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawInFrontOfDevice(SpriteBatch batch, int screenWidth, int screenHeight);

    /// <summary>Draws the mission-failed page.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight);

    /// <summary>Gets the computer's own pointer while it is open, for a touch screen that has to steer it to a finger.</summary>
    /// <remarks>The original's computer moves its pointer by the mouse's counts alone, never to a place; see <see cref="ComputerPointer"/>.</remarks>
    ComputerPointer? Pointer => null;
}

/// <summary>Draws the modern alternative to a title's map computer.</summary>
public interface ITacticalComputerPresenter : IDisposable
{
    /// <summary>Gets whether the fonts loaded and the display can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Gets the field of view the world behind the display is framed at.</summary>
    float FieldOfView { get; }

    /// <summary>Gets or sets whether the level lets the player have the map page at all.</summary>
    /// <remarks>
    /// The level's <c>HumanPlayerInput</c> carries a "Mapcomputer on expression", evaluated every tick
    /// (<c>0x45DCF0</c>). Without the device the computer still opens, on its objectives page
    /// (<c>0x46BB10</c> passes mode 2 to <c>0x46AFB0</c>), and neither text page offers a MAP link
    /// (<c>0x468920</c>, <c>0x468E90</c>). Missions 7 and 14 never have it, mission 8 gains it with
    /// GenericPickup 860, and mission 13 loses it inside AreaActivate 3999.
    /// </remarks>
    bool MapAvailable { get; set; }

    /// <summary>
    /// Gets or sets what the display's save page saves and loads with, as I.G.I. 2's computer does; <see langword="null"/> for a
    /// mission that keeps no save games, which has no such page.
    /// </summary>
    OpenIGI.Game.Computer.IComputerSaves? Saves
    {
        get => null;
        set => _ = value; // Ignored: only an implementer that keeps the value overrides this.
    }

    /// <summary>
    /// Gets or sets where the display's sounds are played, by their definitions' names: the original computer's own
    /// (<c>mapcomp_01</c>, <c>mapcomp_03</c>, <c>mapcomp_05</c>), unplaced; <see langword="null"/> for a silent display.
    /// </summary>
    Action<string>? PlaySound
    {
        get => null;
        set => _ = value; // Ignored: only an implementer that keeps the value overrides this.
    }

    /// <summary>Loads the fonts the display is drawn from.</summary>
    /// <returns><see langword="false"/> when they are unavailable.</returns>
    bool Load();

    /// <summary>Opens the display, centred on where the player is standing.</summary>
    /// <param name="playerUnits">Where the player is, in engine units.</param>
    void Open(Vector3d playerUnits);

    /// <summary>Advances the display for one frame.</summary>
    /// <param name="input">What the player is pressing.</param>
    /// <param name="elapsedSeconds">Real wall-clock seconds since the previous frame.</param>
    /// <param name="playerUnits">Where the player is, in engine units.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void Update(InputState input, float elapsedSeconds, Vector3d playerUnits, int screenWidth, int screenHeight);

    /// <summary>Gets how the world behind the display should be framed.</summary>
    /// <param name="playerUnits">Where the player is, in engine units.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <returns>The view to draw the world with.</returns>
    MapSceneView SceneView(Vector3d playerUnits, int screenWidth, int screenHeight);

    /// <summary>Gets where the camera stands while the display is open.</summary>
    /// <returns>The pose to view the world from.</returns>
    CameraPose Vantage();

    /// <summary>Draws the display.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="runtime">The mission's objectives and transmissions, which the display lists: whichever game's mission it is.</param>
    /// <param name="player">Where the player is, in engine units.</param>
    /// <param name="guards">Where the guards the display knows about are.</param>
    /// <param name="highlights">The marks to show.</param>
    /// <param name="frame">The screen, how far the display has come up, and which way the player faces.</param>
    void Draw(
        SpriteBatch batch,
        IComputerObjectives runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        in ComputerFrame frame);

    /// <summary>Draws the mission-failed page.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight);
}

/// <summary>What the remastered computer is drawn with this frame.</summary>
/// <param name="Width">The screen's width in pixels.</param>
/// <param name="Height">The screen's height in pixels.</param>
/// <param name="RequestedScale">The interface scale the player asked for, or automatic.</param>
/// <param name="Opacity">How far the interface has come up, nought to one.</param>
/// <param name="Feed">How far the map's feed has come up; its look is the computer's own to add.</param>
/// <param name="HeadingRadians">Which way the player faces: nought along +Y, turning towards +X.</param>
public readonly record struct ComputerFrame(int Width, int Height, int RequestedScale, float Opacity, MapFeedSettings Feed, float HeadingRadians)
{
    /// <summary>Gets the name of the key that shuts the computer, for its hint; empty for none.</summary>
    public string CloseKey { get; init; } = string.Empty;
}

/// <summary>Where a title's own font comes from, for the remastered computer to set its text in.</summary>
public interface IComputerFontSource : IDisposable
{
    /// <summary>Gets the title's font, loaded on its first use; <see langword="null"/> where the installation has none.</summary>
    Rendering.BitmapTextRenderer? Font { get; }
}

/// <summary>What a title needs in order to build its map computer.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Resources">The registry the device's own textures are resolved through.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <param name="ResolveText">
/// Turns a string resource name into the text to show. The mission owns the table, so the presenter is
/// handed the lookup rather than the archive: which language a name resolves in is the shell's business
/// and not a renderer's.
/// </param>
/// <param name="Gameplay">The mission the computer belongs to, whose own computer a title's presenter draws; or none.</param>
public readonly record struct MapComputerPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    IOpenIgiLogger Logger,
    Func<string, string> ResolveText,
    IMissionGameplay? Gameplay = null);

/// <summary>What a title needs in order to build the catalogue of places its computer marks.</summary>
/// <param name="LevelRoot">The level's task tree, which the marks are read from.</param>
/// <param name="Variables">The script variables a mark's visibility is gated on.</param>
/// <param name="Expressions">The level's expressions, or <see langword="null"/> for a catalogue that compiles with its own.</param>
public readonly record struct ComputerHighlightRequest(GameTask LevelRoot, ScriptVarRegistry Variables, LevelExpressionRuntime? Expressions = null);

/// <summary>The second, straight-down 3D view owned by the retail map page.</summary>
/// <remarks>
/// In the seam rather than beside the renderer that fills it, because it is what the interface
/// takes: a type a presenter names in its signature is part of the contract, not an implementation
/// detail of one title.
/// </remarks>
public readonly record struct MapSceneView(Camera3D Camera, float FieldOfView, Viewport Viewport);

/// <summary>Where the original's computer has its pointer, in its own display's pixels (the window over the interface's scale).</summary>
/// <param name="X">Across.</param>
/// <param name="Y">Down.</param>
/// <param name="Dragging">Whether the map is being dragged, which holds the pointer still and turns the map by the counts instead.</param>
/// <param name="Width">The computer display's width, which a count's travel is measured against.</param>
public readonly record struct ComputerPointer(float X, float Y, bool Dragging, float Width);

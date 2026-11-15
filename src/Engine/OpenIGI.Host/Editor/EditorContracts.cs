using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host.Editor;

/// <summary>
/// Opens editor sessions. Supplied by the desktop composition root only, so the browser and server builds never
/// link the editor: with no factory the host simply has no editor.
/// </summary>
public interface IEditorFactory
{
    /// <summary>Opens a session over the running host.</summary>
    /// <param name="host">What the session may ask of the host.</param>
    /// <returns>The session; the host disposes it when the editor closes.</returns>
    IEditorSession Open(IEditorHost host);
}

/// <summary>
/// One open editor: it owns the frame's input and draws over the world while it is open, and is disposed — every
/// panel, document and GPU resource with it — when it closes.
/// </summary>
public interface IEditorSession : IDisposable
{
    /// <summary>Gets where in the window the world is shown, or <see langword="null"/> when no world is.</summary>
    /// <remarks>The host projects the world so that it fills this rectangle exactly, undistorted.</remarks>
    UiRect? Viewport { get; }

    /// <summary>Gets whether the level is running (play in editor) rather than held still for editing.</summary>
    /// <remarks>While playing, the game has the whole window and the input; the session only draws its banner.</remarks>
    bool IsPlaying { get; }

    /// <summary>Runs an editor command typed at the console after <c>editor</c>, such as <c>select Crate</c>.</summary>
    /// <param name="arguments">The words after <c>editor</c>.</param>
    /// <returns>What to print, or <see langword="null"/> when the session does not know the command.</returns>
    /// <remarks>A command given before the mission has loaded waits for it, so a launch can name what to select.</remarks>
    string? RunCommand(IReadOnlyList<string> arguments);

    /// <summary>Ends play and goes back to editing; the host calls it when the player presses Escape.</summary>
    void StopPlaying();

    /// <summary>Takes a frame of play: the game has the window and the input, and the session only runs what waits on playing.</summary>
    void WhilePlaying()
    {
    }

    /// <summary>Lays out the interface and handles the frame's input.</summary>
    /// <param name="elapsedSeconds">The real time since the last frame.</param>
    /// <param name="hasInput">Whether the session may read input this frame; false while the console has it.</param>
    void Frame(float elapsedSeconds, bool hasInput);

    /// <summary>Draws the interface over the frame.</summary>
    /// <param name="painter">The painter, over a 2D pass at one unit per pixel.</param>
    void Draw(UiPainter painter);
}

/// <summary>The last rendered frame's view, for picking and for drawing gizmos in the world.</summary>
/// <param name="Eye">The camera position in metres.</param>
/// <param name="ViewProjection">The unjittered world-to-clip transform, for the whole window.</param>
/// <param name="Width">The window width the transform maps to.</param>
/// <param name="Height">The window height.</param>
public readonly record struct EditorView(Vector3d Eye, Matrix4f ViewProjection, float Width, float Height);

/// <summary>A play-test's controls as they stand, while a mission is played from the editor.</summary>
/// <param name="Paused">Whether the level is held still (F6; F7 steps it a tick).</param>
/// <param name="Speed">How fast it runs: 1 is the game's own pace (F8 faster, Shift+F8 slower).</param>
/// <param name="God">Whether nothing hurts the player (F2).</param>
/// <param name="Unseen">Whether guards and cameras do not see the player (F3).</param>
/// <param name="HasCamera">Whether there is an editor camera to stand the player at (F4).</param>
public readonly record struct EditorPlaytest(bool Paused, float Speed, bool God, bool Unseen, bool HasCamera)
{
    /// <summary>Gets whether the guards' minds are drawn over the game: their sight, what each does, where each goes (F10).</summary>
    public bool Overlay { get; init; }
}

/// <summary>The loaded level seen from straight above, as a picture and the world square it covers.</summary>
/// <param name="Width">The picture's width in pixels.</param>
/// <param name="Height">The picture's height in pixels.</param>
/// <param name="Pixels">The picture, row by row from its north edge: ground green by height, buildings stone grey, edged.</param>
/// <param name="WestMetres">The world X of its left edge, in metres.</param>
/// <param name="NorthMetres">The world Y of its top edge, in metres.</param>
/// <param name="MetresPerPixel">How much of the world a pixel covers.</param>
public sealed record OverheadMap(int Width, int Height, Bgra32[] Pixels, double WestMetres, double NorthMetres, double MetresPerPixel)
{
    /// <summary>Gets the height under each pixel in metres, roofs included; NaN where nothing is.</summary>
    public float[] HeightsMetres { get; init; } = [];

    /// <summary>Gets the height of the first surface under a point, in metres, or null off the map.</summary>
    /// <param name="xMetres">East.</param>
    /// <param name="yMetres">North.</param>
    /// <returns>The height.</returns>
    public double? HeightAt(double xMetres, double yMetres)
    {
        int column = (int)Math.Floor((xMetres - WestMetres) / MetresPerPixel);
        int row = (int)Math.Floor((NorthMetres - yMetres) / MetresPerPixel);
        if ((uint)column >= (uint)Width || (uint)row >= (uint)Height || HeightsMetres.Length != Width * Height)
        {
            return null;
        }

        float h = HeightsMetres[(row * Width) + column];
        return float.IsNaN(h) ? null : h;
    }
}

/// <summary>What an editor session may ask of the host.</summary>
public interface IEditorHost
{
    /// <summary>Gets the graphics device, for fonts and textures.</summary>
    IGraphicsDevice Device { get; }

    /// <summary>Gets the audio device, for previewing sounds.</summary>
    IAudioDevice Audio { get; }

    /// <summary>Gets the window's input.</summary>
    InputState Input { get; }

    /// <summary>Gets the window width in pixels.</summary>
    float Width { get; }

    /// <summary>Gets the window height in pixels.</summary>
    float Height { get; }

    /// <summary>Gets the size the editor draws at: the <c>ed_ui_scale</c> setting, or one picked from the screen.</summary>
    float UiScale { get; }

    /// <summary>Gets or sets the system clipboard's text.</summary>
    string ClipboardText { get; set; }

    /// <summary>Sets whether the cursor is locked to the window, as while flying the viewport camera.</summary>
    bool CursorLocked { set; }

    /// <summary>Gets the running game.</summary>
    GameTitle Title { get; }

    /// <summary>Gets the games this build runs that are installed, the running one among them.</summary>
    IReadOnlyList<GameTitle> InstalledTitles { get; }

    /// <summary>Closes the editor, switches the session to another game, and opens the editor again in it.</summary>
    /// <param name="titleId">The game to switch to (<c>igi1</c>, <c>igi2</c>).</param>
    /// <param name="editorCommands">Editor commands to run once it is open again, such as one that reopens a wizard.</param>
    void SwitchTitle(string titleId, IReadOnlyList<string[]> editorCommands);

    /// <summary>Gets the running game's missions, in campaign order.</summary>
    IReadOnlyList<MissionDescriptor> Missions { get; }

    /// <summary>Gets a mission's name as the menus show it, in the player's language.</summary>
    /// <param name="mission">The mission.</param>
    /// <returns>The name.</returns>
    string MissionName(MissionDescriptor mission);

    /// <summary>Gets the loaded mission, or <see langword="null"/>.</summary>
    MissionDescriptor? Mission { get; }

    /// <summary>Gets whether a loaded level is still settling behind its loading panel.</summary>
    bool IsWarmingUp { get; }

    /// <summary>Gets the file system the game reads through, with its overlays.</summary>
    VirtualFileSystem Files { get; }

    /// <summary>Gets the free camera the world is drawn from, once a level is loaded.</summary>
    FreeCamera? Camera { get; }

    /// <summary>Reads the texture a model's draw record samples, as the running level binds that model.</summary>
    /// <param name="model">The model's name, as level data spells it (<c>435_01_1</c>).</param>
    /// <param name="materialSlot">The draw record's material slot.</param>
    /// <param name="data">Receives the texture file.</param>
    /// <returns>Whether the level knows the model and its texture could be read.</returns>
    bool TryReadModelTexture(string model, int materialSlot, out ReadOnlyMemory<byte> data);

    /// <summary>Gets what plays a skinned model in the running level's clips, or <see langword="null"/>.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The animator, or <see langword="null"/> for a model with no rig, no clips, or no level loaded.</returns>
    /// <param name="name">The model's name as level data spells it (<c>003_01_1</c>), so it is posed as whoever wears it; null when not known.</param>
    Game.World.IModelAnimator? AnimateModel(Formats.World.MefModel model, string? name = null);

    /// <summary>Plays an animation file (a set or a clip) on a body of the running level that fits it, for a viewer.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The body and its animator, or <see langword="null"/> when the bytes are not one or no body fits.</returns>
    Game.World.AnimationFilePreview? PreviewAnimation(ReadOnlyMemory<byte> file);

    /// <summary>Gets the named task fields the running level's scripts can read (<c>CutScene_1200.isRun</c>), live.</summary>
    Engine.Tasks.ScriptVarRegistry ScriptVariables { get; }

    /// <summary>
    /// Gets everything the running title's scripts can name: natives with typed signatures, constant families, and the
    /// fields every task type exports, which the editor builds its graph nodes and pin types from.
    /// </summary>
    /// <remarks>Built once per title; callers must not add to it.</remarks>
    Scripting.Vocabulary.ScriptVocabulary Vocabulary { get; }

    /// <summary>
    /// Gets every name the running game's scripts know now: its natives and the constants registered with them, the ones
    /// its content defines included (each weapon's and ammo's own id constant).
    /// </summary>
    IReadOnlyCollection<string> ScriptNames => [];

    /// <summary>The words a mission text resource stands for (a status message's <c>M1_STATUS_03</c>), as the game shows them.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The text, or <see langword="null"/> when the key is not a text resource.</returns>
    string? MissionText(string key);

    /// <summary>Gets whether the running level has a model by the name level data gives it, or null when there is no level.</summary>
    /// <param name="model">The model's name.</param>
    /// <returns>Whether it exists.</returns>
    bool? HasModel(string model);

    /// <summary>Finds the first surface below a point, objects and terrain alike, as something dropped there lands on.</summary>
    /// <param name="pointMetres">The point, in world metres.</param>
    /// <param name="reachMetres">How far down to look.</param>
    /// <returns>The surface's height in world metres, or null when nothing is that close below.</returns>
    double? SurfaceBelow(Core.Numerics.Vector3d pointMetres, double reachMetres);

    /// <summary>Gets the level's ground as the game stands on it, for finding the ground under a point anywhere in the level.</summary>
    Game.World.IGroundHeightField? Ground { get; }

    /// <summary>Gets the open mission's ground as height grids, when it is one the editor can change.</summary>
    Game.World.IEditableTerrain? Terrain { get; }

    /// <summary>Draws the loaded level from straight above, a ray cast down for each pixel: slow, so asked for once a level.</summary>
    /// <param name="maxPixels">The most pixels a side.</param>
    /// <returns>The map, or null when no level is up.</returns>
    OverheadMap? MakeOverheadMap(int maxPixels) => null;

    /// <summary>The loaded level as it looks from straight above: its ground, its models' faces and its collision, to be drawn at any zoom and floor.</summary>
    /// <returns>The scene to come, or null when no level is up.</returns>
    /// <remarks>
    /// Gathered from the level as it is loaded, on the calling (frame's) thread -- the ground, the placements, the collision and
    /// the texture files -- and finished off it: the textures decoded and every face's ground found.
    /// </remarks>
    Task<OverheadScene>? MakeOverheadScene() => null;

    /// <summary>Gets the play-test's controls as they stand.</summary>
    EditorPlaytest Playtest => default;

    /// <summary>Applies a play-test command: pause, run, step [n], speed &lt;x&gt;, god, unseen, camera.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What happened, to say.</returns>
    string PlaytestCommand(string command) => "Play-testing is not offered here.";

    /// <summary>Copies every soldier of the running level as it stands: where, and what its mind is doing.</summary>
    /// <param name="into">The list to fill, cleared first.</param>
    void SnapshotSoldiers(List<Game.Ai.SoldierSnapshot> into) => into?.Clear();

    /// <summary>Gets the last rendered frame's view.</summary>
    EditorView View { get; }

    /// <summary>Gets how many times a level has been loaded: a save made since the last load is not yet in the level that is up.</summary>
    int LevelLoads => 0;

    /// <summary>Gets what plays the mission's cutscenes on the level itself, or <see langword="null"/> where the game has nothing to.</summary>
    IEditorCutscenePreview? CutscenePreview => null;

    /// <summary>Gets the world inspector: entities, their positions and screen projections.</summary>
    IWorldInspector Inspector { get; }

    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Reads a game file exactly as the game would: through the content layers, the editor's project first.</summary>
    /// <param name="path">The path as the game names it: a device or alias, a colon, and the path under it.</param>
    /// <param name="data">Receives the bytes.</param>
    /// <returns><see langword="false"/> when no layer and no archive has the file.</returns>
    bool TryReadFile(string path, out ReadOnlyMemory<byte> data);

    /// <summary>
    /// Reads a game file as the original game would find it: the editor project's own copy when it has one, else the
    /// installation's, never another content layer (remastered textures and the like, which the original cannot read).
    /// </summary>
    /// <param name="path">The path as the game names it: a device, a colon, and the path under it.</param>
    /// <param name="data">Receives the bytes.</param>
    /// <returns><see langword="false"/> when neither the project nor the installation has the file.</returns>
    bool TryReadShippedFile(string path, out ReadOnlyMemory<byte> data);

    /// <summary>Puts a folder on top of the content layers, so what is saved into it is what the game loads.</summary>
    /// <param name="id">The layer's id; mounting the same id again rescans it.</param>
    /// <param name="name">The name the <c>content</c> command lists it under.</param>
    /// <param name="root">The folder, mirroring the installation.</param>
    void MountContent(string id, string name, string root);

    /// <summary>Takes a folder mounted by <see cref="MountContent"/> off the content layers.</summary>
    /// <param name="id">The layer's id.</param>
    void UnmountContent(string id);

    /// <summary>Gets what the loaded level placed in the world, each with the task that owns it.</summary>
    IReadOnlyList<WorldPlacement> Placements { get; }

    /// <summary>Draws everything a task placed moved and turned, from the next frame, without rebuilding the level.</summary>
    /// <param name="owner">What the task placed, as its placements are keyed (<see cref="PlacementKey.Of"/>).</param>
    /// <param name="pose">How far from where it was built; an identity pose puts it back.</param>
    void SetPlacementPose(PlacementKey owner, EditorPose pose);

    /// <summary>Hides or shows everything a task placed, from the next frame.</summary>
    /// <param name="owner">What the task placed.</param>
    /// <param name="hidden">Whether to hide it.</param>
    void SetPlacementHidden(PlacementKey owner, bool hidden);

    /// <summary>Draws only what some tasks placed, or everything again.</summary>
    /// <param name="owners">What to keep drawing, or <see langword="null"/> to end the isolation.</param>
    void SetPlacementIsolation(IReadOnlyCollection<PlacementKey>? owners);

    /// <summary>
    /// Draws another copy of everything a task placed, where it was built, under a key of its own: how a duplicated, pasted
    /// or newly made object appears without the level being rebuilt. Only the picture: collision and lighting catch up when
    /// the level is reloaded.
    /// </summary>
    /// <param name="source">What to copy.</param>
    /// <returns>The copy's key, or <see langword="null"/> when the source placed nothing.</returns>
    PlacementKey? SpawnPlacementCopy(PlacementKey source);

    /// <summary>
    /// Draws a model the level did not place, found by the name level data gives it, under a key of its own: how an object of
    /// a model nothing in the level draws appears without a reload. Only the picture, as with <see cref="SpawnPlacementCopy"/>.
    /// </summary>
    /// <param name="model">The model's name, as a task's <c>Model</c> parameter spells it.</param>
    /// <param name="positionMetres">Where it stands, in world metres.</param>
    /// <param name="orientation">How it is turned.</param>
    /// <returns>Its key, or <see langword="null"/> when the title cannot find the model or offers no lookup.</returns>
    PlacementKey? SpawnPlacement(string model, Vector3d positionMetres, Matrix3f orientation);

    /// <summary>Loads the open mission again from the files as they now stand, keeping the camera where it is.</summary>
    /// <param name="play">Whether to start it as the player (play in editor) rather than held still for editing.</param>
    void ReloadMission(bool play = false);

    /// <summary>Loads a mission to edit, replacing any loaded one. Runs between frames.</summary>
    /// <param name="id">The mission id.</param>
    void OpenMission(int id);

    /// <summary>Closes the mission the editor has open, leaving the editor open with none.</summary>
    void CloseMission();

    /// <summary>Looks for the missions again, after one was made or its descriptor changed.</summary>
    void RescanMissions();

    /// <summary>Closes the editor and returns to the main menu. Runs between frames.</summary>
    void Close();
}

/// <summary>
/// A cutscene played on the level itself, by the game's own code, while the editor holds everything else still: its vehicles
/// drive, its soldiers move, its sounds play and its camera rides, in real time or at any moment of the scene.
/// </summary>
/// <remarks>
/// The level is loaded afresh when the preview ends, so nothing the scene does stays behind. A scene
/// whose story starts it of itself from where the level stands (an intro, or a scene that follows one) is reached as the game
/// reaches it; any other is made to play from its start, the containers it stands in let in.
/// </remarks>
public interface IEditorCutscenePreview
{
    /// <summary>Gets the scene being previewed, by task id, or null when none is.</summary>
    int? Scene { get; }

    /// <summary>Gets or sets whether the scene plays on in real time.</summary>
    bool IsPlaying { get; set; }

    /// <summary>Gets or sets whether the frame is drawn through the scene's own camera, as the player would see it.</summary>
    bool ShowsCamera { get; set; }

    /// <summary>Gets where the scene stands, in seconds of its clock.</summary>
    double Seconds { get; }

    /// <summary>Gets whether the scene has played to its end.</summary>
    bool IsFinished { get; }

    /// <summary>
    /// Gets whether the level is being loaded afresh to play the scene again up to a moment behind the one shown: a moment is
    /// always the one play reaches, so going back means playing again. <see cref="Seconds"/> is the moment asked for meanwhile.
    /// </summary>
    bool IsPreparing { get; }

    /// <summary>Gets how the scene was reached: false when the level reached it of itself, true when it was made to play.</summary>
    bool WasForced { get; }

    /// <summary>Gets the share of the picture's height the scene leaves between its letterbox bars, one for none.</summary>
    float ViewportHeight { get; }

    /// <summary>Starts previewing a scene at its start, ending any other preview first.</summary>
    /// <param name="sceneId">The scene's task id.</param>
    /// <returns>Whether it could be: false when the level has no such scene.</returns>
    bool Begin(int sceneId);

    /// <summary>Puts the level where it stands at a moment of the scene, backwards or forwards.</summary>
    /// <param name="seconds">Seconds of the scene's clock.</param>
    void Seek(double seconds);

    /// <summary>Ends the preview; a level it ran is loaded afresh, so the editor goes on with one nothing has run.</summary>
    void Finish();
}
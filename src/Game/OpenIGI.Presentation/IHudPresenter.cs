using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI;

namespace OpenIGI.Presentation;

/// <summary>Draws the HUD for a player whose gameplay is supplied by its title.</summary>
public interface ITitleHudPresenter : IDisposable
{
    /// <summary>Loads the title's sprites and fonts before drawing.</summary>
    /// <param name="files">The installed title data.</param>
    void Load(VirtualFileSystem files);

    /// <summary>Draws into an open sprite batch, with state read from the title's own player.</summary>
    /// <param name="batch">The open batch.</param>
    /// <param name="player">The playing title's player.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="requestedScale">
    /// The interface magnification the player asked for (<c>-uiscale</c>): <see cref="UiScale.Automatic"/> for the title to derive
    /// its own by the shared rule from the size its interface was laid out for (<see cref="UiScale.Resolve(int, int, int, int, int)"/>),
    /// or a whole number every title's interface is drawn at.
    /// </param>
    void Draw(SpriteBatch batch, OpenIGI.Game.Player.ITitlePlayer player, int screenWidth, int screenHeight, int requestedScale = UiScale.Automatic);

    /// <summary>
    /// Gives the display the status messages of the mission its player stands in, when that is another game's mission: they are
    /// shown with the display's own, in its own look. Set before each <see cref="Draw"/>; empty in a mission of the title's own.
    /// </summary>
    /// <param name="messages">The mission's messages as they stand.</param>
    void ShowMissionMessages(IReadOnlyList<OpenIGI.Game.World.StatusMessageDisplay> messages)
    {
    }
}

/// <summary>Draws a title's heads-up display, in the shape the original drew it.</summary>
/// <remarks>
/// <para>
/// Rule 3 of <c>docs/architecture.md</c> names a HUD element as title content outright, and this is why:
/// the ammunition counter, the health bar, the sniper scope and the action badge are one game's own
/// layout, drawn from one game's own font and sprite archives. Nothing about the shape of a status
/// screen carries to another game.
/// </para>
/// <para>
/// The two HUD presenters are alternatives, not layers: this one reproduces the original's, and
/// <see cref="IRemasteredHudPresenter"/> is the modern reskin the <c>r_hud_remastered</c> setting
/// selects between. <see cref="RemasteredHudPresentation"/> holds the predicates that choose, and is
/// title-agnostic because choosing between two styles is not itself a style.
/// </para>
/// </remarks>
public interface IHudPresenter : IDisposable
{
    /// <summary>Gets whether the archives loaded and the display can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Loads the fonts and sprites the display is drawn from.</summary>
    /// <param name="fileSystem">The file system the archives are read through.</param>
    /// <returns><see langword="false"/> when the installation ships none of them.</returns>
    bool Load(VirtualFileSystem fileSystem);

    /// <summary>Advances whatever in the display animates on its own.</summary>
    void Tick();

    /// <summary>Draws the weapon and ammunition panel.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="weapon">The weapon in hand, or null when empty-handed.</param>
    /// <param name="roundsInClip">Rounds left in the magazine.</param>
    /// <param name="reserve">Rounds left in reserve.</param>
    /// <param name="secondSubCounter">The second counter some weapons carry.</param>
    /// <param name="m203Reserve">Grenade rounds left for an under-barrel launcher.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="scale">The interface scale to draw at.</param>
    void Draw(
        SpriteBatch batch,
        WeaponType? weapon,
        int roundsInClip,
        int reserve,
        int secondSubCounter,
        int m203Reserve,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native);

    /// <summary>Draws the health bar, the interaction ring and the mission clock.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="healthFraction">How much health is left, from zero to one.</param>
    /// <param name="interactionProgress">How far a held interaction has run, or null for none.</param>
    /// <param name="missionTimeRemainingTicks">Ticks left on a timed mission, or null when untimed.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="scale">The interface scale to draw at.</param>
    void DrawPlayerStatus(
        SpriteBatch batch,
        float healthFraction,
        float? interactionProgress,
        int? missionTimeRemainingTicks,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native);

    /// <summary>Draws the sniper scope over the frame.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawSniperScope(SpriteBatch batch, int screenWidth, int screenHeight);

    /// <summary>Draws the binocular overlay and whatever it has picked out.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="targets">The boxes over spotted enemies, or null for none.</param>
    /// <param name="headingRadians">Where the player is looking, for the compass strip.</param>
    /// <param name="simulationTick">The tick, for anything that blinks.</param>
    /// <param name="rangeMetres">The range to what is centred, or null when nothing is.</param>
    void DrawBinocularOverlay(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets = null,
        float headingRadians = 0f,
        int simulationTick = 0,
        float? rangeMetres = null);

    /// <summary>Draws the badge for the action available where the player is looking.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="spriteName">The sprite naming the action.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="scale">The interface scale to draw at.</param>
    void DrawAction(
        SpriteBatch batch,
        string spriteName,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native);

    /// <summary>Draws the mission messages currently on screen.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="messages">The messages to show, in display order.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="scale">The interface scale to draw at.</param>
    void DrawStatusMessages(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float scale = 1f);

    /// <summary>Draws the recording timecode a cut-scene shot wears.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The shot being played.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawHandicamTimecode(SpriteBatch batch, CutsceneFrame frame, int screenWidth, int screenHeight);
}

/// <summary>Draws the modern reskin of a title's heads-up display.</summary>
/// <remarks>
/// A reskin in the strict sense: it shows the same facts, at the same moments, as
/// <see cref="IHudPresenter"/> — nothing here can make a message arrive earlier or a counter say
/// something different. It is still title content, because what it reskins is one game's display.
/// </remarks>
public interface IRemasteredHudPresenter : IDisposable
{
    /// <summary>Gets whether the fonts loaded and the display can be drawn.</summary>
    bool IsReady { get; }

    /// <summary>Gets how far below the top of a design-sized screen the message column starts.</summary>
    /// <remarks>
    /// On the interface because the host lays out its own message cards to the same inset, and a
    /// column that started somewhere else would read as two unrelated stacks of text.
    /// </remarks>
    float MessageTopInset { get; }

    /// <summary>Loads the fonts the display is drawn from.</summary>
    /// <returns><see langword="false"/> when they are unavailable.</returns>
    bool Load();

    /// <summary>Draws the display.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="snapshot">Everything the display shows this frame.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void Draw(SpriteBatch batch, in RemasteredHudSnapshot snapshot, int screenWidth, int screenHeight);

    /// <summary>Draws a session's scoreboard over the picture, while its key is held.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="board">The scoreboard.</param>
    /// <param name="localEntity">This end's own player's entity, whose line is marked, or <see langword="null"/>.</param>
    /// <param name="roomCode">The room's code, when the session is played through one.</param>
    /// <param name="place">Where it is played: the mission or map.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="controls">The host's hand on the board -- the players it may remove, and the pointer -- or <see langword="null"/> for a board only read.</param>
    /// <param name="relayed">Whether this player's connection goes through the TURN relay.</param>
    /// <param name="mode">The mode played, a <see cref="Game.Multiplayer.Modes.GameModes"/> id; empty for co-op.</param>
    /// <param name="besideVote">Whether it shares the screen with the vote for the next map, on its left, its rows fitted to the
    /// room under the match's card; otherwise it stands alone in the middle.</param>
    void DrawScoreboard(
        SpriteBatch batch,
        Game.Multiplayer.Scoreboard board,
        int? localEntity,
        string? roomCode,
        string place,
        int screenWidth,
        int screenHeight,
        ScoreboardControls? controls = null,
        bool relayed = false,
        string mode = "",
        bool besideVote = false)
    {
    }

    /// <summary>Gets where the last board drew each player's remove button, by entity, in pixels: what a host's click is tested against.</summary>
    IReadOnlyList<(int Entity, float X, float Y, float Width, float Height)> ScoreboardRemoveButtons => [];

    /// <summary>Draws the panel a fallen co-op player sees while it waits for a teammate: its revival so far.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="progress">How far the revival has gone, in <c>[0,1]</c>.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawDowned(SpriteBatch batch, float progress, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws the match's banner across the top: its mode and clock, a decided match's result, a warning to get back in play.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="banner">What to show.</param>
    /// <param name="warning">A warning over everything else (out of bounds), or <see langword="null"/>.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawMatchBanner(SpriteBatch batch, Game.Multiplayer.Modes.MatchBanner banner, string? warning, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws a multiplayer mode's HUD: the score strip, the bomb, a plant or defuse under way, a decided round's card.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="hud">What to show.</param>
    /// <param name="warning">A warning over everything else (out of bounds), or <see langword="null"/>.</param>
    /// <param name="useKey">The use key's name, as its hints show it.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="buyKey">The buy menu's key's name.</param>
    void DrawMatchHud(SpriteBatch batch, Game.Multiplayer.Modes.MatchHud hud, string? warning, string useKey, int screenWidth, int screenHeight, string buyKey = "G")
    {
    }

    /// <summary>Draws a match's buy menu: what the armoury sells, each with its price, those the player cannot afford dimmed.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="items">What it sells.</param>
    /// <param name="money">The player's money, or <see langword="null"/> for a free loadout (a deathmatch's).</param>
    /// <param name="buyClock">The buy time left, as <c>0:14</c>, or <see langword="null"/> for a loadout picked at any time.</param>
    /// <param name="pointer">Where the pointer is, in pixels.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawBuyMenu(
        SpriteBatch batch,
        IReadOnlyList<Game.Multiplayer.Modes.ArmouryItem> items,
        int? money,
        string? buyClock,
        (float X, float Y) pointer,
        int screenWidth,
        int screenHeight)
    {
    }

    /// <summary>Gets where the last buy menu drew each item's row, by its index, in pixels: what a click is tested against.</summary>
    IReadOnlyList<(int Index, float X, float Y, float Width, float Height)> BuyMenuRows => [];

    /// <summary>Draws the vote for the next map under a decided match's card: a card for each map offered, with its votes.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="vote">The vote, as the local player sees it.</param>
    /// <param name="clock">The time left to vote, as <c>0:12</c>, or <see langword="null"/>.</param>
    /// <param name="pointer">Where the pointer is, in pixels.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="besideBoard">Whether it shares the screen with the scoreboard, on its right, the maps in two columns; otherwise
    /// it stands alone across the middle.</param>
    void DrawMapVote(
        SpriteBatch batch, Game.Multiplayer.Modes.NextMapVote vote, string? clock, (float X, float Y) pointer, int screenWidth, int screenHeight, bool besideBoard = false)
    {
    }

    /// <summary>Gets where the last vote drew each map's card, by its index, in pixels: what a click is tested against.</summary>
    IReadOnlyList<(int Index, float X, float Y, float Width, float Height)> MapVoteCards => [];

    /// <summary>Draws whom a dead player watches, low in the middle of the screen, and how to watch the next: its killer, for
    /// the kill cam's moment, named in red with how far off it stood.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="watched">The watched player.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawSpectating(SpriteBatch batch, Game.Multiplayer.Modes.SpectateTarget watched, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws the net graph: a few lines on the connection, on a plate at the right of the screen.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawNetGraph(SpriteBatch batch, IReadOnlyList<string> lines, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws the local player's combat feedback round the crosshair: the hit marker, and where shots that struck it came from.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="hud">What to show.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawCombat(SpriteBatch batch, Game.Multiplayer.Modes.CombatHud hud, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws the kills of late down the top right corner, each killer and victim in its team's colour.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="kills">The kills, oldest first.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawKillFeed(SpriteBatch batch, IReadOnlyList<Game.Multiplayer.Modes.KillNote> kills, int screenWidth, int screenHeight)
    {
    }

    /// <summary>Draws other players' names over their heads, each standing in the level and turned to the eye.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="tags">The names and where they stand.</param>
    /// <param name="eye">Where the world is seen from.</param>
    /// <param name="horizontalTangent">The horizontal half-tangent of the view.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawNameTags(
        SpriteBatch batch,
        IReadOnlyList<Game.Ai.WorldNameTag> tags,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
    }

    /// <summary>Draws co-op talk: the lines said, the one being typed, and the places marked, each with who and how far.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="chat">The lines to show, oldest first: those still up, or the whole history while a line is typed.</param>
    /// <param name="entry">The line being typed, or <see langword="null"/>.</param>
    /// <param name="warning">A word about this player's connection, or <see langword="null"/>.</param>
    /// <param name="talking">Who is talking now, by name.</param>
    /// <param name="marks">The places marked.</param>
    /// <param name="eye">Where the world is seen from.</param>
    /// <param name="horizontalTangent">The horizontal half-tangent of the view.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawCoopTalk(
        SpriteBatch batch,
        IReadOnlyList<HudChatLine> chat,
        HudChatEntry? entry,
        string? warning,
        IReadOnlyList<string> talking,
        IReadOnlyList<HudMark> marks,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
    }

    /// <summary>Draws the prompt for the action available where the player is looking.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="view">What the prompt should say and where it is anchored.</param>
    /// <param name="eye">Where the world is seen from, for projecting the anchor.</param>
    /// <param name="horizontalTangent">The horizontal half-tangent of the view.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawInteractionPrompt(
        SpriteBatch batch,
        in InteractionPromptView view,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight);

    /// <summary>Draws the ring that fills while an interaction is held.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="progress">How far the interaction has run, from zero to one.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void DrawInteractionProgress(SpriteBatch batch, float progress, int screenWidth, int screenHeight);
}

/// <summary>What a title needs in order to build its heads-up display.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Logger">Where load diagnostics go.</param>
public readonly record struct HudPresentationRequest(IGraphicsDevice Device, IOpenIgiLogger Logger);

/// <summary>Everything the remaster HUD needs for one draw, collected without mutable renderer state.</summary>
/// <remarks>
/// In the seam rather than beside the renderer that fills it, because it is what the interface
/// takes: a type a presenter names in its signature is part of the contract, not an implementation
/// detail of one title.
/// </remarks>
public readonly record struct RemasteredHudSnapshot(
    float HealthFraction,
    float HeadingRadians,
    WeaponType? Weapon,
    int RoundsInClip,
    int ReserveAmmo,
    HumanWeaponState WeaponState,
    string? ActionSprite,
    IReadOnlyList<MissionObjective> Objectives,
    bool SniperScoped,
    bool WorldInteractionPrompt);

/// <summary>Everything one prompt draw needs, in the level rather than on the screen.</summary>
/// <param name="AnchorMetres">Where the badge's centre stands, in metres.</param>
/// <param name="Opacity">The fade, in <c>[0,1]</c>.</param>
/// <param name="Pop">The eased entry scale, in <c>[0,1]</c>.</param>
/// <param name="KeyLabel">What is printed inside the badge — the bound key, compacted.</param>
/// <param name="Title">The action, in sentence case.</param>
/// <param name="Detail">The qualifier line under the title, or empty.</param>
/// <param name="Progress">The ring's fill in <c>[0,1]</c>, or <see langword="null"/> for no ring.</param>
/// <remarks>
/// In the seam because a presenter names it in its signature. The prompt that <em>fills</em> it is
/// title content and stays where it is — it maps one game's sprite names to wording — but what a
/// prompt looks like on screen is an anchor, an opacity and three strings in any game.
/// </remarks>
public readonly record struct InteractionPromptView(
    Vector3d AnchorMetres,
    float Opacity,
    float Pop,
    string KeyLabel,
    string Title,
    string Detail,
    float? Progress);

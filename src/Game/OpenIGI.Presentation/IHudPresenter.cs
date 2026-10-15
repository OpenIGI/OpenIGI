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
public interface ITitleHudPresenter
{
    /// <summary>Draws into an open sprite batch, with state read from the title's own player.</summary>
    /// <param name="batch">The open batch.</param>
    /// <param name="player">The playing title's player.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    void Draw(SpriteBatch batch, OpenIGI.Game.Player.ITitlePlayer player, int screenWidth, int screenHeight);
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

    /// <summary>Draws the binocular overlay and whatever it has picked out.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="headingRadians">Where the player is looking, for the compass strip.</param>
    /// <param name="tanX">The horizontal half-tangent of the view, for placing the boxes.</param>
    /// <param name="screenWidth">The target width in pixels.</param>
    /// <param name="screenHeight">The target height in pixels.</param>
    /// <param name="targets">The boxes over spotted enemies, or null for none.</param>
    /// <param name="seconds">The wall clock, for anything that animates.</param>
    /// <param name="rangeMetres">The range to what is centred, or null when nothing is.</param>
    void DrawBinocularOverlay(
        SpriteBatch batch,
        float headingRadians,
        float tanX,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets,
        float seconds,
        float? rangeMetres);

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

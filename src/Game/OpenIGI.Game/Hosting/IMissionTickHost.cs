using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Game.Hosting;

/// <summary>What the mission's tick asks of whatever shows and plays it: the camera it may be looking through, and the steps that are picture and sound.</summary>
public interface IMissionTickHost
{
    /// <summary>Gets a value indicating whether the player walks the level, rather than the free camera flying it.</summary>
    bool PlayerMode { get; }

    /// <summary>Stops every sound the cutscenes started, as a skipped intro closes.</summary>
    void StopCutsceneSounds();

    /// <summary>Gets where the free camera is, in metres, or <see langword="null"/> when there is none.</summary>
    Vector3d? FreeCameraPosition { get; }

    /// <summary>Gets the level's built scene, whose placements the cutscenes move.</summary>
    WorldScene? Scene { get; }

    /// <summary>Steps the weather, which falls whatever the player is doing.</summary>
    void StepWeather();

    /// <summary>Plays the vehicles' engines and guns as the cutscene runtime now has them.</summary>
    /// <param name="cutscenes">The cutscene runtime.</param>
    void SoundVehicles(CutsceneRuntime cutscenes);

    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the input the player's update reads, which a scripted capture may stand in for.</summary>
    InputState ViewInput { get; }

    /// <summary>Gets a value indicating whether the failure menu holds the game.</summary>
    bool FailedMenu { get; }

    /// <summary>Gets a value indicating whether the map computer is open.</summary>
    bool ComputerOpen { get; }

    /// <summary>Runs another title's own player in this one's place, if the title brought one.</summary>
    /// <returns>Whether it ran, in which case a title's player does not.</returns>
    bool TickTitlePlayer();

    /// <summary>
    /// Gets a value indicating whether the host's view leaves the player's third-person body to follow the player: walking,
    /// and nothing of the host's -- the failure menu, the computer, the entry shot, the menu's orbit -- holding the view.
    /// </summary>
    bool BodyFollowAllowed { get; }

    /// <summary>Gives the hand a presenter, once the all-weapons cheat has put weapons in it.</summary>
    void CheatWeaponsGranted();

    /// <summary>Plays a proximity mine's arming cue.</summary>
    /// <param name="origin">Where the mine is, in world units.</param>
    void PlayMineTrigger(Vector3d origin);


    /// <summary>Plays and shows what the player's rounds struck, and lays the rockets' trails.</summary>
    void PresentPlayerImpacts();

    /// <summary>
    /// Steps the world's effects on a tick no player's own update steps them: under the death camera, or while another game's
    /// player stands in for the mission's.
    /// </summary>
    void StepWorldEffects();

    /// <summary>Steps the screen's own effects: the damage wash, the flashbang's white-out and the HUD.</summary>
    void StepScreen();

    /// <summary>Applies the test-only writes that hold an outro's gates open.</summary>
    void ApplyTestWrites();

    /// <summary>Points the death camera at where the player's body has fallen.</summary>
    /// <param name="centre">The body's centre.</param>
    void FollowCorpse(Vector3d centre);

    /// <summary>Gets the keys and buttons held this tick.</summary>
    InputState Input { get; }

    /// <summary>Gets the active profile, whose bindings the player's keys are read through.</summary>
    PlayerProfile? InputProfile { get; }

    /// <summary>Gets the ground the player's feet stand on.</summary>
    IGroundHeightField? Ground { get; }

    /// <summary>Gets a value indicating whether the orbit behind a climbing or interacting body has the view.</summary>
    bool IsInteractionViewActive { get; }

    /// <summary>Hands the view to the orbit behind a climbing or interacting body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    /// <param name="facing">The yaw the body faces.</param>
    /// <param name="walls">What pulls the orbit in, or <see langword="null"/>.</param>
    void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls);

    /// <summary>Moves the orbit's pivot after the body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    void FollowInteractionView(Vector3d feet);

    /// <summary>Eases the orbit once on the tick.</summary>
    void TickInteractionView();

    /// <summary>Takes the view back from the orbit.</summary>
    void ResetInteractionView();

    /// <summary>Runs what closes the tick: the body the camera follows and the mission's outcome.</summary>
    void CloseTick();
}

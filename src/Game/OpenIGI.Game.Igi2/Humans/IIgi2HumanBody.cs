using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The animation layer a human's states drive (<c>Human_s.tHumanAnim</c>).</summary>
/// <remarks>
/// The states choose clips by state number — <c>HumanAnim_SetAnimationAndTimeFromState</c> looks the
/// state's weapon-type animation id up through the held weapon's animation table — and read back how far
/// the main controller has got. That is the whole surface the locomotion code needs.
/// </remarks>
public interface IIgi2HumanAnimation
{
    /// <summary>Gets the main controller's time, in clip ticks.</summary>
    int MainTime { get; }

    /// <summary>Gets the length of the main controller's clip, in clip ticks; 0 when none.</summary>
    int MainTotalTime { get; }

    /// <summary>Gets or sets the main controller's time step per tick, in clip ticks.</summary>
    int MainTimeStep { get; set; }

    /// <summary>Gets a value indicating whether the main controller has played to the end (<c>nFlags &amp; 4</c>).</summary>
    bool MainEnded { get; }

    /// <summary>Makes the main controller loop its clip (<c>nFlags |= 1</c>).</summary>
    void LoopMain();

    /// <summary>Advances the main controller by its time step: <c>AnimController_Step(main, nTime + nTimeStep)</c>.</summary>
    /// <remarks>The human's update steps it once a tick, after the body has moved.</remarks>
    void StepMain();

    /// <summary>Takes the upper-body clip off, blending back to the main clip (<c>HumanAnim_ClearUpperBodyAnimation</c>).</summary>
    /// <param name="blendSeconds">The blend; 0 for a cut.</param>
    void ClearUpperBody(float blendSeconds);

    /// <summary>Starts the clip a state plays, at a time, blending in over a duration.</summary>
    /// <param name="state">The locomotion state whose animation to play.</param>
    /// <param name="time">The clip time to start at, in clip ticks.</param>
    /// <param name="blendSeconds">The blend from the current pose; 0 for a cut.</param>
    void PlayState(int state, int time, float blendSeconds);

    /// <summary>
    /// Sets the clip a state plays at its end, <c>HumanAnim_SetAnimationAtEndFromState</c> (<c>igi2 0x58FBC0</c>), as a body already
    /// lying dead is set.
    /// </summary>
    /// <param name="state">The locomotion state whose animation to set.</param>
    /// <param name="blendSeconds">The blend from the current pose; 0 for a cut.</param>
    void PlayStateAtEnd(int state, float blendSeconds) => PlayState(state, 0, blendSeconds);

    /// <summary>Finds a clip of the body's rig by name, <c>Hierarchy_GetAnim</c> (<c>igi2 0x57F360</c>).</summary>
    /// <param name="name">The clip's name.</param>
    /// <returns>The clip, or <see langword="null"/> when the rig has none of that name.</returns>
    Igi2AnimationClip? FindClip(string name) => null;

    /// <summary>
    /// Plays a clip on the whole body without noting a state, <c>HumanAnim_SetAnimation</c> (<c>igi2 0x58F110</c>) called directly.
    /// </summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    void SetClip(Igi2AnimationClip clip, int time, float blendSeconds) => PlayClip(clip, time, blendSeconds, 0);

    /// <summary>Gets the body's rig, or <see langword="null"/> for a model without bones.</summary>
    Igi2BoneHierarchy? Rig { get; }

    /// <summary>Gets or sets the state the upper-body clip was last set from, <c>iStateLastUsedToSetUpperBodyAnimation</c>.</summary>
    int LastUpperBodyState { get; set; }

    /// <summary>Plays a clip on the whole body, noting the state it stands for (<c>HumanAnim_SetAnimation</c>, <c>igi2 0x58F110</c>).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    /// <param name="state">The state, <c>iStateLastUsedToSetMainAnimation</c>.</param>
    void PlayClip(Igi2AnimationClip? clip, int time, float blendSeconds, int state);

    /// <summary>
    /// Plays the clip of a name on the body's rig, noting the state that plays it (<c>HumanAnim_SetAnimation</c>,
    /// <c>igi2 0x58F110</c>, with <c>HumanPlayer_GetAnimStateFromAnimName</c>, <c>0x49E230</c>).
    /// </summary>
    /// <param name="name">The clip's name.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    void PlayNamed(string name, int time, float blendSeconds);

    /// <summary>Gets a value indicating whether the upper-body clip has reached an end; false with none playing.</summary>
    bool UpperBodyEnded { get; }

    /// <summary>Gets the upper-body clip's length in ticks, or <see langword="null"/> with no upper-body clip on.</summary>
    int? UpperBodyTotalTime => null;

    /// <summary>Gets a value indicating whether every blend between clips has finished (<c>HumanAnim_IsBlendFinished</c>).</summary>
    bool IsBlendFinished => true;

    /// <summary>Gets or sets the upper-body clip's step a tick; setting it with none playing does nothing.</summary>
    int UpperBodyTimeStep { get; set; }

    /// <summary>Plays a clip on the upper body (<c>HumanAnim_SetUpperBodyAnimationFromHeader</c>, <c>igi2 0x58F300</c>).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    void SetUpperBody(Igi2AnimationClip? clip, int time, float blendSeconds);

    /// <summary>
    /// Plays a state's clip on the upper body, through the held weapon
    /// (<c>HumanAnim_SetUpperBodyAnimationAndTimeFromState</c>, <c>igi2 0x58F460</c>).
    /// </summary>
    /// <param name="state">The state whose base animation to play.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    void PlayUpperBodyState(int state, int time, float blendSeconds);

    /// <summary>Plays a named clip on the upper body, <c>HumanAnim_SetUpperBodyAnimationAndTime</c>.</summary>
    /// <param name="name">The clip.</param>
    /// <param name="time">The time to play it from, in clip ticks.</param>
    /// <param name="blendSeconds">How long to blend it in.</param>
    void PlayUpperBodyNamed(string name, int time, float blendSeconds)
    {
    }

    /// <summary>Whether the held weapon has an upper-body clip for a state, other than the lower-body stand.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    bool HasUpperBodyClipForState(int state) => false;

    /// <summary>Steps every controller that steps itself, as the body's children run each tick.</summary>
    void AutoStep();

    /// <summary>Sets and clears bits of the active main controller's flags, as a death's entry does to the clip it played.</summary>
    /// <param name="raise">The bits to set.</param>
    /// <param name="clear">The bits to clear, before setting.</param>
    void SetMainFlags(Animation.Igi2AnimControllerBits raise, Animation.Igi2AnimControllerBits clear)
    {
    }
}

/// <summary>The questions a human's states ask of the world around it.</summary>
public interface IIgi2HumanSurroundings
{
    /// <summary>
    /// Puts a body on the list detection looks over, <c>AIDetection_ptDetectionQTaskList</c>, unless it is there already: a
    /// soldier's body is, from the moment it starts to die.
    /// </summary>
    /// <param name="body">The body.</param>
    void RegisterDetectable(Igi2Human body)
    {
    }

    /// <summary>A grenade leaves a soldier's hand, <c>Grenade_New</c> (<c>igi2 0x484FD0</c>) under the level's dynamic task.</summary>
    /// <param name="grenade">The throw.</param>
    /// <returns>Whether it is a grenade that explodes, its physics type's second value nought, which its thrower warns of.</returns>
    bool ThrowGrenade(in Weapons.Igi2GrenadeThrow grenade) => false;

    /// <summary>Gets a value indicating whether the human would fit lying flat where it stands.</summary>
    /// <remarks>
    /// The states test it by setting the posture to lying flat and running the object collision on the
    /// spot: the posture is refused when the collision reports signal bit 4 (<c>Human_ObjectCollision</c>).
    /// </remarks>
    bool FitsLyingFlat();

    /// <summary>Tells the AI the human is moving, so it can be heard (<c>HumanPlayer_SendWalkEvent</c>).</summary>
    void SendWalkEvent();

    /// <summary>Tells the AI the human has landed, so it can be heard.</summary>
    /// <remarks>Event 10 with flags <c>0x442</c> and radius 16384, sent by every landing (<c>HumanPlayer_InAirControl</c>).</remarks>
    void SendLandingEvent();

    /// <summary>Gets a value indicating whether the ground the human stands on is a lift (<c>Elevator</c>).</summary>
    bool IsOnElevator { get; }

    /// <summary>Asks whether the human is under water, and how deep (<c>Water_IsUnderWater</c>, <c>igi2 0x591C30</c>).</summary>
    /// <param name="depth">The depth, set only when the answer is yes.</param>
    /// <returns>Whether any water sheet covers the human's position.</returns>
    bool IsUnderWater(out float depth);

    /// <summary>Stirs the water the human wades in, <c>Water_PlayerMovementEffect</c> (<c>igi2 0x591F00</c>).</summary>
    /// <param name="isRunning">Whether the human runs through it rather than standing or treading.</param>
    void StirWater(bool isRunning);

    /// <summary>Gets the collision engine the human finds its footing with.</summary>
    IIgi2CollisionQuery Collision { get; }

    /// <summary>
    /// Asks what underfoot pushes the human this tick, as a conveyor belt does (<c>ConveyorBelt_ePushObjectQTaskEvent</c>).
    /// </summary>
    /// <param name="ground">What the human stands on.</param>
    /// <param name="footing">The footing hit on it.</param>
    /// <returns>The push, or <see langword="null"/> when the ground does not push.</returns>
    Vector3f? PushOnConveyor(IIgi2Collider ground, Igi2Intersection footing);

    /// <summary>
    /// Gets every human registered in the world, <c>Human_ptQTaskList</c>, in registration order: the bodies a move is kept
    /// out of (<see cref="Igi2Human.HumanCollision"/>).
    /// </summary>
    IReadOnlyList<Igi2Human> Humans => [];

    /// <summary>Gets a level material's landing sound, or <see langword="null"/> when it has none.</summary>
    /// <param name="material">The level material slot, as a hit's <c>ptEnterMaterial</c> names it.</param>
    /// <returns>The sound's name.</returns>
    string? LandingSound(int material);

    /// <summary>
    /// The things a human may activate whose spheres meet its own, in the order the world's stationary tree gives them
    /// (<c>World_TriggerQTaskEventSphereSpecificTree</c> with <c>WORLD_DYNCUBETREE_STATIONARY</c>).
    /// </summary>
    /// <param name="centre">The sphere's centre.</param>
    /// <param name="radius">Its radius.</param>
    /// <returns>The things; none when the world holds none.</returns>
    IEnumerable<Interaction.IIgi2Activatable> FindActivatables(Vector3d centre, float radius) => [];

    /// <summary>Plays a named sound at the human's view (<c>SoundSys_NewSoundFn</c>).</summary>
    /// <param name="name">The sound definition's name.</param>
    void PlaySound(string name);

    /// <summary>Shows the player a message on the status line, <c>StatusMessage_New</c> (<c>igi2 0x510200</c>), localised.</summary>
    /// <param name="key">The message's key in the localised strings.</param>
    void ShowStatusMessage(string key);

    /// <summary>
    /// Throws a weapon into the level as a pickup, <c>GunPickup_New</c> (<c>igi2 0x46C2F0</c>) moving and thrown short, and puts it on
    /// the level's pickup list.
    /// </summary>
    /// <param name="weaponType">The weapon.</param>
    /// <param name="position">Where it starts.</param>
    /// <param name="orientation">How it was held, nine floats row by row.</param>
    /// <param name="throwGamma">The heading it is thrown along.</param>
    /// <param name="ammoInClip">The rounds its load holds.</param>
    /// <param name="ammoCount">The rounds it gives its two fire types.</param>
    void ThrowGunPickup(Weapons.Igi2WeaponTypeTask weaponType, Vector3d position, IReadOnlyList<float> orientation, float throwGamma, int ammoInClip, IReadOnlyList<int> ammoCount)
    {
    }

    /// <summary>Raises an event for the AI to perceive, <c>HumanAI_SendEvent</c> (<c>igi2 0x460A90</c>); nothing hears it until the AI's events are ported.</summary>
    /// <param name="aiEvent">The event.</param>
    void SendAiEvent(in Ai.Igi2AiEvent aiEvent)
    {
    }

    /// <summary>Plays a named sound at the human's view and keeps hold of it, as a state holding a loop does.</summary>
    /// <param name="name">The sound definition's name.</param>
    /// <returns>The playing sound.</returns>
    IIgi2SoundInstance StartSound(string name);

    /// <summary>
    /// Plays a human's sound where it happens, <c>Human_PlaySound</c> (<c>igi2 0x4FB590</c>): the player's at its ears, anyone else's
    /// at the point.
    /// </summary>
    /// <param name="name">The sound definition's name.</param>
    /// <param name="position">Where it happens.</param>
    /// <returns>The playing sound, or <see langword="null"/> when none plays.</returns>
    IIgi2SoundInstance? PlaySoundAt(string name, Vector3d position) => StartSound(name);

    /// <summary>Gets the level's thing of a task id a soldier is told to activate, <c>Level_atLevelQTaskID</c>, or <see langword="null"/>.</summary>
    /// <param name="id">The task id.</param>
    /// <returns>The thing.</returns>
    Interaction.IIgi2Activatable? ActivatableById(int id) => null;

    /// <summary>Gets a level material row's game material, or <see langword="null"/> for none.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The material.</returns>
    World.Igi2GameMaterial? Material(int row) => null;

    /// <summary>Gets a value indicating whether blood is shown, the options' <c>isBlood</c>: on unless turned off.</summary>
    bool IsBlood => true;

    /// <summary>Gets the level's 2-D particles, which blood is thrown through, or <see langword="null"/> when there are none.</summary>
    World.Igi2Particles? Particles => null;

    /// <summary>Leaves a blood stain on a surface, <c>CreateBulletHole</c> with a blood texture; drawing only.</summary>
    /// <param name="stain">The stain.</param>
    void MarkBloodStain(in Igi2BloodStain stain)
    {
    }
}

/// <summary>A sound a state holds on to: the task a <c>QReference</c> keeps, which may end on its own.</summary>
public interface IIgi2SoundInstance
{
    /// <summary>Gets a value indicating whether the sound is still playing (the reference's object still exists).</summary>
    bool IsAlive { get; }

    /// <summary>Moves the sound to the human's position (<c>SoundSys_eSetPositionQTaskEvent</c>).</summary>
    void MoveToHuman();

    /// <summary>Stops the sound and lets go of it (<c>QTask_DeleteQTaskAndQTaskRefSafe</c>).</summary>
    void Delete();

    /// <summary>Sets how loud the sound plays, <c>SoundSys_SetSoundVolumes</c> (<c>igi2 0x42F930</c>).</summary>
    /// <param name="baseVolume">Its own volume; below nought leaves it as it is.</param>
    /// <param name="relativeVolume">The share of that it plays at.</param>
    void SetVolumes(float baseVolume, float relativeVolume)
    {
    }
}

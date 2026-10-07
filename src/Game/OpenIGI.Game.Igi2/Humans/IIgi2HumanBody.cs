using OpenIGI.Core.Numerics;

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

    /// <summary>Takes the upper-body clip off, blending back to the main clip (<c>HumanAnim_ClearUpperBodyAnimation</c>).</summary>
    /// <param name="blendSeconds">The blend; 0 for a cut.</param>
    void ClearUpperBody(float blendSeconds);

    /// <summary>Starts the clip a state plays, at a time, blending in over a duration.</summary>
    /// <param name="state">The locomotion state whose animation to play.</param>
    /// <param name="time">The clip time to start at, in clip ticks.</param>
    /// <param name="blendSeconds">The blend from the current pose; 0 for a cut.</param>
    void PlayState(int state, int time, float blendSeconds);
}

/// <summary>The questions a human's states ask of the world around it.</summary>
public interface IIgi2HumanSurroundings
{
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

    /// <summary>Lets go of the ground: drops the reference to it and forgets the ground intersection.</summary>
    /// <remarks>What a jump's take-off does to <c>ptGroundQTaskRef</c>, <c>isGroundRigidObj</c> and <c>tGroundIntersection.isHit</c>.</remarks>
    void LeaveGround();

    /// <summary>Gets how far the rigid object underfoot moved since the last tick; zero on anything else.</summary>
    /// <returns>The object's position less <c>Human_s.tGroundPrevPos</c>.</returns>
    Vector3d GroundMotion();

    /// <summary>Asks whether the human is under water, and how deep (<c>Water_IsUnderWater</c>, <c>igi2 0x591C30</c>).</summary>
    /// <param name="depth">The depth, set only when the answer is yes.</param>
    /// <returns>Whether any water sheet covers the human's position.</returns>
    bool IsUnderWater(out float depth);

    /// <summary>Gets the landing sound of the material the ground intersection entered, or <see langword="null"/> when there is none.</summary>
    string? GroundLandingSound { get; }

    /// <summary>Plays a named sound at the human's view (<c>SoundSys_NewSoundFn</c>).</summary>
    /// <param name="name">The sound definition's name.</param>
    void PlaySound(string name);

    /// <summary>Plays a named sound at the human's view and keeps hold of it, as a state holding a loop does.</summary>
    /// <param name="name">The sound definition's name.</param>
    /// <returns>The playing sound.</returns>
    IIgi2SoundInstance StartSound(string name);
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
}

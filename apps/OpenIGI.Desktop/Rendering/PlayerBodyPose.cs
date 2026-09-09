namespace OpenIGI.Desktop;

/// <summary>
/// Everything above the player's waist, for the frame their own body is drawn in.
/// </summary>
/// <param name="UpperBodyAnimation">
/// The weapon's <c>UpperBody*</c> clip, in the human's own skeleton-0 set, or <c>-1</c> to hand the
/// torso back to the locomotion clip on bone 0.
/// </param>
/// <param name="UpperBodyLoops">Whether that clip repeats — a stance does, a shot does not.</param>
/// <param name="UpperBodyBlendSeconds">How long it eases in: 0.4 s for a stance, immediate for a shot.</param>
/// <param name="UpperBodySequence">
/// A counter the player camera bumps on every request. The id alone is not enough: a second shot asks
/// for the clip already playing and has to restart it, so what identifies a new request is that the
/// count moved.
/// </param>
/// <param name="AimPitch">Where the player is looking, in radians, positive up.</param>
/// <remarks>
/// <para>
/// Carried as one value rather than five arguments because the five are one thing — the state of the
/// half of the body the legs' locomotion clip does not drive — and because two of them are meaningless
/// apart: an animation id without its sequence cannot be told from the same id re-requested.
/// </para>
/// <para>
/// The split between this and the locomotion clip is the rig's own (<see cref="Game.Player.HumanUpperBody"/>):
/// bone 1's subtree is the torso, head, arms and weapon, and everything outside it is the pelvis and the
/// legs.
/// </para>
/// </remarks>
public readonly record struct PlayerBodyPose(
    int UpperBodyAnimation,
    bool UpperBodyLoops,
    float UpperBodyBlendSeconds,
    int UpperBodySequence,
    float AimPitch);

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// One soldier drawn from the group's geometry, with <b>its own skinned mesh</b>.
/// </summary>
/// <remarks>
/// <para>
/// The group used to share a single skinned mesh, posed from its first member. That was only ever
/// defensible while every soldier in a group ran the same clip from the same start; once they were given
/// real patrol paths they diverged, and the whole group rendered whichever pose the representative
/// happened to be in. With most guards standing and a few walking, that read as <b>no soldier
/// animating at all</b>.
/// </para>
/// <para>
/// Hooking the original's skinner (<c>0x49F700</c>) settles what it does: it is called through a
/// stack-allocated descriptor about once per soldier per frame, so the original skins <b>every soldier
/// separately, every frame</b>. This does the same. It costs one mesh upload per soldier per tick
/// instead of one per group, which is the price of each soldier having its own pose.
/// </para>
/// </remarks>
/// <param name="agent">The soldier this draws.</param>
/// <param name="mesh">Its own skinned mesh.</param>
internal sealed class SoldierMember(SoldierAgent agent, MeshHandle mesh)
{
    /// <summary>The soldier as the shots find it, made the first time one looks.</summary>
    public IShootableBody? Body { get; set; }

    /// <summary>The soldier this draws: who it is, where it spawned, what it is armed with.</summary>
    public SoldierAgent Agent { get; } = agent;

    public MeshHandle Mesh { get; set; } = mesh;

    /// <summary>What this soldier holds, or <see langword="null"/> when the level armed it with nothing.</summary>
    public SoldierHeldWeapon? Weapon { get; set; }

    /// <summary>The carried weapon restored after a temporary stationary-gun mount ends.</summary>
    public SoldierHeldWeapon? CarriedWeapon { get; set; }

    /// <summary>The persistent buffer this soldier's pose is skinned into, allocated once.</summary>
    public Vertex3D[]? SkinScratch { get; set; }

    /// <summary>Gets or sets the lower level of detail <see cref="LevelMesh"/> is skinned at, or nought for none.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the soldier's skin at its lower level of detail, posed alongside <see cref="Mesh"/>.</summary>
    public MeshHandle LevelMesh { get; set; }

    /// <summary>Gets or sets the buffer the lower level is skinned into.</summary>
    public Vertex3D[]? LevelScratch { get; set; }

    /// <summary>
    /// The pose this soldier was skinned from this tick — its rig's, or its corpse's.
    /// </summary>
    /// <remarks>
    /// Kept because <b>anything attached to the body has to be placed from the same pose the body was
    /// drawn from</b>, and for a corpse that is not the pose its rig is playing. Reading the rig
    /// directly is what left a dead guard's rifle hanging in the air where the death animation would
    /// have put his hand.
    /// </remarks>
    public BonePose? DrawnPose { get; set; }

    /// <summary>The pose in the animated mesh's current GPU vertex stream.</summary>
    /// <remarks>
    /// This is separate from <see cref="DrawnPose"/> because a stopped pose may not upload again;
    /// player collision needs the exact pair that is still resident on the device.
    /// </remarks>
    public BonePose? SkinnedPose { get; set; }

    /// <summary>The pose in the animated mesh's previous GPU vertex stream.</summary>
    public BonePose? PreviousSkinnedPose { get; set; }

    /// <summary>Tracks whether this pose still needs an animated-buffer upload.</summary>
    public PoseUploadLatch PoseUploads { get; } = new();

    /// <summary>
    /// The simulated corpse this soldier is drawn as once it has been killed, or
    /// <see langword="null"/> while it is alive or when <c>g_ragdoll</c> is off.
    /// </summary>
    /// <remarks>
    /// Presentation only. The soldier's brain still runs its retail death — same tick, same
    /// authoritative position, same consequences for the guards around it — and this replaces
    /// nothing but the pose that reaches the skinner.
    /// </remarks>
    public RagdollBody? Ragdoll { get; set; }

    /// <summary>Whether the animated mesh took a new pose since the last frame's bookkeeping.</summary>
    /// <remarks>
    /// When it did, the previous-pose stream now holds what was on screen last frame, and the
    /// blend that described last frame is expressed against the OLD stream pair — see
    /// <see cref="SoldierFrameInterpolator.ResolveFrameState"/> for how each case is reported.
    /// </remarks>
    public bool PoseFlippedSinceFrame { get; set; }

    // ---- Captured on the simulation tick: the two states the presentation draws between. ----

    /// <summary>Whether the tick states below have been captured at least once.</summary>
    public bool HasTickState { get; set; }

    /// <summary>Where the body is drawn for the current tick, in metres.</summary>
    public Vector3d TickWhere { get; set; }

    /// <summary>Where it was drawn the tick before.</summary>
    public Vector3d PrevTickWhere { get; set; }

    /// <summary>This tick's world 3×3, verbatim — what an unblended frame draws, to the bit.</summary>
    public Matrix3f TickOrientation { get; set; }

    /// <summary>This tick's heading in radians, for interpolating.</summary>
    public float TickFacing { get; set; }

    /// <summary>The tick before's heading.</summary>
    public float PrevTickFacing { get; set; }

    /// <summary>Whether the hand-bone states below are captured (only armed soldiers need them).</summary>
    public bool HasHandState { get; set; }

    /// <summary>The hand bone's position in body space this tick, in world units.</summary>
    public Vector3f TickHandPosition { get; set; }

    /// <summary>The tick before's.</summary>
    public Vector3f PrevTickHandPosition { get; set; }

    /// <summary>The hand bone's rotation this tick.</summary>
    public Matrix3f TickHandRotation { get; set; } = Matrix3f.Identity;

    /// <summary>The tick before's.</summary>
    public Matrix3f PrevTickHandRotation { get; set; } = Matrix3f.Identity;

    // ---- Resolved once per rendered frame by ResolveFrameState; every pass draws from these. ----

    /// <summary>Whether a frame has been resolved at all.</summary>
    public bool HasFrameState { get; set; }

    /// <summary>Where the body is drawn this frame — between the two tick states when smoothing.</summary>
    public Vector3d DrawnWhere { get; set; }

    /// <summary>Its world 3×3 this frame.</summary>
    public Matrix3f DrawnOrientation { get; set; } = Matrix3f.Identity;

    /// <summary>The ground found under its shadow on the last tick it was drawn.</summary>
    public ShadowGround ShadowGround;

    /// <summary>Where it was drawn LAST frame — the rigid half of its motion vector.</summary>
    public (Vector3d Where, Matrix3f Orientation)? PreviousDrawn { get; set; }

    /// <summary>This frame's share of the previous-pose vertex stream in the drawn position.</summary>
    public float PoseBlend { get; set; }

    /// <summary>The share that reconstructs LAST frame's position from the same streams.</summary>
    public float PreviousPoseBlend { get; set; }

    /// <summary>Whether this frame is the body's first — nothing on screen is its history.</summary>
    public bool NoHistoryThisFrame { get; set; }

    /// <summary>Whether the weapon transform below is resolved.</summary>
    public bool HasWeaponFrameState { get; set; }

    /// <summary>The held weapon's world origin this frame, in metres.</summary>
    public Vector3d DrawnWeaponOrigin { get; set; }

    /// <summary>Its world rotation this frame — heading times hand bone.</summary>
    public Matrix3f DrawnWeaponRotation { get; set; } = Matrix3f.Identity;

    /// <summary>Last frame's weapon transform — a rigid model's exact previous pose.</summary>
    public (Vector3d Origin, Matrix3f Rotation)? PreviousWeapon { get; set; }
}

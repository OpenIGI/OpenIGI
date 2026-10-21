using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>A human body, as I.G.I. 2's <c>Human_s</c> keeps it: the part the player and the soldiers share.</summary>
/// <remarks>
/// <para>
/// The channel block that drives it, the locomotion state machine, the posture, the speeds the body moves
/// by, its damage and its death — and the <c>Human_*</c> functions that work on them whoever the human
/// is. What only the player has (its tuning, its input, its action machine and its own states) lives in
/// the derived class.
/// </para>
/// </remarks>
public abstract partial class Igi2Human : Game.Ai.IAiHuman
{
    /// <summary>The animation controller's ticks per game frame, <c>AnimController_nTicksPerFrame</c> (<c>igi2 0x63681C</c>).</summary>
    public const int AnimationTicksPerFrame = 160;

    /// <summary>The ground tolerance a human normally has, and returns to after a jump or a crawl.</summary>
    public const float LandingGroundTolerance = 819.20001f;

    /// <summary>The signal bit <c>Human_UpdateDie</c> raises when the human dies.</summary>
    protected const uint DeadSignal = 0x20000;

    /// <summary>Initializes a new instance of the <see cref="Igi2Human"/> class.</summary>
    /// <param name="animation">The animation layer the states drive.</param>
    /// <param name="surroundings">The world the states probe.</param>
    /// <param name="createLocomotion">Builds the human's locomotion state machine over the human.</param>
    protected Igi2Human(
        IIgi2HumanAnimation animation,
        IIgi2HumanSurroundings surroundings,
        Func<Igi2Human, TaskStateMachine> createLocomotion)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(surroundings);
        ArgumentNullException.ThrowIfNull(createLocomotion);
        Animation = animation;
        Surroundings = surroundings;
        Locomotion = createLocomotion(this);
    }

    /// <summary>Gets the channel block that drives the human, <c>Human_s.avChannel</c>.</summary>
    public Igi2HumanChannels Channels { get; } = new();

    /// <summary>Gets the animation layer the states drive.</summary>
    public IIgi2HumanAnimation Animation { get; }

    /// <summary>Gets the world the states probe.</summary>
    public IIgi2HumanSurroundings Surroundings { get; }

    /// <summary>Gets the locomotion state machine, <c>Human_s.tState</c>.</summary>
    public TaskStateMachine Locomotion { get; }

    /// <summary>Gets or sets the game's run random stream, <c>Game.tRunRandomState</c>, which flinches and deaths draw from.</summary>
    /// <remarks>Shared with everything else that draws from it; the session supplies the one stream.</remarks>
    public IgiRandom Random { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the human runs rather than walks, <c>Human_s.isRunning</c>.</summary>
    public bool IsRunning { get; protected set; }

    /// <summary>Gets or sets the posture, <c>Human_s.ePose</c>.</summary>
    public Igi2HumanPose Pose { get; set; }

    /// <summary>Gets or sets the postures allowed this tick, <c>Human_s.bAllowedStance</c>.</summary>
    public Igi2AllowedStance AllowedStance { get; set; } = Igi2AllowedStance.All;

    /// <summary>Gets or sets the body's orientation, <c>tModelObj.tOrientation</c>: rows are the local X, Y and Z axes.</summary>
    public Matrix3f Orientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets or sets how much the ground's slope slows the gait, <c>Human_s.vGroundAngleFactor</c>.</summary>
    public float GroundAngleFactor { get; set; }

    /// <summary>Gets or sets the turning speed, <c>Human_s.vTurningSpeed</c>.</summary>
    public float TurningSpeed { get; set; }

    /// <summary>
    /// Gets or sets the translation of the model's root bone, <c>tRootTranslation</c> (<c>BoneDynCubeObj + 0x130</c>): its rig's
    /// first bind translation, which <c>BoneDynCubeObj_SetLODVirModel</c> (<c>igi2 0x567BE0</c>) copies.
    /// </summary>
    public Vector3f RootTranslation { get; set; }

    /// <summary>Gets or sets the model's bounding radius, <c>vRadius</c> (<c>BoneDynCubeObj + 0x148</c>): its mesh's <c>+0x78</c>.</summary>
    public float ModelRadius { get; set; } = 1f;

    /// <summary>Gets the view the states kick, <c>Human_s.ptHumanViewQTask</c>.</summary>
    public Igi2HumanView View { get; } = new();

    /// <summary>Gets the damage the human has taken, <c>Human_s.tHumanDamage</c>.</summary>
    public Igi2HumanDamage Damage { get; } = new();

    /// <summary>Gets a value indicating whether the human has died, <c>Human_s.isDead</c>.</summary>
    public bool IsDead { get; protected set; }

    /// <summary>Gets or sets a value indicating whether the human takes part, <c>isActive</c>: a soldier's script switches it; the player always does.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets how many times a squad first spotted this human, <c>Human.nSpotted</c> (<c>Human+0x1E0</c>): only the player's
    /// counts up; scripts read it as <c>&lt;name&gt;.nSpotted</c>.
    /// </summary>
    public int TimesSpotted { get; set; }

    /// <summary>Sets the death latch a script writes through <c>&lt;name&gt;.isDead</c>, the raw byte retail binds.</summary>
    /// <param name="isDead">The value.</param>
    public void SetDeadLatch(bool isDead) => IsDead = isDead;

    /// <summary>Gets or sets the body's own speed in the air, <c>Human_s.tSpeed</c>.</summary>
    public Vector3f Speed { get; set; }

    /// <summary>Gets or sets the speed the body carries from the ground into the air, <c>Human_s.tAccumulatedSpeed</c>.</summary>
    public Vector3f AccumulatedSpeed { get; set; }

    /// <summary>Gets or sets the speed an impact gave the body, <c>Human_s.tImpactSpeed</c>.</summary>
    public Vector3f ImpactSpeed { get; set; }

    /// <summary>Gets or sets the body's whole speed last tick, <c>Human_s.tTotalSpeed</c>; a death carries it on.</summary>
    public Vector3f TotalSpeed { get; set; }

    /// <summary>Gets or sets how far below the feet the ground still counts as underfoot, <c>Human_s.vGroundTolerance</c>.</summary>
    /// <remarks>Zero from take-off until a jump starts down, so the ground left behind is not found again at once.</remarks>
    public float GroundTolerance { get; set; } = LandingGroundTolerance;

    /// <summary>Gets or sets a value indicating whether landings cannot hurt, <c>Human_s.isProtectedFromFallDamage</c>.</summary>
    /// <remarks>Read only while airborne: a landing judges by what it was during the fall.</remarks>
    public bool IsProtectedFromFallDamage { get; set; }

    /// <summary>Gets a value indicating whether what the human stands on is a rigid object, <c>Human_s.isGroundRigidObj</c>.</summary>
    public bool IsGroundRigidObject { get; private set; }

    /// <summary>Gets or sets where the rigid object underfoot stood last tick, <c>Human_s.tGroundPrevPos</c>.</summary>
    public Vector3d GroundPreviousPosition { get; set; }

    /// <summary>Gets or sets how deep the water the human stands in is, <c>Human_s.vWaterDepth</c>.</summary>
    public float WaterDepth { get; set; }

    /// <summary>Gets or sets the pain sound playing, <c>Human_s.ptHitSoundQTaskRef</c>; death cuts it off.</summary>
    public IIgi2SoundInstance? HitSound { get; set; }

    /// <summary>Gets or sets a value indicating whether the death clip has played out, <c>Human_s.isDeathAnimationDone</c>.</summary>
    public bool IsDeathAnimationDone { get; protected set; }

    /// <summary>Gets or sets a value indicating whether a dead body has come to rest, <c>Human_s.isDeathPositionStabilized</c>.</summary>
    public bool IsDeathPositionStabilized { get; set; }

    /// <summary>Gets or sets a value indicating whether the body has left its pool of blood, <c>isBloodPuddle</c>.</summary>
    public bool IsBloodPuddle { get; set; }

    /// <summary>Gets the body's mark on the ground, which becomes its pool of blood.</summary>
    public Igi2HumanShadow Shadow { get; } = new();

    /// <summary>Gets or sets a value indicating whether the body bleeds no sprites, <c>isBloodSpriteDisabled</c>.</summary>
    public bool IsBloodSpriteDisabled { get; set; }

    /// <summary>Gets or sets the bone collision's movement this tick, <c>tBoneCollisionData.tDeltaMovement</c>.</summary>
    public Vector3f BoneDeltaMovement { get; set; }

    /// <summary>Gets or sets a value indicating whether the upper body turns with the aim, <c>Human_s.isRotateUpperBody</c>.</summary>
    public bool IsRotateUpperBody { get; protected set; } = true;

    /// <summary>Gets or sets a value indicating whether the body's orientation is frozen, <c>Human_s.isLockOrientation</c>.</summary>
    public bool IsLockOrientation { get; protected set; }

    /// <summary>Gets or sets the camera mode, <c>Human_s.eHumanCameraMode</c>: 1, first person, as every human is made (<c>Human_CreateHandler</c>, <c>igi2 0x4FDC90</c>); 5 is the death camera.</summary>
    public int CameraMode { get; protected set; } = 1;

    /// <summary>Gets the weapon the human-level functions act on.</summary>
    protected abstract IIgi2HumanWeapon HumanWeapon { get; }

    /// <summary>Sets the gait's animation rate for this tick: slower uphill.</summary>
    /// <param name="timeScale">The state's own rate.</param>
    /// <remarks>
    /// <c>Human_RunningControl</c> (<c>igi2 0x5030E0</c>):
    /// <c>(1 − clamp(groundAngleFactor, −0.2, 1)) × 160 × timeScale</c> clip ticks per tick, truncated.
    /// </remarks>
    public void ControlGait(float timeScale)
    {
        float factor = GroundAngleFactor;
        if (factor < -0.2f)
        {
            factor = -0.2f;
        }
        else if (factor > 1f)
        {
            factor = 1f;
        }

        Animation.MainTimeStep = Fpu.ToInt32(((Fpu)1f - factor) * (double)AnimationTicksPerFrame * timeScale);
    }

    /// <summary>Death, once the damage reaches the limit: the weapon's sound slot is stopped and the dead bit raised.</summary>
    /// <remarks><c>Human_UpdateDie</c> (<c>igi2 0x504010</c>), single-player path.</remarks>
    public void UpdateDie()
    {
        if (!Damage.IsFatal || IsDead)
        {
            return;
        }

        HumanWeapon.StopSoundSlot();
        IsDead = true;
        Locomotion.Flags |= DeadSignal;
    }

    /// <summary>
    /// A blast's shake on the view, by the eyes' distance from it: full within three times its full range, gone at nine, run out
    /// over its ticks, as both the player's and a soldier's explosion handlers set it.
    /// </summary>
    /// <param name="hit">The blast.</param>
    /// <param name="angleScale">How far a full shake tilts the view, in radians: the player's and a soldier's differ.</param>
    protected void ShakeView(in Weapons.Igi2ExplosionHit hit, float angleScale)
    {
        Vector3d eyes = View.Position, at = hit.Position;
        Fpu dx = (Fpu)eyes.X - at.X, dy = (Fpu)eyes.Y - at.Y, dz = (Fpu)eyes.Z - at.Z;
        Fpu distance = Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
        Fpu past = distance - ((Fpu)hit.FullRange * 3f);
        Fpu share;
        if (past.Value < 0.0)
        {
            share = 1f;
        }
        else
        {
            share = (Fpu)1f - (past / ((Fpu)hit.FullRange * 6f));
            if (share.Value < 0.0 || share.Value == 0.0)
            {
                return;
            }
        }

        View.ShakeFactor = 1f;
        View.ShakeRange = ((Fpu)204.8f * share).F;
        View.ShakeAngle = (share * angleScale).F;
        View.ShakeTime = ((Fpu)1.0 / (double)hit.ExplosionTime).F;
    }

    /// <summary>Cuts off the pain sound and plays one of a set of death cries (<c>Human_PlayDeathSound</c>, <c>igi2 0x503280</c>).</summary>
    /// <param name="prefix">The cries' common name; a draw below <paramref name="count"/> is appended.</param>
    /// <param name="count">How many cries there are.</param>
    protected void PlayDeathSound(string prefix, uint count)
    {
        HitSound?.Delete();
        HitSound = null;
        uint draw = Random.NextBounded(count);
        Surroundings.PlaySound($"{prefix}{draw}");
    }
}

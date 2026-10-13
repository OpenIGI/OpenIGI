using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>
/// The fields of a human's save list that its kind keeps rather than every human: its weapon's flags, its activation, its mission's
/// figures, how well it hits and how well it is seen.
/// </summary>
public sealed class Igi2HumanSaveExtras
{
    /// <summary>Gets or sets a value indicating whether its weapon is ready, <c>isWeaponReady</c>.</summary>
    public bool IsWeaponReady { get; set; }

    /// <summary>Gets or sets a value indicating whether it fires, <c>isWeaponFire</c>.</summary>
    public bool IsWeaponFire { get; set; }

    /// <summary>Gets or sets a value indicating whether it fires the second way, <c>isWeaponAlternateFire</c>.</summary>
    public bool IsWeaponAlternateFire { get; set; }

    /// <summary>Gets or sets a value indicating whether it has fired, <c>isWeaponFired</c>.</summary>
    public bool IsWeaponFired { get; set; }

    /// <summary>Gets or sets a value indicating whether its translation is unscaled, <c>isDisableDeltaTranslationScale</c>.</summary>
    public bool IsDisableDeltaTranslationScale { get; set; }

    /// <summary>Gets or sets a value indicating whether it may not activate, <c>isLockActivate</c>.</summary>
    public bool IsLockActivate { get; set; }

    /// <summary>Gets or sets a value indicating whether it looks for the activate key, <c>isCheckActivateButton</c>.</summary>
    public bool IsCheckActivateButton { get; set; }

    /// <summary>Gets or sets how long it has activated for, <c>nActivateTime</c>.</summary>
    public int ActivateTime { get; set; }

    /// <summary>Gets or sets how likely its rounds are to strike, <c>vWeaponHitProbability</c>.</summary>
    public float WeaponHitProbability { get; set; } = 1f;

    /// <summary>Gets or sets how well it is seen, <c>vVisibilityFactor</c>.</summary>
    public float VisibilityFactor { get; set; } = 1f;

    /// <summary>Gets or sets the difficulty, <c>eGameDifficulty</c>.</summary>
    public int Difficulty { get; set; }

    /// <summary>Gets or sets the mission, <c>iMissionID</c>.</summary>
    public int MissionId { get; set; }

    /// <summary>Gets or sets its kills, <c>nKills</c>.</summary>
    public int Kills { get; set; }

    /// <summary>Gets or sets its hits, <c>nHits</c>.</summary>
    public int Hits { get; set; }

    /// <summary>Gets or sets the games loaded, <c>nGameLoads</c>.</summary>
    public int GameLoads { get; set; }

    /// <summary>Gets or sets the mission's ticks, <c>nTime</c>.</summary>
    public int Time { get; set; }

    /// <summary>Gets or sets the alarms it set off, <c>nTimesAlarmTriggered</c>.</summary>
    public int TimesAlarmTriggered { get; set; }

    /// <summary>Gets or sets its health, <c>nHealth</c>.</summary>
    public int Health { get; set; } = 100;
}

/// <content>
/// Its save state, <c>Human_ptSerializeParameterList</c> (human.c): its channels as they were and are, its damage, its flags, its pose,
/// camera and timers, its angles, its speeds, its distances to the ground and roof, its mission's figures, its state machine
/// (<c>Human_ptSerializeStateParameterList</c>), and what it stands on.
/// </content>
public abstract partial class Igi2Human
{
    private static readonly byte[] IdentityFrame = Frame(Matrix3f.Identity);

    /// <summary>Writes the save state every human keeps, in its list's order.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="extras">The fields its kind keeps.</param>
    protected void WriteHumanState(Igi2StateWriter writer, Igi2HumanSaveExtras extras)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(extras);
        writer.Array(Channels.Last);
        writer.Array(Channels.Current);
        writer.Real(Damage.Damage);
        writer.Bool(Channels.IsLocked);
        writer.Bool(IsDeathAnimationDone);
        writer.Bool(extras.IsWeaponReady);
        writer.Bool(extras.IsWeaponFire);
        writer.Bool(extras.IsWeaponAlternateFire);
        writer.Bool(extras.IsWeaponFired);
        writer.Bool(IsThirdPerson);
        writer.Bool(IsLockOrientation);
        writer.Bool(IsDisableDeltaTranslation);
        writer.Bool(extras.IsDisableDeltaTranslationScale);
        writer.Bool(IsRotateUpperBody);
        writer.Bool(IsDisableCollision);
        writer.Bool(IsGroundCollision);
        writer.Bool(IsObjectCollision);
        writer.Bool(true);
        writer.Bool(IsHumanCollision);
        writer.Bool(extras.IsLockActivate);
        writer.Bool(extras.IsCheckActivateButton);
        writer.Bool(false);
        writer.Bool(false);
        writer.Bool(false);
        writer.Bool(true);
        writer.Bool(IsDead);
        writer.Int((int)Pose);
        writer.Int(CameraMode);
        writer.Int(extras.ActivateTime);
        writer.Int(0);
        writer.Int(NextHitSoundTick);
        writer.Int(0);
        writer.Int(FeetSoundIndex);
        writer.Real(extras.WeaponHitProbability);
        writer.Real(Alpha);
        writer.Real(Alpha);
        writer.Real(Beta);
        writer.Real(Beta);
        writer.Real(Gamma);
        writer.Real(IdealGamma);
        writer.Real(1f);
        writer.Real(GroundAngle);
        writer.Real(GroundAngleFactor);
        writer.Real(0f);
        writer.Real(extras.VisibilityFactor);
        writer.Real3(DeltaTranslation);
        writer.Real3(Speed);
        writer.Real3(ImpactSpeed);
        writer.Real3(TotalSpeed);
        writer.Real3(new Vector3f(0f, 0f, 1f));
        writer.Quaternion(Quaternion32.Identity);
        writer.Real64(DistanceToGround);
        writer.Real64(DistanceToRoof);
        writer.Real64x3(Position);
        writer.Real64x3(Position);
        writer.Real9(Rows(Orientation));
        writer.Int(extras.Difficulty);
        writer.Int(extras.MissionId);
        writer.Int(TimesSpotted);
        writer.Int(MissionShots);
        writer.Int(extras.Kills);
        writer.Int(extras.Hits);
        writer.Int(extras.GameLoads);
        writer.Int(extras.Time);
        writer.Int(extras.TimesAlarmTriggered);
        writer.Int(extras.Health);
        writer.Int(0);

        // Human_ptSerializeStateParameterList: the signal, the completed sequence, the previous state, the sequence, the state and its
        // sequence's work, which the state machine here does not keep.
        writer.Int((int)Locomotion.Flags);
        writer.Int(0);
        writer.Int(Locomotion.PreviousState);
        writer.Int(0);
        writer.Int(Locomotion.CurrentState);
        writer.Array(ReadOnlySpan<byte>.Empty);

        writer.TaskReference(GroundCollider?.Owner as GameTask);
        writer.Real64x3(GroundRelativePosition);
        writer.Real64x3(GroundPreviousPosition);
        writer.Real64x3(Position);
        writer.Real3(GroundSpeed);
        writer.Array(IdentityFrame);
        writer.Array(IdentityFrame);
        writer.Bool(IsUpdatePlatformPos);
        writer.Bool(IsPlatformMoving);
        writer.Bool(IsGroundRigidObject);
        writer.Bool(IsProtectedFromFallDamage);
        writer.Real3(AveragedGroundNormal);
        writer.Real3(SlidingSpeed);
        writer.Real9(Rows(DeadOrientation));
    }

    /// <summary>
    /// Reads the save state every human keeps back. Where it stands is its shadow's position; its state is entered afresh, which sets
    /// up its state's running as retail's resumed sequence would.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The fields its kind keeps.</returns>
    protected Igi2HumanSaveExtras ReadHumanState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2HumanSaveExtras extras = new();
        float[] last = reader.FloatArray();
        float[] current = reader.FloatArray();
        Channels.Restore(current, last);
        float damage = reader.Real();
        Damage.Damage = damage;
        Channels.IsLocked = reader.Bool();
        IsDeathAnimationDone = reader.Bool();
        extras.IsWeaponReady = reader.Bool();
        extras.IsWeaponFire = reader.Bool();
        extras.IsWeaponAlternateFire = reader.Bool();
        extras.IsWeaponFired = reader.Bool();
        IsThirdPerson = reader.Bool();
        IsLockOrientation = reader.Bool();
        IsDisableDeltaTranslation = reader.Bool();
        extras.IsDisableDeltaTranslationScale = reader.Bool();
        IsRotateUpperBody = reader.Bool();
        IsDisableCollision = reader.Bool();
        IsGroundCollision = reader.Bool();
        IsObjectCollision = reader.Bool();
        reader.Bool();
        IsHumanCollision = reader.Bool();
        extras.IsLockActivate = reader.Bool();
        extras.IsCheckActivateButton = reader.Bool();
        reader.Bool();
        reader.Bool();
        reader.Bool();
        reader.Bool();
        IsDead = reader.Bool();
        Pose = (Igi2HumanPose)reader.Int();
        CameraMode = reader.Int();
        extras.ActivateTime = reader.Int();
        reader.Int();
        NextHitSoundTick = reader.Int();
        reader.Int();
        FeetSoundIndex = reader.Int();
        extras.WeaponHitProbability = reader.Real();
        Alpha = reader.Real();
        reader.Real();
        Beta = reader.Real();
        reader.Real();
        Gamma = reader.Real();
        IdealGamma = reader.Real();
        reader.Real();
        reader.Real();
        GroundAngleFactor = reader.Real();
        reader.Real();
        extras.VisibilityFactor = reader.Real();
        DeltaTranslation = reader.Real3();
        Speed = reader.Real3();
        ImpactSpeed = reader.Real3();
        TotalSpeed = reader.Real3();
        reader.Real3();
        reader.Quaternion();
        reader.Real64();
        reader.Real64();
        reader.Real64x3();
        Position = reader.Real64x3();
        Orientation = Matrix(reader.Real9());
        extras.Difficulty = reader.Int();
        extras.MissionId = reader.Int();
        TimesSpotted = reader.Int();
        reader.Int();
        extras.Kills = reader.Int();
        extras.Hits = reader.Int();
        extras.GameLoads = reader.Int();
        extras.Time = reader.Int();
        extras.TimesAlarmTriggered = reader.Int();
        extras.Health = reader.Int();
        reader.Int();

        uint flags = unchecked((uint)reader.Int());
        reader.Int();
        reader.Int();
        reader.Int();
        int state = reader.Int();
        reader.Array();

        reader.TaskReference();
        reader.Real64x3();
        GroundPreviousPosition = reader.Real64x3();
        reader.Real64x3();
        reader.Real3();
        reader.Array();
        reader.Array();
        IsUpdatePlatformPos = reader.Bool();
        IsPlatformMoving = reader.Bool();
        reader.Bool();
        IsProtectedFromFallDamage = reader.Bool();
        AveragedGroundNormal = reader.Real3();
        SlidingSpeed = reader.Real3();
        DeadOrientation = Matrix(reader.Real9());

        if (state >= 0 && state < Locomotion.StateCount)
        {
            Locomotion.Enter(state, this);
        }

        Locomotion.Flags = flags;
        return extras;
    }

    private static float[] Rows(Matrix3f m) => [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];

    private static Matrix3f Matrix(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    // A frame as it lies in memory: nine floats, then its operation count.
    private static byte[] Frame(Matrix3f m)
    {
        byte[] bytes = new byte[40];
        float[] rows = Rows(m);
        for (int i = 0; i < 9; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), rows[i]);
        }

        return bytes;
    }
}

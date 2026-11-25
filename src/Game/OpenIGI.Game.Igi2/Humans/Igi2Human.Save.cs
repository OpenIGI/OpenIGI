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

    /// <summary>Gets or sets the alarms it set off, <c>nTimesAlarmTriggered</c>.</summary>
    public int TimesAlarmTriggered { get; set; }

    /// <summary>Gets or sets its health, <c>nHealth</c>.</summary>
    public int Health { get; set; } = 100;

    /// <summary>Gets or sets its state machine's signal as saved, <c>State.bStateSignal</c>.</summary>
    public uint StateSignal { get; set; }

    /// <summary>Gets or sets the state before the saved one, <c>State.nStatePreviousState</c>.</summary>
    public int PreviousState { get; set; }

    /// <summary>Gets or sets the state it was saved in, <c>State.nStateState</c>.</summary>
    public int State { get; set; }
}

/// <content>
/// Its save state, <c>Human_ptSerializeParameterList</c> (human.c): a model object's, then its channels as they were and are, its damage, its flags, its pose,
/// camera and timers, its angles, its speeds, its distances to the ground and roof, its mission's figures, its state machine
/// (<c>Human_ptSerializeStateParameterList</c>), and what it stands on.
/// </content>
public abstract partial class Igi2Human
{
    /// <summary>Writes the save state every human keeps, in its list's order.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="extras">The fields its kind keeps.</param>
    protected void WriteHumanState(StateWriter writer, Igi2HumanSaveExtras extras)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(extras);
        Igi2ModelObjState.Write(writer, Position, Rows(Orientation), IsActive);
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
        writer.Int(MissionKills);
        writer.Int(MissionHits);
        writer.Int(GameLoads);
        writer.Int(MissionTime);
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

        // What it stands on, and where on it: the task the footing struck, as the save names it, and the platform's notes.
        writer.TaskReference(Surroundings.GroundTaskOf(GroundCollider));
        writer.Real64x3(GroundRelativePosition);
        writer.Real64x3(GroundPreviousPosition);
        writer.Real64x3(GroundOriginalPosition);
        writer.Real3(GroundSpeed);
        writer.Array(Frame(_groundInverseOriginal));
        writer.Array(Frame(_groundRelativeOrientation));
        writer.Bool(IsUpdatePlatformPos);
        writer.Bool(IsPlatformMoving);
        writer.Bool(IsGroundRigidObject);
        writer.Bool(IsProtectedFromFallDamage);
        writer.Real3(AveragedGroundNormal);
        writer.Real3(SlidingSpeed);
        writer.Real9(Rows(DeadOrientation));
    }

    /// <summary>
    /// Reads the save state every human keeps back. Its state machine is left to its kind, whose deserialize handler enters the
    /// state the load puts it in (<see cref="Igi2HumanSaveExtras.State"/> is the one it was saved in).
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The fields its kind keeps.</returns>
    protected Igi2HumanSaveExtras ReadHumanState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2HumanSaveExtras extras = new();
        (Vector3d position, float[] orientation, bool inWorld) = Igi2ModelObjState.Read(reader);
        Position = position;
        Orientation = Matrix(orientation);

        // ModelObjParameters_DeSerializeHandler: taken out of the world, read, and registered again only when the save had it in, so
        // a soldier whose container was shut when the game was saved is out of it once more, neither drawn nor bumped into.
        SetInWorld(inWorld);
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
        GroundAngle = reader.Real();
        GroundAngleFactor = reader.Real();
        reader.Real();
        extras.VisibilityFactor = reader.Real();
        DeltaTranslation = reader.Real3();
        Speed = reader.Real3();
        ImpactSpeed = reader.Real3();
        TotalSpeed = reader.Real3();
        reader.Real3();
        reader.Quaternion();
        DistanceToGround = reader.Real64();
        DistanceToRoof = reader.Real64();
        reader.Real64x3();
        reader.Real64x3();
        reader.Real9();
        extras.Difficulty = reader.Int();
        extras.MissionId = reader.Int();
        TimesSpotted = reader.Int();
        MissionShots = reader.Int();
        MissionKills = reader.Int();
        MissionHits = reader.Int();
        GameLoads = reader.Int();
        MissionTime = reader.Int();
        extras.TimesAlarmTriggered = reader.Int();
        extras.Health = reader.Int();
        reader.Int();

        uint flags = unchecked((uint)reader.Int());
        reader.Int();
        int previous = reader.Int();
        reader.Int();
        int state = reader.Int();
        reader.Array();

        // ModelObjParameters_DeSerializeHandler parses the ground's reference and notes as they were written: the body stands on
        // the platform it was saved on, at its saved place on it, so the platform's next carry puts it where it was saved.
        GroundCollider = Surroundings.GroundOf(reader.TaskReference());
        GroundRelativePosition = reader.Real64x3();
        GroundPreviousPosition = reader.Real64x3();
        GroundOriginalPosition = reader.Real64x3();
        GroundSpeed = reader.Real3();
        ReadFrame(reader, _groundInverseOriginal);
        ReadFrame(reader, _groundRelativeOrientation);
        IsUpdatePlatformPos = reader.Bool();
        IsPlatformMoving = reader.Bool();
        IsGroundRigidObject = reader.Bool();
        IsProtectedFromFallDamage = reader.Bool();
        AveragedGroundNormal = reader.Real3();
        SlidingSpeed = reader.Real3();
        DeadOrientation = Matrix(reader.Real9());

        extras.StateSignal = flags;
        extras.PreviousState = previous;
        extras.State = state;
        return extras;
    }

    /// <summary>
    /// Stops what a body carried from before a load, as <c>HumanPlayer_DeSerializeHandler</c> does once its list is read: the
    /// speed it took off with (<c>tAccumulatedSpeed</c>) and how far it moved over its last move (<c>tWorldSpeed</c>), neither of
    /// which a save keeps.
    /// </summary>
    protected void ForgetCarriedSpeedsForLoad()
    {
        AccumulatedSpeed = default;
        WorldSpeed = default;
    }

    /// <summary>
    /// Enters the state a load puts a state machine in, as a kind's deserialize handler does: the saved fields put back, then the
    /// state entered from the saved one -- its exit run, the saved state left as the one before, the new state's entry run.
    /// </summary>
    /// <param name="machine">The state machine: the body's, or the player's action machine.</param>
    /// <param name="state">The state to enter.</param>
    /// <param name="signal">The saved signal, <c>bStateSignal</c>.</param>
    /// <param name="previous">The saved previous state.</param>
    /// <param name="saved">The saved state.</param>
    protected void EnterLoadedState(Engine.Tasks.TaskStateMachine machine, int state, uint signal, int previous, int saved)
    {
        ArgumentNullException.ThrowIfNull(machine);
        machine.Flags = signal;
        machine.RestoreSaved(previous, saved);
        machine.Enter(state, this);
    }

    /// <summary>Enters the state a load puts the body in (<see cref="EnterLoadedState(Engine.Tasks.TaskStateMachine, int, uint, int, int)"/>).</summary>
    /// <param name="state">The state to enter.</param>
    /// <param name="saved">What the save held.</param>
    protected void EnterLoadedState(int state, Igi2HumanSaveExtras saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        EnterLoadedState(Locomotion, state, saved.StateSignal, saved.PreviousState, saved.State);
    }

    /// <summary>
    /// Gets the parts a human keeps as child tasks: its weapon, which <c>Human_CreateHandler</c> makes, then its view, which
    /// <c>Human_UpdateInternalDataHandler</c> makes (human.c).
    /// </summary>
    /// <param name="inventory">What it carries, if it carries anything.</param>
    /// <returns>The parts.</returns>
    public IReadOnlyList<SavedPart> SavedParts(Weapons.Igi2Inventory? inventory) =>
        inventory is null
            ? [new("HumanView", new ViewState(View))]
            : [new(Saves.Igi2SaveSlots.WeaponPart, inventory), new("HumanView", new ViewState(View))];

    private static float[] Rows(Matrix3f m) => [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];

    private static Matrix3f Matrix(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    // A frame as it lies in memory: nine floats, then its operation count.
    private static byte[] Frame(float[] rows)
    {
        byte[] bytes = new byte[40];
        for (int i = 0; i < 9; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), rows[i]);
        }

        return bytes;
    }

    // A frame read back as it lay in memory: its nine floats, its operation count dropped.
    private static void ReadFrame(StateReader reader, float[] into)
    {
        float[] read = reader.FloatArray();
        if (read.Length >= 9)
        {
            Array.Copy(read, into, 9);
        }
    }

    private sealed class ViewState(Igi2HumanView view) : ISavedState
    {
        public void WriteState(StateWriter writer) => view.WriteState(writer);

        public void ReadState(StateReader reader) => view.ReadState(reader);
    }
}

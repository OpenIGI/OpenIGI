using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>
/// Its save state, <c>HumanSoldier_ptSerializeParameterList</c>: a human's (its difficulty, how well it hits and how well it is seen
/// are a soldier's own), then the state that last set its main clip, which squads have seen it dead and whether a camera has.
/// </content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>Writes its save state.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteSoldierState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteHumanState(writer, new Igi2HumanSaveExtras
        {
            Difficulty = (int)Difficulty,
            WeaponHitProbability = WeaponHitProbability,
            VisibilityFactor = VisibilityFactor,
        });
        writer.Int(Animation.LastState);
        writer.Int(unchecked((int)(uint)_reportedSquads));
        writer.Int(unchecked((int)(uint)(_reportedSquads >> 32)));
        writer.Bool(IsSeenByCamera);
    }

    /// <summary>
    /// Reads its save state back, <c>HumanSoldier_DeSerializeHandler</c> (humansoldier.c): a gun it mans is let go first; once read,
    /// it faces its heading alone, its weapon stops and falls silent and its upper body idles; then a body read back dead lies
    /// dead (<see cref="LoadDead"/>) and a living one stands (<see cref="LoadAlive"/>), whatever state either was saved in.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <remarks>
    /// A save is read onto the level as it is now, so whatever the soldier has done since -- died, mounted a gun, fired -- is put
    /// right here rather than left over.
    /// </remarks>
    public void ReadSoldierState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (MountedGun is Igi2StationaryGunTask gun)
        {
            DeactivateStationaryGun(gun);
        }

        Igi2HumanSaveExtras extras = ReadHumanState(reader);
        int lastMainState = reader.Int();
        uint seenLow = unchecked((uint)reader.Int());
        uint seenHigh = unchecked((uint)reader.Int());
        _reportedSquads = seenLow | ((ulong)seenHigh << 32);
        IsSeenByCamera = reader.Bool();
        WeaponHitProbability = extras.WeaponHitProbability;
        VisibilityFactor = extras.VisibilityFactor;

        float[] facing = EngineEuler.Rotation(0f, 0f, Gamma);
        Orientation = new Matrix3f(facing[0], facing[1], facing[2], facing[3], facing[4], facing[5], facing[6], facing[7], facing[8]);
        // A weapon still firing lets go of both triggers, and every sound it has stops: the block HumanPlayer_InitStateSwim
        // runs too.
        Weapon.Firing?.ReleaseAutoFire();
        Weapon.Firing?.Effects.StopAllSounds();
        UpperBodyFlags = 0;
        UpperBodyState = Igi2SoldierUpperBody.Idle;

        if (IsDead)
        {
            LoadDead(lastMainState, extras);
        }
        else
        {
            LoadAlive(extras);
        }
    }

    // The dead branch: the body lies in its pool, already spread, its light the body's (vLightmap); heading fixed, still
    // (tBoneCollisionData.tDeltaMovement cleared), settled, its mind dead too (no death checks run again), and it lies dead at
    // the end of the clip the state it died in last set (HumanSoldier_InitStateDead).
    private void LoadDead(int lastMainState, Igi2HumanSaveExtras saved)
    {
        IsBloodPuddle = true;
        Shadow.BecomePool(Lightmap, spread: false);
        IsLockOrientation = true;
        IsRotateUpperBody = false;
        IsDisableDeltaTranslation = false;
        BoneDeltaMovement = default;
        BoneCollisionDelta = default;
        Animation.SetMainFlags(raise: Igi2AnimControllerBits.NoRootTranslation, clear: Igi2AnimControllerBits.None);
        LifeState = DeadAndStill;
        IsDeathPositionStabilized = true;
        MarkMindDead(true);
        Animation.LastState = lastMainState;
        EnterLoadedState(Igi2SoldierState.Dead, saved);
    }

    // The living branch, in the handler's order: its mark is a shadow again; the stand's clip from its start
    // (HumanAnim_SetAnimationAndTimeFromState with state 0, time 0, no blend); free to turn and move by its clip, still, not
    // settled, not dying, its mind alive; a death's 0x40 cleared from every controller it has, so each moves the body by its
    // root again; the main clip stepped once; and it stands (HumanSoldier_InitStateStand).
    private void LoadAlive(Igi2HumanSaveExtras saved)
    {
        IsBloodPuddle = false;
        Shadow.BecomeShadow();
        PlayRow(Igi2SoldierState.Stand, 0f);
        IsLockOrientation = false;
        IsRotateUpperBody = true;
        IsDisableDeltaTranslation = false;
        BoneDeltaMovement = default;
        BoneCollisionDelta = default;
        IsDeathPositionStabilized = false;
        LifeState = 0;
        MarkMindDead(false);
        Animation.ClearFlagsOnEveryController(Igi2AnimControllerBits.NoRootTranslation);
        Animation.StepMain();
        EnterLoadedState(Igi2SoldierState.Stand, saved);
    }

    // HumanAI's own dead byte, which the soldier's handler sets with the body's (HumanSoldier_DeSerializeHandler).
    private void MarkMindDead(bool isDead)
    {
        if (Mind is Ai.Runtime.Igi2HumanAi mind)
        {
            mind.IsDead = isDead;
        }
    }
}

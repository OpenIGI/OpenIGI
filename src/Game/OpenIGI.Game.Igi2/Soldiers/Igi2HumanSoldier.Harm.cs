using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>
/// The death clip a soldier's death plays next, <c>HumanSoldier_iDeathAnimCycle</c> (<c>igi2 0x7D46188</c>): one counter every soldier
/// shares, going round eleven clips.
/// </summary>
public sealed class Igi2DeathAnimCycle
{
    /// <summary>Gets or sets the next clip's place in the cycle, nought to ten.</summary>
    public int Next { get; set; }
}

/// <content>Flinching from a hit, and dying.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>A soldier dying, <c>HUMANSOLDIER_LIFESTATE_DYING</c>.</summary>
    public const int Dying = 1;

    /// <summary>A soldier dead and at rest, <c>HUMANSOLDIER_LIFESTATE_DEAD</c>.</summary>
    public const int DeadAndStill = 2;

    /// <summary>
    /// The death clips a standing death goes round, <c>HumanSoldier_InitStateDie</c>'s eleven (<c>igi2 0x4E8745</c>): the first three a
    /// head shot's.
    /// </summary>
    public static IReadOnlyList<int> DeathRows { get; } = [65, 66, 67, 71, 72, 73, 74, 75, 76, 77, 78];

    /// <summary>The death clips a kneeling death draws from, <c>HumanSoldier_InitStateKneelDie</c>'s six (<c>igi2 0x4E896B</c>).</summary>
    public static IReadOnlyList<int> KneelDeathRows { get; } = [55, 56, 57, 58, 59, 60];

    /// <summary>The row a kneeling soldier's short flinch to the legs plays, <c>27</c>.</summary>
    public const int KneelHitRow = 27;

    private const float OneOverPi = -0.31830987f;
    private const float Pi = 3.1415927f;

    /// <summary>Gets or sets the death clip counter the level's soldiers share.</summary>
    public Igi2DeathAnimCycle DeathCycle { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the dying soldier's cry has sounded, <c>tDieContext.isSoundPlayed</c>.</summary>
    public bool IsDeathSoundPlayed { get; set; }

    /// <summary>
    /// A flinch, <c>HumanSoldier_InitStateHit</c> (<c>igi2 0x4E7CD0</c>): a short clip drawn from three, or the zone's long one once
    /// the hits since the last flinch pass the threshold. A hit to the body or arms plays it on the upper body (the legs standing
    /// tense if upright); one to the legs plays it on the whole body, a kneeling soldier's from the kneeling clips, with the upper
    /// body tense over it.
    /// </summary>
    private void EnterHit()
    {
        uint draw = Random.NextRaw();
        int shortHit = (int)(draw % 3);

        // A flinch always follows a hit, which names its zone.
        Igi2DamagePart part = Damage.LastPart == Igi2DamagePart.None ? Igi2DamagePart.RightFoot : Damage.LastPart;
        Igi2SoldierHitRows rows = Igi2SoldierHitRows.Of(part);
        bool onUpperBody = part is not (Igi2DamagePart.RightFoot or Igi2DamagePart.RightLowerLeg or Igi2DamagePart.RightUpperLeg
            or Igi2DamagePart.LeftFoot or Igi2DamagePart.LeftLowerLeg or Igi2DamagePart.LeftUpperLeg);
        ReleaseUpperBody();

        bool kneeling = Pose == Igi2HumanPose.Crouching;
        if (RecoilMeter >= RecoilThreshold && !IsLongHitAnimPlayed)
        {
            IsLongHitAnimPlayed = true;
            if (onUpperBody)
            {
                FlinchUpperBody(rows.LongHit);
            }
            else
            {
                FlinchLowerBody(kneeling ? rows.KneelLongHit : rows.LongHit);
            }
        }
        else if (onUpperBody)
        {
            FlinchUpperBody(rows.ShortHits[shortHit]);
        }
        else
        {
            FlinchLowerBody(kneeling ? KneelHitRow : rows.ShortHits[shortHit]);
        }

        IsRotateUpperBody = false;
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    private void FlinchUpperBody(int row)
    {
        if (Pose == Igi2HumanPose.Standing)
        {
            PlayRow(TenseStandRow, 0f);
        }

        Animation.PlayUpperBodyState(row, 0, 0f);
        Locomotion.Current = (_, _) => RunHit(upperBody: true);
    }

    private void FlinchLowerBody(int row)
    {
        PlayRow(row, 0f);
        Animation.PlayUpperBodyState(UpperTense, 0, 0f);
        Locomotion.Current = (_, _) => RunHit(upperBody: false);
    }

    /// <summary>
    /// A flinch's tick, <c>HumanSoldier_RunStateHitLowerBody</c> (<c>igi2 0x4E7E70</c>) and <c>RunStateHitUpperBody</c>
    /// (<c>0x4E8040</c>): off the ground it falls, dead it dies, hit again it flinches again, and once its clip ends it kneels or
    /// stands. A whole-body flinch waits on the main clip and keeps its upper body tense; an upper-body one waits on that clip.
    /// </summary>
    private void RunHit(bool upperBody)
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            if (!upperBody)
            {
                Animation.ClearUpperBody(0f);
            }

            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Animation.ClearUpperBody(0f);
            Go(Pose == Igi2HumanPose.Crouching ? Igi2SoldierState.KneelDie : Igi2SoldierState.Die);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            IsRotateUpperBody = true;
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & (upperBody ? Igi2SoldierSignal.UpperBodyEnded : Igi2SoldierSignal.MainEnded)) != 0)
        {
            IsRotateUpperBody = true;
            Go((s & Igi2SoldierSignal.Kneel) != 0 ? Igi2SoldierState.Kneel : Igi2SoldierState.Stand);
        }
        else if (!upperBody && (s & Igi2SoldierSignal.UpperBodyEnded) != 0)
        {
            Animation.PlayUpperBodyState(UpperTense, 0, 0f);
        }
    }

    /// <summary>
    /// A standing death, <c>HumanSoldier_InitStateDie</c> (<c>igi2 0x4E8450</c>): the weapon stops, the body stands upright, and the
    /// clip and the push it takes are chosen by how it died and which way the shot came.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How it died is nought for no weapon's hit yet ported: the soldier turns from the shot and falls one of two ways if it came
    /// from behind, or the third if from the front, pushed along it. By a shotgun likewise between two. By a gun, a head, chest or
    /// body shot from behind that did more than two damage throws it back three times in ten. Otherwise it plays the next of the
    /// shared eleven clips, a head shot one of the first three, pushed along the shot by a gun or a pistol.
    /// </para>
    /// <para>Not yet: the gun pickups it drops (<c>HumanSoldier_CreateGunPickups</c>, <c>0x4E8360</c>).</para>
    /// </remarks>
    private void EnterDie()
    {
        StopWeapon(onlyWhenBusy: true);
        IsDeathSoundPlayed = false;
        Pose = Igi2HumanPose.Standing;
        DeathDirection = Normalised(DeathDirection);
        Vector3f d = DeathDirection;
        Igi2DamagePart part = Damage.LastPart;
        int row;
        switch (DeathByType)
        {
            case 0:
            {
                uint draw = Random.NextRaw();
                row = TurnFromShot() ? (draw % 10 >= 5 ? 81 : 80) : 82;
                Push(d, 680f, 272f);
                break;
            }

            case 1:
                row = TurnFromShot() ? 81 : 68;
                Push(d, 680f, 272f);
                break;

            case 2 when part is Igi2DamagePart.Head or Igi2DamagePart.Chest or Igi2DamagePart.UpperBody
                && TurnFromShot()
                && Random.NextDouble() < 0.30000001f
                && DeathDamage > 2.0f:
                row = 64;
                Push(d, 1088f, 544f);
                break;

            default:
                row = CycledDeath(part, d);
                break;
        }

        PlayRow(row, 0f);
        Animation.SetMainFlags(raise: Igi2AnimControllerBits.None, clear: Igi2AnimControllerBits.Loop);
        LieStill(lockOrientation: false);
        GroundTolerance = 8192f;
        Animation.SetMainFlags(raise: Igi2AnimControllerBits.NoRootTranslation, clear: Igi2AnimControllerBits.None);
        Locomotion.Current = (_, _) => RunDie();
    }

    /// <summary>The next of the shared death clips, a head shot's one of the first three, and its push.</summary>
    private int CycledDeath(Igi2DamagePart part, Vector3f d)
    {
        int cycle = DeathCycle.Next;
        int row;
        if (part == Igi2DamagePart.Head)
        {
            row = DeathRows[cycle % 3];
        }
        else
        {
            switch (DeathByType)
            {
                case 2:
                    Push(d, 340f, 136f);
                    break;
                case 3:
                    Push(d, 204f, 136f);
                    break;
                default:
                    Speed = new Vector3f(((Fpu)0f + Speed.X).F, ((Fpu)0f + Speed.Y).F, ((Fpu)0f + Speed.Z).F);
                    break;
            }

            row = DeathRows[cycle];
        }

        DeathCycle.Next = (cycle + 1) % DeathRows.Count;
        return row;
    }

    /// <summary>
    /// A kneeling death, <c>HumanSoldier_InitStateKneelDie</c> (<c>igi2 0x4E8950</c>): the weapon stops, and one of six kneeling
    /// clips is drawn; the body keeps its heading.
    /// </summary>
    /// <remarks>Not yet: the gun pickups it drops.</remarks>
    private void EnterKneelDie()
    {
        IsDeathSoundPlayed = false;
        StopWeapon(onlyWhenBusy: false);
        Pose = Igi2HumanPose.Crouching;
        uint draw = Random.NextRaw();
        PlayRow(KneelDeathRows[(int)(draw % (uint)KneelDeathRows.Count)], 0f);
        LieStill(lockOrientation: true);
        GroundTolerance = 8192f;
        Animation.SetMainFlags(raise: Igi2AnimControllerBits.NoRootTranslation, clear: Igi2AnimControllerBits.None);
        Locomotion.Current = (_, _) => RunDie();
    }

    /// <summary>
    /// A death's tick, <c>HumanSoldier_RunStateDie</c> (<c>igi2 0x4E8C00</c>): the soldier is dying; once the clip ends the body's fall
    /// is heard, and <c>RunStateDie2</c> (<c>0x4E8C70</c>) takes over.
    /// </summary>
    private void RunDie()
    {
        LifeState = Dying;
        if ((Signal & Igi2SoldierSignal.MainEnded) == 0)
        {
            return;
        }

        Locomotion.Current = (_, _) => RunDieAfterClip();

        // The event's direction is left as the stack held it; none is meant.
        SendAiEvent(new Igi2AiEvent(Igi2AiEvent.GroundImpact, -1, 0x442, 20480f, Position, default, this));
    }

    /// <summary><c>HumanSoldier_RunStateDie2</c> (<c>igi2 0x4E8C70</c>): lying, the clip done, and dead once the body is at rest.</summary>
    private void RunDieAfterClip()
    {
        Pose = Igi2HumanPose.LyingFlat;
        IsDeathAnimationDone = true;
        if (IsDeathPositionStabilized)
        {
            LifeState = DeadAndStill;
        }
    }

    /// <summary>
    /// The weapon let go: the first fire type's trigger released (<c>Weapon+0x70</c>) and the status back to idle
    /// (<c>Weapon+0x38</c>), always for a kneeling death and for a standing one only when the weapon is busy.
    /// </summary>
    private void StopWeapon(bool onlyWhenBusy)
    {
        if (Weapon.Inventory is not { } inventory || (onlyWhenBusy && inventory.Status == default))
        {
            return;
        }

        if (Weapon.Firing is { } firing)
        {
            firing.FireTypeState(0).IsReleaseFire = true;
        }

        inventory.Status = default;
    }

    /// <summary>The camera and body set as a death leaves them: no turning of the upper body, the upper body off, no root motion.</summary>
    private void LieStill(bool lockOrientation)
    {
        CameraMode = 3;
        View.IsLockOrientation = true;
        IsLockOrientation = lockOrientation;
        IsRotateUpperBody = false;
        Animation.ClearUpperBody(0f);
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        DeltaTranslation = default;
        IsDisableDeltaTranslation = true;
    }

    /// <summary>The body pushed along the shot and up: <c>tSpeed += d × scale</c>, the height's share plus a lift.</summary>
    private void Push(Vector3f d, float scale, float lift)
    {
        float z = (((Fpu)d.Z * scale) + lift).F;
        Fpu x = (Fpu)d.X * scale, y = (Fpu)d.Y * scale;
        Speed = new Vector3f((x + Speed.X).F, (y + Speed.Y).F, ((Fpu)z + Speed.Z).F);
    }

    /// <summary>
    /// Turns the soldier away from the killing shot, <c>HumanSoldier_FindCollisionDirectionAndTurn</c> (<c>igi2 0x4EC900</c>): the turn
    /// channel takes the shot's heading in the body's frame, a half turn more when the shot came from behind, over minus pi.
    /// </summary>
    /// <returns>Whether the shot came from behind, or there was none.</returns>
    private bool TurnFromShot()
    {
        Vector3f shot = DeathDirection;
        if (shot.X == 0f && shot.Y == 0f && shot.Z == 0f)
        {
            return true;
        }

        Vector3f n = Normalised(shot);
        Matrix3f o = Orientation;
        float x = ((((Fpu)n.Y * o.M21) + ((Fpu)n.Z * o.M31)) + ((Fpu)n.X * o.M11)).F;
        float y = ((((Fpu)n.Z * o.M32) + ((Fpu)n.X * o.M12)) + ((Fpu)n.Y * o.M22)).F;
        Fpu facing = (((Fpu)n.Z * o.M32) + ((Fpu)o.M22 * n.Y)) + ((Fpu)o.M12 * n.X);
        bool behind = facing.Value <= 0.0;
        float gamma = Igi2Pose.CalcGamma(x, y);
        if (behind)
        {
            gamma = ((Fpu)gamma + Pi).F;
        }

        Channels[Igi2HumanChannel.LookX] = ((Fpu)gamma * OneOverPi).F;
        return behind;
    }

    /// <summary>A direction scaled to one, as a death and its turn normalise it; a zero one stays zero.</summary>
    private static Vector3f Normalised(Vector3f d)
    {
        Fpu length = Fpu.Sqrt((((Fpu)d.X * d.X) + ((Fpu)d.Y * d.Y)) + ((Fpu)d.Z * d.Z));
        if (!(length.Value > 0.0))
        {
            return default;
        }

        Fpu inverse = (Fpu)1.0 / length;
        return new Vector3f((inverse * d.X).F, (inverse * d.Y).F, (inverse * d.Z).F);
    }
}

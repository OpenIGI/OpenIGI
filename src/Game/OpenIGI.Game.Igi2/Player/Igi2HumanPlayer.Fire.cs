using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Firing: the action states that pull and let go the trigger, the kick the view takes, and where the player's weapon
/// fires from (<c>HumanPlayer_apfAction</c> 2, 3, 4 and 9).
/// </content>
/// <remarks>
/// <para>
/// The trigger is pulled once a pull: the fire state raises <see cref="IsFire"/> on its first tick, and the update fires
/// the weapon after the view is placed. Held fire after that is the weapon's own. The state watches the weapon: a shot
/// that kicked is taken by the view and plays the fire mode on the upper body, a weapon gone empty puts the trigger up,
/// and the fire key coming up lets go.
/// </para>
/// <para>
/// A weapon that never runs out reloads itself when empty, and the AI hears it (<c>HumanPlayer_SendHandleWeaponAIEvent</c>).
/// </para>
/// </remarks>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>How much of a kick, up and sideways summed, spreads the weapon's precision, <c>0x3ECCCCCD</c>.</summary>
    private const float KickbackSpread = 0.40000001f;

    /// <summary>Degrees to radians as the kick and the inaccuracy take them, <c>0x3C8EFA35</c>.</summary>
    private const float DegreesToRadians = 0.017453292f;

    /// <summary>A whole turn as the inaccuracy's angle takes it, <c>0x40C90FDB</c>.</summary>
    private const float WholeTurn = 6.2831855f;

    /// <summary>The perfect-shot timer at which inaccuracy stops, in ticks: a player still that long fires true.</summary>
    public const int PerfectShotTicks = 60;

    /// <summary>The weapon ids the idle state compares the weapon in hand with to plant a charge, <c>WEAPON_ID_C4BOMB</c>.</summary>
    public const string C4BombWeaponId = "WEAPON_ID_C4BOMB";

    /// <summary>The channel each fire type's trigger is, <c>HumanPlayer_anFireTypeChannel</c> (<c>igi2 0x63E034</c>).</summary>
    private static readonly Igi2HumanChannel[] FireTypeChannels = [Igi2HumanChannel.Fire, Igi2HumanChannel.AlternateFire];

    /// <summary>Gets or sets a value indicating whether the update is to pull the trigger this tick, <c>isFire</c>.</summary>
    public bool IsFire { get; set; }

    /// <summary>Gets or sets a value indicating whether the fire state is on its first tick, <c>isFirstFire</c>.</summary>
    public bool IsFirstFire { get; set; }

    /// <summary>Gets or sets the ticks a grenade has been held with its pin out, <c>nGrenadeHeldTime</c>.</summary>
    public int GrenadeHeld { get; set; }

    /// <inheritdoc/>
    public override bool IsPlayer => true;

    /// <inheritdoc/>
    public override int GrenadeHeldTime => GrenadeHeld;

    /// <inheritdoc/>
    public override bool HasFireFrame => true;

    /// <summary>
    /// Where the player's weapon fires from: <c>HumanPlayer_WeaponGetParentParametersHandler</c> (<c>igi2 0x4BCEC0</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view's position and orientation. With inaccuracy asked for and the player not still for a second
    /// (<see cref="PerfectShotTicks"/>), the orientation is turned off true by the weapon's and the posture's settled
    /// precision together: two draws from the run stream, the first scaling the precision in degrees for how far, the
    /// second a whole turn for which way round, built as a rotation by those two angles and taken after the view's own
    /// (the view's orientation times it).
    /// </para>
    /// <para>
    /// The copy-protection degrade factors scale both the timer and the precision by how long the game has run; in a
    /// genuine copy they are one (<c>DEGRADE_PlayerPerfectShotTimer</c>, <c>DEGRADE_PlayerAccuracy</c>, both
    /// <c>0x3F800000</c>) and the scale drops out exactly. The view's override position is not ported.
    /// </para>
    /// </remarks>
    public override Igi2FireFrame GetFireFrame(bool addInaccuracy)
    {
        Matrix3f v = View.Orientation;
        float[] o = [v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33];
        if (!addInaccuracy || PerfectShotTimer >= PerfectShotTicks)
        {
            return new Igi2FireFrame(View.Position, o);
        }

        float precision = ((Fpu)ActualWeaponPrecision + ActualModifiedPrecision).F;
        float far = (float)Random.NextDouble();
        float round = (float)Random.NextDouble();
        float beta = ((Fpu)round * WholeTurn).F;
        float alpha = (((Fpu)precision * DegreesToRadians) * far).F;
        float[] n = EngineEuler.Rotation(alpha, beta, 0f);

        float[] turned = new float[9];
        for (int c = 0; c < 3; c++)
        {
            turned[c] = ((((Fpu)n[c] * o[0]) + ((Fpu)n[6 + c] * o[2])) + ((Fpu)n[3 + c] * o[1])).F;
            turned[3 + c] = ((((Fpu)n[6 + c] * o[5]) + ((Fpu)n[c] * o[3])) + ((Fpu)n[3 + c] * o[4])).F;
            turned[6 + c] = ((((Fpu)n[3 + c] * o[7]) + ((Fpu)n[6 + c] * o[8])) + ((Fpu)n[c] * o[6])).F;
        }

        return new Igi2FireFrame(View.Position, turned);
    }

    /// <summary>
    /// The weapon objects the player holds its weapon by: in first person, the clip's <c>weapon</c> attach objects on the
    /// view (<c>Human_GetWeaponObjectHandler</c>, <c>igi2 0x500A10</c>, the view's controller's <c>WeaponMagicObj</c>
    /// children).
    /// </summary>
    /// <returns>The number.</returns>
    /// <remarks>Seen from behind, or with the camera elsewhere, the weapon is the one on the body (<see cref="Igi2Human.BodyWeaponObjectCount"/>).</remarks>
    public override int WeaponObjectCount()
    {
        if (IsThirdPerson || CameraMode != 1)
        {
            return BodyWeaponObjectCount();
        }

        if (View.ObjectAnimation.Clip is not { } clip)
        {
            return 0;
        }

        int count = 0;
        foreach (Formats.Animation.Igi2AnimationAttachObject attached in clip.AttachObjects)
        {
            if (string.Equals(attached.Name, Igi2FirstPersonFrame.WeaponAttachName, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The kick a shot gives the view, <c>HumanPlayer_Kickback</c> (<c>igi2 0x4B4690</c>).</summary>
    /// <remarks>
    /// The weapon's cross-hair damping becomes the player's, the kick up and sideways summed spreads the weapon's
    /// precision by four tenths of it, each is added to the view's offset in radians, and the view's offsets die away by
    /// the weapon's kick damping.
    /// </remarks>
    private void Kickback(Igi2WeaponFiring firing)
    {
        float alpha = firing.AlphaKickback;
        float gamma = firing.GammaKickback;
        CrossHairDamping = firing.CrossHairDamping;
        ActualWeaponPrecision = ((((Fpu)alpha + gamma) * KickbackSpread) + ActualWeaponPrecision).F;
        View.PitchOffset = (((Fpu)alpha * DegreesToRadians) + View.PitchOffset).F;
        View.YawOffset = (((Fpu)gamma * DegreesToRadians) + View.YawOffset).F;
        View.OffsetDamping = firing.KickbackDamping;
    }

    /// <summary>Takes a shot's kick, if it kicked, and plays the fire mode on the upper body.</summary>
    private void TakeKickback(Igi2WeaponFiring firing)
    {
        if (!firing.IsKickback)
        {
            return;
        }

        Kickback(firing);
        firing.TakeKickback();
        SetUpperBody(6);
    }

    /// <summary>The trigger's first tick: <c>HumanPlayer_InitActionInitFire</c> (<c>igi2 0x4B5060</c>) raises the first-shot flag and fires.</summary>
    private void EnterInitFire()
    {
        IsFirstFire = true;
        Action.Current = (_, _) => RunFireRegular();
        RunFireRegular();
    }

    /// <summary>The trigger held, <c>HumanPlayer_RunActionFireRegular</c> (<c>igi2 0x4B5090</c>).</summary>
    /// <remarks>
    /// <para>
    /// Firing keeps the perfect-shot timer at nothing. A lock locks. The fire type's key up after the first tick lets go,
    /// unless the weapon waits on the server: the trigger stays unpulled, a last kick is taken, and the release state
    /// follows.
    /// </para>
    /// <para>
    /// Otherwise the weapon's status decides. A shot that kicked is taken. An empty weapon ends the pull: with ammunition
    /// that never runs out it reloads where it stands; otherwise the trigger is let go and the weapon made ready, and once
    /// the first-person clip has played the empty state follows. Last, a fire type in the slot fires on the first tick —
    /// every tick for a laser designator — and the first tick is over.
    /// </para>
    /// </remarks>
    private void RunFireRegular()
    {
        PerfectShotTimer = 0;
        if (IsLockAction)
        {
            GoAction(Igi2PlayerActionState.Lock);
            return;
        }

        Igi2WeaponFiring? firing = Weapon.Firing;
        Igi2Inventory? inventory = firing?.Inventory;
        if (Channels[FireTypeChannels[FireType]] == 0f && !IsFirstFire && inventory?.Status != Igi2WeaponStatus.Waiting)
        {
            IsFire = false;
            if (firing is not null)
            {
                TakeKickback(firing);
            }

            GoAction(Igi2PlayerActionState.FireRelease);
            return;
        }

        if (firing is not null && inventory is not null)
        {
            if (inventory.Status == Igi2WeaponStatus.Fire)
            {
                TakeKickback(firing);
            }
            else if (inventory.Status == Igi2WeaponStatus.Empty)
            {
                IsFirstFire = false;
                if (IsUnlimitedAmmo)
                {
                    firing.Reload();
                    SendHandleWeaponAiEvent();
                }
                else
                {
                    firing.FireTypeState(FireType).IsReleaseFire = true;
                    inventory.Status = Igi2WeaponStatus.Ready;
                    if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) != 0)
                    {
                        GoAction(Igi2PlayerActionState.FireEmpty);
                        return;
                    }
                }
            }
        }

        if (HeldItem?.WeaponType is { } weaponType && weaponType.FireTaskType(FireType) != Igi2WeaponConstants.NoTaskType
            && (IsFirstFire || weaponType.WeaponCategory == LaserDesignatorCategory))
        {
            IsFire = true;
        }

        IsFirstFire = false;
    }

    /// <summary>The fire state proper, <c>HumanPlayer_InitActionFire</c> (<c>igi2 0x4B5830</c>): it does nothing at all.</summary>
    private void EnterFire() => Action.Current = static (_, _) => { };

    /// <summary>
    /// Letting go: the fire type's trigger is to come up at the end of the tick and the weapon is ready
    /// (<c>HumanPlayer_InitActionFireRelease</c>, <c>igi2 0x4B5850</c>).
    /// </summary>
    private void EnterFireRelease()
    {
        if (Weapon.Firing is { } firing)
        {
            firing.FireTypeState(FireType).IsReleaseFire = true;
            firing.Inventory.Status = Igi2WeaponStatus.Ready;
        }

        Action.Current = (_, _) => RunFireRelease();
    }

    /// <summary>
    /// Once the first-person clip has played, idle; a fire key pressed again first fires again with the fire type it
    /// picks (<c>HumanPlayer_RunActionFireRelease</c>, <c>igi2 0x4B5880</c>).
    /// </summary>
    private void RunFireRelease()
    {
        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            GoAction(Igi2PlayerActionState.Idle);
        }
        else if (Channels[Igi2HumanChannel.Fire] != 0f || Channels[Igi2HumanChannel.AlternateFire] != 0f)
        {
            FireType = Channels[Igi2HumanChannel.AlternateFire] != 0f ? 1 : 0;
            GoAction(Igi2PlayerActionState.InitFire);
        }
    }

    /// <summary>
    /// Firing with nothing to fire: the stand mode on the upper body and the dry click on the view
    /// (<c>HumanPlayer_InitActionFireEmpty</c>, <c>igi2 0x4B5940</c>).
    /// </summary>
    private void EnterFireEmpty()
    {
        SetUpperBody(1);
        Surroundings.PlaySound(DryFireSound);
        Action.Current = (_, _) => RunFireEmpty();
    }

    /// <summary>Waits for the fire type's key to come up, then idle; a lock locks (<c>HumanPlayer_RunActionFireEmpty</c>, <c>igi2 0x4B5B40</c>).</summary>
    private void RunFireEmpty()
    {
        if (IsLockAction)
        {
            GoAction(Igi2PlayerActionState.Lock);
        }
        else if (Channels[FireTypeChannels[FireType]] == 0f)
        {
            GoAction(Igi2PlayerActionState.Idle);
        }
    }

    /// <summary>The click a weapon with nothing to fire makes, <c>guns_dry_1</c>.</summary>
    public const string DryFireSound = "guns_dry_1";
}

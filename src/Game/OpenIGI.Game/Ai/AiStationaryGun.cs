using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Ai;

#pragma warning disable CS1591 // The class-level contract documents the directly corresponding retail fields.

/// <summary>The live part of one retail <c>StationaryGun</c> task used by a HumanAI gunner.</summary>
/// <remarks>
/// IGI 1 stores the cone at task offsets <c>+480/+484/+488</c>, the owning Human at <c>+520</c>, and
/// the occupied byte at <c>+540</c>. The cone test in <c>0x48D340</c> transforms the target into the
/// gun's local frame, reverses it because the barrel faces local -Y, and builds signed alpha/gamma
/// angles from that vector.
/// </remarks>
public sealed class AiStationaryGun
{
    private object? _occupant;
    private int _roundSerial;

    public AiStationaryGun(
        int taskId,
        Vector3d position,
        Matrix3f orientation,
        float maximumAlphaDegrees = 25f,
        float minimumAlphaDegrees = -25f,
        float maximumGammaDegrees = 90f,
        string weaponId = "WEAPON_ID_M2HB",
        int ammunition = -1)
    {
        TaskId = taskId;
        Position = position;
        Orientation = orientation;
        MaximumAlphaDegrees = maximumAlphaDegrees;
        MinimumAlphaDegrees = minimumAlphaDegrees;
        MaximumGammaDegrees = maximumGammaDegrees;
        WeaponId = weaponId;
        Ammunition = ammunition;
        RemainingAmmunition = ammunition;
    }

    public int TaskId { get; }
    public Vector3d Position { get; }
    public Matrix3f Orientation { get; }
    public float MaximumAlphaDegrees { get; }
    public float MinimumAlphaDegrees { get; }
    public float MaximumGammaDegrees { get; }
    public string WeaponId { get; }
    public int Ammunition { get; }
    public int RemainingAmmunition { get; private set; }
    public int FireCooldownTicks { get; set; }
    public WeaponType? Weapon { get; private set; }
    public bool IsOccupied => _occupant is not null;

    /// <summary>
    /// Gets or sets whether another game's gun stands on this one's mount in its stead: this one is then neither drawn nor
    /// manned, and that game's player and soldiers work the other.
    /// </summary>
    public bool IsHandedOver { get; set; }

    /// <summary>Gets or sets the stand model's mount point, in its own frame (<c>0x474420</c>'s <c>0x414E20</c>).</summary>
    public Vector3f MountVertex { get; set; }

    /// <summary>Gets or sets the gun model's muzzle, magic vertex 2, in its own frame (<c>0x478BA0</c>).</summary>
    public Vector3f MuzzleVertex { get; set; }

    /// <summary>Gets the barrel's elevation the occupant has steered to, in radians (<c>+504</c>).</summary>
    public float AimPitch { get; private set; }

    /// <summary>Gets the barrel's traverse the occupant has steered to, in radians (<c>+508</c>).</summary>
    public float AimYaw { get; private set; }

    /// <summary>Gets the last round's kick in elevation, in radians (<c>+496</c>).</summary>
    public float KickPitch { get; private set; }

    /// <summary>Gets the last round's kick in traverse, in radians (<c>+500</c>).</summary>
    public float KickYaw { get; private set; }

    /// <summary>Gets the elevation the barrel is shown and fired at: the aim plus the kick (<c>+512</c>).</summary>
    public float DisplayPitch { get; private set; }

    /// <summary>Gets the traverse the barrel is shown and fired at: the aim plus the kick (<c>+516</c>).</summary>
    public float DisplayYaw { get; private set; }

    /// <summary>Gets whether the gun put a round out on its last tick, for the muzzle flame.</summary>
    public bool FiredThisTick { get; private set; }

    /// <summary>Gets a serial that advances once per round fired, for the muzzle flame's roll.</summary>
    public int ShotSequence { get; private set; }

    /// <summary>Gets the stand's orientation turned by the traverse: <c>0x474420</c> writes it to <c>+112</c>.</summary>
    /// <remarks>The authored orientation times <c>Z(traverse)</c> (<c>0x4B3BE0</c>), dotting rows into columns.</remarks>
    public Matrix3f TurnedOrientation => Orientation * Matrix3f.RotationZ(DisplayYaw);

    /// <summary>Gets the gun's orientation: the turned stand times <c>0x4B38E0(elevation, 0, π)</c>.</summary>
    /// <remarks>
    /// The π turns the gun model, whose barrel is authored on local +Y, onto the stand's local -Y. The
    /// barrel is the result's middle column, which is the direction <c>0x478BA0</c> fires along.
    /// </remarks>
    public Matrix3f GunOrientation =>
        TurnedOrientation * Matrix3f.FromEngineEulerAngles(DisplayPitch, 0f, MathF.PI);

    /// <summary>Gets where the gun sits: the stand's mount point, turned with it, in world units.</summary>
    public Vector3d GunPosition => Position + ToWorld(TurnedOrientation, MountVertex);

    /// <summary>Gets where rounds leave: the gun model's muzzle, in world units.</summary>
    public Vector3d MuzzlePosition => GunPosition + ToWorld(GunOrientation, MuzzleVertex);

    /// <summary>Gets the eye an occupant sees from, in world units.</summary>
    /// <remarks>
    /// <c>0x474420</c> hands the occupant's view this point with the gun's orientation (<c>0x4658D0</c>),
    /// which the camera then uses outright (<c>0x45FCD0</c>, mode 4): half a metre back along the barrel
    /// and a fifth of a metre up from where the gun sits.
    /// </remarks>
    public Vector3d SightPosition
    {
        get
        {
            Matrix3f gun = GunOrientation;
            Vector3d at = GunPosition;
            return new Vector3d(
                at.X + (gun.M13 * SightRiseUnits) - (gun.M12 * SightSetBackUnits),
                at.Y + (gun.M23 * SightRiseUnits) - (gun.M22 * SightSetBackUnits),
                at.Z + (gun.M33 * SightRiseUnits) - (gun.M32 * SightSetBackUnits));
        }
    }

    /// <summary>How far behind the gun an occupant's eye sits, in world units (<c>0x474420</c>'s 2048).</summary>
    public const float SightSetBackUnits = 2048f;

    /// <summary>How far above the gun an occupant's eye sits, in world units (<c>0x474420</c>'s 819.2).</summary>
    public const float SightRiseUnits = 819.2f;

    /// <summary>How far an empty gun's barrel sinks each tick, in radians: five degrees (<c>0x474190</c>).</summary>
    public const float DroopPerTick = 0.087266468f;

    /// <summary>How much of a kick is left after a tick (<c>0x474190</c>).</summary>
    public const float KickDecay = 0.89999998f;

    /// <summary>Gets or sets the stand model's magic vertex 0, in its own frame: where state 36 measures the seat from.</summary>
    /// <remarks><c>0x444B30</c> asks <c>0x414E20</c> for vertex 0, which is the origin for a stand with none.</remarks>
    public Vector3f SeatVertex { get; set; }

    /// <summary>
    /// The point at which state 36 places the gunner, 0.7 m behind the local -Y barrel -- in X and Y only: its Z is the
    /// gun's pivot, and the body keeps its own height (<c>0x45C8B0</c>).
    /// </summary>
    /// <remarks>
    /// <c>0x444B30</c> hands the entry the stand's position plus its magic vertex 0, and its frame as it stands now --
    /// <c>+112</c>, turned by the traverse -- and the entry steps 2867.2 along that frame's local Y. Probe, mission 8:
    /// soldier 403 sat straight behind gun 778 the first time and 45 degrees round the second, the gun left turned.
    /// </remarks>
    public Vector3d MountPosition =>
        Position + ToWorld(TurnedOrientation, SeatVertex)
        + TurnedOrientation.Transposed().Transform(new Vector3d(0.0, 2867.2, 0.0));

    /// <summary>The heading state 36 takes from the gun's turned frame (<c>0x4B3C50</c>) and turns by π.</summary>
    public float MountFacing => MathF.Atan2(TurnedOrientation.M21, TurnedOrientation.M11) + MathF.PI;

    /// <summary>Binds the task's authored <c>WEAPON_ID_*</c> after the weapon table is available.</summary>
    public void BindWeapon(WeaponType? weapon) => Weapon = weapon;

    public bool IsOccupiedBy(object owner) => ReferenceEquals(_occupant, owner);

    public bool TryMount(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (IsHandedOver || (_occupant is not null && !ReferenceEquals(_occupant, owner)))
        {
            return false;
        }

        _occupant = owner;
        return true;
    }

    public void Dismount(object owner)
    {
        if (ReferenceEquals(_occupant, owner))
        {
            _occupant = null;
        }
    }

    /// <summary>Returns the changing ammunition value the firing child observes after each round.</summary>
    public int AmmunitionSample => Ammunition < 0 ? int.MaxValue - _roundSerial : RemainingAmmunition;

    public bool ConsumeRound()
    {
        if (Ammunition >= 0)
        {
            if (RemainingAmmunition <= 0)
            {
                return false;
            }

            RemainingAmmunition--;
        }

        _roundSerial++;
        return true;
    }

    /// <summary>Turns the barrel by an occupant's look, then holds it inside the authored limits.</summary>
    /// <param name="pitch">The change in elevation, in radians; positive raises the barrel.</param>
    /// <param name="yaw">The change in traverse, in radians; positive swings it anticlockwise.</param>
    /// <remarks>
    /// <para>
    /// <c>0x474190</c> adds the occupant's two look deltas to <c>+504</c>/<c>+508</c> and clamps them
    /// in <c>0x4748B0</c>.
    /// </para>
    /// <para>
    /// The port steers per frame rather than per tick, so the barrel follows the mouse as smoothly as
    /// the walking view does. The shown angles move by exactly what the aim moved, which leaves the
    /// kick they carry where the tick put it.
    /// </para>
    /// </remarks>
    public void Steer(float pitch, float yaw)
    {
        float pitchBefore = AimPitch;
        float yawBefore = AimYaw;
        AimPitch += pitch;
        AimYaw += yaw;
        ClampAim();
        DisplayPitch += AimPitch - pitchBefore;
        DisplayYaw += AimYaw - yawBefore;
    }

    /// <summary>Turns the barrel onto a point, as far as the authored limits let it.</summary>
    /// <param name="target">The point, in world units.</param>
    /// <remarks>
    /// An AI gunner steers the gun with its own turn inputs, which <c>0x474190</c> adds to <c>+504</c>/<c>+508</c>
    /// exactly as it does a player's look; the gunner then sees and fires along the gun (<c>0x475310</c> hands it the
    /// gun's <c>+368</c>/<c>+392</c>). The port's gunner aims at its tracked point outright, so the gun takes the angles
    /// that point stands at from the stand: the cone's own alpha and gamma (<see cref="Contains"/>).
    /// </remarks>
    public void AimAt(Vector3d target)
    {
        Vector3d local = -Orientation.Transform(target - GunPosition);
        float yaw = (float)SignedAngleFromPositiveY(local.Y, local.X, negateWhenSecondNegative: false);
        float pitch = -(float)Math.Atan2(local.Z, Math.Sqrt((local.X * local.X) + (local.Y * local.Y)));
        Steer(pitch - AimPitch, yaw - AimYaw);
    }

    /// <summary>Runs the gun's own tick for an occupant: the kick shown, then decayed.</summary>
    /// <remarks>
    /// <c>0x474190</c> with an occupant: the barrel is shown at the aim plus the kick, and the kick
    /// decays. The round that follows (<see cref="RecordShot"/>) then replaces the kick outright.
    /// </remarks>
    public void TickOccupied()
    {
        ClampAim();
        DisplayPitch = AimPitch + KickPitch;
        DisplayYaw = AimYaw + KickYaw;
        KickPitch *= KickDecay;
        KickYaw *= KickDecay;
        FiredThisTick = false;
    }

    /// <summary>Runs the gun's own tick with nobody on it: the aim sinks to its lowest elevation.</summary>
    /// <remarks>
    /// <c>0x474190</c> with no occupant steps <c>+504</c> five degrees a tick toward <c>+484</c> and
    /// touches nothing else. The shown angles (<c>+512</c>/<c>+516</c>) are left where the last
    /// occupant put them, so an abandoned gun stays pointing where it was left, and the next one to
    /// take it starts from the sunken aim.
    /// </remarks>
    public void TickEmpty()
    {
        float rest = MinimumAlphaDegrees * DegreesToRadians;
        AimPitch = AimPitch < rest
            ? MathF.Min(AimPitch + DroopPerTick, rest)
            : MathF.Max(AimPitch - DroopPerTick, rest);
        FiredThisTick = false;
    }

    /// <summary>Takes one tick's firing result: the kick it left, and whether a round went.</summary>
    /// <param name="fired">Whether a round went off.</param>
    /// <param name="kickPitch">The round's kick in elevation, in radians.</param>
    /// <param name="kickYaw">The round's kick in traverse, in radians.</param>
    /// <remarks>
    /// <c>0x474360</c> writes the gun's answer straight into <c>+496</c>/<c>+500</c>: a tick without a
    /// round leaves no kick at all.
    /// </remarks>
    public void RecordShot(bool fired, float kickPitch, float kickYaw)
    {
        KickPitch = fired ? kickPitch : 0f;
        KickYaw = fired ? kickYaw : 0f;
        FiredThisTick = fired;
        if (fired)
        {
            unchecked
            {
                ShotSequence++;
            }
        }
    }

    // 0x4748B0: elevation between +484 and +480; traverse within +-488 unless that is a full turn.
    private void ClampAim()
    {
        AimPitch = Math.Clamp(
            AimPitch,
            MinimumAlphaDegrees * DegreesToRadians,
            MaximumAlphaDegrees * DegreesToRadians);
        float sideways = MaximumGammaDegrees * DegreesToRadians;
        if (sideways < 2f * MathF.PI)
        {
            AimYaw = Math.Clamp(AimYaw, -sideways, sideways);
        }
    }

    // A point in a frame, taken into the world: M*v, the column-vector product.
    private static Vector3d ToWorld(Matrix3f orientation, Vector3f local)
    {
        Vector3f offset = orientation.Transposed().Transform(local);
        return new Vector3d(offset.X, offset.Y, offset.Z);
    }

    private const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>Reproduces the strict angle comparisons in <c>0x48D5C1</c>.</summary>
    public bool Contains(Vector3d target)
    {
        Vector3d local = -Orientation.Transform(target - Position);
        double alpha = SignedAngleFromPositiveY(local.Y, local.Z, negateWhenSecondNegative: true);
        double gamma = SignedAngleFromPositiveY(local.Y, local.X, negateWhenSecondNegative: false);
        const double degreesToRadians = Math.PI / 180.0;
        return Math.Abs(gamma) < MaximumGammaDegrees * degreesToRadians
            && MinimumAlphaDegrees * degreesToRadians < alpha
            && MaximumAlphaDegrees * degreesToRadians > alpha;
    }

    private static double SignedAngleFromPositiveY(
        double y,
        double second,
        bool negateWhenSecondNegative)
    {
        double length = Math.Sqrt((y * y) + (second * second));
        if (length == 0.0)
        {
            return 0.0;
        }

        double angle = Math.Acos(Math.Clamp(y / length, -1.0, 1.0));
        bool negate = negateWhenSecondNegative ? second < 0.0 : second > 0.0;
        return negate ? -angle : angle;
    }
}

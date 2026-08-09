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

    /// <summary>The point at which state 36 places the gunner, 0.7 m behind the local -Y barrel.</summary>
    public Vector3d MountPosition =>
        Position + Orientation.Transposed().Transform(new Vector3d(0.0, 2867.2, 0.0));

    /// <summary>The heading state 36 takes from the gun orientation and turns by π.</summary>
    public float MountFacing => MathF.Atan2(Orientation.M21, Orientation.M11) + MathF.PI;

    /// <summary>Binds the task's authored <c>WEAPON_ID_*</c> after the weapon table is available.</summary>
    public void BindWeapon(WeaponType? weapon) => Weapon = weapon;

    public bool IsOccupiedBy(object owner) => ReferenceEquals(_occupant, owner);

    public bool TryMount(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_occupant is not null && !ReferenceEquals(_occupant, owner))
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

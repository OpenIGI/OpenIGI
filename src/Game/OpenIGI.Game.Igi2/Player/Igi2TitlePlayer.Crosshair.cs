using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The player as the remastered crosshair reads him. <b>Not retail</b>: the original's sight is drawn from the same precision.</content>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>How far ahead the crosshair looks for an enemy, in world units: three hundred metres.</summary>
    public const double AimReachUnits = 300.0 * 4096.0;

    private const float DegreesToRadians = MathF.PI / 180f;

    private readonly WorldLineTrace _aimTrace = new(1);
    private long _shotsFired;
    private int _bulletsSeen = -1;

    /// <summary>The crosshair a weapon is aimed with, from the sight its type declares (<c>SIGHTDISPLAYTYPE_*</c>).</summary>
    /// <param name="weapon">The weapon's type.</param>
    /// <returns>Its crosshair.</returns>
    public static CrosshairShape ShapeOf(Igi2WeaponTypeTask weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (weapon.WeaponType == (int)Igi2WeaponTypeKind.Knife)
        {
            return CrosshairShape.Melee;
        }

        return weapon.SightDisplayType switch
        {
            1 => CrosshairShape.Pistol,
            2 => CrosshairShape.Shotgun,
            3 => CrosshairShape.Smg,
            4 => CrosshairShape.Rifle,
            5 => CrosshairShape.MachineGun,
            6 => CrosshairShape.Launcher,
            7 => CrosshairShape.Grenade,
            8 => CrosshairShape.Sniper,
            _ => CrosshairShape.None,
        };
    }

    /// <summary>What the crosshair shows of the player this frame.</summary>
    /// <returns>The feedback.</returns>
    /// <remarks>
    /// The spread is the precision the shot is thrown off by (<c>vActualWeaponPrecision + vActualModifiedPrecision</c>, in
    /// degrees, a round straying up to that far): it opens as the weapon kicks and the posture steadies it back. A round is
    /// counted as the weapon's bullet count rises, which starts again at nought when a weapon is put in hand.
    /// </remarks>
    public PlayerAimFeedback AimFeedback()
    {
        if (Human.Weapon.Inventory is not { Current: { } held } inventory)
        {
            _bulletsSeen = -1;
            return PlayerAimFeedback.Of(CrosshairShape.None, 0f, false, _shotsFired, Human.Shots);
        }

        int bullets = inventory.TotalBulletsFired;
        if (_bulletsSeen >= 0 && bullets > _bulletsSeen)
        {
            _shotsFired += bullets - _bulletsSeen;
        }

        _bulletsSeen = bullets;
        CrosshairShape shape = ShapeOf(held.WeaponType);
        float spread = shape is CrosshairShape.None or CrosshairShape.Melee or CrosshairShape.Grenade
            ? 0f
            : (Human.ActualWeaponPrecision + Human.ActualModifiedPrecision) * DegreesToRadians;
        return PlayerAimFeedback.Of(shape, spread, shape != CrosshairShape.None && EnemyUnderAim(), _shotsFired, Human.Shots);
    }

    /// <summary>
    /// Whether a living enemy is under the crosshair: the first thing a line along the view meets -- a wall, the ground, a
    /// body -- is a soldier of another side, of either game.
    /// </summary>
    /// <returns>Whether an enemy is there.</returns>
    public bool EnemyUnderAim()
    {
        Vector3d eye = Human.View.Position;
        Matrix3f view = Human.View.Orientation;
        Vector3f forward = new(view.M12, view.M22, view.M32);
        _aimTrace.Starts[0] = eye;
        _aimTrace.Ends[0] = new Vector3d(eye.X + (forward.X * AimReachUnits), eye.Y + (forward.Y * AimReachUnits), eye.Z + (forward.Z * AimReachUnits));
        _aimTrace.Includes = owner => !ReferenceEquals(owner, Human);
        WorldIntersection hit = _aimTrace.Intersections[0];
        hit.IsHit = false;
        hit.HitObject = null;
        CollisionWorld.TraceLines(_aimTrace, Igi2CollisionWorld.EveryTypeGroup | Igi2CollisionWorld.HumanGroup | Igi2CollisionWorld.TerrainGroup);
        if (!hit.IsHit)
        {
            return false;
        }

        object? struck = hit.HitObject?.Handler ?? hit.HitObject?.Owner;
        return struck switch
        {
            Igi2HumanSoldier { IsDead: false } soldier => soldier.Team != Human.Team,
            HumanTask { IsDead: false } other => other.Team != Human.Team,
            _ => false,
        };
    }
}

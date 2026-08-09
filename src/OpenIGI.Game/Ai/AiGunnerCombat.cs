using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

#pragma warning disable CS1591 // The class-level contract documents the compact command surface.

/// <summary>The mount/dismount arbitration around IGI 1's ordinary guard firing child.</summary>
/// <remarks><c>0x48D2C0</c> initializes the 30-tick cone counter; <c>0x48D340</c> runs it.</remarks>
public sealed class AiGunnerCombat
{
    public const int ConeGraceTicks = 30;

    private readonly object _owner;
    private AiStationaryGun? _mounted;
    private int _coneTicks = ConeGraceTicks;

    public AiGunnerCombat(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public AiStationaryGun? MountedGun => _mounted;
    public bool IsMounted => _mounted is not null;
    public int ConeTicks => _coneTicks;

    public void Begin()
    {
        End();
        _coneTicks = ConeGraceTicks;
    }

    public AiGunnerCommand Update(AiStationaryGun? gun, Vector3d target, bool hasDetectionSlot)
    {
        if (!hasDetectionSlot)
        {
            bool wasMounted = IsMounted;
            End();
            return new AiGunnerCommand(
                MountChannelValue: wasMounted ? 1f : null,
                DismountedThisTick: wasMounted,
                Ended: true);
        }

        if (_mounted is not null && !ReferenceEquals(_mounted, gun))
        {
            _mounted.Dismount(_owner);
            _mounted = null;
        }

        bool usable = gun is not null && (!gun.IsOccupied || gun.IsOccupiedBy(_owner));
        bool inside = usable && gun!.Contains(target);

        if (_mounted is null)
        {
            if (!inside)
            {
                _coneTicks = ConeGraceTicks;
                return new AiGunnerCommand(RunCarriedWeapon: true);
            }

            if (_coneTicks != 0)
            {
                _coneTicks--;
                return new AiGunnerCommand(RunCarriedWeapon: true);
            }

            if (!gun!.TryMount(_owner))
            {
                _coneTicks = ConeGraceTicks;
                return new AiGunnerCommand(RunCarriedWeapon: true);
            }

            _mounted = gun;
            return new AiGunnerCommand(MountChannelValue: gun.TaskId, MountedThisTick: true);
        }

        if (inside)
        {
            _coneTicks = ConeGraceTicks;
            return new AiGunnerCommand(RunMountedWeapon: true);
        }

        if (_coneTicks != 0)
        {
            _coneTicks--;
            return new AiGunnerCommand(RunMountedWeapon: true);
        }

        AiStationaryGun previous = _mounted;
        previous.Dismount(_owner);
        _mounted = null;
        return new AiGunnerCommand(MountChannelValue: 1f, DismountedThisTick: true);
    }

    public void End()
    {
        _mounted?.Dismount(_owner);
        _mounted = null;
    }
}

public readonly record struct AiGunnerCommand(
    bool RunCarriedWeapon = false,
    bool RunMountedWeapon = false,
    float? MountChannelValue = null,
    bool MountedThisTick = false,
    bool DismountedThisTick = false,
    bool Ended = false);

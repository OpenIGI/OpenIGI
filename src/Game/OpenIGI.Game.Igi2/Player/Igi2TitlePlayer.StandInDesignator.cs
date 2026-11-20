using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The laser designator, where another game's player stands for this one.</content>
/// <remarks>
/// The level hands its player a designator and its scripts wait for it to rest on a target
/// (<c>LaserDesignator_GetTicksHeldOnTask</c>). A player of a game that has no designator sights the target through its own
/// binoculars instead: while the level's player carries the designator and the player standing in has its binoculars raised,
/// what it looks at is designated, by the designator's own line (<see cref="Igi2LaserDesignatorFire.Designate"/>) and range.
/// </remarks>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>The designator's weapon id.</summary>
    public const string LaserDesignatorId = "WEAPON_ID_LASERDESIGNATOR";

    private readonly WorldLineTrace _standInDesignatorTrace = new(1);
    private bool _standInDesignates;

    // Rests the designator on what the player standing in sights, or lets it go as the binoculars come down.
    private void DesignateForStandIn(ITitlePlayer standIn)
    {
        if (Weapons?.Firing is not { LaserDesignator: { } state, World: { } world }
            || Inventory?.FindWeapon(LaserDesignatorId) is not { IsActive: true } carried)
        {
            return;
        }

        if (standIn is not ITitleBinoculars { IsBinocularsRaised: true }
            || carried.WeaponType.Descendants().OfType<Igi2LaserDesignatorTask>().FirstOrDefault() is not { } designator)
        {
            if (_standInDesignates)
            {
                _standInDesignates = false;
                state.LoseLock();
            }

            return;
        }

        // The view's rows are its right, up and forward axes.
        Vector3f forward = standIn.ViewOrientation.Row3;
        Vector3d from = standIn.EyePosition;
        double range = designator.Range * 4096.0;
        _standInDesignates = true;
        Igi2LaserDesignatorFire.Designate(
            state,
            world,
            _standInDesignatorTrace,
            from,
            new Vector3d(from.X + (forward.X * range), from.Y + (forward.Y * range), from.Z + (forward.Z * range)),
            StoodFor);
    }
}

using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>A mounted gun manned while another game's player stands for this one.</content>
/// <remarks>
/// A mounted gun is the level's: it turns, fires and rides whatever carries it by I.G.I. 2's rule, worked by a body of I.G.I. 2's
/// (<see cref="IIgi2GunOperator"/>), and the level's scripts seat its player on one by that body (<c>HumanPlayer_Activate</c>).
/// So while a gun is manned, this player's own body mans it -- its look, its trigger, its seat and its view -- and the player
/// standing in is carried at the seat, where the level's soldiers find it and its own game takes their hits. Off the gun it is
/// put down where the body stood and plays on.
/// </remarks>
public sealed partial class Igi2TitlePlayer
{
    // Whether the player standing in was carried on a gun last tick, and so is to be put down as the gun is left.
    private bool _carriedOnGun;

    /// <summary>
    /// Gets a value indicating whether this player's own body mans a mounted gun in the stead of the player standing in for it.
    /// </summary>
    public bool MansGunForStandIn => StoodFor is not null && Human.ActivatingTarget is Igi2StationaryGunTask;

    /// <summary>Seats the player standing in on one of the level's mounted guns, as its use key asks: this player's own body mans it.</summary>
    /// <param name="gun">The gun.</param>
    /// <returns><see langword="true"/> when the gun is now manned.</returns>
    public bool ManGunForStandIn(Igi2StationaryGunTask gun)
    {
        ArgumentNullException.ThrowIfNull(gun);
        if (StandInPlayer is not { } standIn || StoodFor is null || MansGunForStandIn || StandIn is { IsDead: true })
        {
            return false;
        }

        // The body goes to the gun from where the player stands, not from where the level first put it.
        Human.PopPositionDroppingGround(standIn.Position);
        Human.ScriptActivate(gun);
        return MansGunForStandIn;
    }

    // The player standing in, while it is the one played: not while this player's own body mans a gun for it.
    private ITitlePlayer? ActingStandIn => MansGunForStandIn ? null : StandInPlayer;

    // Carries the player standing in at the gun's seat while this body mans a gun, and puts it down as the gun is left. Answers
    // whether this body runs the tick.
    private bool CarryStandInOnGun()
    {
        IScriptedHuman? carried = StoodFor?.StandIn as IScriptedHuman;
        if (MansGunForStandIn)
        {
            _carriedOnGun = true;
            carried?.HoldAt(Human.Position);
            return true;
        }

        if (_carriedOnGun)
        {
            _carriedOnGun = false;
            carried?.PopPosition(Human.Position);
        }

        return false;
    }
}

using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>The fuse of a grenade in Project I.G.I.'s player's hand under a manual cook (<see cref="GrenadeCook.Manual"/>).</summary>
/// <remarks>
/// <para>
/// Not retail: the original never burns a fuse in the hand, and lights the whole of <see cref="GrenadePhysics.FuseTicks"/> as
/// the grenade leaves it. Under a manual cook the cook key lights it once the throw is past its pin, and it burns a tick at a
/// time for as long as the grenade is in hand; what is left is the fuse the grenade flies with, and at nothing it goes off
/// where it is. Under the original's cook nothing is ever lit.
/// </para>
/// </remarks>
internal sealed class GrenadeHandFuse
{
    private GrenadeCookLatch _latch;

    /// <summary>Gets the fuse left, in ticks, or -1 while it is unlit.</summary>
    internal int TicksLeft { get; private set; } = -1;

    /// <summary>Gets whether the fuse burnt down with the grenade still in hand.</summary>
    internal bool ExplodedInHand { get; private set; }

    /// <summary>Gets whether the fuse is alight and the grenade still in hand.</summary>
    internal bool Burning => TicksLeft > 0;

    /// <summary>Puts a fresh grenade in hand: no press noted, nothing lit.</summary>
    internal void Reset()
    {
        _latch.Reset();
        TicksLeft = -1;
        ExplodedInHand = false;
    }

    /// <summary>One tick of the grenade in hand.</summary>
    /// <param name="mode">When the player's grenades cook.</param>
    /// <param name="cookKeyDown">Whether the cook key is down this tick.</param>
    /// <param name="pinReached">Whether the throw has reached its pin.</param>
    /// <returns><see langword="true"/> on the tick the fuse burns down and the grenade goes off in hand.</returns>
    internal bool Burn(GrenadeCook mode, bool cookKeyDown, bool pinReached)
    {
        if (mode != GrenadeCook.Manual)
        {
            return false;
        }

        _latch.Note(cookKeyDown);
        if (TicksLeft < 0 && _latch.PinDue(mode, pinReached))
        {
            TicksLeft = GrenadePhysics.FuseTicks;
        }

        if (TicksLeft <= 0 || --TicksLeft > 0)
        {
            return false;
        }

        ExplodedInHand = true;
        return true;
    }
}

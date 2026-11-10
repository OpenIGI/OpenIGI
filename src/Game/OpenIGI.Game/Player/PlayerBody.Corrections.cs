using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>The body moved from outside its own tick: a co-op client making up what the server did to it.</summary>
public sealed partial class PlayerBody
{
    // A tick's share of the knock: 0x4614B0 damps the accumulator (+1648 *= 0.9) before the move adds it, so a round's knock is
    // felt at nine tenths from the very first tick. The one place the order lives, for the body's own tick and a late knock's.
    private static Vector3f FadeHitShove(Vector3f shove) => HumanSlopeSlide.Damp(shove);

    /// <summary>Carries the body through the ticks of a knock it took some ticks ago, and keeps what of the knock is left.</summary>
    /// <param name="push">The knock as it was added (<see cref="AddHitShove"/>).</param>
    /// <param name="ticks">How many ticks it has carried the body since.</param>
    /// <param name="move">
    /// How each tick's share moves the body, returning how far it went; <see langword="null"/> for the walls' sweep, as the
    /// body's own move takes it.
    /// </param>
    /// <returns>Where the knock had carried the body after each of those ticks, from where it stood, oldest first.</returns>
    /// <remarks>
    /// A co-op client learns of a knock the server gave its player some ticks late and makes each of those ticks' share up at
    /// once. Each share goes through the walls as the body's own move would take it (0x461A70): a wall slides it along, and
    /// the knock is kept -- nothing but a landing clears it (0x40BE40). Taken in one move without the walls, a few rounds'
    /// knock at a high ping carried the body through a tunnel wall into the void outside. The ground is left to the next
    /// tick's move, whose step-down finds the floor under the new place as walking onto it would.
    /// </remarks>
    public List<Vector3d> CatchUpHitShove(Vector3f push, int ticks, Func<Vector3d, Vector3d>? move = null)
    {
        move ??= SlideBy;
        Vector3f left = push;
        Vector3d moved = default;
        List<Vector3d> each = new(Math.Max(ticks, 0));
        for (int i = 0; i < ticks; i++)
        {
            left = FadeHitShove(left);
            moved += move(new Vector3d(left.X, left.Y, left.Z));
            each.Add(moved);
        }

        AddHitShove(left.X, left.Y);
        return each;
    }

    // Moves the body by a share of a knock, through the walls as its own move is; returns how far it actually went.
    private Vector3d SlideBy(Vector3d delta)
    {
        Vector3d start = Position;
        Vector3d next = start + delta;
        if (Walls is not null || TerrainWalls is not null)
        {
            next = HumanWallSweep.Sweep(start, next, IsGrounded, IsCrouched, _sweepCast ??= SweepCast).Destination;
        }

        Position = next;
        return next - start;
    }

    /// <summary>Stops a fall the server's body never made: what it carried is dropped, and the next move stands it on the floor.</summary>
    /// <remarks>
    /// A co-op correction moves the body to where the server has it, standing. Moved there still falling, the fall's speed
    /// took it through the floor again before the ground could catch it, and the server's next word came a round trip
    /// later: the player fell and was put back without end. The ground bit is the server's word too, so the next tick's
    /// step-down settles the feet where the correction put them, and no landing (nor its damage) is made of a fall that
    /// was never the game's.
    /// </remarks>
    public void StopStrayFall()
    {
        _pendingLandingVelocityZ = null;
        _pendingWalkOffVelocity = null;
        Velocity = default;
        InheritedVelocity = default;
        IsGrounded = true;
    }

    /// <summary>
    /// Brings the body to rest, standing: its fall stopped (<see cref="StopStrayFall"/>), and the knock of the rounds that hit
    /// it and the slide of the slope it stood on gone, as the events that zero all six velocity words leave it
    /// (<c>0x40BE40</c>, <c>0x4113E0</c>). For a body a save game puts somewhere else: what moved it where it was does not
    /// move it there.
    /// </summary>
    public void ComeToRest()
    {
        StopStrayFall();
        HitShove = default;
        SlopeSlide = default;
        _previousStep = default;
    }
}

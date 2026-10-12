using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>Keeping out of other humans, <c>Human_HumanCollision</c>.</summary>
public abstract partial class Igi2Human
{
    /// <summary>How near in height another human must be to block a move: 1.8 metres.</summary>
    private const double HumanBlockHeight = 7372.8;

    /// <summary>How near across another human must be to block a move: 0.75 metres.</summary>
    private const double HumanBlockRadius = 3072.0;

    /// <summary>
    /// Refuses a move's horizontal part that brings the body closer to a living human it overlaps, then takes the move's
    /// speed from what is left of it, <c>Human_HumanCollision</c> (<c>human.c</c>).
    /// </summary>
    /// <param name="start">Where the move started.</param>
    /// <param name="to">Where it ends; its X and Y go back to the start when a human blocks it.</param>
    /// <param name="speed">Receives the move as made: end minus start, after every collision has had its say.</param>
    /// <remarks>
    /// Every human registered in the world (<c>Human_ptQTaskList</c>) is looked at, the dead and the mover itself excepted. One
    /// within 1.8 metres in height and 0.75 metres across that the move brings nearer than it started blocks it; each one that
    /// does is told (<see cref="OnBlockedByHuman"/>). The speed is taken whether or not anything blocked, so the ground the
    /// body was set onto shows in it.
    /// </remarks>
    public void HumanCollision(Vector3d start, ref Vector3d to, out Vector3f speed)
    {
        foreach (Igi2Human other in Surroundings.Humans)
        {
            if (other.IsDead || ReferenceEquals(other, this))
            {
                continue;
            }

            Fpu height = Fpu.Abs((Fpu)to.Z - other.Position.Z);
            if (!(height.Value < HumanBlockHeight))
            {
                continue;
            }

            Fpu dy = (Fpu)to.Y - other.Position.Y, dx = (Fpu)to.X - other.Position.X;
            Fpu across = Fpu.Sqrt((dy * dy) + (dx * dx));
            if (!(across.Value < HumanBlockRadius))
            {
                continue;
            }

            Fpu wasY = (Fpu)start.Y - other.Position.Y, wasX = (Fpu)start.X - other.Position.X;
            if (across.Value < Fpu.Sqrt((wasY * wasY) + (wasX * wasX)).Value)
            {
                to = new Vector3d(start.X, start.Y, to.Z);
                OnBlockedByHuman();
            }
        }

        speed = new Vector3f(((Fpu)to.X - start.X).F, ((Fpu)to.Y - start.Y).F, ((Fpu)to.Z - start.Z).F);
    }

    /// <summary>What a body does when another human blocks its move: nothing, but the player's makes a noise.</summary>
    protected virtual void OnBlockedByHuman()
    {
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>How a mind aims its soldier's grenade, <c>HumanAI_GetGrenadeOrientationAndSpeedToTarget</c> (<c>igi2 0x4632A0</c>).</content>
public sealed partial class Igi2HumanAi
{
    /// <summary>The angles a grenade is tried at, steepest first: sixty, forty-five and thirty degrees, in radians.</summary>
    public static readonly float[] GrenadeThrowAngles = [1.0471976f, 0.7853982f, 0.5235988f];

    /// <summary>The furthest a grenade is thrown, fifty metres across the ground.</summary>
    public const float GrenadeReachMetres = 50f;

    /// <summary>The speed, in units a tick, of a metre a second: 136.</summary>
    public const double GrenadeSpeedScale = 136.0;

    /// <summary>What gravity takes off a grenade's rise each tick, <c>TraceTrajectory</c>: 9.8 m/s² in units a tick a tick.</summary>
    public const float GrenadeGravityPerTick = 44.600887f;

    /// <summary>How near a flight comes to the target, on every axis, to have reached it: a metre and a half.</summary>
    public const float GrenadeArrivalReach = 6144f;

    /// <summary>The ticks a flight is followed before it is given up, past any throw fifty metres can need.</summary>
    private const int GrenadeFlightLimit = 900;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Each angle in turn: from the hand to the grenade target, under fifty metres across, the speed that lands a throw at that
    /// angle on the target's height under gravity (<c>v² = g·d² / 2(h - d·tan a)</c>, none where the target is out of reach that
    /// way), its rise the angle's share and its run straight at the target across the ground. The first throw whose flight meets
    /// nothing on its way (<see cref="TryFlyGrenade"/>) is the throw; the orientation is level.
    /// </para>
    /// </remarks>
    public bool TryAimGrenade(Igi2HumanSoldier soldier, Vector3d hand, out Vector3f speed, out float[] orientation, out int flyTime)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        orientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        speed = default;
        flyTime = 0;
        if (GrenadeTargetPosition is not { } target)
        {
            return false;
        }

        foreach (float angle in GrenadeThrowAngles)
        {
            float rise = (float)Math.Tan(angle);
            float acrossX = (float)((target.X - hand.X) * 0.000244140625);
            float acrossY = (float)((target.Y - hand.Y) * 0.000244140625);
            float across = (float)Math.Sqrt((acrossX * (double)acrossX) + (acrossY * (double)acrossY));
            if (across <= 0f || across >= GrenadeReachMetres)
            {
                continue;
            }

            double height = (target.Z * 0.000244140625) - (hand.Z * 0.000244140625) - (across * (double)rise);
            double squared = across * (double)across * -9.800000190734863 / (height + height);
            if (squared < 0.0)
            {
                continue;
            }

            float launch = (float)(Math.Sqrt(squared) * GrenadeSpeedScale);
            if (acrossX != 0f || acrossY != 0f)
            {
                double inverse = 1.0 / Math.Sqrt((acrossX * (double)acrossX) + (acrossY * (double)acrossY));
                acrossX = (float)(acrossX * inverse);
                acrossY = (float)(acrossY * inverse);
            }

            speed = new Vector3f(launch * acrossX, launch * acrossY, launch * rise);
            if (TryFlyGrenade(soldier.Surroundings.Collision, hand, speed, target, out flyTime))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Follows a throw a tick at a time, <c>TraceTrajectory</c>: gravity takes its rise, it moves its speed, and the step is
    /// traced against what a grenade meets. It reaches the target within <see cref="GrenadeArrivalReach"/> on every axis, or
    /// once it has gone past it; a step that meets something first is a throw that does not get there.
    /// </summary>
    /// <param name="world">What the flight meets.</param>
    /// <param name="from">Where it leaves the hand.</param>
    /// <param name="speed">Its speed, units a tick.</param>
    /// <param name="target">Where it is thrown at.</param>
    /// <param name="flyTime">Receives the ticks it flies clear.</param>
    /// <returns>Whether it gets there.</returns>
    public static bool TryFlyGrenade(IIgi2CollisionQuery world, Vector3d from, Vector3f speed, Vector3d target, out int flyTime)
    {
        ArgumentNullException.ThrowIfNull(world);
        flyTime = 0;
        float rise = speed.Z;
        float wayX = (float)(target.X - from.X), wayY = (float)(target.Y - from.Y), wayZ = (float)(target.Z - from.Z);
        Vector3d at = from;
        WorldLineTrace step = new(1);
        for (int tick = 0; tick < GrenadeFlightLimit; tick++)
        {
            rise -= GrenadeGravityPerTick;
            Vector3d next = new(speed.X + at.X, speed.Y + at.Y, rise + at.Z);
            float leftX = (float)(target.X - next.X), leftY = (float)(target.Y - next.Y);
            double leftZ = target.Z - next.Z;
            double ahead = (leftZ * wayZ) + (leftY * (double)wayY) + (leftX * (double)wayX);
            if (Math.Abs(leftX) < GrenadeArrivalReach && Math.Abs(leftY) < GrenadeArrivalReach && Math.Abs(leftZ) < GrenadeArrivalReach)
            {
                return true;
            }

            if (ahead < 0.0)
            {
                return true;
            }

            step.Starts[0] = at;
            step.Ends[0] = next;
            step.Count = 1;
            world.TraceLines(step, Igi2AiGraph.CoverGroups);
            if (step.Intersections[0].IsHit)
            {
                return false;
            }

            flyTime++;
            at = next;
        }

        return false;
    }
}

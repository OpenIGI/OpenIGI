using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Presentation.Igi1;

/// <summary>A round through a corpse: the body bleeds as a living one does, and the round goes on.</summary>
/// <remarks>
/// <para>
/// <b>Presentation, like the ragdoll it hits.</b> The original never offers a round to the dead — the shot
/// path asks only the living — so a corpse cannot stop a round, take damage, or change what a round behind
/// it meets, and none of that changes here. What a player sees is the one thing added: the spray, the
/// drops that fall and mark the floor, and the splash on whatever is behind, from the same blood the living
/// throw.
/// </para>
/// <para>
/// A simulated corpse is met where it is drawn, on its ragdoll's capsules; one drawn by its death clip (with
/// <c>g_ragdoll</c> off) on its skinned collision mesh in that clip's pose, the test a living soldier gets.
/// </para>
/// </remarks>
public sealed partial class SoldierRenderer
{
    /// <inheritdoc/>
    public bool TryCorpseCentre(SoldierView view, out Vector3d centre)
    {
        centre = default;
        if (FindMember(view)?.Ragdoll is not { } corpse)
        {
            return false;
        }

        centre = corpse.Centre;
        return true;
    }

    /// <summary>Bleeds the nearest corpse a round passes through before it stops.</summary>
    /// <param name="origin">Where the round started, in world units.</param>
    /// <param name="rayEnd">Where it was cast to.</param>
    /// <param name="stop">How far along it went before something stopped it — a surface or a living body.</param>
    private void BleedCorpses(Vector3d origin, Vector3d rayEnd, double stop)
    {
        if (_sim.BloodSpray is not { } bleed)
        {
            return;
        }

        Vector3d line = rayEnd - origin;
        double length = line.Length;
        if (length <= 0.0)
        {
            return;
        }

        Vector3d along = line / length;
        double reach = Math.Min(stop, length);
        Vector3d end = origin + (along * reach);

        double nearest = reach;
        Vector3d wound = default;
        SoldierView? bleeding = null;
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                SoldierView view = member.Agent.View;
                if (!_sim.HealthOf(view).IsDead)
                {
                    continue;
                }

                if (member.Ragdoll is { } corpse)
                {
                    for (int body = 0; body < corpse.BodyCount; body++)
                    {
                        corpse.CapsuleOf(body, out Vector3d from, out Vector3d to, out double radius);
                        if (EntersCapsule(origin, along, nearest, from, to, radius, out double at))
                        {
                            nearest = at;
                            wound = origin + (along * at);
                            bleeding = view;
                        }
                    }
                }
                else if (group.Body.CollisionMesh is { } mesh)
                {
                    double ground = _sim.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                    Vector3d feet = new(view.Position.X, view.Position.Y, ground);
                    Matrix3f m = view.Orientation;
                    if (HumanHitTest.TryCastMesh(
                            origin, end, feet, new Vector3f(m.M12, m.M22, m.M32), mesh, view.Pose(), view.RootSample.Z, out HumanHit hit)
                        && hit.Distance < nearest)
                    {
                        nearest = hit.Distance;
                        wound = hit.Position;
                        bleeding = view;
                    }
                }
            }
        }

        if (bleeding is not null)
        {
            bleed(bleeding, wound, new Vector3f((float)along.X, (float)along.Y, (float)along.Z));
        }
    }

    // Where a ray first enters a capsule, if it does short of a distance. The nearest approach of the ray
    // to the capsule's axis, stepped back by however far inside the radius that approach is: exact for a
    // ray square to the axis, and close enough for where blood starts on anything else.
    private static bool EntersCapsule(
        Vector3d origin, Vector3d along, double limit, Vector3d from, Vector3d to, double radius, out double at)
    {
        at = 0.0;
        Vector3d end = origin + (along * limit);
        Vector3d onRay = Vector3d.ClosestOnSegment(origin, end, (from + to) * 0.5);
        Vector3d onAxis = Vector3d.ClosestOnSegment(from, to, onRay);
        for (int round = 0; round < 3; round++)
        {
            onRay = Vector3d.ClosestOnSegment(origin, end, onAxis);
            onAxis = Vector3d.ClosestOnSegment(from, to, onRay);
        }

        double apart = Vector3d.Distance(onRay, onAxis);
        if (apart >= radius)
        {
            return false;
        }

        at = Math.Max(0.0, Vector3d.Dot(onRay - origin, along) - Math.Sqrt((radius * radius) - (apart * apart)));
        return at < limit;
    }
}

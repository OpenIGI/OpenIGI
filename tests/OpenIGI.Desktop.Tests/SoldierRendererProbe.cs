using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// What the soldier renderer tests read and rearrange of a loaded renderer: its members and collision meshes,
/// where a soldier stands, and a task's ancestry for a failure message.
/// </summary>
internal static class SoldierRendererProbe
{
    private const double PresentationOffsetMetres = 8.0;

    internal static MeshBackedSoldier FirstMeshBackedSoldier(SoldierRenderer renderer)
    {
        foreach (SoldierGroup group in renderer.GroupsForTesting)
        {
            if (group.Body.CollisionMesh is not MefCollisionMesh mesh)
            {
                continue;
            }

            foreach (SoldierMember member in group.Soldiers)
            {
                SoldierView view = member.Agent.View;
                return new MeshBackedSoldier(view, mesh, member);
            }
        }

        throw new Xunit.Sdk.XunitException("Mission four did not provide a loaded soldier collision mesh.");
    }

    internal static void KeepOnlyTarget(SoldierRenderer renderer, SoldierMember target)
    {
        foreach (SoldierGroup group in renderer.GroupsForTesting)
        {
            List<SoldierMember> members = group.Soldiers;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(members[i], target))
                {
                    members.RemoveAt(i);
                }
            }
        }
    }

    internal static SoldierMember MemberFor(SoldierRenderer renderer, SoldierView target)
    {
        foreach (SoldierGroup group in renderer.GroupsForTesting)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (ReferenceEquals(member.Agent.View, target))
                {
                    return member;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No renderer member owns soldier '{target.Soldier.Name}'.");
    }

    internal static void MovePresentationAwayFromSimulation(SoldierRenderer renderer, SoldierMember member, bool smooth)
    {
        Vector3d simulatedRoot = member.TickWhere;
        member.PrevTickWhere = new Vector3d(
            simulatedRoot.X + PresentationOffsetMetres,
            simulatedRoot.Y,
            simulatedRoot.Z);
        if (smooth)
        {
            // Interpolate a turn too: the collision pose must follow the same DrawnOrientation as the
            // displayed mesh, not merely the displayed translation.
            member.PrevTickFacing = member.TickFacing + (MathF.PI * 0.5f);
        }
        member.HasTickState = true;
        renderer.BeginFrame(tickFraction: 0.5f, smooth);
    }

    internal static IEnumerable<(SoldierView View, MefCollisionMesh Mesh)> MeshBackedSoldiers(SoldierRenderer renderer)
    {
        foreach (SoldierGroup group in renderer.GroupsForTesting)
        {
            if (group.Body.CollisionMesh is not MefCollisionMesh mesh)
            {
                continue;
            }

            foreach (SoldierMember member in group.Soldiers)
            {
                SoldierView view = member.Agent.View;
                yield return (view, mesh);
            }
        }
    }

    internal static Vector3d FeetOnGround(SoldierRenderer renderer, SoldierView soldier) => new(
        soldier.Position.X,
        soldier.Position.Y,
        renderer.Simulation.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z));

    internal static Dictionary<SoldierView, Igi1HumanAi>.KeyCollection RendererSoldiers(SoldierRenderer renderer) =>
        RendererBrains(renderer).Keys;

    internal static Dictionary<SoldierView, Igi1HumanAi> RendererBrains(SoldierRenderer renderer) => renderer.Simulation.Brains;

    internal static string DescribeAncestry(GameTask task)
    {
        List<string> ancestry = [];
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            string state = current is ConditionalContainerTask container
                ? $" run={container.IsRun} condition='{container.Condition}'"
                : string.Empty;
            ancestry.Add($"{current.Type.Name}_{current.Id}{state}");
        }

        return string.Join(" <- ", ancestry);
    }
}

/// <summary>A soldier whose group carries a collision mesh, and its renderer member.</summary>
internal readonly record struct MeshBackedSoldier(SoldierView View, MefCollisionMesh Mesh, SoldierMember Member);

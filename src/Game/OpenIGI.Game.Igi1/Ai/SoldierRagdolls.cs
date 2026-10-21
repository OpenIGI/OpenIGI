using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What the soldiers' corpses and dropped guns are simulated with: one prepared rig per skeleton, and the world they fall against.</summary>
/// <remarks>
/// Nothing here changes what the original does: the death clip still plays underneath, and a ragdoll only replaces the
/// pose that is drawn.
/// </remarks>
/// <param name="context">The world casts and ground the corpses collide with.</param>
internal sealed class SoldierRagdolls(SoldierContext context)
{
    private readonly SoldierContext _context = context;

    private CollisionTriangleGather? _dropTriangles;

    private WorldRayCast? _dropTrace;

    /// <summary>Gets one prepared rig per skeleton, shared by every corpse wearing it.</summary>
    // One prepared rig per skeleton, shared by every corpse wearing it. Building it walks the whole bone
    // graph twice, so it must not happen per death -- and it holds no live state, so it need not.
    public Dictionary<BoneAnimationSet, RagdollSkeleton> RagdollRigs { get; } = [];

    /// <summary>Gets or sets what corpses collide against, built on first use.</summary>
    public RagdollWorld? RagdollWorld { get; set; }

    /// <summary>Gives dropped weapons the world they collide with.</summary>
    /// <param name="triangles">The solid triangles near a box, terrain included, for the realistic body.</param>
    /// <param name="trace">A cast against everything solid, terrain included, for the original's drop probe.</param>
    public void SetDroppedWeaponWorld(CollisionTriangleGather? triangles, WorldRayCast? trace)
    {
        _dropTriangles = triangles;
        _dropTrace = trace;
        RagdollWorld = null;
    }

    /// <summary>The prepared rig for an animation set, built once and shared by every corpse wearing it.</summary>
    /// <remarks>
    /// Keyed by the SET rather than the skeleton, because the joint limits are measured from the set's
    /// own clips — the animators' demonstration of how far each joint is meant to move, which is what
    /// gives a knee a hinge without any bone being named. Building it sweeps every rotation key in the
    /// set twice, so it must happen once per mission and not once per death.
    /// </remarks>
    public RagdollSkeleton? RagdollRigFor(BoneAnimationSet set, MefModel? model)
    {
        if (RagdollRigs.TryGetValue(set, out RagdollSkeleton? rig))
        {
            return rig;
        }

        try
        {
            rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);
        }
        catch (ArgumentException ex)
        {
            // A rig the solver cannot use is not fatal: the retail death clip is still playing
            // underneath, so the body falls the way it always did and only the remaster is skipped.
            _context.Logger.Warning(SoldierContext.LogCategory, $"Skeleton {set.Skeleton.Id} cannot drive a ragdoll: {ex.Message}");
            return null;
        }

        RagdollRigs[set] = rig;
        return rig;
    }

    /// <summary>What corpses collide against — the same two queries a living body uses.</summary>
    public RagdollWorld RagdollWorldForCorpses()
    {
        if (RagdollWorld is { } world)
        {
            return world;
        }

        Igi1HumanAi.GroundQuery? ground = _context.MakeGroundQuery();
        RagdollWorld = new RagdollWorld(
            _context.Cast,
            ground is null ? null : (double x, double y, double z, out double groundZ) => ground(x, y, z, out groundZ))
        {
            Triangles = _dropTriangles,
            Trace = _dropTrace,
        };
        return RagdollWorld;
    }
}

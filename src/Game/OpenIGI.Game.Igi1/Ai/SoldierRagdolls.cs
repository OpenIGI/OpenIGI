using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

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

    /// <summary>Gets one prepared rig per animation set and model, shared by every corpse wearing both.</summary>
    // Keyed by the MODEL as well as the set, because the capsules are measured from the mesh: two soldier
    // models on one animation set are two different bodies, and keying on the set alone sized every one
    // of them after whichever happened to die first.
    public Dictionary<(BoneAnimationSet Set, MefModel? Model), RagdollSkeleton> RagdollRigs { get; } = [];

    // The measured joint ranges depend on the set alone, and sweeping its clips is the expensive part.
    private readonly Dictionary<BoneAnimationSet, RagdollLimits> _limits = [];

    /// <summary>Drops every prepared rig and measurement, when the mission they belong to ends.</summary>
    public void ForgetRigs()
    {
        RagdollRigs.Clear();
        _limits.Clear();
    }

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
        if (RagdollRigs.TryGetValue((set, model), out RagdollSkeleton? rig))
        {
            return rig;
        }

        try
        {
            if (!_limits.TryGetValue(set, out RagdollLimits? limits))
            {
                limits = RagdollLimits.Measure(set);
                _limits[set] = limits;
            }

            rig = RagdollSkeleton.Build(set.Skeleton, limits, model);
        }
        catch (ArgumentException ex)
        {
            // A rig the solver cannot use is not fatal: the retail death clip is still playing
            // underneath, so the body falls the way it always did and only the remaster is skipped.
            _context.Logger.Warning(SoldierContext.LogCategory, $"Skeleton {set.Skeleton.Id} cannot drive a ragdoll: {ex.Message}");
            return null;
        }

        RagdollRigs[(set, model)] = rig;
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

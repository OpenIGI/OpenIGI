using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Places what a model attaches — the magic objects of its <c>ATTA</c> tree, and theirs in turn — in
/// its parent's frame, and collects the ladders among them.
/// </summary>
/// <remarks>
/// These are most of a level's contents: the script names 501 objects in level 1, and their models
/// attach thousands more.
/// </remarks>
internal static class WorldAttachmentWalker
{
    /// <summary>Places a node's attached children, and theirs, in the parent's frame.</summary>
    /// <remarks>
    /// An attachment's position is in its parent model's space and in the same engine units as a vertex,
    /// so it converts to metres the same way a placement's does. The rotations compose in the order the
    /// camera already uses for a placement's own orientation.
    /// </remarks>
    internal static int AddAttached(
        LoadedMission mission,
        IOpenIgiLogger log,
        WorldObject node,
        Vector3d position,
        Matrix3f drawOrientation,
        Matrix3f taskOrientation,
        string set,
        string taskName,
        GameTask owner,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        MagicObjectRegistry? magic,
        List<WorldPlacement> placements,
        List<LadderPlacement> ladders)
    {
        int added = 0;
        foreach (WorldObject child in node.Children)
        {
            string magicTaskTypeName = string.Empty;
            bool isRotor = false;
            float magicExplosionFireballs = 0f;
            int magicExplosionFragments = 0;
            string magicExplosionSound = string.Empty;
            string magicDestroyedModel = string.Empty;
            int magicExplosionDelayTicks = 0;
            float magicExplosionDamageFactor = 0f;
            float magicExplosionFalloffMetres = 0f;
            float magicExplosionRadiusMetres = 0f;
            float magicExplosionDamageThreshold = 0f;
            WorldModelBinding? magicDestroyedReplacement = null;
            if (magic is not null && magic.TryGet(child.Name, out MagicObjectDefinition childDefinition))
            {
                magicTaskTypeName = magic.TaskTypeName(childDefinition.TaskType);
                isRotor = string.Equals(magicTaskTypeName, "TASKTYPE_ROTOR", StringComparison.Ordinal);
                magicExplosionFireballs = childDefinition.ExplosionFireballs;
                magicExplosionFragments = childDefinition.ExplosionFragments;
                magicExplosionSound = childDefinition.ExplosionSound;
                magicDestroyedModel = childDefinition.DestroyedModel;
                magicExplosionDelayTicks = childDefinition.ExplosionDelayTicks;
                magicExplosionDamageFactor = childDefinition.ExplosionDamageFactor;
                magicExplosionFalloffMetres = childDefinition.ExplosionFalloffMetres;
                magicExplosionRadiusMetres = childDefinition.ExplosionRadiusMetres;
                magicExplosionDamageThreshold = childDefinition.ExplosionDamageThreshold;
                if (childDefinition.DestroyedModel.Length > 0
                    && byName.TryGetValue(childDefinition.DestroyedModel, out var destroyed))
                {
                    magicDestroyedReplacement = new WorldModelBinding(
                        destroyed.Instance,
                        destroyed.Pack,
                        destroyed.Model);
                }
            }
            // Some retail rotor attachments have no magic-object entry in the level registry.  The
            // verified Bell/Mil attachment families remain rotor presentation objects even when that
            // lookup misses; keep this fallback alongside the develop-side destructible metadata.
            if (!isRotor
                && owner is DeclaredTask { Type.Name: "Heli" }
                && HelicopterRotorCatalog.IsKnownRotorModel(child.Name))
            {
                isRotor = true;
            }
            // Parent-to-world is the column-vector product M·v, matching how the renderer applies the
            // parent's own orientation (`uModelView * v`); Transform is the row-vector v·M, its
            // inverse, which placed a rotated parent's contents on the wrong side of it. The rotation
            // composition below is already the column-vector convention's parent∘child order.
            Vector3f offset = drawOrientation.Transposed().Transform(child.Position);
            Vector3d childPosition = new(
                position.X + (offset.X / WorldScene.UnitsPerMetre),
                position.Y + (offset.Y / WorldScene.UnitsPerMetre),
                position.Z + (offset.Z / WorldScene.UnitsPerMetre));
            Matrix3f childDrawOrientation = drawOrientation * child.Orientation;

            // sub_4DFA50 does not copy an ATTA matrix into the spawned magic task. It rebuilds a
            // right-handed basis from the attachment's first two axes before composing it with its
            // parent. After the model-to-world axis conversion used by this port, that rebuilt local
            // frame is exactly the ATTA orientation's transpose. Keeping the task frame separate from
            // the draw frame matters for quarter-turn attachments: the old blanket 180-degree fix made
            // the first building ladder look right but pointed WaterTower's 435_02_1 ladder inward.
            Matrix3f childTaskOrientation = taskOrientation * child.Orientation.Transposed();

            // A ladder is a TASKTYPE_LADDER magic object whose model carries the climb line as its magic
            // vertices 2 (foot) and 3 (head). Collected here, where its world transform is known.
            TryCollectLadder(
                child,
                childPosition,
                childDrawOrientation,
                childTaskOrientation,
                magic,
                ladders);

            // A child draws only if its model is also a pack instance, because the textures a sub-mesh
            // uses are named by the instance, not by the model. Its subtree is walked either way: the
            // lightmap numbering already counted it, and its own children may well be instances.
            // An Afterburner (0x42BEB0) draws through its own primitive instead of its model, and that primitive's
            // draw callback (0x42C4B0) is an empty stub: the original never shows it. Its children still follow.
            bool neverDrawn = string.Equals(magicTaskTypeName, "TASKTYPE_AFTERBURNER", StringComparison.Ordinal);
            if (!neverDrawn && byName.TryGetValue(child.Name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
            {
                // The attachment draws in the rebuilt right-handed frame sub_4DFA50 builds, which after the
                // model-to-world axis conversion is the ATTA orientation's transpose — the same childTaskOrientation
                // the ladder task frame uses (and the reference editor's attLocalRot = R_row^T). The old draw
                // frame dropped the transpose, which put attached sub-meshes such as a building's windows on the
                // wrong side of their parent. The ladder's climb line keeps the non-transposed draw frame: its
                // magic-vertex positions were pinned to the retail game's climb line.
                placements.Add(new WorldPlacement(
                    childPosition,
                    childTaskOrientation,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    taskName,
                    owner,
                    set,
                    set.Length > 0 ? child.LightmapIndex : -1)
                {
                    IsRotor = isRotor,
                    IsAttached = true,
                    MagicTaskTypeName = magicTaskTypeName,
                    MagicExplosionFireballs = magicExplosionFireballs,
                    MagicExplosionFragments = magicExplosionFragments,
                    MagicExplosionSound = magicExplosionSound,
                    MagicDestroyedModel = magicDestroyedModel,
                    MagicExplosionDelayTicks = magicExplosionDelayTicks,
                    MagicExplosionDamageFactor = magicExplosionDamageFactor,
                    MagicExplosionFalloffMetres = magicExplosionFalloffMetres,
                    MagicExplosionRadiusMetres = magicExplosionRadiusMetres,
                    MagicExplosionDamageThreshold = magicExplosionDamageThreshold,
                    DestroyedReplacement = magicDestroyedReplacement,
                });
                WorldSceneDiagnostics.LogDerivedPlacement(
                    log,
                    mission,
                    owner as DeclaredTask,
                    child.Name,
                    childPosition,
                    childTaskOrientation,
                    "ATTA");
                added++;
            }

            added += AddAttached(
                mission, log, child, childPosition, childDrawOrientation, childTaskOrientation, set,
                taskName, owner, byName, magic, placements, ladders);
        }

        return added;
    }

    /// <summary>The magic-object task type a ladder is spawned with.</summary>
    private const string LadderTaskType = "TASKTYPE_LADDER";

    /// <summary>Records a ladder when a node is one — its climb line from magic vertices 2 and 3.</summary>
    private static void TryCollectLadder(
        WorldObject child,
        Vector3d positionMetres,
        Matrix3f drawOrientation,
        Matrix3f taskOrientation,
        MagicObjectRegistry? magic,
        List<LadderPlacement> ladders)
    {
        if (magic is null
            || !magic.TryGet(child.Name, out MagicObjectDefinition definition)
            || !string.Equals(magic.TaskTypeName(definition.TaskType), LadderTaskType, StringComparison.Ordinal))
        {
            return;
        }

        MefModel model = child.Models.Highest;
        if (!model.TryGetMagicVertex(1, out Vector3f activationTop)
            || !model.TryGetMagicVertex(2, out Vector3f foot)
            || !model.TryGetMagicVertex(3, out Vector3f head))
        {
            return;
        }

        Vector3d origin = new(
            positionMetres.X * WorldScene.UnitsPerMetre,
            positionMetres.Y * WorldScene.UnitsPerMetre,
            positionMetres.Z * WorldScene.UnitsPerMetre);

        ladders.Add(new LadderPlacement(
            origin,
            ToWorldUnits(origin, drawOrientation, activationTop),
            ToWorldUnits(origin, drawOrientation, foot),
            ToWorldUnits(origin, drawOrientation, head),
            taskOrientation));
    }

    /// <summary>Places a model-space magic vertex in the world, in engine units.</summary>
    /// <remarks>
    /// <b>Verified against the running game</b> (the ladder mount computed to the byte from
    /// this): the magic vertex is rotated as <c>v · M</c> (<see cref="Matrix3f.Transform(Vector3f)"/>), NOT
    /// the transpose. Using the transpose negated the vertex's X and Y (Z survives on an axis-aligned
    /// ladder), which stood the climbing body ~6 cm off the rungs. Unlike an attachment offset, a magic
    /// vertex takes the plain product.
    /// </remarks>
    private static Vector3d ToWorldUnits(Vector3d originUnits, Matrix3f orientation, Vector3f local)
    {
        Vector3f rotated = orientation.Transform(local);
        return new Vector3d(originUnits.X + rotated.X, originUnits.Y + rotated.Y, originUnits.Z + rotated.Z);
    }
}

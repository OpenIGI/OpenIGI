using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What the soldiers' simulation reads of one soldier model: the model, its collision mesh, and its lowest vertex.</summary>
/// <param name="model">The model every soldier of the group is drawn from and posed by, or <see langword="null"/> when none loaded.</param>
/// <param name="lowestVertexZ">The model's lowest vertex in its own space, in metres, which stands it on the ground.</param>
/// <remarks>
/// CPU data only, shared by every soldier of the model: a round is scored against the collision mesh, a corpse's rig is
/// built from the model, and a body is set down by its lowest vertex. The mesh it is drawn as is the renderer's.
/// </remarks>
public sealed class SoldierBodyModel(MefModel? model, float lowestVertexZ)
{
    private MefCollisionMesh? _collisionMesh;
    private bool _collisionMeshRead;

    /// <summary>Gets the model every soldier in the group draws, kept so the group can be re-skinned each tick.</summary>
    public MefModel? Model { get; } = model;

    /// <summary>Gets the model's lowest vertex, in metres.</summary>
    public float LowestVertexZ { get; } = lowestVertexZ;

    /// <summary>Gets the model's skinned collision mesh, read once — the geometry a round is scored against.</summary>
    public MefCollisionMesh? CollisionMesh
    {
        get
        {
            if (!_collisionMeshRead)
            {
                _collisionMeshRead = true;
                if (Model is not null && MefCollisionMesh.TryRead(Model.Container, out MefCollisionMesh? read))
                {
                    _collisionMesh = read;
                }
            }

            return _collisionMesh;
        }
    }

    /// <summary>Gets a value indicating whether the one-time collision-chunk read has already happened.</summary>
    public bool CollisionMeshPrepared => _collisionMeshRead;

    /// <summary>Reads a soldier model and measures it in a soldier's stand pose.</summary>
    /// <param name="mission">The packs the model is read from.</param>
    /// <param name="modelName">The model's name, as the soldier's task spells it.</param>
    /// <param name="posed">The soldier whose stand pose the model is measured in.</param>
    /// <param name="logger">Where a model that cannot stand is reported.</param>
    /// <param name="body">Receives the model and its lowest vertex.</param>
    /// <param name="geometry">Receives the model's unskinned geometry, whose indices and subsets whatever draws it uses.</param>
    /// <param name="standSkin">Receives the geometry skinned in the stand pose, which whatever draws it uploads.</param>
    /// <returns><see langword="false"/> when no pack holds the model, it is not skinned, it has nothing to draw, or it cannot be posed.</returns>
    /// <remarks>
    /// The lowest point of the posed skin, in metres below the skeleton root: Z rotation cannot change it, so it is a property of
    /// the (model, skeleton, animation) triple, and it is what <see cref="SoldierSimulation.Stand"/> rests on the ground. The
    /// pose is unrotated -- the facing is each soldier's own -- and only the base vertices count: the skin's tail holds the
    /// extra-influence records, which no draw record indexes.
    /// </remarks>
    public static bool TryPose(
        LoadedMission mission,
        string modelName,
        SoldierView posed,
        IOpenIgiLogger logger,
        [NotNullWhen(true)] out SoldierBodyModel? body,
        [NotNullWhen(true)] out MeshGeometry? geometry,
        [NotNullWhen(true)] out Vertex3D[]? standSkin)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(posed);
        ArgumentNullException.ThrowIfNull(logger);
        body = null;
        geometry = null;
        standSkin = null;
        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            logger.Warning(SoldierContext.LogCategory, $"'{posed.Soldier.Name}' names model '{modelName}', which no pack holds.");
            return false;
        }

        if (!MefSkinner.IsSkinned(model))
        {
            logger.Warning(SoldierContext.LogCategory, $"'{modelName}' is {model.Format}, not a skinned model.");
            return false;
        }

        if (MefMeshBuilder.Build(model, 1f) is not { } built)
        {
            logger.Warning(SoldierContext.LogCategory, $"'{modelName}' has nothing to draw.");
            return false;
        }

        Vertex3D[] skinned = [.. built.Vertices];
        int written;
        try
        {
            written = MefSkinner.Skin(model, posed.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException ex)
        {
            logger.Warning(SoldierContext.LogCategory, $"'{modelName}' could not be posed: {ex.Message}");
            return false;
        }

        float lowest = 0f;
        for (int i = 0; i < written; i++)
        {
            lowest = Math.Min(lowest, skinned[i].Position.Z);
        }

        body = new SoldierBodyModel(model, lowest);
        geometry = built;
        standSkin = skinned;
        return true;
    }
}

using OpenIGI.Formats.World;

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
}

using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// I.G.I. 2's collision world built of another game's level: the solids its soldiers stand, look and shoot through, and the
/// ground under them.
/// </summary>
/// <remarks>
/// <para>
/// Both games trace placed models through one engine handler (Project I.G.I.'s <c>World_TraceLines</c>, <c>0x4CC2A0</c>, is
/// I.G.I. 2's), over the same collision mesh levels and the same row-major local-to-world frame, so each placement becomes an
/// I.G.I. 2 solid as it stands: its model's collision levels read in the host's layout, its position in units, its orientation
/// copied.
/// </para>
/// <para>
/// Each solid stands where the level placed it; a model that moves carries its solid along through
/// <see cref="Igi2PlacedCarriers"/>.
/// </para>
/// <para>
/// The games number their game materials differently, so every face's material is translated into the row I.G.I. 2's table
/// gives it; a material it has no row for reads as none, which I.G.I. 2 treats as a plain solid.
/// </para>
/// </remarks>
public static class Igi2PlacedCollision
{
    /// <summary>Builds the world.</summary>
    /// <param name="placements">The level's placed models, as its world scene placed them.</param>
    /// <param name="layout">The layout the host's collision meshes are in.</param>
    /// <param name="ground">The host's ground, answering I.G.I. 2's lines, or <see langword="null"/> for none.</param>
    /// <param name="terrain">The level's terrain, which the world names as its ground, or <see langword="null"/> for none.</param>
    /// <param name="material">Translates a host game material into I.G.I. 2's row, or <see langword="null"/> for none.</param>
    /// <param name="solids">Receives each placement's solid, for <see cref="Igi2PlacedCarriers"/>; <see langword="null"/> for none.</param>
    /// <returns>The world.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Igi2CollisionWorld Build(
        IEnumerable<WorldPlacement> placements,
        CollisionLayout layout,
        IWorldGroundCollider? ground,
        TerrainData? terrain,
        Func<int, int?> material,
        IDictionary<WorldPlacement, Igi2RigidObject>? solids = null)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(material);

        List<Igi2RigidObject> made = [];
        foreach (WorldPlacement placement in placements)
        {
            if (Solid(placement, layout, material) is { } solid)
            {
                made.Add(solid);
                solids?.Add(placement, solid);
            }
        }

        return new Igi2CollisionWorld(made, ground) { Ground = terrain };
    }

    /// <summary>One placement as an I.G.I. 2 solid, or <see langword="null"/> when its model has no collision.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="layout">The layout the host's collision meshes are in.</param>
    /// <param name="material">Translates a host game material into I.G.I. 2's row.</param>
    /// <returns>The solid.</returns>
    public static Igi2RigidObject? Solid(WorldPlacement placement, CollisionLayout layout, Func<int, int?> material)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(material);
        Igi2CollisionMesh?[] levels = new Igi2CollisionMesh?[MefCollisionMesh.MeshLevels];
        bool any = false;
        for (int level = 0; level < levels.Length; level++)
        {
            if (MefCollisionMesh.TryRead(placement.Model.Container, level, out MefCollisionMesh? mesh, layout)
                && mesh is { Faces.Length: > 0 })
            {
                Igi2CollisionMesh solid = new(mesh);
                for (int row = 0; row < solid.Rows.Length; row++)
                {
                    solid.Rows[row] = solid.Rows[row] == 0xFFFF ? 0xFFFF : material(solid.Rows[row]) ?? 0xFFFF;
                }

                levels[level] = solid;
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        Igi2RigidObject placed = new(
            new Igi2CollisionOwner(placement.Owner.Type.Name, placement.Owner.Id, placement.Instance.Name, placement.IsAttached ? 1 : 0),
            levels,
            placement.Model.NativeMarkerBound,
            isBuildingMesh: false)
        {
            Position = placement.Position * WorldScene.UnitsPerMetre,
        };
        placement.Orientation.CopyTo(placed.Orientation);
        return placed;
    }
}

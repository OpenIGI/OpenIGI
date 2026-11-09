using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A placement's mesh as a bomb's blast breaks it: the geometry in the model's own space, metres, and each subset's texture.</summary>
/// <param name="Placement">The placement.</param>
/// <param name="Geometry">Its mesh.</param>
/// <param name="SubsetTextures">The texture each of the mesh's subsets is drawn with, in subset order; none where it has none.</param>
public sealed record ShatterSource(WorldPlacement Placement, MeshGeometry Geometry, TextureHandle[] SubsetTextures);

/// <summary>The world as a bomb's blast breaks it: what stands near, what it is made of, and what it leaves.</summary>
public interface IShatterableWorld
{
    /// <summary>The placements whose bounds reach within a distance of a point.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="centreMetres">The point, in world metres.</param>
    /// <param name="reachMetres">How far from it.</param>
    /// <returns>The placements, with how far each one's bounds lie from the point.</returns>
    List<(WorldPlacement Placement, double Distance)> PlacementsNear(WorldScene scene, Vector3d centreMetres, double reachMetres);

    /// <summary>A placement's mesh and textures.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Its mesh and textures, or <see langword="null"/>.</returns>
    ShatterSource? ShatterSourceOf(WorldPlacement placement);

    /// <summary>Takes a placement out of the frame, shattered.</summary>
    /// <param name="placement">The placement.</param>
    void Shatter(WorldPlacement placement);

    /// <summary>Draws a placement holed.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="holed">Its holed mesh.</param>
    /// <returns>Whether it took.</returns>
    bool Breach(WorldPlacement placement, MeshGeometry holed);

    /// <summary>Puts back everything shattered or holed.</summary>
    void MendShattered();
}

/// <summary>The world as a bomb's blast breaks it: what stands near, what it is made of, and what it leaves.</summary>
public sealed partial class WorldRenderer : IShatterableWorld
{
    /// <summary>The placements whose bounds reach within a distance of a point, and could be touched by a blast there.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="centreMetres">The point, in world metres.</param>
    /// <param name="reachMetres">How far from it.</param>
    /// <returns>The placements, in the scene's order, with how far each one's bounds lie from the point.</returns>
    /// <remarks>
    /// The world's own objects alone: a rotor, a placement suppressed, one already shattered or holed, and anything the title
    /// destroyed are left out, and so is what an attached object (a pane, a barrel, a charge) is: those are the blast's to
    /// break for good, through the destructibles, and are not a shatter's.
    /// </remarks>
    public List<(WorldPlacement Placement, double Distance)> PlacementsNear(WorldScene scene, Vector3d centreMetres, double reachMetres)
    {
        ArgumentNullException.ThrowIfNull(scene);
        List<(WorldPlacement, double)> near = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.IsSuppressed || placement.IsRotor || placement.IsAttached || placement.MagicTaskTypeName.Length > 0
                || _transforms.Shattered.Contains(placement) || _subsets.IsBreached(placement)
                || _transforms.Destructibles?.IsDestroyed(placement) == true || _transforms.Destruction?.IsDestroyed(placement) == true)
            {
                continue;
            }

            if ((placement.BentGeometry ?? _subsets.GetGeometry(placement)) is not { } geometry)
            {
                continue;
            }

            BoundingSphere bounds = geometry.Bounds;
            Vector3f offset = placement.Orientation.Transposed().Transform(bounds.Centre);
            Vector3d centre = placement.Position + new Vector3d(offset.X, offset.Y, offset.Z);
            double distance = Math.Max(0.0, (centre - centreMetres).Length - bounds.Radius);
            if (distance <= reachMetres)
            {
                near.Add((placement, distance));
            }
        }

        return near;
    }

    /// <summary>A placement's mesh and textures, for a blast to break.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Its mesh and textures, or <see langword="null"/> when it has no mesh.</returns>
    public ShatterSource? ShatterSourceOf(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if ((placement.BentGeometry ?? _subsets.GetGeometry(placement)) is not { } geometry)
        {
            return null;
        }

        var textures = new TextureHandle[geometry.Subsets.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            MeshSubset subset = geometry.Subsets[i];
            textures[i] = subset.HasMaterial && placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string name)
                ? _textures.GetTexture(name)
                : TextureHandle.None;
        }

        return new ShatterSource(placement, geometry, textures);
    }

    /// <summary>Takes a placement out of the frame: a blast has shattered it, and its pieces are drawn in its place.</summary>
    /// <param name="placement">The placement.</param>
    public void Shatter(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        _transforms.Shattered.Add(placement);
        _frameState.Invalidate();
    }

    /// <summary>Draws a placement with a hole blown through it, until the world is mended.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="holed">Its mesh with the hole cut through it, in the model's own space.</param>
    /// <returns>Whether it took.</returns>
    public bool Breach(WorldPlacement placement, MeshGeometry holed)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(holed);
        bool took = _subsets.Breach(placement, holed);
        _frameState.Invalidate();
        return took;
    }

    /// <summary>Puts back everything a blast shattered or holed, as the level is put back for the next round.</summary>
    public void MendShattered()
    {
        _transforms.Shattered.Clear();
        _subsets.MendBreaches();
        _frameState.Invalidate();
    }
}

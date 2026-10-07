using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>The level's terrain as the collision sees it: the ground's tiles, its discard boxes and its materials.</summary>
public sealed class Igi2TerrainCollider : IIgi2Collider
{
    /// <summary>The number of level material slots, <c>World_t.aptQMaterial</c>; a terrain material outside them has none.</summary>
    public const int MaterialSlots = 0x40;

    private readonly Igi2TerrainSurface _surface;

    /// <summary>Initializes a new instance of the <see cref="Igi2TerrainCollider"/> class.</summary>
    /// <param name="surface">The level's ground.</param>
    /// <param name="owner">What a hit reports as struck: the terrain.</param>
    public Igi2TerrainCollider(Igi2TerrainSurface surface, object owner)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets what the ground stands for in the level: the terrain.</summary>
    public object Owner { get; }

    /// <summary>
    /// Vertical lines against the ground, <c>Terrain_GetNextZHandler</c> (<c>igi2 0x572E80</c>): each line's start
    /// is sampled, and the ground there is struck when its height lies between the line's two ends.
    /// </summary>
    /// <param name="trace">The lines and their running results.</param>
    /// <remarks>
    /// Only the start's X and Y are read, so a line that is not vertical is treated as one that is. The distance is
    /// the height difference from the start, and a hit replaces the line's result only when nearer. A point over a
    /// discard box has no ground. The hit reports the terrain material's level material, the triangle's normal in
    /// both frames, and the lightmap's light.
    /// </remarks>
    public void NextZ(Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Vector3d origin = _surface.Origin;
        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i];
            double endZ = trace.Ends[i].Z;
            float relativeY = ((Fpu)start.Y - origin.Y).F;
            float relativeX = ((Fpu)start.X - origin.X).F;
            if (!Igi2TerrainVertex.TrySample(_surface.Tiles, relativeX, relativeY, out Igi2TerrainSample sample))
            {
                return;
            }

            double height = ((Fpu)sample.RelativeZ + origin.Z).Value;
            double high = start.Z <= endZ ? endZ : start.Z;
            double low = start.Z <= endZ ? start.Z : endZ;
            double distance = Fpu.Abs((Fpu)start.Z - height).Value;
            if (!(height >= low) || !(height <= high))
            {
                continue;
            }

            Igi2Intersection hit = trace.Intersections[i];
            if (Igi2DiscardBox.AnyContains(_surface.DiscardBoxes, relativeX, relativeY)
                || (hit.IsHit && !(distance < hit.Distance)))
            {
                continue;
            }

            hit.IsHit = true;
            hit.HitObject = this;
            hit.Position = new Vector3d(start.X, start.Y, height);
            hit.LocalPosition = new Vector3f(
                ((Fpu)start.X - origin.X).F,
                ((Fpu)start.Y - origin.Y).F,
                ((Fpu)height - origin.Z).F);
            hit.Distance = distance;
            hit.Light = sample.Light;
            hit.IsDoubleFace = false;
            hit.EnterMaterial = LevelMaterial(sample.Material);
            hit.LeaveMaterial = null;
            hit.Normal = sample.Normal;
            hit.LocalNormal = sample.Normal;
        }
    }

    /// <summary>The level material a terrain material id stands for: its <c>TerrainMaterial</c>'s <c>Game material</c>.</summary>
    /// <param name="id">The id a material map holds.</param>
    /// <returns>The level material slot, or <see langword="null"/> when no <c>TerrainMaterial</c> has the id or its slot is out of range.</returns>
    public int? LevelMaterial(byte id) =>
        _surface.Materials.TryGetValue(id, out TerrainMaterialTask? material) && material.GameMaterial is >= 0 and < MaterialSlots
            ? material.GameMaterial
            : null;
}

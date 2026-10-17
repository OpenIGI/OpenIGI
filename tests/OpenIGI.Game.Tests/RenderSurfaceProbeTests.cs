using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins that the F12 inspector's <c>tex=</c> readout names the texture actually on the surface.
/// </summary>
/// <remarks>
/// <para>
/// Against retail geometry, because a <see cref="WorldPlacement"/> names a <c>MefModel</c> and only a
/// real pack file can construct one — the same reason <c>LightFixtureExtractorTests</c> takes meshes
/// rather than placements.
/// </para>
/// <para>
/// The readout exists so that a surface can be named in <c>materials.json</c>, which is keyed by texture
/// name; a probe that answered confidently and wrongly would send somebody to author a material for a
/// texture they are not looking at, and nothing downstream would contradict it. So the assertion is
/// geometric rather than "it returned something": a ray is aimed down the normal of one chosen triangle,
/// and the probe has to come back with that triangle's own texture at the distance it was fired from.
/// </para>
/// </remarks>
public sealed class RenderSurfaceProbeTests
{
    /// <summary>A ray fired at a known triangle names that triangle's texture, at the right distance.</summary>
    [RequiresGameDataFact]
    public void ARayNamesTheTextureOnTheTriangleItMeets()
    {
        int probed = 0;

        foreach (WorldPlacement placement in RetailLevelOneGround.Scene().Placements)
        {
            if (!TryPickTriangle(placement, out Vector3d a, out Vector3d b, out Vector3d c, out int slot)
                || !placement.Pack.TryGetTextureName(placement.Instance, slot, out string expected))
            {
                continue;
            }

            Vector3d centre = new((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
            Vector3d normal = Normalise(Cross(Minus(b, a), Minus(c, a)));

            // Stood off along the face's own normal, so the ray meets this triangle square-on and the
            // distance it comes back with is the stand-off it was fired from.
            const double StandOff = 250.0;
            Vector3d origin = new(
                centre.X + (normal.X * StandOff),
                centre.Y + (normal.Y * StandOff),
                centre.Z + (normal.Z * StandOff));
            Vector3d direction = new(-normal.X * StandOff * 2.0, -normal.Y * StandOff * 2.0, -normal.Z * StandOff * 2.0);

            Assert.True(
                RenderSurfaceProbe.TryFindTexture(placement, origin, direction, out string named, out double distance),
                $"'{placement.Instance.Name}' has a drawn triangle the probe did not meet.");

            // Not necessarily the chosen triangle's own texture: another of the model's surfaces may
            // stand between the stand-off point and this one, and then the nearer surface is the right
            // answer. What must hold is that the answer is a texture THIS model draws, and that it was
            // met on the way in rather than behind the target.
            Assert.Contains(named, TexturesOf(placement));
            Assert.InRange(distance, 1.0, StandOff * 2.0);

            if (named == expected)
            {
                // The unobstructed case, which is the one the readout is for.
                Assert.InRange(distance, StandOff * 0.99, StandOff * 1.01);
            }

            if (++probed == 40)
            {
                break;
            }
        }

        Assert.True(probed > 0, "Level 1 offered no placement with a drawn, textured triangle.");
    }

    /// <summary>A ray that misses the object names nothing, rather than the nearest surface anyway.</summary>
    [RequiresGameDataFact]
    public void ARayThatMissesNamesNothing()
    {
        foreach (WorldPlacement placement in RetailLevelOneGround.Scene().Placements)
        {
            if (!TryPickTriangle(placement, out Vector3d a, out _, out _, out _))
            {
                continue;
            }

            // Parallel to the object and far to one side of it: a ray that shares none of its space.
            Vector3d origin = new(a.X + 1_000_000.0, a.Y + 1_000_000.0, a.Z + 1_000_000.0);

            Assert.False(
                RenderSurfaceProbe.TryFindTexture(placement, origin, new Vector3d(0.0, 0.0, -100_000.0), out _, out _));
            return;
        }

        Assert.Fail("Level 1 offered no placement with a drawn, textured triangle.");
    }

    private static IEnumerable<string> TexturesOf(WorldPlacement placement)
    {
        foreach (MefDrawRecord record in placement.Model.DrawRecords)
        {
            if (record.HasMaterial
                && placement.Pack.TryGetTextureName(placement.Instance, record.MaterialSlot, out string name))
            {
                yield return name;
            }
        }
    }

    // The first drawn triangle with area to it, carried to world units the way LightmapBaker and the
    // collision index both carry theirs: the placement's rotation applied, its metre position scaled in.
    private static bool TryPickTriangle(
        WorldPlacement placement,
        out Vector3d a,
        out Vector3d b,
        out Vector3d c,
        out int slot)
    {
        a = b = c = default;
        slot = MefDrawRecord.NoMaterialSlot;

        ReadOnlySpan<MefVertex> vertices = placement.Model.Vertices;

        foreach (MefDrawRecord record in placement.Model.DrawRecords)
        {
            if (!record.HasMaterial)
            {
                continue;
            }

            ReadOnlySpan<ushort> indices = record.Indices.Span;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int x = record.ToVertexIndex(indices[i]);
                int y = record.ToVertexIndex(indices[i + 1]);
                int z = record.ToVertexIndex(indices[i + 2]);

                if ((uint)x >= (uint)vertices.Length
                    || (uint)y >= (uint)vertices.Length
                    || (uint)z >= (uint)vertices.Length)
                {
                    continue;
                }

                Vector3d p = ToWorldUnits(placement, vertices[x].Position);
                Vector3d q = ToWorldUnits(placement, vertices[y].Position);
                Vector3d r = ToWorldUnits(placement, vertices[z].Position);
                Vector3d cross = Cross(Minus(q, p), Minus(r, p));

                if (Math.Sqrt(Dot(cross, cross)) < 1.0)
                {
                    continue;
                }

                a = p;
                b = q;
                c = r;
                slot = record.MaterialSlot;
                return true;
            }
        }

        return false;
    }

    private static Vector3d ToWorldUnits(WorldPlacement placement, Vector3f local)
    {
        Vector3f rotated = placement.Orientation.Transform(local);
        return new Vector3d(
            rotated.X + (placement.Position.X * WorldScene.UnitsPerMetre),
            rotated.Y + (placement.Position.Y * WorldScene.UnitsPerMetre),
            rotated.Z + (placement.Position.Z * WorldScene.UnitsPerMetre));
    }

    private static Vector3d Minus(Vector3d l, Vector3d r) => new(l.X - r.X, l.Y - r.Y, l.Z - r.Z);

    private static Vector3d Cross(Vector3d l, Vector3d r) =>
        new((l.Y * r.Z) - (l.Z * r.Y), (l.Z * r.X) - (l.X * r.Z), (l.X * r.Y) - (l.Y * r.X));

    private static double Dot(Vector3d l, Vector3d r) => (l.X * r.X) + (l.Y * r.Y) + (l.Z * r.Z);

    private static Vector3d Normalise(Vector3d value)
    {
        double length = Math.Sqrt(Dot(value, value));
        return length > 1e-9 ? new Vector3d(value.X / length, value.Y / length, value.Z / length) : new Vector3d(0, 0, 1);
    }
}

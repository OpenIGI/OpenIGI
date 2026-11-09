using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Fracture;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>A building brought down: the order its slabs go in, and the dust and the heap it leaves.</summary>
public sealed partial class BlastDebris
{
    // A building's collapse starts its dust a few ticks after the blast, as the first slabs land.
    private const int CloudDelayTicks = 8;

    // How far above the ground under it a structure's foot may be and still stand on it; the narrowest footprint that is a
    // building, with a heap and a cloud of its own, rather than a wall or a fence.
    private const double GroundedMetres = 0.6;
    private const float LeastFootprintMetres = 2.5f;

    // Starts a building's collapse: its dust, its heap, and how its slabs will go.
    private Collapse BringDown(ShatterPlan plan, BombBlast blast, DebrisCollision collision)
    {
        WorldPlacement placement = plan.Source.Placement;
        Matrix3f localToWorld = placement.Orientation.Transposed();
        Vector3f low = plan.Low;
        Vector3f high = plan.High;
        Vector3f footprint = new((low.X + high.X) * 0.5f, (low.Y + high.Y) * 0.5f, low.Z);
        Vector3f offset = localToWorld.Transform(footprint);
        Vector3d groundMetres = placement.Position + new Vector3d(offset.X, offset.Y, offset.Z);
        float halfX = (high.X - low.X) * 0.5f;
        float halfY = (high.Y - low.Y) * 0.5f;
        float reach = MathF.Sqrt((halfX * halfX) + (halfY * halfY)) * 0.85f;
        float height = MathF.Max(high.Z - low.Z, 1f);
        uint seed = blast.Seed ^ Hash(placement.TaskName) ^ Hash(placement.Instance.Name);

        // Whether it stands on the ground: a pipe bridge, a tank on legs or a fence along a wall top comes down as pieces that
        // fall, with no stumps left in the air and no heap where nothing lands.
        Vector3d groundUnits = groundMetres * WorldUnits.PerMetre;
        bool grounded = !collision.TryGroundUnder(groundUnits, out double groundZ)
            || groundUnits.Z - groundZ < GroundedMetres * WorldUnits.PerMetre;
        if (!grounded || MathF.Min(halfX, halfY) * 2f < LeastFootprintMetres)
        {
            return Frame(plan, blast, grounded);
        }

        groundUnits = new Vector3d(groundUnits.X, groundUnits.Y, Math.Min(groundUnits.Z, groundZ));
        groundMetres = groundUnits / WorldUnits.PerMetre;

        // The volume of dust only where the fireball is marched too; the particles' surge everywhere.
        if (_budget.CellScale >= 1f)
        {
            _clouds.Add(new CollapseCloud(groundMetres, reach, height, seed, CloudDelayTicks));
        }

        Collapsed?.Invoke(groundUnits, reach, height, seed);

        var mound = new RubbleMound(
            groundUnits,
            reach * 0.9 * WorldUnits.PerMetre,
            Math.Clamp(height * 0.26, 0.7, 4.0) * WorldUnits.PerMetre,
            seed);
        AddHeap(mound, plan);
        collision.AddMound(mound);

        return Frame(plan, blast, grounded: true);
    }

    // How a building's slabs will go -- its box, where the blast was, how high its stumps stand -- without its dust or heap.
    private static Collapse Frame(ShatterPlan plan, BombBlast blast, bool grounded)
    {
        WorldPlacement placement = plan.Source.Placement;
        uint seed = blast.Seed ^ Hash(placement.TaskName) ^ Hash(placement.Instance.Name);
        var random = new IgiRandom(seed ^ 0xC011A95Eu);
        Vector3d fromPlacement = blast.CentreMetres - placement.Position;
        Vector3f blastLocal = placement.Orientation.Transform(new Vector3f((float)fromPlacement.X, (float)fromPlacement.Y, (float)fromPlacement.Z));
        return new Collapse(plan.Low, plan.High, blastLocal, placement.Orientation.Transposed(), 1.2f + (random.NextSingle() * 1.6f), grounded);
    }

    private static uint Hash(string text)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char ch in text)
            {
                h = (h ^ ch) * 16777619u;
            }

            return h;
        }
    }

    /// <summary>How one slab of a building goes.</summary>
    /// <param name="Blown">Whether the blast throws it, as it throws a prop's pieces, rather than the collapse dropping it.</param>
    /// <param name="HoldTicks">How long it stands before it goes; negative for a stump that stands for good.</param>
    /// <param name="Velocity">How it starts to move when it goes, world units a tick.</param>
    /// <param name="Spin">How it starts to turn, radians a tick about world axes.</param>
    private readonly record struct CollapseMotion(bool Blown, int HoldTicks, Vector3d Velocity, Vector3d Spin);

    /// <summary>A building coming down, in its own mesh's space: its box, where the blast was and how high its stumps stand.</summary>
    private sealed class Collapse(Vector3f low, Vector3f high, Vector3f blastLocal, Matrix3f localToWorld, float stumpMetres, bool grounded)
    {
        private readonly Vector3f _middle = (low + high) * 0.5f;

        // How one of its slabs goes: blown out if the blast was right at it; otherwise left standing as a stump, or held a
        // moment and then dropped (a roof, a floor) or toppled outward (a wall), the far side of it last.
        public CollapseMotion MotionOf(FractureChunk chunk, List<Vertex3D> vertices, float toBlast, BombBlast blast, IgiRandom random)
        {
            if (toBlast < MathF.Max(blast.RadiusMetres * 0.22f, 3f))
            {
                return new CollapseMotion(true, 0, default, default);
            }

            Vector3f pieceLow = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3f pieceHigh = new(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vertex3D v in vertices)
            {
                Vector3f p = v.Position + chunk.Centre;
                pieceLow = new Vector3f(MathF.Min(pieceLow.X, p.X), MathF.Min(pieceLow.Y, p.Y), MathF.Min(pieceLow.Z, p.Z));
                pieceHigh = new Vector3f(MathF.Max(pieceHigh.X, p.X), MathF.Max(pieceHigh.Y, p.Y), MathF.Max(pieceHigh.Z, p.Z));
            }

            float height = MathF.Max(high.Z - low.Z, 0.5f);
            float share = Math.Clamp((chunk.Centre.Z - low.Z) / height, 0f, 1f);
            float size = MathF.Max((high - low).Length, 1f);
            float far = BombBlast.Smooth(0f, size, toBlast);

            Vector3f outward = Flat(chunk.Centre - _middle, Flat(chunk.Centre - blastLocal, Vector3f.UnitX));
            Vector3f towardBlast = Flat(blastLocal - _middle, Vector3f.UnitX);
            bool farSide = Vector3f.Dot(outward, towardBlast) < 0.2f;
            Vector3f extent = pieceHigh - pieceLow;
            bool flat = extent.Z < 0.4f * MathF.Max(extent.X, extent.Y);

            // A stump: the foot of a wall, still standing where it stood -- never more than the lower part of it.
            float stump = MathF.Min(stumpMetres, height * 0.4f);
            bool foot = grounded && !flat && pieceLow.Z - low.Z < 0.4f && pieceHigh.Z - low.Z < stump;
            if (foot && random.NextSingle() < (farSide ? 0.9f : 0.45f))
            {
                return new CollapseMotion(false, -1, default, default);
            }

            Vector3f outwardWorld = localToWorld.Transform(outward);
            Vector3f awayWorld = localToWorld.Transform(Flat(chunk.Centre - blastLocal, outward));
            double toUnits = UnitsPerMetre / 30.0;
            if (flat)
            {
                // A roof or a floor: it drops, first, nearly straight, a little shoved off the blast.
                int hold = (int)(3f + (10f * far) + (6f * random.NextSingle()));
                Vector3d velocity = new Vector3d(
                    ((random.NextSingle() - 0.5f) * 0.8f) + (awayWorld.X * (1f - far) * 1.5f),
                    ((random.NextSingle() - 0.5f) * 0.8f) + (awayWorld.Y * (1f - far) * 1.5f),
                    -0.5f - (random.NextSingle() * 1.2f)) * toUnits;
                Vector3d spin = new Vector3d(random.NextSingle() - 0.5, random.NextSingle() - 0.5, (random.NextSingle() - 0.5) * 0.3) * 0.05;
                return new CollapseMotion(false, hold, velocity, spin);
            }

            // A wall: held as long as what it carries, the far side and the foot last, then tipped outward over its foot.
            int wait = Math.Max(2, (int)(8f + (22f * far) + (10f * random.NextSingle()) - (6f * share)));
            float push = 0.6f + (random.NextSingle() * 1.8f);
            float shove = (1f - far) * 3f;
            Vector3d wallVelocity = new Vector3d(
                (outwardWorld.X * push) + (awayWorld.X * shove),
                (outwardWorld.Y * push) + (awayWorld.Y * shove),
                0.2f) * toUnits;
            float tip = (0.025f + (0.05f * random.NextSingle())) * (0.5f + share);
            Vector3f axis = Vector3f.Cross(Vector3f.UnitZ, outwardWorld);
            Vector3d wallSpin = new Vector3d(axis.X, axis.Y, axis.Z) * tip
                + (new Vector3d(random.NextSingle() - 0.5, random.NextSingle() - 0.5, random.NextSingle() - 0.5) * 0.015);
            return new CollapseMotion(false, wait, wallVelocity, wallSpin);
        }

        // A direction flattened onto the ground and made unit, or a fallback where it has none.
        private static Vector3f Flat(Vector3f v, Vector3f fallback)
        {
            Vector3f flat = new(v.X, v.Y, 0f);
            return flat.LengthSquared > 1e-6f ? flat.Normalized() : fallback;
        }
    }
}

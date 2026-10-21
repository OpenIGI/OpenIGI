using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Turns impact particles into the camera-relative quads <see cref="ImpactParticleRenderer"/> streams
/// through its one shared mesh: velocity-stretched spark streaks, soft billboards and glass shards.
/// </summary>
/// <remarks>
/// Owns the vertex array and how much of it this frame has filled. Every emitted quad is
/// finite-checked; a particle that cannot be drawn is dropped rather than stamped across the screen.
/// </remarks>
internal sealed class ParticleQuadBuilder
{
    private const int MaximumVertices = ImpactParticleRenderer.MaximumParticles * 4;
    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private int _used;

    /// <summary>Gets the vertex array the quads are written into.</summary>
    internal Vertex3D[] Vertices => _vertices;

    /// <summary>Gets how many vertices this frame has written.</summary>
    internal int Used => _used;

    /// <summary>Starts a new frame's quads at the front of the array.</summary>
    internal void Reset() => _used = 0;

    /// <summary>Adds a glass shard: its own triangle, translucent and fading with age.</summary>
    internal void AddGlassShardQuad(ref ImpactParticle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d centreUnits = Vector3d.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d a = ((centreUnits + p.GlassA) / WorldScene.UnitsPerMetre) - eye;
        Vector3d b = ((centreUnits + p.GlassB) / WorldScene.UnitsPerMetre) - eye;
        Vector3d c = ((centreUnits + p.GlassC) / WorldScene.UnitsPerMetre) - eye;
        Vector3f normal = new((float)p.GlassNormal.X, (float)p.GlassNormal.Y, (float)p.GlassNormal.Z);
        float opacity = MathF.Min(0.9f, life * 0.9f)
            * NearFade(Vector3d.Distance(centreUnits / WorldScene.UnitsPerMetre, eye));
        Bgra32 colour = Tint(0.72f * opacity, 0.92f * opacity, opacity, opacity);

        Vector3f af = new((float)a.X, (float)a.Y, (float)a.Z);
        Vector3f bf = new((float)b.X, (float)b.Y, (float)b.Z);
        Vector3f cf = new((float)c.X, (float)c.Y, (float)c.Z);
        if (!FiniteChecks.IsFinite(af) || !FiniteChecks.IsFinite(bf) || !FiniteChecks.IsFinite(cf) || !FiniteChecks.IsFinite(normal))
        {
            return;
        }

        _vertices[_used] = new Vertex3D(af, normal, 0f, 0f, colour: colour);
        _vertices[_used + 1] = new Vertex3D(bf, normal, 1f, 0f, colour: colour);
        _vertices[_used + 2] = new Vertex3D(cf, normal, 0.5f, 1f, colour: colour);
        _vertices[_used + 3] = new Vertex3D(af, normal, 0f, 0f, colour: colour);
        _used += 4;
    }

    /// <summary>Adds a dust puff as a soft billboard that thins as it ages.</summary>
    internal void AddDustQuad(ref ImpactParticle p, Vector3d eye, float alpha)
    {
        // Thins as it ages and as it grows -- a plume spreads its matter, it does not mint more.
        // The 0.55 is the third tuning of this number: 0.5 read as a hint, 0.7 as a smoke grenade.
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        float opacity = life * life * 0.55f;
        AddBillboard(ref p, eye, alpha, p.Size, opacity);
    }

    /// <summary>Adds a retail fireball or smoke sprite at its current strip frame.</summary>
    internal void AddRetailSpriteQuad(ref ImpactParticle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        float strength = p.IsRetailSmoke ? life * 0.75f : life;
        AddBillboard(
            ref p,
            eye,
            alpha,
            p.Size,
            strength,
            p.RetailU0,
            p.RetailV0,
            p.RetailU1,
            p.RetailV1);
    }

    /// <summary>Adds a spark as a streak stretched along its travel, or a dot when seen head-on.</summary>
    internal void AddSparkQuad(ref ImpactParticle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);

        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d head = Vector3d.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d headMetres = (head / WorldScene.UnitsPerMetre) - eye;

        double distance = headMetres.Length;
        float nearFade = NearFade(distance);
        if (!(nearFade > 0f))
        {
            return;
        }

        // Stretched along a third of a tick's travel, exactly the tracer's streak trick: a fast
        // spark reads as a line of light, a dying one collapses to a point.
        Vector3d tailMetres = new(
            headMetres.X - (p.Velocity.X * 0.35 / UnitsPerMetre),
            headMetres.Y - (p.Velocity.Y * 0.35 / UnitsPerMetre),
            headMetres.Z - (p.Velocity.Z * 0.35 / UnitsPerMetre));

        Vector3d along = tailMetres - headMetres;
        Vector3d acrossD = new(
            (along.Y * headMetres.Z) - (along.Z * headMetres.Y),
            (along.Z * headMetres.X) - (along.X * headMetres.Z),
            (along.X * headMetres.Y) - (along.Y * headMetres.X));
        double acrossLength = acrossD.Length;

        float strength = life * nearFade;
        Bgra32 headColour = Tint(p.Red * strength, p.Green * strength, p.Blue * strength, strength);
        Bgra32 tailColour = Tint(
            p.Red * strength * 0.4f, p.Green * strength * 0.4f, p.Blue * strength * 0.4f, strength);

        if (!(acrossLength > 1e-9))
        {
            // Head-on: the streak has no width axis, so it is a dot this frame.
            AddBillboard(ref p, eye, alpha, p.Size * 1.5f, strength);
            return;
        }

        acrossD /= acrossLength;
        float half = (p.Size * 0.5f) + ((float)distance * ImpactParticleRenderer.SizeBoostPerMetre);
        Vector3f across = new(
            (float)(acrossD.X * half), (float)(acrossD.Y * half), (float)(acrossD.Z * half));
        Vector3f headF = new((float)headMetres.X, (float)headMetres.Y, (float)headMetres.Z);
        Vector3f tailF = new((float)tailMetres.X, (float)tailMetres.Y, (float)tailMetres.Z);
        Vector3f normal = new(
            (float)(-headMetres.X / distance), (float)(-headMetres.Y / distance), (float)(-headMetres.Z / distance));

        if (!FiniteChecks.IsFinite(headF) || !FiniteChecks.IsFinite(tailF) || !FiniteChecks.IsFinite(across) || !FiniteChecks.IsFinite(normal))
        {
            return;
        }

        _vertices[_used] = new Vertex3D(headF + across, normal, 0.5f, 0f, colour: headColour);
        _vertices[_used + 1] = new Vertex3D(headF - across, normal, 0.5f, 1f, colour: headColour);
        _vertices[_used + 2] = new Vertex3D(tailF - across, normal, 1f, 1f, colour: tailColour);
        _vertices[_used + 3] = new Vertex3D(tailF + across, normal, 1f, 0f, colour: tailColour);
        _used += 4;
    }

    private void AddBillboard(
        ref ImpactParticle p,
        Vector3d eye,
        float alpha,
        float sizeMetres,
        float strength,
        float u0 = 0f,
        float v0 = 0f,
        float u1 = 1f,
        float v1 = 1f)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d position = Vector3d.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d centre = (position / WorldScene.UnitsPerMetre) - eye;
        double distance = centre.Length;
        float nearFade = NearFade(distance);
        if (!(distance > 1e-6) || !(nearFade > 0f))
        {
            return;
        }

        strength *= nearFade;

        Vector3d toEye = centre / -distance;
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = new(
            (reference.Y * toEye.Z) - (reference.Z * toEye.Y),
            (reference.Z * toEye.X) - (reference.X * toEye.Z),
            (reference.X * toEye.Y) - (reference.Y * toEye.X));
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return;
        }

        rightD /= rightLength;
        Vector3d upD = new(
            (toEye.Y * rightD.Z) - (toEye.Z * rightD.Y),
            (toEye.Z * rightD.X) - (toEye.X * rightD.Z),
            (toEye.X * rightD.Y) - (toEye.Y * rightD.X));

        float half = (sizeMetres * 0.5f) + ((float)distance * ImpactParticleRenderer.SizeBoostPerMetre);
        Vector3f right = new((float)(rightD.X * half), (float)(rightD.Y * half), (float)(rightD.Z * half));
        Vector3f up = new((float)(upD.X * half), (float)(upD.Y * half), (float)(upD.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        if (!FiniteChecks.IsFinite(centreF) || !FiniteChecks.IsFinite(right) || !FiniteChecks.IsFinite(up) || !FiniteChecks.IsFinite(normal))
        {
            return;
        }

        Bgra32 colour = Tint(p.Red * strength, p.Green * strength, p.Blue * strength, strength);

        _vertices[_used] = new Vertex3D(centreF - right - up, normal, u0, v0, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, u1, v0, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, u1, v1, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, u0, v1, colour: colour);
        _used += 4;
    }

    private static float NearFade(double distanceMetres) =>
        Math.Clamp(((float)distanceMetres - ImpactParticleRenderer.NearCullMetres) / ImpactParticleRenderer.NearFadeMetres, 0f, 1f);

    private static Bgra32 Tint(float r, float g, float b, float a)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

        // Alpha floored at one: Vertex3D reads an all-zero colour as "no tint" and substitutes
        // white, which a faded particle must never round to.
        return new Bgra32(Channel(b), Channel(g), Channel(r), Math.Max((byte)1, Channel(a)));
    }
}

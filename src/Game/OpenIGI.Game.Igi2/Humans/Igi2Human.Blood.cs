using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>A blood stain left on a surface, <c>CreateBulletHole</c> with a blood texture.</summary>
/// <param name="Struck">The surface.</param>
/// <param name="Intersection">Where on it.</param>
/// <param name="Texture">The stain's texture.</param>
/// <param name="Radius">The stain's radius, in world units.</param>
public readonly record struct Igi2BloodStain(IIgi2Collider Struck, Igi2Intersection Intersection, string Texture, float Radius);

/// <summary>Blood: the spurt where a round enters flesh and the stains it leaves, <c>HumanDamage_CreateFleshEnterEffect</c>.</summary>
public abstract partial class Igi2Human
{
    /// <summary>The sprite of the blood mist at the wound, <c>blood1.spr</c>.</summary>
    public const string BloodMistSprite = "blood1.spr";

    /// <summary>The sprite of the flying blood drops, <c>blood2.spr</c>.</summary>
    public const string BloodDropSprite = "blood2.spr";

    /// <summary>How far behind the wound a stain is looked for: three metres.</summary>
    private const double BloodStainReach = 12288.0;

    /// <summary>The stains a wound may leave behind it, <c>hole_blood1.tex</c> and <c>hole_blood2.tex</c>.</summary>
    private static readonly string[] WoundStains = ["hole_blood1.tex", "hole_blood2.tex"];

    /// <summary>The stains a flying drop may leave, <c>hole_blood3.tex</c> (even draw) and <c>hole_blood4.tex</c> (odd draw).</summary>
    private static readonly string[] DropStains = ["hole_blood3.tex", "hole_blood4.tex"];

    private readonly Igi2LineTrace _stainTrace = new(1) { Includes = static owner => owner is not Igi2Human };

    private Igi2Particle2DSystem? _bloodMist;
    private Igi2Particle2DSystem? _bloodDrops;

    /// <summary>
    /// Blood where a round goes into the body: a stain on what lies three metres beyond the wound, a mist at the wound, a cone of
    /// drops thrown forward along the round and a wider spray thrown back, <c>HumanDamage_CreateFleshEnterEffect</c>.
    /// </summary>
    /// <param name="direction">The round's direction.</param>
    /// <param name="position">The wound.</param>
    /// <param name="wounded">The human hurt, whose light tints the mist; <see langword="null"/> when no human was.</param>
    /// <remarks>
    /// <para>
    /// Nothing happens with blood turned off in the options. Otherwise the run stream is drawn from: for a stain, the size and
    /// which of two textures (only when the line beyond the wound ends in a material whose bullets leave a penetrating hole);
    /// then a count of 12 to 14 drops forward and one draw for each one's spin; then 16 to 18 drops back, and a draw each. The
    /// drops' directions come from the particle module's own stream.
    /// </para>
    /// <para>
    /// Each human keeps one mist and one spray system, made anew once the last has burnt out. The spray's drops leave stains
    /// where they land (<see cref="StainWhereDropLands"/>).
    /// </para>
    /// </remarks>
    public void CreateFleshEnterEffect(Vector3f direction, Vector3d position, Igi2Human? wounded)
    {
        if (!Surroundings.IsBlood || Surroundings.Particles is not { } particles)
        {
            return;
        }

        Vector3f along = Unit(direction);
        _stainTrace.Starts[0] = position;
        _stainTrace.Ends[0] = new Vector3d(
            (((Fpu)along.X * BloodStainReach) + position.X).Value,
            (((Fpu)along.Y * BloodStainReach) + position.Y).Value,
            (((Fpu)along.Z * BloodStainReach) + position.Z).Value);
        Igi2Intersection beyond = _stainTrace.Intersections[0];
        beyond.IsHit = false;
        beyond.HitObject = null;
        Surroundings.Collision.TraceLines(_stainTrace, Igi2CollisionWorld.EveryTypeGroup);
        if (beyond.IsHit && beyond.EnterMaterial is int enter && beyond.LeaveMaterial is null
            && Surroundings.Material(enter)?.BulletEnterEffect.Parameters is { PenetratingBulletHole.Length: > 0 })
        {
            float radius = (((Fpu)Random.NextDouble() + 1.0) * 614.4000244140625f).F;
            string stain = WoundStains[Random.NextBounded(2)];
            if (beyond.HitObject is { } struck && Igi2BulletHoles.Takes(struck))
            {
                Surroundings.MarkBloodStain(new Igi2BloodStain(struck, beyond, stain, radius));
            }
        }

        // The mist takes the wounded body's light: up to 128 grey.
        float grey = 127f;
        if (wounded is not null)
        {
            Fpu lit = (Fpu)wounded.Lightmap * 128.0;
            grey = lit.Value >= 128.0 ? 128f : lit.F;
        }

        if (_bloodMist is not { IsDeleted: false })
        {
            _bloodMist = particles.New(
                BloodMistSprite, animated: true, frames: 0, acceleration: default, color: new Vector3f(grey, grey, grey),
                width: 1228.800048828125f, height: 1228.800048828125f, range: 409600f, transparency: 0, animationDelta: 0,
                sizeDelta: 0f, shininess: 0f, maxParticles: 2, onCollision: null);
        }

        _bloodMist.AddParticle(position, default, intensity: 1f, rotationDelta: 0f, rotation: 0f, size: 1f, lifeTime: 15, fadeTime: 0);

        if (_bloodDrops is not { IsDeleted: false })
        {
            _bloodDrops = particles.New(
                BloodDropSprite, animated: false, frames: 0, acceleration: new Vector3f(0f, 0f, -14.866963386535645f),
                color: new Vector3f(grey, grey, grey), width: 143.36000061035156f, height: 143.36000061035156f, range: 409600f,
                transparency: 0x80000, animationDelta: 0, sizeDelta: 0f, shininess: 0f, maxParticles: 20, onCollision: StainWhereDropLands);
        }

        int forward = (int)(Random.NextRaw() % 3) + 12;
        for (int i = 0; i < forward; i++)
        {
            _bloodDrops.AddConeParticle(position, along, 0.7853981852531433f, 379f, 606f, 1f, DropSpin(), 0f, 1f, 30, 22);
        }

        Vector3f back = Unit(direction);
        back = new Vector3f(-back.X, -back.Y, -back.Z);
        int backward = (int)(Random.NextRaw() % 3) + 16;
        for (int i = 0; i < backward; i++)
        {
            _bloodDrops.AddConeParticle(position, back, 3.1415927410125732f, 37f, 113f, 1f, DropSpin(), 0f, 1f, 30, 22);
        }
    }

    /// <summary>
    /// A blood drop meeting a surface that takes marks, while it moves into the surface, stains it: a size and a texture from the
    /// run stream, <c>HumanDamage_BloodParticle2DCollisionCallback</c>.
    /// </summary>
    /// <param name="landing">Where the drop met the surface.</param>
    private void StainWhereDropLands(Igi2ParticleCollision landing)
    {
        Igi2Intersection hit = landing.Intersection;
        if (hit.HitObject is not { } struck || !Igi2BulletHoles.Takes(struck))
        {
            return;
        }

        Fpu dx = (Fpu)landing.Start.X - landing.End.X, dy = (Fpu)landing.Start.Y - landing.End.Y, dz = (Fpu)landing.Start.Z - landing.End.Z;
        Vector3f n = hit.Normal;
        if (n.X != 0f || n.Y != 0f || n.Z != 0f)
        {
            n = Unit(n);
            hit.Normal = n;
        }

        if (!((((dz * n.Z) + (dy * n.Y)) + (dx * n.X)).Value < 0.0))
        {
            return;
        }

        float radius = (((Fpu)Random.NextDouble() * 204.8000030517578f) + 102.4000015258789f).F;
        string stain = DropStains[Random.NextRaw() & 1];
        Surroundings.MarkBloodStain(new Igi2BloodStain(struck, hit, stain, radius));
    }

    /// <summary>A drop's spin: about a degree and a half either side of a small turn, from one run-stream draw used twice.</summary>
    private float DropSpin()
    {
        Fpu share = (Fpu)(double)(ushort)Random.NextRaw() * IgiRandom.Scale;
        return (((((share - 0.5) + share) - 0.5) * 0.05235987901687622) + 0.012799081392586231).F;
    }

    /// <summary>A vector scaled by the reciprocal of its length, or left as it is when zero.</summary>
    private static Vector3f Unit(Vector3f v)
    {
        if (v.X == 0f && v.Y == 0f && v.Z == 0f)
        {
            return v;
        }

        Fpu length = Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z));
        Fpu inverse = (Fpu)1.0 / length;
        return new Vector3f(((Fpu)v.X * inverse).F, ((Fpu)v.Y * inverse).F, ((Fpu)v.Z * inverse).F);
    }
}

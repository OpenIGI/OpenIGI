using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.World.Particles;

/// <summary>
/// A system of 2-D particles, <c>Particle2DDynCubeObj</c>: the mist and drops a wound throws, the puff a round kicks off a wall,
/// smoke, a rocket's trail. Both titles' engines run the same code (Project I.G.I. <c>0x4FDF00</c> makes one and
/// <c>0x4FD660</c> steps it; I.G.I. 2's <c>Particle2DDynCubeObj_New</c> and <c>_RunHandler</c>), in the original's x87 arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// The particles sit in slots; one that dies swaps places with the last live one, so the order changes as they die, as the
/// original's pointer table does. A system that is empty when it runs deletes itself (it is self-destructing from creation).
/// </para>
/// <para>
/// Each tick a particle gains the system's acceleration, moves by its speed, turns by its spin and grows by the system's size
/// delta; an animated system's particle steps its frame. Then its life ticks down, or, once spent, its intensity fades; it dies
/// at no intensity or no size.
/// </para>
/// <para>
/// A system with a collider checks each live particle every fifteen ticks, in a phase given when it is added: its countdown is
/// taken down, and at nought a line from where it was last checked to where it is joins the tick's batch and the countdown
/// reloads. The batch is traced at once, each strike answered, and its particle dies. The particle that dies is the one the line
/// was made for <i>by its index when the lines were made</i>: an earlier death this pass may have swapped another particle into
/// that slot, and then that one dies instead, as in the original.
/// </para>
/// </remarks>
public sealed class Particle2DSystem
{
    private readonly Particle2DModule _module;
    private readonly Particle2D[] _slots;
    private readonly Vector3d[] _starts;
    private readonly Vector3d[] _ends;
    private readonly bool[] _struck;
    private readonly int[] _lineParticle;

    internal Particle2DSystem(Particle2DModule module, int maxParticles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParticles);
        _module = module;
        _slots = new Particle2D[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            _slots[i] = new Particle2D();
        }

        _starts = new Vector3d[maxParticles];
        _ends = new Vector3d[maxParticles];
        _struck = new bool[maxParticles];
        _lineParticle = new int[maxParticles];
    }

    /// <summary>Gets the live particles, in slot order.</summary>
    public ArraySegment<Particle2D> Particles => new(_slots, 0, Count);

    /// <summary>Gets how many particles are alive.</summary>
    public int Count { get; private set; }

    /// <summary>Gets how many particles the system holds at most.</summary>
    public int MaxParticles => _slots.Length;

    /// <summary>Gets a value indicating whether the system has deleted itself.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Gets or sets a value indicating whether the system deletes itself once empty (from creation, yes).</summary>
    public bool IsSelfDestruct { get; set; } = true;

    /// <summary>Gets or sets how its particles are drawn.</summary>
    public required Particle2DLook Look { get; set; }

    /// <summary>Gets or sets what is added to each particle's speed each tick.</summary>
    public Vector3f Acceleration { get; set; }

    /// <summary>Gets or sets what each particle's size grows by each tick; a system that grows draws its particles by their size.</summary>
    public float SizeDelta { get; set; }

    /// <summary>Gets or sets what its particles meet, or <see langword="null"/> when they pass through.</summary>
    public IParticleCollider? Collider { get; set; }

    /// <summary>Adds a particle, unless the system is full, <c>Particle2DDynCubeObj_AddParticle</c>.</summary>
    /// <param name="position">Where it starts.</param>
    /// <param name="speed">Its speed, per tick.</param>
    /// <param name="intensity">Its intensity: its alpha, or how bright its colour is.</param>
    /// <param name="rotationDelta">What its rotation turns by each tick.</param>
    /// <param name="rotation">Its rotation.</param>
    /// <param name="size">Its size.</param>
    /// <param name="lifeTime">The ticks it lives before it fades.</param>
    /// <param name="fadeTime">The ticks it fades over; none fades it at once.</param>
    /// <returns>Whether it was added.</returns>
    public bool AddParticle(Vector3d position, Vector3f speed, float intensity, float rotationDelta, float rotation, float size, int lifeTime, int fadeTime)
    {
        if (Count >= _slots.Length)
        {
            return false;
        }

        Particle2D particle = _slots[Count];
        particle.Position = position;
        particle.LastCheckedPosition = position;
        particle.Speed = speed;
        particle.CollisionCountdown = (short)_module.TakeCollisionPhase();
        particle.LifeTime = (short)lifeTime;
        particle.Intensity = intensity;
        particle.Rotation = rotation;
        particle.Size = size;
        particle.RotationDelta = rotationDelta;
        particle.Frame = 0;
        particle.FrameTicks = 0;
        particle.Fade = fadeTime != 0 ? ((Fpu)1.0 / fadeTime).F : 1f;
        Count++;
        return true;
    }

    /// <summary>
    /// Adds a particle thrown somewhere within a cone, at a speed between two, <c>Particle2DDynCubeObj_AddConeParticle</c>: three
    /// draws of the module's own stream pick the angle off the axis, the turn around it and the speed.
    /// </summary>
    /// <param name="position">Where it starts.</param>
    /// <param name="direction">The cone's axis.</param>
    /// <param name="maxAngle">The cone's full opening angle, in radians.</param>
    /// <param name="minSpeed">The slowest it may go.</param>
    /// <param name="maxSpeed">The fastest it may go.</param>
    /// <param name="intensity">Its intensity.</param>
    /// <param name="rotationDelta">What its rotation turns by each tick.</param>
    /// <param name="rotation">Its rotation.</param>
    /// <param name="size">Its size.</param>
    /// <param name="lifeTime">The ticks it lives before it fades.</param>
    /// <param name="fadeTime">The ticks it fades over.</param>
    /// <returns>Whether it was added.</returns>
    public bool AddConeParticle(
        Vector3d position, Vector3f direction, float maxAngle, float minSpeed, float maxSpeed,
        float intensity, float rotationDelta, float rotation, float size, int lifeTime, int fadeTime)
    {
        Vector3f z = RigidBodyKernels.Normalise<Fpu>(direction);

        // A side axis from whichever of X or Y the cone's axis is further from.
        Fpu ux = 1f, uy = 0f;
        if (Math.Abs(z.X) > 0.75f)
        {
            ux = 0f;
            uy = 1f;
        }

        Vector3f side = new(
            (((Fpu)z.Z * uy) - ((Fpu)z.Y * 0f)).F,
            (-((ux * z.Z) - ((Fpu)z.X * 0f))).F,
            ((ux * z.Y) - (uy * z.X)).F);
        Vector3f y = RigidBodyKernels.Normalise<Fpu>(side);
        Vector3f x = new(
            (((Fpu)y.Y * z.Z) - ((Fpu)y.Z * z.Y)).F,
            (-(((Fpu)y.X * z.Z) - ((Fpu)y.Z * z.X))).F,
            (((Fpu)y.X * z.Y) - ((Fpu)y.Y * z.X)).F);

        IgiRandom random = _module.Random;
        Fpu off = (Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale;
        float around = ((Fpu)3.1415927410125732f - (((Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale) * 6.2831854820251465f)).F;
        float tilt = (((Fpu)maxAngle * 0.5f) - (off * maxAngle)).F;
        (float tx, float ty, float tz) = TurnedUp(tilt, around);
        Fpu speedShare = (Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale;

        // The turn's third column carried into the cone's frame.
        float vx = ((((Fpu)tx * x.X) + ((Fpu)ty * y.X)) + ((Fpu)tz * z.X)).F;
        float vy = ((((Fpu)tx * x.Y) + ((Fpu)ty * y.Y)) + ((Fpu)tz * z.Y)).F;
        float vz = ((((Fpu)tx * x.Z) + ((Fpu)ty * y.Z)) + ((Fpu)tz * z.Z)).F;
        Fpu speed = (speedShare * ((Fpu)maxSpeed - minSpeed)) + minSpeed;
        Fpu outZ = vz;
        if (vx != 0f || vy != 0f || vz != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)vx * vx) + ((Fpu)vy * vy)) + ((Fpu)vz * vz));
            vx = ((Fpu)vx * inverse).F;
            vy = ((Fpu)vy * inverse).F;
            outZ = inverse * vz;
        }

        return AddParticle(position, new Vector3f(((Fpu)vx * speed).F, ((Fpu)vy * speed).F, (outZ * speed).F), intensity, rotationDelta, rotation, size, lifeTime, fadeTime);
    }

    /// <summary>Lets every particle go at once, <c>Particle2DDynCubeObj_RemoveParticles</c>.</summary>
    public void RemoveParticles() => Count = 0;

    /// <summary>Moves every live particle by an offset, as a smoke whose source moves carries its plume (<c>Particle2DDynCubeObj_MoveParticles</c>).</summary>
    /// <param name="move">The offset.</param>
    public void MoveParticles(Vector3d move)
    {
        for (int i = 0; i < Count; i++)
        {
            Vector3d p = _slots[i].Position;
            _slots[i].Position = new Vector3d(((Fpu)p.X + move.X).Value, ((Fpu)move.Y + p.Y).Value, ((Fpu)p.Z + move.Z).Value);
        }
    }

    /// <summary>A tick of the system, <c>Particle2DDynCubeObj_RunHandler</c>.</summary>
    /// <returns><see langword="false"/> once the system has deleted itself.</returns>
    public bool Run()
    {
        if (IsDeleted)
        {
            return false;
        }

        if (Count == 0)
        {
            if (IsSelfDestruct)
            {
                IsDeleted = true;
                _module.Remove(this);
                return false;
            }

            return true;
        }

        int lastFrame = Look.IsAnimated ? Look.Frames - 1 : 0;
        int i = 0;
        while (i < Count)
        {
            Particle2D p = _slots[i];
            Vector3f a = Acceleration, v = p.Speed;
            v = new Vector3f(((Fpu)v.X + a.X).F, ((Fpu)a.Y + v.Y).F, ((Fpu)a.Z + v.Z).F);
            p.Speed = v;
            p.Position = new Vector3d(((Fpu)v.X + p.Position.X).Value, ((Fpu)v.Y + p.Position.Y).Value, ((Fpu)v.Z + p.Position.Z).Value);
            p.Rotation = ((Fpu)p.RotationDelta + p.Rotation).F;
            p.Size = ((Fpu)SizeDelta + p.Size).F;
            if (lastFrame != 0)
            {
                p.FrameTicks++;
                if (p.FrameTicks >= Look.AnimationDelta)
                {
                    p.FrameTicks = 0;
                    if (p.Frame < lastFrame)
                    {
                        p.Frame++;
                    }
                }
            }

            if (p.LifeTime != 0)
            {
                p.LifeTime--;
            }
            else
            {
                p.Intensity = ((Fpu)p.Intensity - p.Fade).F;
                if (p.Intensity <= 0f || p.Size <= 0f)
                {
                    RemoveAt(i);
                    continue;
                }
            }

            i++;
        }

        if (Collider is { } collider)
        {
            Collide(collider);
        }

        return true;
    }

    /// <summary>Lets the system go at once, as its owner deleting it does.</summary>
    public void Delete()
    {
        if (!IsDeleted)
        {
            IsDeleted = true;
            _module.Remove(this);
        }
    }

    // The third column of the turn by a tilt about X then a turn about Z, as Pose_SetRotation builds it with no roll.
    private static (float X, float Y, float Z) TurnedUp(float alpha, float gamma)
    {
        Fpu sinGamma = Fpu.Sin(gamma), cosGamma = Fpu.Cos(gamma);
        float sinBeta = Fpu.Sin(0f).F, cosBeta = Fpu.Cos(0f).F;
        float p01 = (-sinGamma).F, p02 = ((Fpu)sinBeta * cosGamma).F;
        float p11 = cosGamma.F, p12 = ((Fpu)sinBeta * sinGamma).F;
        float p22 = cosBeta;
        Fpu sinAlpha = Fpu.Sin(alpha), cosAlpha = Fpu.Cos(alpha);
        return (((cosAlpha * p02) + (-sinAlpha * p01)).F, ((cosAlpha * p12) + (-sinAlpha * p11)).F, (cosAlpha * p22).F);
    }

    private void Collide(IParticleCollider collider)
    {
        int lines = 0;
        for (int i = 0; i < Count; i++)
        {
            Particle2D p = _slots[i];
            if (p.CollisionCountdown != 0)
            {
                p.CollisionCountdown--;
            }

            if (p.CollisionCountdown == 0)
            {
                _lineParticle[lines] = i;
                _starts[lines] = p.LastCheckedPosition;
                _ends[lines] = p.Position;
                _struck[lines] = false;
                lines++;
                p.LastCheckedPosition = p.Position;
                p.CollisionCountdown = Particle2DModule.CollisionInterval;
            }
        }

        if (lines == 0)
        {
            return;
        }

        collider.Collide(_starts.AsSpan(0, lines), _ends.AsSpan(0, lines), _struck.AsSpan(0, lines));
        for (int k = 0; k < lines; k++)
        {
            if (_struck[k] && Count != 0)
            {
                RemoveAt(_lineParticle[k]);
            }
        }
    }

    private void RemoveAt(int index)
    {
        int last = Count - 1;
        if (index != last)
        {
            (_slots[index], _slots[last]) = (_slots[last], _slots[index]);
        }

        Count--;
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

/// <summary>
/// A physics object of the original that is nothing but its type: a body, its box and its contact points, stepped by the
/// shared rigid-body step (<c>0x4FC0E0</c>). The thrown grenade is one.
/// </summary>
/// <remarks>
/// Made as <c>0x4FB5F0</c> clears a physics object and <c>Body_InitFromType</c> (<c>0x4FB700</c>) fills it from its type:
/// the type's box (metres times 4096) for the mass and inertia, the inverse inertia then raised by a hundredth, and one
/// contact point for each the type defines, sitting on its magic vertex of the model, with the point's kind, material,
/// bounce and frictions. It collides with the world (<c>+1752</c>) and is not held level.
/// </remarks>
public sealed class RetailPhysicsObjectBody : RetailRigidBody
{
    /// <summary>What the grenade's speed keeps each tick in the air, the float at <c>0x534144</c>.</summary>
    public const float GrenadeAirDrag = 0.985f;

    /// <summary>What the grenade's angular momentum keeps each step (<c>0x476D90</c>'s third argument).</summary>
    public const float GrenadeSpinDrag = 0.92f;

    private readonly PhysicsObjectType _type;

    private RetailPhysicsObjectBody(PhysicsObjectType type) => _type = type;

    /// <summary>Gets the type it was made from.</summary>
    public PhysicsObjectType Type => _type;

    /// <inheritdoc/>
    protected override float TypeMass => _type.Mass;

    /// <inheritdoc/>
    protected override float SleepSpeed => _type.RestSpeed;

    /// <inheritdoc/>
    protected override float SleepSpin => _type.RestAngularVelocitySquared;

    /// <inheritdoc/>
    protected override Wing[] AeroSurfaces => [];

    /// <summary>Makes a body of a type at a pose and speed, as a thrown grenade is made (<c>0x477040</c>).</summary>
    /// <param name="type">Its type.</param>
    /// <param name="model">Its model, for the contact points' magic vertices and its radius.</param>
    /// <param name="position">Where it starts, world units.</param>
    /// <param name="orientation">Its orientation.</param>
    /// <param name="velocity">Its speed, units a tick.</param>
    /// <returns>The body.</returns>
    public static RetailPhysicsObjectBody Create(PhysicsObjectType type, MefModel? model, Vector3d position, Matrix3f orientation, Vector3f velocity)
    {
        ArgumentNullException.ThrowIfNull(type);
        RetailPhysicsObjectBody body = new(type);
        body.Dimensions[0] = body.Dimensions[1] = body.Dimensions[2] = 1.0f;
        body.ModelRadius = model?.NativeMarkerBound ?? 1.0f;
        body.RunCollide = true;
        body.Planar = false;

        // 0x4FB700: the box from the type's edges in metres, the inertia a hundredth stiffer, the points from the model.
        float z = ((X87)type.Size.Z * 4096.0f).F;
        float y = ((X87)type.Size.Y * 4096.0f).F;
        float x = ((X87)type.Size.X * 4096.0f).F;
        body.SetBox(type.Mass, x, y, z);
        for (int k = 0; k < 3; k++)
        {
            body.InverseInertia[k] = ((X87)body.InverseInertia[k] * 1.00999999f).F;
        }

        int count = Math.Clamp(type.ContactPoints.Count, 0, PointCount);
        body.CollideEnabled = type.ContactPoints.Count;
        for (int p = 0; p < count; p++)
        {
            PhysicsContactPointType defined = type.ContactPoints[p];
            RetailCarPoint point = body.Points[p];
            point.Side = defined.Type;
            point.Restitution = defined.Bounce;
            point.Collides = true;
            point.IsCollisionPoint = true;
            point.Contact = false;
            point.FrictionX = defined.FrictionX;
            point.FrictionY = defined.FrictionY;
            point.Compression = 0f;
            if (model is not null && model.TryGetMagicVertex(defined.MagicVertex, out Vector3f v))
            {
                point.Position[0] = ((X87)body.Dimensions[0] * v.X).F;
                point.Position[1] = ((X87)body.Dimensions[1] * v.Y).F;
                point.Position[2] = ((X87)body.Dimensions[2] * v.Z).F;
            }
        }

        // 0x477040: placed, its orientation and its speed set, the speed's estimate refreshed.
        body.Position[0] = position.X;
        body.Position[1] = position.Y;
        body.Position[2] = position.Z;
        float largest = Math.Max(body.Dimensions[0], Math.Max(body.Dimensions[1], body.Dimensions[2]));
        body.Radius = ((X87)body.ModelRadius * largest).F;
        orientation.CopyTo(body.M);
        body.MatrixCounter = 0;
        body.Velocity[0] = velocity.X;
        body.Velocity[1] = velocity.Y;
        body.Velocity[2] = velocity.Z;
        body.UpdateSpeed();
        Array.Copy(body.Position, body.PreviousPosition, 3);
        Array.Copy(body.M, body.PreviousM, 9);
        body.PreviousMatrixCounter = body.MatrixCounter;
        body.DeltaM[0] = body.DeltaM[4] = body.DeltaM[8] = 1.0f;
        return body;
    }

    /// <summary>
    /// Moves a thrown grenade a tick (<c>0x476D90</c>): unless it may rest, the shared step with the original's gravity, no
    /// air damping and 0.92 of its spin kept; then, touching nothing, its speed keeps <see cref="GrenadeAirDrag"/>.
    /// </summary>
    /// <param name="world">What its contact points' rays meet.</param>
    public void StepGrenade(IRetailCarWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (IsAsleep())
        {
            return;
        }

        Step(world, Gravity, 1.0f, GrenadeSpinDrag);
        if (ContactCount != 0)
        {
            return;
        }

        for (int k = 0; k < 3; k++)
        {
            Velocity[k] = ((X87)Velocity[k] * GrenadeAirDrag).F;
        }

        UpdateSpeed();
    }

    /// <summary>Gets the orientation as a matrix, row by row as the body keeps it.</summary>
    /// <returns>The orientation.</returns>
    public Matrix3f Orientation() => new(M[0], M[1], M[2], M[3], M[4], M[5], M[6], M[7], M[8]);
}

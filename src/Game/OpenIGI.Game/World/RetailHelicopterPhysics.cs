using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

/// <summary>
/// Ports the retail Heli live update (0x431B70), its force helper (0x431E30), Rotor::Tick
/// (0x42D440), and the force accumulator at 0x4EC6A0.
/// Forces and application points are body-local; the accumulator converts linear acceleration to
/// world space with the pre-integration attitude. Retail damps angular momentum by 0.92 after the
/// quaternion step and deliberately does not damp linear velocity.
/// </summary>
internal static class RetailHelicopterPhysics
{
    internal static void Apply(
        CutsceneTransformState transform,
        HelicopterPhysicsDefinition physics)
    {
        const float Gravity = 44.600887f;
        const float AngularDamping = 0.92000002f;
        Matrix3f attitude = transform.Orientation;

        // The airborne stabilizer right-multiplies a small local-X correction before forces are
        // accumulated. The first mover call initializes the body and does not execute this branch.
        if (transform.PhysicsTicks > 0)
        {
            Vector3f horizontalForward = new(attitude.M11, attitude.M21, 0f);
            float horizontalLength = horizontalForward.Length;
            if (horizontalLength != 0f)
            {
                float bankError = ((horizontalForward.X / horizontalLength) * attitude.M13)
                    + ((horizontalForward.Y / horizontalLength) * attitude.M23);
                float correction = -bankError * 0.7f * physics.Torque.Z * 0.0002908882086657217f;
                attitude *= Matrix3f.RotationX(correction);
            }
        }

        float mass = physics.Mass;
        Vector3f force = default;
        Vector3f torque = default;
        void AddForce(Vector3f value, Vector3f point)
        {
            force += value;
            torque += Vector3f.Cross(point, value);
        }

        // Rotor::Tick runs after Heli::Tick and appends its force records to the body accumulator.
        // Consume the records produced at the end of the preceding scheduler tick before adding the
        // body/control forces for this tick. Applying lift immediately used the newly levelled attitude
        // one frame too early and made the Mission 1 flyby gain too much forward and vertical speed.
        force += transform.QueuedRotorForce;
        torque += transform.QueuedRotorTorque;
        transform.QueuedRotorForce = default;
        transform.QueuedRotorTorque = default;

        if (transform.Control != default)
        {
            AddForce(
                new Vector3f(0f, transform.Control.X * physics.Torque.X * mass * -0.8f, 0f),
                new Vector3f(0f, 0f, 819.20001f));
            AddForce(
                new Vector3f(transform.Control.Y * physics.Torque.Y * mass, 0f, 0f),
                new Vector3f(0f, 0f, 409.600006f));
            AddForce(
                new Vector3f(
                    transform.Control.Z * physics.Torque.Z * transform.Thrust * mass * 0.079999998f,
                    0f,
                    0f),
                new Vector3f(0f, 12288f, 0f));
        }

        // Heli passes -worldVelocity through the current attitude into three authored local drag
        // samples. Each contributes direction * dot(normalizedAir, direction) * speed^2 * coefficient.
        // The Bell record is the captured (20 X, 3 Y, 50 Z) triplet; its modest quadratic resistance
        // is already measurable by tick 30 and prevents the long flyby from running hundreds of metres
        // ahead of its linked camera.
        Vector3f airWorld = new(
            (float)-transform.Velocity.X,
            (float)-transform.Velocity.Y,
            (float)-transform.Velocity.Z);
        Vector3f localAir = attitude.Transform(airWorld);
        float localSpeedSquared = localAir.LengthSquared;
        if (localSpeedSquared > 0f)
        {
            Vector3f airDirection = localAir / MathF.Sqrt(localSpeedSquared);
            float dynamicPressure = localSpeedSquared * 0.00032699999f;
            foreach (Wing surface in physics.Wings)
            {
                float magnitude = Vector3f.Dot(airDirection, surface.Normal)
                    * surface.Area
                    * dynamicPressure;
                AddForce(surface.Normal * magnitude, surface.Position);
            }
        }

        Vector3f localAcceleration = force / mass;
        Vector3f worldAcceleration = new(
            Vector3f.Dot(attitude.Row1, localAcceleration),
            Vector3f.Dot(attitude.Row2, localAcceleration),
            Vector3f.Dot(attitude.Row3, localAcceleration) - Gravity);
        // The rigid accumulator stores world velocity as three Real32 values at +264. Position is
        // Real64, but each step adds the already-rounded velocity. Keeping velocity as an unrounded
        // Vector3d slowly pushes linked cameras metres away over a long cutscene even when every force
        // and orientation matches retail.
        transform.Velocity = new Vector3d(
            (float)(transform.Velocity.X + worldAcceleration.X),
            (float)(transform.Velocity.Y + worldAcceleration.Y),
            (float)(transform.Velocity.Z + worldAcceleration.Z));

        transform.AngularMomentum += torque;
        Vector3f dimensions = physics.Dimensions * (float)WorldScene.UnitsPerMetre;
        float inverseIx = InverseBoxInertia(mass, dimensions.Y, dimensions.Z);
        float inverseIy = InverseBoxInertia(mass, dimensions.X, dimensions.Z);
        float inverseIz = InverseBoxInertia(mass, dimensions.X, dimensions.Y);
        Vector3f localAngularRate = transform.AngularMomentum
            * new Vector3f(inverseIx, inverseIy, inverseIz);
        Vector3f worldAngularRate = new(
            Vector3f.Dot(attitude.Row1, localAngularRate),
            Vector3f.Dot(attitude.Row2, localAngularRate),
            Vector3f.Dot(attitude.Row3, localAngularRate));
        if (worldAngularRate != default)
        {
            transform.Orientation = IntegrateOrientation(attitude, worldAngularRate);
        }
        else
        {
            transform.Orientation = attitude;
        }
        transform.AngularMomentum *= AngularDamping;

        // Child rotor tasks now observe the post-integration attitude and queue local force records for
        // the next Heli::Tick, matching the retail task traversal order.
        QueueRotorForces(transform, transform.Orientation, physics);

        static float InverseBoxInertia(float bodyMass, float sideA, float sideB)
        {
            float inertia = bodyMass * ((sideA * sideA) + (sideB * sideB)) * (1f / 12f);
            return inertia == 0f ? 0f : (1f / inertia) * 1.01f;
        }
    }

    internal static void QueueRotorForces(
        CutsceneTransformState transform,
        Matrix3f attitude,
        HelicopterPhysicsDefinition physics)
    {
        const float Gravity = 44.600887f;
        float lift = physics.Mass * transform.Thrust * Gravity;
        if (lift != 0f)
        {
            // Rotor::Tick (0x42D440) takes the angle between the horizontal projection of column two
            // and column three, then stores cos(angle) * lift in local Y and lift in local Z.
            Vector3f horizontalRotor = new(attitude.M12, attitude.M22, 0f);
            Vector3f bodyUp = attitude.Column3;
            float rotorTiltCosine = 0f;
            if (horizontalRotor.LengthSquared != 0f && bodyUp.LengthSquared != 0f)
            {
                rotorTiltCosine = Vector3f.Dot(horizontalRotor, bodyUp)
                    / (horizontalRotor.Length * bodyUp.Length);
            }
            transform.QueuedRotorForce += new Vector3f(0f, rotorTiltCosine * lift, lift);
        }

        // Main- and tail-rotor axial records normally cancel on the Bell/Mil hierarchy. Preserve the
        // authored net coefficient for custom or incomplete assemblies, including its moment arm.
        if (transform.RotorAxialCoefficient != 0f && transform.Thrust != 0f)
        {
            Vector3f axial = new(
                physics.Mass * transform.Thrust * transform.RotorAxialCoefficient, 0f, 0f);
            transform.QueuedRotorForce += axial;
            transform.QueuedRotorTorque += Vector3f.Cross(new Vector3f(0f, 8192f, 0f), axial);
        }
    }

    private static Matrix3f IntegrateOrientation(Matrix3f matrix, Vector3f angularRate)
    {
        MatrixToQuaternion(matrix, out float x, out float y, out float z, out float w);
        float halfX = angularRate.X * 0.5f;
        float halfY = angularRate.Y * 0.5f;
        float halfZ = angularRate.Z * 0.5f;
        float nextX = x + (halfX * w) + (halfY * z) - (halfZ * y);
        float nextY = y + (halfY * w) + (halfZ * x) - (halfX * z);
        float nextZ = z + (halfZ * w) + (halfX * y) - (halfY * x);
        float nextW = w - (halfX * x) - (halfY * y) - (halfZ * z);
        float inverseLength = 1f / MathF.Sqrt(
            (nextX * nextX) + (nextY * nextY) + (nextZ * nextZ) + (nextW * nextW));
        x = nextX * inverseLength;
        y = nextY * inverseLength;
        z = nextZ * inverseLength;
        w = nextW * inverseLength;
        return new Matrix3f(
            1f - (2f * y * y) - (2f * z * z),
            (2f * x * y) - (2f * w * z),
            (2f * y * w) + (2f * x * z),
            (2f * w * z) + (2f * x * y),
            1f - (2f * x * x) - (2f * z * z),
            (2f * y * z) - (2f * x * w),
            (2f * x * z) - (2f * y * w),
            (2f * y * z) + (2f * x * w),
            1f - (2f * x * x) - (2f * y * y));
    }

    private static void MatrixToQuaternion(
        Matrix3f matrix,
        out float x,
        out float y,
        out float z,
        out float w)
    {
        float trace = matrix.M11 + matrix.M22 + matrix.M33;
        if (trace > 0f)
        {
            float root = MathF.Sqrt(trace + 1f);
            w = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M32 - matrix.M23) * scale;
            y = (matrix.M13 - matrix.M31) * scale;
            z = (matrix.M21 - matrix.M12) * scale;
            return;
        }

        if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
        {
            float root = MathF.Sqrt(matrix.M11 - matrix.M22 - matrix.M33 + 1f);
            x = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            y = (matrix.M12 + matrix.M21) * scale;
            z = (matrix.M13 + matrix.M31) * scale;
            w = (matrix.M32 - matrix.M23) * scale;
        }
        else if (matrix.M22 >= matrix.M33)
        {
            float root = MathF.Sqrt(matrix.M22 - matrix.M11 - matrix.M33 + 1f);
            y = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M12 + matrix.M21) * scale;
            z = (matrix.M23 + matrix.M32) * scale;
            w = (matrix.M13 - matrix.M31) * scale;
        }
        else
        {
            float root = MathF.Sqrt(matrix.M33 - matrix.M11 - matrix.M22 + 1f);
            z = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M13 + matrix.M31) * scale;
            y = (matrix.M23 + matrix.M32) * scale;
            w = (matrix.M21 - matrix.M12) * scale;
        }
    }
}

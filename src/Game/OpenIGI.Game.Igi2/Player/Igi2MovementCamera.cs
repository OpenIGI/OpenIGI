using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The orbiting camera of timed interactions (mode 3) and of the player's death (mode 5), <c>humancamera.c</c>'s movement and death
/// cameras: it circles the body's root, the mouse turning it, and is kept out of whatever it would enter.
/// </summary>
/// <remarks>
/// <para>
/// Entering the mode (<c>HumanCamera_MovementInit</c>, <c>HumanCamera_DeathCameraInit</c>) looks for room around the body
/// (<c>computermap.c sub_4DE380</c>): from the root, lines of 12288 — the ideal distance and two metres — at eight pitches in turn,
/// eight headings at each from a quarter turn right, in the body's frame, against everything but the player; the first line that
/// meets nothing, or water while looking down on it, is taken outright, otherwise the longest. The camera starts two metres short
/// of where that line stopped.
/// </para>
/// <para>
/// Each tick (<c>HumanCamera_UpdateAlphaAndGammaWithCollisions</c>) the mouse turns it by up to six degrees a tick, its pitch held
/// between −71.2° and 89°; it is placed on its orbit (<c>sub_4DE620</c>), then pushed out of anything near
/// (<c>sub_4DE830</c>): twelve lines of two metres from it, along an icosahedron's corners, against everything, each one that
/// strikes pushing it along the face's normal by what the line fell short, the pushes averaged. Its pitch, heading and distance are
/// then read back from where it stands (<c>sub_4DE720</c>), the distance no more than the ideal, and it looks back at the root
/// (<c>sub_4DEE70</c>). Only when nothing but the player's own body was struck does the distance move back toward the ideal, by a
/// thirtieth of a metre a tick.
/// </para>
/// </remarks>
internal sealed class Igi2MovementCamera
{
    /// <summary>The camera's resting distance from the body, <c>vIdealDistance</c>: five metres.</summary>
    private const float IdealDistance = 10240f;

    /// <summary>How far it starts, before the search for room, <c>vDistance</c>: half a metre.</summary>
    private const float StartDistance = 1024f;

    /// <summary>The length of the room search's lines beyond the ideal, and of each push probe: two metres.</summary>
    private const double Probe = 2048.0;

    /// <summary>How far the distance recovers a tick.</summary>
    private const double Recovery = 136.53334045410156;

    /// <summary>The most it turns a tick, six degrees.</summary>
    private const double TurnLimit = 0.10471975803375244;

    /// <summary>The mouse's analog to radians, forty degrees a unit.</summary>
    private const double MouseTurn = 0.6981317400932312;

    /// <summary>The pitches the room search tries, in order (<c>igi2 0x6B8158</c>).</summary>
    private static readonly float[] SearchPitches =
    [
        0.5825036764144897f, 0.1941678822040558f, 0f, 0.9708394408226013f,
        1.359175205230713f, -0.3883357644081116f, -0.7766715288162231f, -1.1650073528289795f,
    ];

    /// <summary>The push probes' directions, an icosahedron's corners (<c>igi2 0x6B80C8</c>).</summary>
    private static readonly Vector3f[] ProbeDirections =
    [
        new(0f, 0.5257310271263123f, 0.8506510257720947f),
        new(0f, -0.5257310271263123f, 0.8506510257720947f),
        new(0.8506510257720947f, 5.450969883802337e-11f, 0.5257310271263123f),
        new(0.5257310271263123f, 0.8506510257720947f, 3.608580101399639e-10f),
        new(-0.5257310271263123f, 0.8506510257720947f, 3.112219926215687e-10f),
        new(-0.8506510257720947f, 1.844590036270688e-10f, 0.5257310271263123f),
        new(-0.5257310271263123f, -0.8506510257720947f, 3.112209934208465e-10f),
        new(0.5257310271263123f, -0.8506510257720947f, 3.6085601173851956e-10f),
        new(0.8506510257720947f, -2.6777799422283977e-10f, -0.5257310271263123f),
        new(2.5473800846498307e-10f, -0.5257310271263123f, -0.8506510257720947f),
        new(6.482400172203029e-10f, 0.5257310271263123f, -0.8506510257720947f),
        new(-0.8506510257720947f, -1.577070071756559e-11f, -0.5257310271263123f),
    ];

    private readonly Igi2LineTrace _search = new(1);
    private readonly Igi2LineTrace _probes = new(ProbeDirections.Length);
    private int _mode;
    private float _alpha;
    private float _gamma;
    private float _distance;

    /// <summary>Gets where the camera stands.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the camera's frame: right, forward and up as its columns.</summary>
    public Matrix3f Orientation { get; private set; } = Matrix3f.Identity;

    /// <summary>The camera's tick, <c>HumanCamera_RunHandler</c> in modes 3 and 5.</summary>
    /// <param name="human">The player.</param>
    /// <param name="world">The level's collision.</param>
    /// <param name="mouseX">The mouse across, as the input port's analog.</param>
    /// <param name="mouseY">The mouse up and down, likewise.</param>
    public void Run(Igi2HumanPlayer human, Igi2CollisionWorld world, double mouseX, double mouseY)
    {
        ArgumentNullException.ThrowIfNull(human);
        ArgumentNullException.ThrowIfNull(world);
        if (human.CameraMode is not (3 or 5))
        {
            _mode = 0;
            return;
        }

        // GetHumanCameraInfo: in these modes the anchor is the body's root (sub_580CA0), the frame the body's.
        Vector3d anchor = human.HeldRootPosition();
        Matrix3f frame = human.Orientation;
        if (_mode != human.CameraMode)
        {
            _mode = human.CameraMode;
            _alpha = 0f;
            _gamma = 0f;
            _distance = StartDistance;
            FindRoom(human, world, anchor, frame);
        }

        Turn(((Fpu)mouseY * MouseTurn).F, ((Fpu)mouseX * MouseTurn).F);
        Vector3d position = OnOrbit(anchor, frame);
        bool struck = PushOut(human, world, ref position);
        ReadBack(anchor, frame, position);
        Position = position;
        LookAt(anchor, position);
        if (!struck)
        {
            if (_distance < IdealDistance)
            {
                float grown = ((Fpu)_distance + Recovery).F;
                _distance = grown > IdealDistance ? IdealDistance : grown;
            }
            else if (_distance > IdealDistance)
            {
                float shrunk = ((Fpu)_distance - Recovery).F;
                _distance = shrunk < IdealDistance ? IdealDistance : shrunk;
            }
        }
    }

    // computermap.c sub_4DE380: the room search on entering the mode.
    private void FindRoom(Igi2HumanPlayer human, Igi2CollisionWorld world, Vector3d anchor, Matrix3f frame)
    {
        float length = ((Fpu)IdealDistance + Probe).F;
        double best = 0.0;
        float bestPitch = 0f;
        float bestHeading = -3.1415927410125732f;
        _search.Evaluator = human;
        _search.Includes = owner => !ReferenceEquals(owner, human);
        foreach (float pitch in SearchPitches)
        {
            float heading = -1.5707963705062866f;
            float across = (float)Math.Cos(pitch);
            float up = (float)Math.Sin(pitch);
            for (int i = 0; i < 8; i++)
            {
                double x = Math.Cos(heading) * across;
                double y = Math.Sin(heading) * across;
                Vector3f direction = new(
                    (float)((x * frame.M11) + (up * frame.M13) + (y * frame.M12)),
                    (float)((x * frame.M21) + (up * frame.M23) + (y * frame.M22)),
                    (float)((x * frame.M31) + (up * frame.M33) + (y * frame.M32)));
                _search.Starts[0] = anchor;
                _search.Ends[0] = new Vector3d(
                    ((double)direction.X * length) + anchor.X,
                    ((double)direction.Y * length) + anchor.Y,
                    (double)((Fpu)direction.Z * length).F + anchor.Z);
                world.TraceLines(_search, uint.MaxValue);
                Igi2Intersection hit = _search.Intersections[0];
                if (!hit.IsHit || (hit.HitObject is World.Igi2WaterBody && pitch > 0f))
                {
                    Settle(length, pitch, heading);
                    return;
                }

                if (hit.Distance > best)
                {
                    best = hit.Distance;
                    bestPitch = pitch;
                    bestHeading = heading;
                }

                heading = ((Fpu)heading + 0.7853981852531433).F;
            }
        }

        Settle(best, bestPitch, bestHeading);
    }

    private void Settle(double clear, float pitch, float heading)
    {
        _alpha = pitch;
        _gamma = heading;
        double room = clear - Probe;
        _distance = room <= 0.0 ? 0f : (float)room;
    }

    // HumanCamera_UpdateAlphaAndGammaWithCollisions: the turn, at most six degrees a tick, the pitch held in its range.
    private void Turn(float deltaAlpha, float deltaGamma)
    {
        double da = deltaAlpha < -TurnLimit ? -TurnLimit : (deltaAlpha > TurnLimit ? TurnLimit : deltaAlpha);
        double dg = deltaGamma < -TurnLimit ? -TurnLimit : (deltaGamma > TurnLimit ? TurnLimit : deltaGamma);
        double alpha = _alpha - (float)da;
        _alpha = alpha < -1.242674469947815 ? -1.2426745f : (alpha > 1.5533430576324463 ? 1.5533431f : (float)alpha);
        _gamma = (float)(dg + _gamma);
    }

    // sub_4DE620: the place on the orbit, in the body's frame, at the distance.
    private Vector3d OnOrbit(Vector3d anchor, Matrix3f frame)
    {
        double cosAlpha = Math.Cos(_alpha);
        float x = (float)(Math.Cos(_gamma) * cosAlpha);
        double y = cosAlpha * Math.Sin(_gamma);
        double z = Math.Sin(_alpha);
        float wx = (float)((z * frame.M13) + (y * frame.M12) + ((double)x * frame.M11));
        float wy = (float)((z * frame.M23) + (y * frame.M22) + ((double)x * frame.M21));
        float wz = (float)((z * frame.M33) + (y * frame.M32) + ((double)x * frame.M31));
        return new Vector3d(
            ((double)wx * _distance) + anchor.X,
            ((double)wy * _distance) + anchor.Y,
            (double)((Fpu)wz * _distance).F + anchor.Z);
    }

    // sub_4DE830: the push out of whatever is near; whether anything but the player's own body was struck.
    private bool PushOut(Igi2HumanPlayer human, Igi2CollisionWorld world, ref Vector3d position)
    {
        for (int i = 0; i < ProbeDirections.Length; i++)
        {
            Vector3f d = ProbeDirections[i];
            _probes.Starts[i] = position;
            _probes.Ends[i] = new Vector3d(
                ((double)d.X * Probe) + position.X,
                ((double)d.Y * Probe) + position.Y,
                (double)(float)((double)d.Z * Probe) + position.Z);
        }

        world.TraceLines(_probes, uint.MaxValue);
        double sx = 0.0, sy = 0.0, sz = 0.0;
        int hits = 0;
        bool struck = false;
        for (int i = 0; i < ProbeDirections.Length; i++)
        {
            Igi2Intersection hit = _probes.Intersections[i];
            if (!hit.IsHit)
            {
                continue;
            }

            double shortfall = Probe - hit.Distance;
            sx += (float)(hit.Normal.X * shortfall);
            sy += (float)(hit.Normal.Y * shortfall);
            sz += shortfall * hit.Normal.Z;
            hits++;
            if (!ReferenceEquals(hit.HitObject?.Owner, human))
            {
                struck = true;
            }
        }

        if (hits != 0)
        {
            double share = 1.0 / hits;
            float shareF = (float)share;
            float pushZ = (float)(shareF * (float)sz);
            position = new Vector3d(
                (sx * share) + position.X,
                ((double)shareF * (float)sy) + position.Y,
                (double)pushZ + position.Z);
        }

        return struck;
    }

    // sub_4DE720: the pitch, heading and distance read back from where the camera stands, in the body's frame.
    private void ReadBack(Vector3d anchor, Matrix3f frame, Vector3d position)
    {
        double dx = position.X - anchor.X, dy = position.Y - anchor.Y, dz = position.Z - anchor.Z;
        float lx = (float)((dz * frame.M31) + (dy * frame.M21) + (dx * frame.M11));
        float ly = (float)((dz * frame.M32) + (dy * frame.M22) + (dx * frame.M12));
        float lz = (float)((dz * frame.M33) + (dy * frame.M23) + (dx * frame.M13));
        float gamma = lx == 0f ? 0f : (float)Math.Atan2(ly, lx);
        double length = Math.Sqrt(((double)lz * lz) + ((double)ly * ly) + ((double)lx * lx));
        float pitch = length == 0.0 ? 0f : (float)Math.Asin((float)(lz / length));
        _distance = (float)length > IdealDistance ? IdealDistance : (float)length;
        _alpha = pitch;
        _gamma = gamma;
    }

    // sub_4DEE70: look back at the anchor, with world up, even on tilted ground.
    private void LookAt(Vector3d anchor, Vector3d position)
    {
        Fpu dx = (Fpu)anchor.X - position.X, dy = (Fpu)anchor.Y - position.Y, dz = (Fpu)anchor.Z - position.Z;
        Vector3f forward;
        if (dx.Value == 0.0 && dy.Value == 0.0)
        {
            forward = new(0f, 1f, 0f);
        }
        else
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz));
            forward = new((dx * inverse).F, (dy * inverse).F, (dz * inverse).F);
        }

        Vector3f right = RigidBodyKernels.Normalise<Fpu>(new(forward.Y, -forward.X, 0f));
        Vector3f up = RigidBodyKernels.Normalise<Fpu>(new(
            (((Fpu)right.Y * forward.Z) - ((Fpu)right.Z * forward.Y)).F,
            (((Fpu)right.Z * forward.X) - ((Fpu)right.X * forward.Z)).F,
            (((Fpu)right.X * forward.Y) - ((Fpu)right.Y * forward.X)).F));
        Orientation = new(right.X, forward.X, up.X, right.Y, forward.Y, up.Y, right.Z, forward.Z, up.Z);
    }
}

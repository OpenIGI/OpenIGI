using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The movement camera used while the player performs a timed interaction.</summary>
internal sealed class Igi2MovementCamera
{
    private readonly Igi2LineTrace _trace = new(1);
    private bool _active;
    private float _alpha;
    private float _gamma;
    private float _distance;

    public Vector3d Position { get; private set; }

    public Matrix3f Orientation { get; private set; } = Matrix3f.Identity;

    public void Run(Igi2HumanPlayer human, Igi2CollisionWorld world, double mouseX, double mouseY)
    {
        if (human.CameraMode != 3) { _active = false; return; }
        // humancamera.c HumanCamera_MovementInit: start a quarter metre away, aim for 2.5 metres.
        // computermap.c sub_4DE380: choose a clear direction around the body before orbiting.
        Vector3d anchor = human.BonePosition("center");
        if (anchor == human.Position) { anchor = human.View.Position; }
        if (!_active)
        {
            _alpha = 0f;
            _gamma = -1.5707964f;
            _distance = 1024f;
            double best = -1.0;
            for (int i = 0; i < 8; i++)
            {
                float gamma = ((Fpu)(-1.5707963705062866) + ((Fpu)i * 0.7853981852531433)).F;
                Vector3d direction = Direction(human, 0f, gamma);
                double clear = Trace(human, world, anchor, direction, 12288f);
                if (clear > best) { best = clear; _gamma = gamma; }
                if (clear >= 12288.0) { break; }
            }
            _distance = (float)Math.Max(0.0, Math.Min(_distance, best - 2048.0));
            _active = true;
        }
        // humancamera.c HumanCamera_UpdateAlphaAndGammaWithCollisions: 6 degrees per tick, pitch limits, 1/30 metre recovery.
        float da = Math.Clamp(((Fpu)mouseY * 0.6981317400932312).F, -0.10471976f, 0.10471976f);
        float dg = Math.Clamp(((Fpu)mouseX * 0.6981317400932312).F, -0.10471976f, 0.10471976f);
        _alpha = Math.Clamp(((Fpu)_alpha - da).F, -1.2426745f, 1.5533431f);
        _gamma = ((Fpu)_gamma + dg).F;
        Vector3d offset = Direction(human, _alpha, _gamma);
        float available = (float)Math.Max(0.0, Trace(human, world, anchor, offset, 12288f) - 2048.0);
        _distance = Math.Min(available, Math.Min(10240f, ((Fpu)_distance + 136.53334045410156).F));
        Position = new(((Fpu)offset.X * _distance + anchor.X).Value,
            ((Fpu)offset.Y * _distance + anchor.Y).Value, ((Fpu)offset.Z * _distance + anchor.Z).Value);
        float yaw = (((Fpu)human.Gamma + _gamma) + 1.5707963705062866).F;
        Orientation = Igi2Pose.FromAngles(default, new Vector3f(-_alpha, 0f, yaw)).ToMatrix();
    }

    private static Vector3d Direction(Igi2HumanPlayer human, float alpha, float gamma)
    {
        Fpu ca = Fpu.Cos(alpha);
        Fpu x = Fpu.Cos(gamma) * ca;
        Fpu y = Fpu.Sin(gamma) * ca;
        Fpu z = Fpu.Sin(alpha);
        Matrix3f frame = human.Orientation;
        return new((((x * frame.M11) + (z * frame.M13)) + (y * frame.M12)).Value,
            (((x * frame.M21) + (z * frame.M23)) + (y * frame.M22)).Value,
            (((x * frame.M31) + (z * frame.M33)) + (y * frame.M32)).Value);
    }

    private double Trace(Igi2HumanPlayer human, Igi2CollisionWorld world, Vector3d anchor, Vector3d direction, float distance)
    {
        _trace.Starts[0] = anchor;
        _trace.Ends[0] = anchor + (direction * distance);
        _trace.Evaluator = human;
        _trace.Includes ??= owner => !ReferenceEquals(owner, human);
        world.TraceLines(_trace, uint.MaxValue);
        return _trace.Intersections[0].IsHit ? _trace.Intersections[0].Distance : distance;
    }
}

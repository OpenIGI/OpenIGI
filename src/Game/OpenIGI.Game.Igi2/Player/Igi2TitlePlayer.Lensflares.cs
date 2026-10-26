using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The level's lens flares as this frame's view sees them (<c>LensflareQTask_EnterHandler</c>).</content>
public sealed partial class Igi2TitlePlayer
{
    private readonly List<Igi2LensflareQuad> _lensflareQuads = [];
    private Igi2LensflareTask[]? _lensflares;
    private Igi2LevelRun? _lensflaresOf;

    /// <summary>
    /// Takes a frame of every lens flare in the level: its sun put far along its direction from the eye and projected through the view,
    /// seen as much as a line of sight towards it lets through (<c>Lensflaretask_CalcIntensity</c>), and its items' quads.
    /// </summary>
    /// <param name="width">The display's width, in pixels.</param>
    /// <param name="height">The display's height, in pixels.</param>
    /// <returns>The quads to draw this frame, in pixels; none outside a level.</returns>
    /// <remarks>Called once a frame, because each call eases the flares' intensities a step, as the original does each frame.</remarks>
    public IReadOnlyList<Igi2LensflareQuad> Lensflares(int width, int height)
    {
        _lensflareQuads.Clear();
        if (Level is not { } level || width <= 0 || height <= 0)
        {
            return _lensflareQuads;
        }

        if (!ReferenceEquals(_lensflaresOf, level))
        {
            _lensflares = [.. level.Root.Descendants().OfType<Igi2LensflareTask>()];
            _lensflaresOf = level;
        }

        if (_lensflares is not { Length: > 0 } flares)
        {
            return _lensflareQuads;
        }

        Vector3d eye = EyePosition;
        Matrix3f view = ViewOrientation;
        float tangent = Human.View.FieldOfView > 1e-4f ? Human.View.FieldOfView : 1f;
        double scale = width * 0.5 / tangent;
        foreach (Igi2LensflareTask flare in flares)
        {
            flare.FollowDirlight();
            Vector3f d = flare.Direction;
            Vector3d toward = new(d.X * Igi2LensflareTask.SunDistance, d.Y * Igi2LensflareTask.SunDistance, d.Z * Igi2LensflareTask.SunDistance);

            // TransContext_ProjectWorldPos2: the view's rows taken against the offset, divided by its depth.
            double x = (view.M11 * toward.X) + (view.M12 * toward.Y) + (view.M13 * toward.Z);
            double y = (view.M21 * toward.X) + (view.M22 * toward.Y) + (view.M23 * toward.Z);
            double z = (view.M31 * toward.X) + (view.M32 * toward.Y) + (view.M33 * toward.Z);
            System.Numerics.Vector2 screen = z > 0.0
                ? new((float)((width * 0.5) + (x / z * scale)), (float)((height * 0.5) - (y / z * scale)))
                : new(-1f, -1f);
            _lensflareQuads.AddRange(flare.Frame(
                screen,
                (float)z,
                System.Numerics.Vector2.Zero,
                new System.Numerics.Vector2(width, height),
                () => SunSeen(eye, new Vector3d(eye.X + toward.X, eye.Y + toward.Y, eye.Z + toward.Z))));
        }

        return _lensflareQuads;
    }

    // World_LineOfSight towards the sun through every group, the ground among them: how much of it gets through.
    private float SunSeen(Vector3d eye, Vector3d sun)
    {
        if (Materials is not IWorldMaterialTransparency materials)
        {
            return 1f;
        }

        // Its evaluate event (512) leaves the looker out: the line starts inside the player's own body.
        WorldLineOfSight sight = new(eye, sun, materials) { Includes = owner => !ReferenceEquals(owner, Human) };
        CollisionWorld.LineOfSight(sight, uint.MaxValue);
        return sight.IsHit ? sight.Transparency : 1f;
    }
}

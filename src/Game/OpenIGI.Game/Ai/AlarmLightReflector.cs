using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>The turning reflector inside an alarm light (<c>TASKTYPE_ALARMLIGHTREFLECTOR</c>, <c>0x439A50</c>).</summary>
/// <param name="placement">The reflector's attachment placement.</param>
/// <param name="light">The alarm light it turns in.</param>
/// <remarks>
/// Only a light made in a model that carries it has one: magic objects are made once, as the light is set up in its on model
/// (<c>0x43A450</c>), and a later change of model does not make them again.
/// </remarks>
public sealed class AlarmLightReflector(WorldPlacement placement, AlarmSpeaker light)
{
    /// <summary>How far it turns a tick while its light is on, a sixtieth of a turn (<c>0x3DD67750</c>).</summary>
    public const float TurnPerTick = 0.10471975803375244f;

    /// <summary>Half the flare's size, in world units (<c>0x439F10</c>).</summary>
    public const float FlareHalfSizeUnits = 6144f;

    /// <summary>Gets the reflector's placement.</summary>
    public WorldPlacement Placement { get; } = placement;

    /// <summary>Gets its light.</summary>
    public AlarmSpeaker Light { get; } = light;

    /// <summary>Gets its turn about its own X axis, in radians.</summary>
    public float Angle { get; private set; }

    /// <summary>Gets its turn a tick ago.</summary>
    public float PreviousAngle { get; private set; }

    /// <summary>Gets or sets the light's own placement orientation, which side of it the flare is seen from.</summary>
    public Matrix3f LightOrientation { get; set; } = Matrix3f.Identity;

    /// <summary>Turns it a tick while its light is on and whole (<c>0x439B70</c>).</summary>
    /// <param name="on">Whether the light is on.</param>
    public void Run(bool on)
    {
        PreviousAngle = Angle;
        if (on && !Light.IsDestroyed)
        {
            Angle += TurnPerTick;
        }
    }

    /// <summary>Its orientation as drawn: its placement's, turned about its own X axis.</summary>
    /// <param name="alpha">How far the frame is between the last two ticks.</param>
    public Matrix3f Orientation(float alpha) =>
        Placement.Orientation * Matrix3f.RotationX(PreviousAngle + ((Angle - PreviousAngle) * Math.Clamp(alpha, 0f, 1f)));

    /// <summary>
    /// The flare it shows while its light is on, <c>0x439F10</c>: at its first magic vertex, seen only from in front of the light,
    /// its red the strength of the turn's cosine-like swing, <c>|fmod(angle, pi) / pi - 1/2|</c> twice over.
    /// </summary>
    /// <param name="eyeUnits">Where it is seen from.</param>
    /// <param name="alpha">How far the frame is between the last two ticks.</param>
    /// <param name="glow">Receives the flare.</param>
    /// <returns>Whether it shows one.</returns>
    public bool TryGetFlare(Vector3d eyeUnits, float alpha, out WorldGlow glow)
    {
        glow = default;
        if (!Light.WasOn || Light.IsDestroyed)
        {
            return false;
        }

        Vector3d toEye = eyeUnits - Light.PositionUnits;
        Matrix3f l = LightOrientation;
        if (!((toEye.X * l.M12) + (toEye.Y * l.M22) + (toEye.Z * l.M32) > 0.0))
        {
            return false;
        }

        Vector3f vertex = Placement.Model is { } model && model.TryGetMagicVertex(0, out Vector3f point) ? point : Vector3f.Zero;
        Matrix3f turned = Orientation(alpha);
        Vector3f offset = turned.Transposed().Transform(vertex);
        Vector3d at = (Placement.Position * WorldScene.UnitsPerMetre) + new Vector3d(offset.X, offset.Y, offset.Z);

        // The angle only grows from nought, so the C runtime's fmod is the plain remainder.
        double swing = Math.Abs((((double)Angle % Math.PI) * 0.31830987334251404) - 0.5);
        byte red = (byte)Math.Clamp(swing * 2.0 * 128.0, 0.0, 255.0);
        glow = new WorldGlow(at, FlareHalfSizeUnits, new OpenIGI.Core.Graphics.Bgra32(0, 0, red, 128));
        return true;
    }
}

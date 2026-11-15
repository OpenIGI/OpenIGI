using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Core.Cutscenes;

/// <summary>What the curves view plots of a scene's camera.</summary>
public enum CutsceneChannel
{
    /// <summary>How fast the camera flies, metres a second.</summary>
    Speed,

    /// <summary>How fast it turns, degrees a second.</summary>
    TurnRate,

    /// <summary>Where it is across, metres.</summary>
    X,

    /// <summary>Where it is along, metres.</summary>
    Y,

    /// <summary>How high it is, metres.</summary>
    Z,

    /// <summary>Its turn about X (Alpha), degrees.</summary>
    TurnX,

    /// <summary>Its turn about Y (Beta), degrees.</summary>
    TurnY,

    /// <summary>Its turn about the up axis (Gamma), degrees.</summary>
    TurnZ,

    /// <summary>Its lens across the picture, degrees.</summary>
    Lens,
}

/// <summary>
/// A scene's camera as curves over its clock, as Unreal's curve editor shows a transform's channels: each channel the game blends
/// (place, the three angles, the lens) read off its own path (<see cref="CutsceneCameraMath.Evaluate"/>), and the speed and turn
/// rate it flies at; a key's value on a channel written back into its knot.
/// </summary>
/// <remarks>Values are as written: a riding key's place is relative to where its ride stood as the level loaded.</remarks>
public static class CutsceneCurves
{
    private const double Degree = Math.PI / 180;
    private const double Step = 1 / 60.0;

    /// <summary>Gets whether a channel is a key's own value, which a key can be dragged on, rather than a rate.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns><see langword="true"/> for a value.</returns>
    public static bool IsValue(CutsceneChannel channel) => channel is not (CutsceneChannel.Speed or CutsceneChannel.TurnRate);

    /// <summary>A channel's unit, for the axis.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The unit.</returns>
    public static string Unit(CutsceneChannel channel) => channel switch
    {
        CutsceneChannel.Speed => "m/s",
        CutsceneChannel.TurnRate => "°/s",
        CutsceneChannel.X or CutsceneChannel.Y or CutsceneChannel.Z => "m",
        _ => "°",
    };

    /// <summary>A channel's value on the path at a moment.</summary>
    /// <param name="path">The scene's keys as the path reads them.</param>
    /// <param name="scale">The scene's time scale.</param>
    /// <param name="seconds">The moment, in scene seconds.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The value, in the channel's unit.</returns>
    public static double At(CutsceneKey[] path, float scale, double seconds, CutsceneChannel channel)
    {
        ArgumentNullException.ThrowIfNull(path);
        switch (channel)
        {
            case CutsceneChannel.Speed:
            {
                double from = Math.Max(0, seconds - Step);
                double to = seconds + Step;
                return (CutsceneCameraMath.Evaluate(path, scale, to, -1f).Position - CutsceneCameraMath.Evaluate(path, scale, from, -1f).Position).Length / WorldUnits.PerMetre / (to - from);
            }

            case CutsceneChannel.TurnRate:
            {
                double from = Math.Max(0, seconds - Step);
                double to = seconds + Step;
                CutsceneMoves.Angles a = CutsceneMoves.AnglesAt(path, scale, from), b = CutsceneMoves.AnglesAt(path, scale, to);
                double turned = Math.Sqrt(Math.Pow(b.Alpha - a.Alpha, 2) + Math.Pow(b.Beta - a.Beta, 2) + Math.Pow(b.Gamma - a.Gamma, 2));
                return turned / Degree / (to - from);
            }

            case CutsceneChannel.X or CutsceneChannel.Y or CutsceneChannel.Z:
            {
                Vector3d at = CutsceneCameraMath.Evaluate(path, scale, seconds, -1f).Position / WorldUnits.PerMetre;
                return channel == CutsceneChannel.X ? at.X : channel == CutsceneChannel.Y ? at.Y : at.Z;
            }

            case CutsceneChannel.Lens:
                return 2 * Math.Atan(CutsceneCameraMath.Evaluate(path, scale, seconds, -1f).FieldOfView) / Degree;

            default:
            {
                CutsceneMoves.Angles angles = CutsceneMoves.AnglesAt(path, scale, seconds);
                return (channel == CutsceneChannel.TurnX ? angles.Alpha : channel == CutsceneChannel.TurnY ? angles.Beta : angles.Gamma) / Degree;
            }
        }
    }

    /// <summary>A key's own value on a channel, as written.</summary>
    /// <param name="camera">The key's <c>EditCamera</c>.</param>
    /// <param name="channel">A value channel.</param>
    /// <returns>The value, in the channel's unit.</returns>
    public static double Of(MissionTask camera, CutsceneChannel channel)
    {
        CutsceneKey key = CutsceneMoves.KeyOf(camera);
        return channel switch
        {
            CutsceneChannel.X => key.Position.X / WorldUnits.PerMetre,
            CutsceneChannel.Y => key.Position.Y / WorldUnits.PerMetre,
            CutsceneChannel.Z => key.Position.Z / WorldUnits.PerMetre,
            CutsceneChannel.TurnX => key.Alpha / Degree,
            CutsceneChannel.TurnY => key.Beta / Degree,
            CutsceneChannel.TurnZ => key.Gamma / Degree,
            CutsceneChannel.Lens => 2 * Math.Atan(key.FieldOfView) / Degree,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Not a key's own value."),
        };
    }

    /// <summary>The step that sets a key's value on a channel.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="camera">The key.</param>
    /// <param name="channel">A value channel.</param>
    /// <param name="value">The value, in the channel's unit.</param>
    /// <returns>The step.</returns>
    public static IEditorCommand Set(MissionDocument document, MissionTask camera, CutsceneChannel channel, double value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(camera);
        if (channel is CutsceneChannel.X or CutsceneChannel.Y or CutsceneChannel.Z)
        {
            Vector3d at = CutsceneMoves.KeyOf(camera).Position;
            double units = Math.Round(value * WorldUnits.PerMetre, 2);
            double[] position = channel == CutsceneChannel.X ? [units, at.Y, at.Z] : channel == CutsceneChannel.Y ? [at.X, units, at.Z] : [at.X, at.Y, units];
            return SetParameterCommand.Numbers(document, camera, "Position", position);
        }

        return channel switch
        {
            CutsceneChannel.Lens => SetParameterCommand.Numbers(document, camera, "FOV", [Math.Round(Math.Tan(Math.Clamp(value, 1, 175) * Degree / 2), 4)]),
            CutsceneChannel.TurnX => SetParameterCommand.Numbers(document, camera, "Alpha", [Math.Round(value * Degree, 6)]),
            CutsceneChannel.TurnY => SetParameterCommand.Numbers(document, camera, "Beta", [Math.Round(value * Degree, 6)]),
            CutsceneChannel.TurnZ => SetParameterCommand.Numbers(document, camera, "Gamma", [Math.Round(value * Degree, 6)]),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Not a key's own value."),
        };
    }
}

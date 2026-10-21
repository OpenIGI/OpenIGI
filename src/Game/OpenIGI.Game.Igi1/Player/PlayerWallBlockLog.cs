using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The "what am I walking into" diagnostic: names the object the wall sweep stopped the body against, for
/// the doorway-collision hunt.
/// </summary>
internal sealed class PlayerWallBlockLog
{
    /// <summary>The heights the doorway diagnostic probes forward at, in world units above the feet.</summary>
    /// <remarks>Ankle, waist, chest and head — enough to catch a threshold, a lintel or a mid wall.</remarks>
    private static readonly double[] BlockProbeHeights = [409.6, 1638.4, 4096.0, 7372.8];

    /// <summary>How far ahead the diagnostic probes for the blocking object, in world units (about 1.5 m).</summary>
    private const double BlockProbeReachUnits = 6144.0;

    private readonly IOpenIgiLogger _logger;
    private readonly PlayerBody _body;
    private string _lastBlockLabel = string.Empty;

    /// <summary>Initializes the log for one body.</summary>
    /// <param name="logger">Where the contacts are written.</param>
    /// <param name="body">The body whose contacts are named.</param>
    internal PlayerWallBlockLog(IOpenIgiLogger logger, PlayerBody body)
    {
        _logger = logger;
        _body = body;
    }

    /// <summary>Gets whether the expensive wall-contact diagnostic is enabled.</summary>
    internal bool IsEnabled => _logger.IsEnabled(LogLevel.Information);

    /// <summary>
    /// Names the object the wall sweep stopped the body against, when the body is pressed into one while
    /// trying to move — the "what am I walking into" log for the doorway-collision hunt.
    /// </summary>
    /// <param name="walls">The level's placed objects, or <see langword="null"/> when none are loaded.</param>
    /// <param name="interactions">The level's activatables, for naming a fence's power.</param>
    /// <param name="forward">The forward input this tick.</param>
    /// <param name="strafe">The strafe input this tick.</param>
    /// <remarks>
    /// It fires only on a fresh contact (the blocking object changed) so a held-forward push logs once,
    /// not every tick. The line names the model instance, the placing task, and where the ray met it. The
    /// configured logger records it only at verbose information level and buffers any file output.
    /// </remarks>
    internal void LogIfAny(
        ObjectCollisionIndex? walls,
        WorldInteractionRegistry? interactions,
        float forward,
        float strafe)
    {
        // Identifying the blocker casts four extra world rays. Do none of that work unless verbose
        // diagnostics are actually being collected; minimal/off logging must be a zero-cost path.
        if (!IsEnabled
            || walls is null
            || !_body.TouchedWall
            || (forward == 0f && strafe == 0f))
        {
            _lastBlockLabel = string.Empty;
            return;
        }

        // Forward is the body's own facing, (sin, cos) — the way the walk carries it (PlayerBody.FacingBasis).
        double fx = Math.Sin(_body.Yaw);
        double fy = Math.Cos(_body.Yaw);

        string label = string.Empty;
        WorldRayHit nearest = default;
        double best = double.MaxValue;
        double bestHeight = 0.0;

        foreach (double height in BlockProbeHeights)
        {
            Vector3d origin = new(_body.Position.X, _body.Position.Y, _body.Position.Z + height);
            Vector3d end = new(origin.X + (fx * BlockProbeReachUnits), origin.Y + (fy * BlockProbeReachUnits), origin.Z);

            if (walls.TryIdentify(origin, end, out string hitLabel, out WorldRayHit hit) && hit.Distance < best)
            {
                best = hit.Distance;
                label = hitLabel;
                nearest = hit;
                bestHeight = height;
            }
        }

        if (label.Length == 0 || label == _lastBlockLabel)
        {
            return;
        }

        _lastBlockLabel = label;

        double probeMetres = bestHeight / PlayerCamera.UnitsPerMetre;
        double aheadMetres = best / PlayerCamera.UnitsPerMetre;
        string line = FormattableString.Invariant(
            $"BLOCKED by {label} at ({nearest.Position.X:F0}, {nearest.Position.Y:F0}, {nearest.Position.Z:F0}) — probe {probeMetres:F2}m, {aheadMetres:F2}m ahead; player ({_body.Position.X:F0}, {_body.Position.Y:F0}, {_body.Position.Z:F0}) yaw {_body.Yaw:F2}");

        _logger.Information("Player", line);
        if (label.Contains("Fence", StringComparison.OrdinalIgnoreCase))
        {
            WorldInteraction? fence = interactions?.FindElectrifiedFenceNear(nearest.Position);
            _logger.Information(
                "Player.Fence",
                $"wall contact task={(fence is null ? "-" : fence.Task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                    + $"powered={(fence?.IsElectrified == true ? "true" : "false")} "
                    + $"contact={(fence is null ? "miss" : "candidate")} "
                    + $"player=({_body.Position.X:F0},{_body.Position.Y:F0},{_body.Position.Z:F0})");
        }
    }
}

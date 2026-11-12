using OpenIGI.Core.Configuration;
using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Registers the command that reads the camera's pose back as command-line arguments.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// A capture script needs absolute coordinates, and there is otherwise no way to get one out of the
    /// game except by writing it down from a debugger. This prints the pose in the exact spelling
    /// <c>-campos</c>, <c>-yaw</c> and <c>-pitch</c> take, so a viewpoint found by flying around is
    /// copied into a script rather than retyped and rounded. The eye is the inspector's, which is
    /// whichever camera is actually drawing -- the player's, the free camera's or a cinematic one.
    /// </remarks>
    private void RegisterCameraCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "campos",
            "Prints where the camera is, spelled as the -campos, -yaw and -pitch a capture takes.",
            context =>
            {
                Camera3D eye = _inspectorEye;
                Vector3f forward = eye.Orientation.Row3;

                // The inverse of Camera3D.LookRotation: its forward is
                // (sin yaw * cos pitch, cos yaw * cos pitch, sin pitch), so the pitch comes straight off
                // z and the yaw off the other two -- atan2 with x FIRST, because yaw here is measured
                // from +Y rather than from +X.
                float pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
                float yaw = MathF.Atan2(forward.X, forward.Y);

                string pose = string.Create(CultureInfo.InvariantCulture,
                    $"-campos {eye.Position.X:0.##},{eye.Position.Y:0.##},{eye.Position.Z:0.##} -yaw {yaw:0.####} -pitch {pitch:0.####}");

                // To the log as well as the scrollback, because the caller that most wants this is a
                // headless -runat capture, which has no scrollback to read.
                context.Print(pose);
                _logger.Information("Camera", pose);
            });

        // The other way round: stands the walking player at a place, in metres as campos prints them. For scripted runs that
        // need the player somewhere it did not start -- a save game made away from the spawn, to see a load put it back.
        console.Commands.Add(
            "setpos",
            "Stands the player at a place: setpos <x> <y> <z> [yaw], in the metres campos prints.",
            context =>
            {
                if (TitlePlayer is not { } player || _titleFeatures?.Placement is not { } placement
                    || !double.TryParse(context[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    || !double.TryParse(context[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    || !double.TryParse(context[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                {
                    context.Print("setpos <x> <y> <z> [yaw] needs a walking player and three numbers.");
                    return;
                }

                float yaw = float.TryParse(context[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float turned) ? turned : player.Heading ?? 0f;
                bool stood = placement.TryPlace(new Vector3d(x, y, z) * Game.World.WorldScene.UnitsPerMetre, yaw);
                string said = stood ? string.Create(CultureInfo.InvariantCulture, $"Standing at {x:0.##},{y:0.##},{z:0.##}.") : "Nothing to stand on there.";
                context.Print(said);
                _logger.Information("Camera", said);
            });
    }
}

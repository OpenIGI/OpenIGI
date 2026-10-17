using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
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

                string pose = FormattableString.Invariant(
                    $"-campos {eye.Position.X:0.##},{eye.Position.Y:0.##},{eye.Position.Z:0.##} -yaw {yaw:0.####} -pitch {pitch:0.####}");

                // To the log as well as the scrollback, because the caller that most wants this is a
                // headless -runat capture, which has no scrollback to read.
                context.Print(pose);
                _logger.Information("Camera", pose);
            });
    }
}

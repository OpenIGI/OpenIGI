using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The switch between renderers: asking for it, and picking the game back up after it.</summary>
internal sealed partial class GameHost
{
    // Whether the resumed pose has been put back: once, for the mission the switch reloaded.
    private bool _resumePlaced;

    /// <summary>Gets where to pick the game up again, when it ended to change renderer.</summary>
    /// <remarks>
    /// Null when the game ended for any other reason. The composition root reads it once <see cref="Run"/>
    /// returns, builds the other renderer on the same window, and starts a new game with it as
    /// <see cref="Configuration.HostOptions.Resume"/>.
    /// </remarks>
    public RendererSwitch? RendererSwitchRequest { get; private set; }

    /// <summary>Registers the command the switch key is bound to.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterRendererSwitchCommand(ConsoleSystem console) =>
        console.Commands.Add(
            "r_switch_renderer",
            "Rebuilds the game on the other renderer -- legacy Vulkan or the RHI -- in the same place.",
            context =>
            {
                RendererSwitchRequest = new RendererSwitch(_activeMission?.Id ?? 0, _conditions, CurrentPose());
                context.Print("Switching renderer; the game will be back in a moment.");
                _logger.Information("Renderer", $"Switching renderer from {Describe(RendererSwitchRequest)}.");

                // The ordinary way out, so settings are saved on it as on any other.
                _stopRequested = true;
            });

    /// <summary>Where the view is now, or null where there is no mission to be anywhere in.</summary>
    private RendererSwitchPose? CurrentPose()
    {
        if (_activeMission is null)
        {
            return null;
        }

        if (_playerMode && _player is { } player)
        {
            Vector3d feet = player.Body.Position / PlayerCamera.UnitsPerMetre;
            return new RendererSwitchPose(feet, player.Body.Yaw, player.Body.Pitch, Flying: false);
        }

        return _camera is { } camera
            ? new RendererSwitchPose(camera.Position, camera.Yaw, camera.Pitch, Flying: true)
            : null;
    }

    /// <summary>Puts back the hour and weather the switched-from game was played in, before its mission loads.</summary>
    /// <remarks>
    /// Through the same variable the campaign screen sets, so the loader lights the level for that hour
    /// exactly as it would have after the player chose it there.
    /// </remarks>
    private void ResumeConditions()
    {
        if (_options.Resume is not { } resume)
        {
            return;
        }

        _conditions = resume.Conditions;
        RenderCVars.TimeOfDay.Value = resume.Conditions.StartHour;
    }

    /// <summary>Puts the view back where it was, once the resumed mission has placed its player.</summary>
    private void ResumePose()
    {
        if (_options.Resume is not { Pose: { } pose } || _resumePlaced)
        {
            return;
        }

        _resumePlaced = true;

        if (pose.Flying)
        {
            _camera ??= new FreeCamera(pose.Position, pose.Yaw, pose.Pitch);
            _camera.MoveTo(pose.Position, pose.Yaw, pose.Pitch);
            _playerMode = false;
        }
        else if (_player is { } player && player.Place(_ground, pose.Position, pose.Yaw))
        {
            player.Body.Pitch = pose.Pitch;
            _playerMode = true;
        }
        else
        {
            _logger.Warning("Renderer", $"Could not stand the player back at {pose.Position}; starting where the mission starts.");
            return;
        }

        _logger.Information("Renderer", $"Resumed {Describe(_options.Resume)}.");
    }

    private static string Describe(RendererSwitch request) => request.Pose is { } pose
        ? FormattableString.Invariant(
            $"mission {request.MissionId} {(pose.Flying ? "flying" : "standing")} at ({pose.Position.X:F2}, {pose.Position.Y:F2}, {pose.Position.Z:F2}) yaw {pose.Yaw:F3} pitch {pose.Pitch:F3}")
        : request.MissionId > 0 ? $"mission {request.MissionId}" : "the menu";
}

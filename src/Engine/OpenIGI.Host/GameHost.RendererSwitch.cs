using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Services;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The switch between renderers: asking for it, and picking the game back up after it.</summary>
public sealed partial class GameHost
{
    // Whether the resumed pose has been put back: once, for the mission the switch reloaded.
    private bool _resumePlaced;

    // The picture a switch holds across, read from this renderer or shown by it.
    private readonly RendererSwitchService _switch;

    // No keys held and no mouse moved: what the view is steered by while the switched-from picture is up,
    // so the new renderer comes up on exactly the view the old one was showing.
    private readonly InputState _stillInput = new();

    /// <summary>Gets the input the player and the free camera are steered by this frame.</summary>
    private InputState ViewInput => ScriptedViewInput
        ?? (_switch.IsHolding ? _stillInput : _window.Input);

    /// <summary>
    /// Gets or sets input a harness steers the player with in place of the window's, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A lockstep run marks the window's input captured every frame so a capture cannot see the mouse; a
    /// scenario that has to press the trigger hands its own state here instead.
    /// </remarks>
    internal InputState? ScriptedViewInput { get; set; }

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
            "Rebuilds the game over the other graphics API -- Vulkan or WebGPU -- in the same place.",
            context =>
            {
                if (_switch.IsPending)
                {
                    return;
                }

                _switch.Request(new RendererSwitch(
                    _session.Mission?.Id ?? 0,
                    _conditions,
                    SwitchedHour(),
                    CurrentPose()));
                context.Print("Switching renderer; the game will be back in a moment.");
            });

    /// <summary>Reads the picture a switch holds across, and ends the game once it has it.</summary>
    /// <remarks>Called at the end of every frame, after it has been presented.</remarks>
    private void AdvanceRendererSwitch()
    {
        if (_switch.Advance(_window.Width, _window.Height) is not { } done)
        {
            return;
        }

        RendererSwitchRequest = done;

        // The ordinary way out, so settings are saved on it as on any other.
        _stopRequested = true;
    }

    /// <summary>The hour a switch carries, as <c>r_time_of_day</c> takes it.</summary>
    /// <remarks>
    /// The sun's own hour only while a day is running, which is the one case where it has moved from what
    /// the level was loaded for. Otherwise the variable as it stands -- which is the hour chosen, or the
    /// negative that means the level's authored sun, and that has to stay negative: pinned to the number
    /// it happened to equal, the level was lit by the day model at that hour rather than by its authored
    /// sun, and every session after the first switch looked different from the first.
    /// </remarks>
    private float SwitchedHour() =>
        _session.Mission is null ? -1f
        : RenderCVars.DayNightCycle.Value ? _timeOfDay.DayNight.TimeHours
        : RenderCVars.TimeOfDay.Value;

    /// <summary>Where the view is now, or null where there is no mission to be anywhere in.</summary>
    private RendererSwitchPose? CurrentPose()
    {
        if (_session.Mission is null)
        {
            return null;
        }

        if (_playerMode && HostedPlayer is { } player)
        {
            Vector3d feet = player.Body.Position / WorldScene.UnitsPerMetre;
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

        // The hour the sun was at, which the loader lights the level for; the campaign's choice only where
        // there was no lit level to read one from.
        RenderCVars.TimeOfDay.Value = resume.TimeHours >= 0f ? resume.TimeHours : resume.Conditions.StartHour;

        // The picture the other renderer last showed, up on this one before anything else is, so the
        // switch reads as that picture holding still rather than as a load.
        if (resume.Frame is { } frame)
        {
            _switch.HoldResumeFrame(frame, _window.Width, _window.Height);
        }
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
        else if (HostedPlayer is { } player && player.Place(_mission.Ground, pose.Position, pose.Yaw))
        {
            player.Body.Pitch = pose.Pitch;
            _playerMode = true;
        }
        else
        {
            _logger.Warning("Renderer", $"Could not stand the player back at {pose.Position}; starting where the mission starts.");
            return;
        }

        _logger.Information("Renderer", FormattableString.Invariant($"Resumed {RendererSwitchService.Describe(_options.Resume)}; the sun is at hour {_timeOfDay.DayNight.TimeHours:F2}."));
    }
}

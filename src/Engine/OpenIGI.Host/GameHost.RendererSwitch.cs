using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Host;

/// <summary>The switch between renderers: asking for it, and picking the game back up after it.</summary>
internal sealed partial class GameHost
{
    // How many frames the switched-from picture is read over. The RHI copies the presented frame back only
    // once asked to, so the first read of a session is black and a frame or two later it holds the picture.
    private const int SwitchCaptureFrames = 3;

    // Whether the resumed pose has been put back: once, for the mission the switch reloaded.
    private bool _resumePlaced;

    // A switch asked for and waiting on its picture, and how many more frames to read it over.
    private RendererSwitch? _switchPending;
    private int _switchCaptureFramesLeft;

    // The switched-from picture, held on screen in place of the loading screen while the resumed mission
    // loads and settles.
    private TextureHandle _resumeFrame = TextureHandle.None;
    private ResumeHold _resumeHold;

    // No keys held and no mouse moved: what the view is steered by while the switched-from picture is up,
    // so the new renderer comes up on exactly the view the old one was showing.
    private readonly InputState _stillInput = new();

    /// <summary>Gets the input the player and the free camera are steered by this frame.</summary>
    private InputState ViewInput => ScriptedViewInput
        ?? (_resumeHold == ResumeHold.Holding ? _stillInput : _window.Input);

    /// <summary>
    /// Gets or sets input a harness steers the player with in place of the window's, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A lockstep run marks the window's input captured every frame so a capture cannot see the mouse; a
    /// scenario that has to press the trigger hands its own state here instead.
    /// </remarks>
    internal InputState? ScriptedViewInput { get; set; }

    /// <summary>Where a resumed game is in holding the switched-from picture.</summary>
    private enum ResumeHold
    {
        /// <summary>No picture held: loads show their own screen.</summary>
        None,

        /// <summary>The picture is on screen in place of the loading screen.</summary>
        Holding,

        /// <summary>The level has been revealed; the rest of this load draws nothing over it.</summary>
        Revealed,
    }

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
                if (_switchPending is not null)
                {
                    return;
                }

                _switchPending = new RendererSwitch(
                    _activeMission?.Id ?? 0,
                    _conditions,
                    SwitchedHour(),
                    CurrentPose());
                _switchCaptureFramesLeft = SwitchCaptureFrames;
                context.Print("Switching renderer; the game will be back in a moment.");
                _logger.Information("Renderer", $"Switching renderer from {Describe(_switchPending)}.");
            });

    /// <summary>Reads the picture a switch holds across, and ends the game once it has it.</summary>
    /// <remarks>Called at the end of every frame, after it has been presented.</remarks>
    private void AdvanceRendererSwitch()
    {
        if (_switchPending is not { } pending)
        {
            return;
        }

        int width = _window.Width;
        int height = _window.Height;
        Bgra32[] pixels = new Bgra32[Math.Max(width * height, 0)];

        if (pixels.Length > 0)
        {
            _device.CaptureFrame(width, height, pixels);
        }

        if (--_switchCaptureFramesLeft > 0)
        {
            return;
        }

        RendererSwitchRequest = pending with
        {
            Frame = pixels.Length > 0 ? new RendererSwitchFrame(width, height, pixels) : null,
        };

        _switchPending = null;

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
        _activeMission is null ? -1f
        : RenderCVars.DayNightCycle.Value ? _dayNight.TimeHours
        : RenderCVars.TimeOfDay.Value;

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

        // The hour the sun was at, which the loader lights the level for; the campaign's choice only where
        // there was no lit level to read one from.
        RenderCVars.TimeOfDay.Value = resume.TimeHours >= 0f ? resume.TimeHours : resume.Conditions.StartHour;

        // The picture the other renderer last showed, up on this one before anything else is, so the
        // switch reads as that picture holding still rather than as a load.
        if (resume.Frame is { } frame && frame.Width == _window.Width && frame.Height == _window.Height)
        {
            _resumeFrame = _device.CreateTexture(
                new TextureDescription(frame.Width, frame.Height, 1, TextureFilter.Point, TextureAddressMode.Clamp),
                frame.Pixels);
            _resumeHold = ResumeHold.Holding;

            _device.Clear(Bgra32.Black);
            PaintResumeFrame();
            _device.SwapBuffers();
        }
    }

    /// <summary>Paints the held picture in place of the loading screen, where a switch is being resumed.</summary>
    /// <returns><see langword="true"/> when the loading screen is not to be painted.</returns>
    private bool PaintResumeFrame()
    {
        switch (_resumeHold)
        {
            case ResumeHold.Holding:
                // The loading backdrop pass with every effect of it off -- no blur, grade, vignette, grain,
                // lines, bars or zoom -- is the picture as it was.
                _device.DrawLoadingBackdrop(
                    _resumeFrame,
                    new LoadingBackdropSettings(
                        TimeSeconds: 0f,
                        Blur: 0f,
                        Desaturation: 0f,
                        Vignette: 0f,
                        Zoom: 1f,
                        Grain: 0f,
                        Scanlines: 0f,
                        Opacity: 1f,
                        Letterbox: -0.01f,
                        HasPicture: true,
                        AspectRatio: _window.Height > 0 ? _window.Width / (float)_window.Height : 1.777f,
                        Sweep: 0f));
                return true;

            case ResumeHold.Revealed:
                return true;

            default:
                return false;
        }
    }

    /// <summary>Lets the held picture go as the level is revealed, and forgets it once the load has ended.</summary>
    /// <param name="revealed">Whether the level has been revealed.</param>
    /// <param name="loadActive">Whether the load sequence is still running.</param>
    private void AdvanceResumeHold(bool revealed, bool loadActive)
    {
        if (_resumeHold == ResumeHold.Holding && revealed)
        {
            // A cut, not the load's dissolve: the eye goes from one renderer's picture straight to the other.
            _resumeHold = ResumeHold.Revealed;
        }

        if (_resumeHold == ResumeHold.Revealed && !loadActive)
        {
            _resumeHold = ResumeHold.None;
            _device.DestroyTexture(_resumeFrame);
            _resumeFrame = TextureHandle.None;
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

        _logger.Information("Renderer", FormattableString.Invariant($"Resumed {Describe(_options.Resume)}; the sun is at hour {_dayNight.TimeHours:F2}."));
    }

    private static string Describe(RendererSwitch request) => request.Pose is { } pose
        ? FormattableString.Invariant(
            $"mission {request.MissionId} at hour {request.TimeHours:F2} {(pose.Flying ? "flying" : "standing")} at ({pose.Position.X:F2}, {pose.Position.Y:F2}, {pose.Position.Z:F2}) yaw {pose.Yaw:F3} pitch {pose.Pitch:F3}")
        : request.MissionId > 0 ? FormattableString.Invariant($"mission {request.MissionId} at hour {request.TimeHours:F2}") : "the menu";
}

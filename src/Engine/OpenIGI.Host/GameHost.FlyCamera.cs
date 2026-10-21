using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

using System.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Steers the free camera and the sun gizmo.</summary>
    private void UpdateFlyingView(FrameLoopState state)
    {
        // G shows the sun gizmo, but only while flying: it takes the cursor to drag its rings, which the
        // walking player needs for looking. Leaving the free camera puts it away.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.G) && !_playerMode)
        {
            _sunGizmo.Toggle();
        }

        if (_playerMode)
        {
            _sunGizmo.Hide();
        }

        if (_camera is not null)
        {
            int nowMilliseconds = _loop.TimeSource.Milliseconds;
            float elapsed = Math.Clamp((nowMilliseconds - state.LastCameraMilliseconds) / 1000f, 0f, 0.1f);
            state.LastCameraMilliseconds = nowMilliseconds;

            float rawX = _window.Input.Mouse.DeltaX;
            float rawY = _window.Input.Mouse.DeltaY;
            bool right = _window.Input.Mouse.IsDown(Input.MouseButton.Right);

            // Resolved per frame rather than cached, so the options menu's sensitivity slider takes
            // effect on the next frame instead of the next launch. It is two multiplies and a Pow.
            MouseLookSettings look = MouseLookSettings.FromProfile(_configuration.Active);
            if (HostedPlayer is not null)
            {
                HostedPlayer.LookSettings = look;
            }

            if (Viewpoint is { } lookViewpoint)
            {
                lookViewpoint.LookSettings = look;
            }

            if (state.Console.WantsPointer)
            {
                // The developer console is a pointer-driven tool: its panels are dragged and
                // resized. This branch comes first and is repeated every pass for the same reason
                // the pause menu's is -- the lock below would otherwise be re-asserted a frame
                // later and the cursor would snap back to the centre under the reader's hand.
                // HoldStill matters just as much here: taking the cursor back on close warps the
                // pointer, and without re-arming the settling frame that warp is read as aim.
                // The system cursor stays hidden: the console draws its own, as the shell does,
                // so the pointer looks the same everywhere and is drawn over the panels rather
                // than by the compositor on top of them.
                HostedPlayer?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (MenuOwnsPointer(_menuFlow.PauseMenu, _session.FailedMenu))
            {
                // A paused view does not turn, and the cursor has to be free to reach the menu. The
                // release is repeated every pass rather than done once on the way in, so nothing can
                // quietly take the pointer back while the menu is up; it also re-arms the look's
                // settling frame, so the warp that re-capturing the cursor produces on resume is not
                // read as a movement the player made.
                //
                // The mission-failed screen belongs here for the same reason. It arrives while the
                // player is dead and the death camera is orbiting, and EVERY branch below this one
                // takes the pointer: the orbit, and then -- because _playerMode outlives the player --
                // the look, whose Look() sets IsCapturingMouse and locks. The screen released the
                // cursor each frame and the branch below re-took it, which is the pointer that
                // flashed and would not move. Standing every camera branch down is what actually
                // settles it; excluding them one at a time only moves the fight further down.
                HostedPlayer?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (Viewpoint?.IsDeathViewActive == true)
            {
                // Dead: the mouse orbits the death camera around the body rather than turning a head that
                // is no longer taking input -- HumanCamera_DeathCameraControlInput. The cursor stays locked
                // so the orbit keeps turning however far the mouse is moved.
                Viewpoint?.ControlDeathView(rawX, rawY);
                _window.CursorLocked = !state.CaptureMode;

                // The look is not driving the view this frame, so it must not consider itself
                // capturing: whenever the orbit hands control back, the first frame of look is a
                // settling one and the re-lock's re-centring is absorbed rather than aimed with.
                HostedPlayer?.ReleaseLook();
            }
            else if (HostedPlayer is { IsOnLadder: true }
                or { IsInWorldInteraction: true, IsMountedStationaryGun: false })
            {
                // A mounted gun is not among these: its look turns the barrel, so it falls through
                // to the ordinary first-person look below.
                //
                // On a ladder the mouse orbits the third-person interaction camera around the climbing
                // body, as the original's mode-3 camera does -- HumanCamera_ThirdPersonControlInput -- so
                // you can look around while you climb rather than being locked behind the character.
                Viewpoint?.ControlInteractionView(rawX, rawY);
                _window.CursorLocked = !state.CaptureMode;
                HostedPlayer?.ReleaseLook();
            }
            else if (_computerOpen)
            {
                // The computer takes the pointer outright. Re-arming the look here is what stops
                // closing it from throwing the view: dropping the lock restores the desktop pointer,
                // and taking it back re-centres the backend's virtual one.
                _window.CursorLocked = false;
                HostedPlayer?.ReleaseLook();
            }
            else if (_mission.Cutscenes?.IsPlaying == true)
            {
                HostedPlayer?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (_playerMode && TitlePlayer is not null)
            {
                // A title's own player takes its look on the tick, as the original does; the frame only
                // gathers what the mouse and the keys did since the last one.
                GatherTitleInput();
                _window.CursorLocked = !state.CaptureMode;
            }
            else if (_playerMode && HostedPlayer is not null)
            {
                // Looking is per frame so the view is as smooth as the display allows; the body itself
                // moves on the fixed tick below, so it steps identically whatever the frame rate.
                HostedPlayer.Look(_window.Input, elapsed);
                _window.CursorLocked = !state.CaptureMode && HostedPlayer.IsCapturingMouse;
            }
            else if (_sunGizmo.Active)
            {
                // The sun gizmo owns the cursor while it is up: free it so its rings can be dragged, and
                // leave the camera where it is so a drag turns the sun rather than the view. The gizmo is
                // a free-camera tool, so this only ever runs while flying.
                _window.CursorLocked = false;
            }
            else
            {
                _camera.Update(ViewInput, elapsed);

                // The cursor is locked away while a mouse button is held, the way a scene editor's is,
                // so the view keeps turning however far the mouse is dragged. It comes back on release.
                _window.CursorLocked = !state.CaptureMode && _camera.IsCapturingMouse;
            }

            if (_options.LogCamera && (right || rawX != 0f || rawY != 0f))
            {
                _logger.Information(
                    "Camera",
                    $"rmb={(right ? 1 : 0)} raw=({rawX:0.##},{rawY:0.##}) pos=({_camera.Position.X:0.##},"
                        + $"{_camera.Position.Y:0.##},{_camera.Position.Z:0.##}) fwd={_camera.Camera.Orientation.Row3} "
                        + $"right={_camera.Camera.Orientation.Row1} dt={elapsed:0.####}");
            }
        }
    }

    /// <summary>Moves the free camera along the scripted flight a capture asked for, by the rendered-frame index.</summary>
    private void StepScriptedFlight(FrameLoopState state)
    {
        Debug.Assert(_camera is not null, "Flown only while a level is up; see RenderWorld.");


        // The scripted flight, driven off the rendered-frame index so the motion is exactly
        // reproducible under lockstep. The opening pose is captured once; each subsequent frame
        // moves the camera a straight fraction of the way to the end pose. An unset end component
        // holds the opening value, so -camend alone dollies without turning and -yawend alone pans
        // in place. Only in the free camera -- a flight forces -freecam, so _playerMode is false.
        if (_options.HasFlight && !_playerMode)
        {
            if (!state.FlightStarted)
            {
                state.FlightStartPos = _camera.Position;
                state.FlightStartYaw = _camera.Yaw;
                state.FlightStartPitch = _camera.Pitch;
                state.FlightStarted = true;
            }

            // -flightframes shortens the move without shortening the capture, so the frames past it
            // hold the end pose and the flight comes to a stop. The clamp is what holds it.
            int span = Math.Max(
                1,
                (_options.FlightFrames
                ?? (_options.IsBenchmark ? BenchmarkSchedule.MeasurementEndFrame : _options.ExitAfterFrames)) - 1);
            float t = Math.Clamp((float)(state.FlightFrame - _options.FlightDelay) / span, 0f, 1f);

            Vector3d endPos = _options.CameraEnd is { } e
                ? new Vector3d(e.X, e.Y, e.Z)
                : state.FlightStartPos;
            float endYaw = _options.CameraYawEnd ?? state.FlightStartYaw;
            float endPitch = _options.CameraPitchEnd ?? state.FlightStartPitch;

            (float shakeYaw, float shakePitch) = HandheldShake(state.FlightFrame, _options.CameraShake);
            if ((state.FlightFrame & 1) == 1)
            {
                shakeYaw += _options.CameraSwing * (MathF.PI / 180f);
            }

            _camera.MoveTo(
                new Vector3d(
                    state.FlightStartPos.X + ((endPos.X - state.FlightStartPos.X) * t),
                    state.FlightStartPos.Y + ((endPos.Y - state.FlightStartPos.Y) * t),
                    state.FlightStartPos.Z + ((endPos.Z - state.FlightStartPos.Z) * t)),
                state.FlightStartYaw + ((endYaw - state.FlightStartYaw) * t) + shakeYaw,
                state.FlightStartPitch + ((endPitch - state.FlightStartPitch) * t) + shakePitch);
        }
    }

    // The angular offset a hand holding a camera would add on a given frame, in radians.
    //
    // Modelled on a hand rather than on noise, because the two are not interchangeable for what this
    // tests. A hand drifts: slow wandering of about a degree with a faster, much smaller tremor riding on
    // it. Per-frame white noise would instead point somewhere new every frame, which no camera has ever
    // done and which a temporal filter would rightly treat as unreprojectable and discard -- so it would
    // measure the filter's give-up path rather than its reprojection.
    //
    // Summed sines at frequencies with no common multiple, so the pattern does not repeat over any
    // capture worth taking, driven off the FRAME INDEX rather than the clock so -lockstep keeps a run
    // repeatable. Weights sum to one per axis, which is what makes the amplitude mean what it says, and
    // the two axes carry different frequencies and phases so the motion wanders instead of tracking a
    // diagonal.
    private static (float Yaw, float Pitch) HandheldShake(int frame, float degrees)
    {
        if (degrees <= 0f)
        {
            return (0f, 0f);
        }

        // Hand tremor is quoted in Hz, so the frequencies below are meaningless without a rate to read
        // them against. The capture path steps at a fixed sixty under -lockstep.
        const float FramesPerSecond = 60f;
        float t = frame / FramesPerSecond;
        float amplitude = degrees * (MathF.PI / 180f);

        float yaw =
            (0.60f * MathF.Sin((2f * MathF.PI * 0.37f * t) + 0.0f))
            + (0.28f * MathF.Sin((2f * MathF.PI * 0.91f * t) + 1.7f))
            + (0.12f * MathF.Sin((2f * MathF.PI * 7.30f * t) + 4.2f));

        float pitch =
            (0.55f * MathF.Sin((2f * MathF.PI * 0.29f * t) + 2.4f))
            + (0.30f * MathF.Sin((2f * MathF.PI * 1.13f * t) + 5.1f))
            + (0.15f * MathF.Sin((2f * MathF.PI * 6.10f * t) + 0.9f));

        return (amplitude * yaw, amplitude * pitch);
    }
}

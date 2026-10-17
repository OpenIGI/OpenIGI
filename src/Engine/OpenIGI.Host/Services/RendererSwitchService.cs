using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The picture a renderer switch holds across: read back from the renderer being left, and shown by the one coming up
/// in place of the loading screen until the resumed mission is revealed.
/// </summary>
/// <param name="device">The device the picture is read from or shown on.</param>
/// <param name="logger">Where the switch is reported.</param>
internal sealed class RendererSwitchService(IGraphicsDevice device, IOpenIgiLogger logger) : IHostService
{
    // How many frames the switched-from picture is read over. The RHI copies the presented frame back only once asked
    // to, so the first read of a session is black and a frame or two later it holds the picture.
    private const int CaptureFrames = 3;

    // A switch asked for and waiting on its picture, and how many more frames to read it over.
    private RendererSwitch? _pending;
    private int _captureFramesLeft;

    // The switched-from picture, held on screen in place of the loading screen while the resumed mission loads and
    // settles.
    private TextureHandle _resumeFrame = TextureHandle.None;
    private Hold _hold;

    /// <summary>Where a resumed game is in holding the switched-from picture.</summary>
    private enum Hold
    {
        /// <summary>No picture held: loads show their own screen.</summary>
        None,

        /// <summary>The picture is on screen in place of the loading screen.</summary>
        Holding,

        /// <summary>The level has been revealed; the rest of this load draws nothing over it.</summary>
        Revealed,
    }

    /// <summary>Gets whether a switch has been asked for and is waiting on its picture.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>Gets whether the switched-from picture is on screen, while the view must hold still under it.</summary>
    public bool IsHolding => _hold == Hold.Holding;

    /// <inheritdoc/>
    public string Name => "Renderer switch";

    /// <summary>Asks for a switch, which completes once its picture has been read.</summary>
    /// <param name="request">Where the game is to be picked up again.</param>
    public void Request(RendererSwitch request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _pending = request;
        _captureFramesLeft = CaptureFrames;
        logger.Information("Renderer", $"Switching renderer from {Describe(request)}.");
    }

    /// <summary>Reads the picture a switch holds across; called at the end of every frame, after it has been presented.</summary>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <returns>The completed switch, with its picture, once it has one; otherwise <see langword="null"/>.</returns>
    public RendererSwitch? Advance(int width, int height)
    {
        if (_pending is not { } pending)
        {
            return null;
        }

        Bgra32[] pixels = new Bgra32[Math.Max(width * height, 0)];
        if (pixels.Length > 0)
        {
            device.CaptureFrame(width, height, pixels);
        }

        if (--_captureFramesLeft > 0)
        {
            return null;
        }

        _pending = null;
        return pending with
        {
            Frame = pixels.Length > 0 ? new RendererSwitchFrame(width, height, pixels) : null,
        };
    }

    /// <summary>Puts the picture the other renderer last showed up on this one, before anything else is.</summary>
    /// <param name="frame">The picture.</param>
    /// <param name="width">The window's width, which the picture must match.</param>
    /// <param name="height">The window's height.</param>
    /// <remarks>So the switch reads as that picture holding still rather than as a load.</remarks>
    public void HoldResumeFrame(RendererSwitchFrame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != width || frame.Height != height)
        {
            return;
        }

        _resumeFrame = device.CreateTexture(
            new TextureDescription(frame.Width, frame.Height, 1, TextureFilter.Point, TextureAddressMode.Clamp),
            frame.Pixels);
        _hold = Hold.Holding;

        device.Clear(Bgra32.Black);
        PaintResumeFrame(width, height);
        device.SwapBuffers();
    }

    /// <summary>Paints the held picture in place of the loading screen, where a switch is being resumed.</summary>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <returns><see langword="true"/> when the loading screen is not to be painted.</returns>
    public bool PaintResumeFrame(int width, int height)
    {
        switch (_hold)
        {
            case Hold.Holding:
                // The loading backdrop pass with every effect of it off -- no blur, grade, vignette, grain, lines,
                // bars or zoom -- is the picture as it was.
                device.DrawLoadingBackdrop(
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
                        AspectRatio: height > 0 ? width / (float)height : 1.777f,
                        Sweep: 0f));
                return true;

            case Hold.Revealed:
                return true;

            default:
                return false;
        }
    }

    /// <summary>Lets the held picture go as the level is revealed, and forgets it once the load has ended.</summary>
    /// <param name="revealed">Whether the level has been revealed.</param>
    /// <param name="loadActive">Whether the load sequence is still running.</param>
    public void AdvanceHold(bool revealed, bool loadActive)
    {
        if (_hold == Hold.Holding && revealed)
        {
            // A cut, not the load's dissolve: the eye goes from one renderer's picture straight to the other.
            _hold = Hold.Revealed;
        }

        if (_hold == Hold.Revealed && !loadActive)
        {
            _hold = Hold.None;
            device.DestroyTexture(_resumeFrame);
            _resumeFrame = TextureHandle.None;
        }
    }

    /// <summary>Describes where a switch picks the game up, for the log.</summary>
    /// <param name="request">The switch.</param>
    /// <returns>A line.</returns>
    public static string Describe(RendererSwitch request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Pose is { } pose
            ? FormattableString.Invariant(
                $"mission {request.MissionId} at hour {request.TimeHours:F2} {(pose.Flying ? "flying" : "standing")} at ({pose.Position.X:F2}, {pose.Position.Y:F2}, {pose.Position.Z:F2}) yaw {pose.Yaw:F3} pitch {pose.Pitch:F3}")
            : request.MissionId > 0 ? FormattableString.Invariant($"mission {request.MissionId} at hour {request.TimeHours:F2}") : "the menu";
    }

    /// <inheritdoc/>
    /// <remarks>Nothing of its own to free: a held picture is the device's, and goes with it.</remarks>
    public void Dispose()
    {
    }
}

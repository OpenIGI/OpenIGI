using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Forgetting the frames before this one, all at once.</summary>
/// <remarks>
/// <para>
/// Everything temporal in the frame believes this frame continues the last: the motion is measured against
/// the previous camera, the upscaler blends its history in, the traced shadows and the fog accumulate, the
/// occlusion reprojects, and an NVIDIA reconstruction keeps a history of its own. Across a cut none of that
/// is true, and each keeps showing the old view for as long as its history lasts.
/// </para>
/// <para>
/// So a reset is one request that reaches all of them, latched until the next scene capture consumes it,
/// and lasting exactly that frame: the motion is measured as none, the upscaler writes the frame without
/// blending, the accumulations restart, and a reconstruction is told to reset.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // What has asked for a reset since the last capture consumed one.
    private HistoryResetReason _historyResetPending;

    // Whether the frame being captured is one that forgets the past. Read by the temporal resolve and the
    // reconstruction; cleared once the temporal stage has run.
    private bool _historyResetThisFrame;

    /// <inheritdoc/>
    public void ResetHistory(HistoryResetReason reason)
    {
        ThrowIfDisposed();
        _historyResetPending |= reason;
    }

    /// <summary>Gets a value indicating whether this frame starts every history over.</summary>
    internal bool HistoryResetThisFrame => _historyResetThisFrame;

    /// <summary>How far the camera may move between two frames before they are taken to be unrelated, in metres.</summary>
    /// <remarks>
    /// Above anything the game moves a camera continuously — a jet in the third mission's ending covers a few
    /// metres a frame even at thirty — and far below any cut, teleport or respawn.
    /// </remarks>
    private const double DiscontinuityMetres = 25.0;

    /// <summary>How far the camera may turn between two frames before they are taken to be unrelated.</summary>
    /// <remarks>Cosine of 75 degrees: a turn that size in one frame leaves nothing of the old view worth keeping.</remarks>
    private const float DiscontinuityTurnCosine = 0.2588f;

    /// <summary>How much the field of view may change between two frames before the history is dropped.</summary>
    /// <remarks>A scope or the binoculars coming up is a jump of several times; a smooth zoom is a few percent a frame.</remarks>
    private const float DiscontinuityZoom = 1.25f;

    /// <summary>Applies a pending reset at the start of a scene capture.</summary>
    /// <remarks>
    /// The cuts the game says nothing about are caught here too: a camera that jumped further, turned further
    /// or zoomed further in one frame than anything continuous does. The device sees every frame's camera, so
    /// this catches them all, whichever of the game's many camera paths produced them.
    /// </remarks>
    private void ConsumeHistoryReset()
    {
        _historyResetPending |= DetectDiscontinuity();

        if (_historyResetPending == HistoryResetReason.None)
        {
            return;
        }

        if (_logger is { } logger && logger.IsEnabled(LogLevel.Debug, "Renderer"))
        {
            logger.Debug("Renderer", $"History reset: {_historyResetPending}.");
        }

        _historyResetPending = HistoryResetReason.None;
        _historyResetThisFrame = true;

        // The previous frame is this one: the motion field comes out zero, and the occlusion, which keys its
        // own history on the same flag, starts over with it.
        _hasPreviousFrame = false;

        _tracedSun?.Invalidate();
        _tracedLamps?.Invalidate();
        _volumetricFog?.Invalidate();
    }

    /// <summary>Compares this frame's camera with the previous one's.</summary>
    /// <returns>The reasons the two frames are unrelated, or none.</returns>
    private HistoryResetReason DetectDiscontinuity()
    {
        if (!_hasPreviousFrame)
        {
            return HistoryResetReason.None;
        }

        HistoryResetReason reasons = HistoryResetReason.None;

        double dx = _cameraPositionPrecise.X - _previousCameraPosition.X;
        double dy = _cameraPositionPrecise.Y - _previousCameraPosition.Y;
        double dz = _cameraPositionPrecise.Z - _previousCameraPosition.Z;
        if ((dx * dx) + (dy * dy) + (dz * dz) > DiscontinuityMetres * DiscontinuityMetres)
        {
            reasons |= HistoryResetReason.Discontinuity;
        }

        // The view direction is the rotation's third column, negated; the cosine between the two is their dot.
        float facing = (_viewToWorld.M13 * _previousViewToWorld.M13)
            + (_viewToWorld.M23 * _previousViewToWorld.M23)
            + (_viewToWorld.M33 * _previousViewToWorld.M33);
        if (facing < DiscontinuityTurnCosine)
        {
            reasons |= HistoryResetReason.Discontinuity;
        }

        float zoom = _unprojection.YScale / MathF.Max(_previousUnprojection.YScale, 1e-6f);
        if (zoom > DiscontinuityZoom || zoom < 1f / DiscontinuityZoom)
        {
            reasons |= HistoryResetReason.ProjectionChanged;
        }

        return reasons;
    }

    /// <summary>Ends the frame's reset, once everything temporal has run.</summary>
    private void EndHistoryReset() => _historyResetThisFrame = false;
}

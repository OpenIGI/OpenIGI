using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>How a frame accumulates into the ones before it, and what reconstructs it to the output size.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ITemporalDevice
{
    /// <summary>Sets how this frame accumulates into the ones before it.</summary>
    /// <param name="settings">
    /// The accumulation, or <see cref="TemporalAaSettings.Off"/> for none. Its jitter is the offset the
    /// caller has <em>already</em> applied to the projection it draws with; it is reported here so the
    /// backend can account for it, not so the backend can apply it.
    /// </param>
    /// <param name="projection">
    /// The scene's projection <em>without</em> the jitter. Reprojection has to compare two frames in one
    /// frame of reference, and the jitter is precisely what differs between them; the unjittered matrix
    /// is that shared reference. This is passed rather than read from
    /// <see cref="IDrawStateDevice.SetProjection"/> because the view model draws with a projection of its own, and by the
    /// time the frame resolves that is the one the device last saw.
    /// </param>
    /// <param name="cameraPosition">
    /// The camera's world position in metres, used to work out how far it has travelled since the
    /// previous frame. Supplied in double precision: this world's coordinates run to tens of thousands
    /// of units, and the per-frame travel is often a fraction of one, so the backend must difference two
    /// positions of that magnitude to recover it. Doing that in single precision loses the travel to
    /// cancellation and smears the reprojection under translation; passing it double lets the backend
    /// keep the subtraction exact and narrow only the small result to float.
    /// </param>
    /// <param name="viewToWorld">
    /// The rotation taking a view-space direction back into camera-relative world space.
    /// </param>
    /// <remarks>
    /// The backend keeps the previous frame's values itself, so a caller supplies only what is true
    /// now. A frame in which this is not called is a frame with no history, which is the correct
    /// behaviour on a camera cut.
    /// </remarks>
    void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3d cameraPosition,
        Matrix3f viewToWorld);

    /// <summary>Forgets every temporal history at the start of the next scene capture.</summary>
    /// <param name="reason">Why, for the log.</param>
    /// <remarks>
    /// The upscaler's history, the traced shadows' and the fog's accumulation, the occlusion's, and an
    /// NVIDIA reconstruction's own (its reset flag) all start over, and the frame's motion is measured as
    /// none. Reasons accumulate until the capture that consumes them; calling it twice costs nothing.
    /// </remarks>
    void ResetHistory(HistoryResetReason reason);

    /// <summary>Decides what the frame will be reconstructed with, and the render size that needs.</summary>
    /// <param name="request">What the settings ask for, and the output size.</param>
    /// <returns>
    /// What will run. An NVIDIA reconstruction the device cannot run comes back as the engine's own, with the
    /// reason; one it can, with the render size its runtime asked for.
    /// </returns>
    /// <remarks>Called once a frame before the scene target is sized; cheap when nothing has changed.</remarks>
    ReconstructionPlan PlanReconstruction(in ReconstructionRequest request);

    /// <summary>Gets a value indicating whether this frame's traced lighting goes to the frame unfiltered.</summary>
    /// <remarks>
    /// True while DLSS ray reconstruction is the denoiser: every traced light fires one ray a pixel from white
    /// noise, and the game's own indirect gather is asked for the same -- one ray a pixel, with none of its
    /// probe pooling or accumulation -- rather than its denoised probe grid.
    /// </remarks>
    bool RawLighting { get; }

    /// <summary>Gets each reconstruction kind with an empty string where it can run, and the reason where it cannot.</summary>
    /// <remarks>
    /// The engine's own is always available. The NVIDIA kinds need the optional runtime, started on an adapter
    /// that runs them; the settings page greys them out with the reason otherwise.
    /// </remarks>
    IReadOnlyDictionary<ReconstructionKind, string> ReconstructionSupport { get; }
}

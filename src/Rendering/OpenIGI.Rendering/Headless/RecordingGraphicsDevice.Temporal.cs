using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The temporal settings each frame was given, in order.</summary>
    public List<TemporalAaSettings> Temporal { get; } = [];

    /// <inheritdoc/>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3d cameraPosition,
        Matrix3f viewToWorld) => Record(Temporal, settings);

    /// <summary>Every history reset asked for, in order.</summary>
    public List<HistoryResetReason> HistoryResets { get; } = [];

    /// <inheritdoc/>
    public void ResetHistory(HistoryResetReason reason) => Record(HistoryResets, reason);

    /// <summary>Every reconstruction plan asked for, in order.</summary>
    public List<ReconstructionRequest> ReconstructionRequests { get; } = [];

    /// <summary>Gets or sets what <see cref="PlanReconstruction"/> answers; the engine's own unless a test says otherwise.</summary>
    public Func<ReconstructionRequest, ReconstructionPlan> ReconstructionAnswer { get; set; } = _ => ReconstructionPlan.Engine();

    /// <inheritdoc/>
    public ReconstructionPlan PlanReconstruction(in ReconstructionRequest request)
    {
        Record(ReconstructionRequests, request);
        return ReconstructionAnswer(request);
    }

    /// <inheritdoc/>
    public bool RawLighting { get; set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<ReconstructionKind, string> ReconstructionSupport { get; set; } =
        new Dictionary<ReconstructionKind, string> { [ReconstructionKind.Engine] = string.Empty };
}

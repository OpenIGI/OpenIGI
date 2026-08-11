using OpenIGI.Formats.Animation;

namespace OpenIGI.Desktop;

/// <summary>Limits a stopped animated pose to the two uploads needed by its interpolation buffers.</summary>
internal sealed class PoseUploadLatch
{
    private BonePose? _lastPose;
    private bool _duplicatePending;

    /// <summary>Reports whether this pose must be uploaded on the current tick.</summary>
    /// <remarks>
    /// A changed pose uploads immediately. The first unchanged tick uploads it once more so both the
    /// current and previous-pose buffers hold the stopped pose; subsequent unchanged ticks do no work.
    /// </remarks>
    public bool ShouldUpload(BonePose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);

        if (!ReferenceEquals(_lastPose, pose))
        {
            _lastPose = pose;
            _duplicatePending = true;
            return true;
        }

        if (_duplicatePending)
        {
            _duplicatePending = false;
            return true;
        }

        return false;
    }
}

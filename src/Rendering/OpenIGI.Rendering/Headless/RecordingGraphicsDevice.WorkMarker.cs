namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    private sealed class RecordingWorkMarker(RecordingGraphicsDevice device) : IGpuWorkMarker
    {
        /// <inheritdoc/>
        public bool IsComplete => device.SubmittedWorkComplete;
    }
}

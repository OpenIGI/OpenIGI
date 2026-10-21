using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    private sealed class RecordingWorkMarker(RecordingGraphicsDevice device) : IGpuWorkMarker
    {
        public bool IsComplete => device.SubmittedWorkComplete;
    }
}

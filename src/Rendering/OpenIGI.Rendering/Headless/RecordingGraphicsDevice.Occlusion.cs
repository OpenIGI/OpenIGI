using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    private uint _nextOcclusionQuery = 1;

    /// <inheritdoc/>
    public OcclusionQueryHandle CreateOcclusionQuery() => new(_nextOcclusionQuery++);

    /// <inheritdoc/>
    public bool OcclusionVisible(OcclusionQueryHandle query) => true;

    /// <inheritdoc/>
    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
    }

    /// <inheritdoc/>
    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
    }
}

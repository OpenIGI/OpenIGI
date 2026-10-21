using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    private uint _nextOcclusionQuery = 1;

    public OcclusionQueryHandle CreateOcclusionQuery() => new(_nextOcclusionQuery++);

    public bool OcclusionVisible(OcclusionQueryHandle query) => true;

    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
    }

    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
    }
}

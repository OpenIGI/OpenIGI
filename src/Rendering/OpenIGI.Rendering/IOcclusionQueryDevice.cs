using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>Asynchronous occlusion queries: a bounding box tested against the depth already drawn.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IOcclusionQueryDevice
{
    /// <summary>Creates one asynchronous occlusion query.</summary>
    OcclusionQueryHandle CreateOcclusionQuery();

    /// <summary>Returns the latest non-blocking result; unknown queries are conservatively visible.</summary>
    bool OcclusionVisible(OcclusionQueryHandle query);

    /// <summary>Issues a bounding-box visibility test against depth already drawn this frame.</summary>
    void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds);

    /// <summary>Destroys an asynchronous occlusion query.</summary>
    void DestroyOcclusionQuery(OcclusionQueryHandle query);
}

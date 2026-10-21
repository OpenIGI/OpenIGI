using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The shadow casters submitted, in order.</summary>
    public List<(MeshHandle Mesh, Matrix4f ModelView, int IndexCount)> ShadowCasters { get; } = [];

    /// <inheritdoc/>
    public void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount) =>
        ShadowCasters.Add((mesh, modelView, indexCount));

    /// <summary>Gets how many times the level's accumulated world has been released.</summary>
    public int WorldReleaseCount { get; private set; }

    /// <inheritdoc/>
    public void ReleaseWorld() => WorldReleaseCount++;
}

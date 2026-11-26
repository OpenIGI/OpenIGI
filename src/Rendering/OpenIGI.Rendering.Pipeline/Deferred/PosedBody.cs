using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// A body as it was drawn this frame, taken for a mask laid over the finished picture later in the frame: the binoculars'
/// silhouette, the thermal sensor's heat.
/// </summary>
/// <param name="Vertices">The pose it was drawn from.</param>
/// <param name="PreviousVertices">The pose before it, which the blend mixes with the first.</param>
/// <param name="Indices">The mesh's indices.</param>
/// <param name="Ranges">The index ranges that make up the model.</param>
/// <param name="Transform">Its model-view-projection, unjittered: the mask is laid over the finished picture.</param>
/// <param name="PoseBlend">How much of the previous pose is in the position drawn.</param>
/// <remarks>
/// The buffers are taken when the body is handed over rather than when the mask is drawn, so the mask is the pose that was
/// drawn whatever the pose ring does in between.
/// </remarks>
internal readonly record struct PosedBody(
    IRhiBuffer Vertices,
    IRhiBuffer PreviousVertices,
    IRhiBuffer Indices,
    MeshDrawRange[] Ranges,
    Matrix4f Transform,
    float PoseBlend)
{
    /// <summary>Draws the body's ranges, with whatever pipeline and push data the pass has set.</summary>
    /// <param name="pass">The open pass, whose pipeline reads the two pose streams.</param>
    public void Draw(IRhiRenderPassEncoder pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        pass.SetVertexBuffer(0, Vertices);
        pass.SetVertexBuffer(1, PreviousVertices);
        pass.SetIndexBuffer(Indices, RhiIndexFormat.Uint16);
        foreach (MeshDrawRange range in Ranges)
        {
            pass.DrawIndexed(range.IndexCount, 1, range.IndexOffset);
        }
    }
}

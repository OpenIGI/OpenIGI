using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The silhouette highlight: what the binoculars marked, laid over the finished picture. See <see cref="SilhouettePass"/>.</summary>
public sealed partial class GraphicsDevice
{
    private readonly List<SilhouetteDraw> _silhouetteDraws = [];
    private SilhouettePass? _silhouette;

    /// <inheritdoc/>
    /// <remarks>
    /// The buffers are taken now rather than when the highlight is drawn, so the mask is the pose that was drawn whatever the
    /// pose ring does in between. Not under a card capture, which records the world and not what is marked in it.
    /// </remarks>
    public void DrawSilhouette(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, SilhouetteGroup group)
    {
        ThrowIfDisposed();
        RhiMesh resource = Resolve(mesh);
        if (ranges.IsEmpty || CapturingCards)
        {
            return;
        }

        foreach (MeshDrawRange range in ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexCount);
            if (range.IndexOffset + range.IndexCount > resource.IndexCapacity)
            {
                throw new ArgumentException(
                    $"Indices {range.IndexOffset}..{range.IndexOffset + range.IndexCount} lie outside the mesh.",
                    nameof(ranges));
            }
        }

        // The world's projection as the temporal passes are handed it, without the frame's jitter: the finished picture has
        // resolved the jitter away, and a mask drawn with it crawled a part of a pixel every frame.
        Matrix4f steady = _temporalProjection;

        resource.MarkPoseSlotsBound(_frameCounter);
        _silhouetteDraws.Add(new SilhouetteDraw(
            resource.VertexBuffer,
            resource.PreviousVertexBuffer,
            resource.IndexBuffer,
            ranges.ToArray(),
            steady * modelView,
            _poseBlend,
            group));
    }

    /// <summary>Lays the frame's highlight over its finished picture, where anything was marked.</summary>
    /// <param name="target">The finished picture.</param>
    private void RecordSilhouette(IRhiTextureView target)
    {
        if (_silhouetteDraws.Count == 0)
        {
            return;
        }

        _silhouette ??= new SilhouettePass(_device, _retired);
        _silhouette.Record(
            Encoder(),
            target,
            Math.Min(target.Texture.Width, CurrentMode.Width),
            Math.Min(target.Texture.Height, CurrentMode.Height),
            _silhouetteDraws,
            _profiler.Time(RenderStage.ToneMapGrade, "silhouette"));
        _silhouetteDraws.Clear();
    }

    private void DisposeSilhouette()
    {
        _silhouette?.Dispose();
        _silhouette = null;
    }
}

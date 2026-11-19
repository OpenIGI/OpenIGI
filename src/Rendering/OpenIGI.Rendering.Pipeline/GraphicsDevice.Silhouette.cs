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
    /// <remarks>Not under a card capture, which records the world and not what is marked in it.</remarks>
    public void DrawSilhouette(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, SilhouetteGroup group)
    {
        ThrowIfDisposed();
        if (TakeBody(mesh, ranges, modelView) is { } body)
        {
            _silhouetteDraws.Add(new SilhouetteDraw(body, group));
        }
    }

    /// <summary>Takes a body as it was drawn, for a mask laid over the finished picture later in the frame.</summary>
    /// <param name="mesh">The body's mesh, in the pose currently set.</param>
    /// <param name="ranges">The index ranges that make up the body.</param>
    /// <param name="modelView">Its transform into view space.</param>
    /// <returns>The body, or null where there is nothing to take: no ranges, or a card capture.</returns>
    /// <exception cref="ArgumentException">A range lies outside the mesh.</exception>
    private PosedBody? TakeBody(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView)
    {
        RhiMesh resource = Resolve(mesh);
        if (ranges.IsEmpty || CapturingCards)
        {
            return null;
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
        return new PosedBody(
            resource.VertexBuffer,
            resource.PreviousVertexBuffer,
            resource.IndexBuffer,
            ranges.ToArray(),
            steady * modelView,
            _poseBlend);
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

using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The bodies of I.G.I. 2's infrared view, through a shader of their own. See <see cref="InfraredPipelineCache"/>.</summary>
/// <remarks>
/// Drawn into whichever image the frame is in: the lit image during a capture's forward pass, over the lit and resolved
/// scene the original never lit them into; the frame itself on the direct path. Anything else -- a shadow cascade, a card
/// capture, the geometry pass -- draws none.
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The texture factor's alpha the see-through draw's coverage is the ramp's times (<c>D3DRS_TEXTUREFACTOR 0x80FFFFFF</c>).</summary>
    private const float InfraredSeenThroughCoverage = 128f / 255f;

    private InfraredPipelineCache? _infraredDirect;
    private InfraredPipelineCache? _infraredForward;

    /// <inheritdoc/>
    public void DrawInfrared(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, TextureHandle ramp, InfraredDraw draw)
    {
        ThrowIfDisposed();

        if (ranges.IsEmpty || !ramp.IsValid || DrawingShadow || CapturingCards)
        {
            return;
        }

        bool seenThrough = draw == InfraredDraw.SeenThrough;
        MeshDrawCall call = Call(mesh, 0, 0, ramp, TextureHandle.None, seenThrough ? BlendMode.AlphaBlend : BlendMode.Opaque);

        Span<float> push = stackalloc float[InfraredPipelineCache.PushDataFloats];
        (_projection * modelView).CopyColumnMajorTo(push);
        push[16] = modelView.M11;
        push[17] = modelView.M12;
        push[18] = modelView.M13;
        push[20] = modelView.M21;
        push[21] = modelView.M22;
        push[22] = modelView.M23;
        push[24] = modelView.M31;
        push[25] = modelView.M32;
        push[26] = modelView.M33;
        push[28] = _poseBlend;
        push[29] = seenThrough ? InfraredSeenThroughCoverage : 1f;

        if (Capturing)
        {
            if (CurrentPass is not ScenePass.Forward || _scene is not { } scene)
            {
                return;
            }

            // Under ray reconstruction, the opaque scene as it stood before the first transparent draw.
            TakeColourBeforeTransparency(scene, transparent: seenThrough);
            if (_scenePass is not { } pass)
            {
                return;
            }

            _infraredForward ??= new InfraredPipelineCache(
                _device,
                _materialLayout,
                SceneTargetSet.FormatOf(SceneTargetId.Lit),
                SceneTargetSet.FormatOf(SceneTargetId.Depth));

            // Over the linear lit image, divided by the exposure the resolve will multiply back.
            push[30] = 1f / MathF.Max(_sceneExposure, 1e-3f);
            push[31] = 1f;
            _meshes.DrawRanges(pass, _infraredForward.For(draw), call, ranges, MemoryMarshal.AsBytes(push), _frameCounter);
            return;
        }

        _infraredDirect ??= new InfraredPipelineCache(_device, _materialLayout, PresentFormat, DepthFormat);

        // The direct path stores display values and is never tone mapped.
        push[30] = 1f;
        push[31] = 0f;
        _meshes.DrawRanges(Pass(), _infraredDirect.For(draw), call, ranges, MemoryMarshal.AsBytes(push), _frameCounter);
    }

    private void DisposeInfrared()
    {
        _infraredForward?.Dispose();
        _infraredForward = null;
        _infraredDirect?.Dispose();
        _infraredDirect = null;
    }
}

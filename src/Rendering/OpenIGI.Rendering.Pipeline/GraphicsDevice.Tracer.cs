using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The rounds' tracers, through a shader of their own rather than the world's forward one.</summary>
/// <remarks>
/// <para>
/// The forward mesh shader fogs every translucent draw toward the fog's colour by its depth, which is right for
/// the world and wrong for a tracer: the original switches Direct3D's fog off for its line (<c>0x50DCC0</c>), and
/// a guard's line drawn in the colour of mission 1's fog, over mission 1's fogged scene, was not there at all.
/// It also had no blend that both laid the retail colour over the scene and raised the lit image's alpha, which
/// the temporal resolve reads to keep a one-tick line out of its history. See <see cref="TracerPipelineCache"/>.
/// </para>
/// <para>
/// Drawn into whichever image the frame is in: the lit image during a capture's forward pass, the frame itself
/// on the direct path. Anything else -- a shadow cascade, a card capture, the geometry pass -- draws none.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private TracerPipelineCache? _tracerDirect;
    private TracerPipelineCache? _tracerForward;

    /// <inheritdoc/>
    public void DrawTracers(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TracerStyle style,
        float radiance)
    {
        ThrowIfDisposed();

        if (indexCount <= 0 || DrawingShadow || CapturingCards)
        {
            return;
        }

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, TextureHandle.None, TextureHandle.None, BlendMode.Additive);

        Span<float> push = stackalloc float[TracerPipelineCache.PushDataFloats];
        (_projection * modelView).CopyColumnMajorTo(push);
        push[16] = style == TracerStyle.Retail ? 0f : 1f;
        push[17] = radiance;

        if (Capturing)
        {
            if (CurrentPass is not ScenePass.Forward || _scene is not { } scene)
            {
                return;
            }

            // Under ray reconstruction, the opaque scene as it stood before the first transparent draw.
            TakeColourBeforeTransparency(scene, transparent: true);
            if (_scenePass is not { } pass)
            {
                return;
            }

            _tracerForward ??= new TracerPipelineCache(
                _device,
                _materialLayout,
                SceneTargetSet.FormatOf(SceneTargetId.Lit),
                SceneTargetSet.FormatOf(SceneTargetId.Depth));

            // Over the linear lit image, divided by the exposure the resolve will multiply back.
            push[18] = 1f / MathF.Max(_sceneExposure, 1e-3f);
            push[19] = 1f;
            _meshes.Draw(pass, _tracerForward.For(style), call, MemoryMarshal.AsBytes(push), _frameCounter);
            return;
        }

        _tracerDirect ??= new TracerPipelineCache(_device, _materialLayout, PresentFormat, DepthFormat);

        // The direct path stores display values and is never tone mapped.
        push[18] = 1f;
        push[19] = 0f;
        _meshes.Draw(Pass(), _tracerDirect.For(style), call, MemoryMarshal.AsBytes(push), _frameCounter);
    }

    private void DisposeTracers()
    {
        _tracerForward?.Dispose();
        _tracerForward = null;
        _tracerDirect?.Dispose();
        _tracerDirect = null;
    }
}

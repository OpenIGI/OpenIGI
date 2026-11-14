using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The places a multiplayer mode plays for, marked in the world through a shader of their own (<see cref="ModePointPipelineCache"/>).</summary>
/// <remarks>
/// Drawn as the tracers are, into whichever image the frame is in: the lit image during a capture's forward pass (in linear
/// light, divided by the exposure the resolve multiplies back), the frame itself on the direct path. A shadow cascade, a card
/// capture or the geometry pass draws none. One draw a point, a disc and a low wall, a handful of triangles: every preset
/// draws it.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private ModePointPipelineCache? _modePointDirect;
    private ModePointPipelineCache? _modePointForward;

    /// <inheritdoc/>
    public void DrawModePoint(MeshHandle mesh, int indexOffset, int indexCount, in Matrix4f modelView, in ModePointLook look)
    {
        ThrowIfDisposed();
        if (indexCount <= 0 || DrawingShadow || CapturingCards)
        {
            return;
        }

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, TextureHandle.None, TextureHandle.None, BlendMode.Additive);
        Span<float> push = stackalloc float[ModePointPipelineCache.PushDataFloats];
        (_projection * modelView).CopyColumnMajorTo(push);
        push[16] = look.Red;
        push[17] = look.Green;
        push[18] = look.Blue;
        push[19] = look.Brightness;
        push[20] = look.Letter;
        push[21] = look.Seconds;
        push[22] = look.Alarm;

        if (Capturing)
        {
            if (CurrentPass is not ScenePass.Forward || _scene is not { } scene)
            {
                return;
            }

            TakeColourBeforeTransparency(scene, transparent: true);
            if (_scenePass is not { } pass)
            {
                return;
            }

            _modePointForward ??= new ModePointPipelineCache(
                _device, _materialLayout, SceneTargetSet.FormatOf(SceneTargetId.Lit), SceneTargetSet.FormatOf(SceneTargetId.Depth));
            push[23] = 1f / MathF.Max(_sceneExposure, 1e-3f);
            push[24] = 1f;
            _meshes.Draw(pass, _modePointForward.Pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter);
            return;
        }

        _modePointDirect ??= new ModePointPipelineCache(_device, _materialLayout, PresentFormat, DepthFormat);
        push[23] = 1f;
        push[24] = 0f;
        _meshes.Draw(Pass(), _modePointDirect.Pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter);
    }

    private void DisposeModePoints()
    {
        _modePointForward?.Dispose();
        _modePointForward = null;
        _modePointDirect?.Dispose();
        _modePointDirect = null;
    }
}

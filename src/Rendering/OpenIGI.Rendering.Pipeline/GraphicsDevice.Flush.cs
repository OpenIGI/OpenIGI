using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Finishing what a frame has recorded so far, in the middle of it, so it can be read back.</summary>
/// <remarks>
/// <para>
/// A frame's work is recorded as it goes and submitted when the frame ends, so something asked for part
/// way through -- a capture of the frame as drawn so far, a diagnostic reading a compute result -- has
/// nothing it could wait for. A flush submits what has been recorded, has the device send it and wait for
/// it, and then carries on: whichever pass was open is opened again over what it had drawn, so the draws
/// after the flush land where they would have.
/// </para>
/// <para>
/// It stalls the host on the GPU, which is what the callers want and what nothing drawn every frame should
/// do.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>Submits the frame recorded so far, with anything else to record first, and waits for it.</summary>
    /// <param name="record">Work to record after what the frame has drawn and before the flush, outside any pass.</param>
    /// <exception cref="InvalidOperationException">No frame is being recorded.</exception>
    private void FlushFrame(Action<IRhiCommandEncoder>? record = null)
    {
        if (!_inFrame)
        {
            throw new InvalidOperationException("There is no frame being recorded to flush.");
        }

        IRhiTextureView? shadowLayer = _shadowPass is not null ? _shadowLayer : null;
        ScenePass? scenePass = _scenePass is not null ? CurrentPass : null;

        CloseShadowPass();
        CloseScenePass();
        ClosePass();

        record?.Invoke(Encoder());

        SealEncoder("frame.flush");
        _device.Submit(CollectionsMarshal.AsSpan(_sealed));
        _sealed.Clear();
        _device.Flush();
        ArmRecordedWorkMarkers();

        // Picked up again where it broke off. The frame's own pass opens by itself at the next draw into
        // it, loading what is there, so only a scene pass or a cascade has to be reopened here.
        if (shadowLayer is not null)
        {
            OpenShadowLayer(shadowLayer, _shadowLayerSize, _shadowLayerLabel, _shadowRotation, resume: true);
        }
        else if (scenePass is { } pass && _scene is { } scene)
        {
            switch (pass)
            {
                case ScenePass.Geometry:
                    BeginGeometryPass(scene, resume: true);
                    break;

                case ScenePass.GlassLayer:
                    BeginGlassLayerPass(scene, resume: true);
                    break;

                case ScenePass.Forward:
                    BeginForwardPass(scene);
                    break;
            }
        }
    }
}

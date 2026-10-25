using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <content>The draw clip: a scissor narrowing the scene's and the frame's passes to a part of the view.</content>
public sealed partial class GraphicsDevice
{
    private ViewClip? _drawClip;

    /// <inheritdoc/>
    /// <remarks>
    /// Applied to whichever pass is open -- the scene's, or the frame's -- and to every one opened after, by narrowing the scissor
    /// each sets with its viewport. The shadow maps' pass sets its own and is never narrowed: a light's view has no window onto the
    /// outside.
    /// </remarks>
    public void SetDrawClip(in ViewClip? clip)
    {
        if (_drawClip == clip)
        {
            return;
        }

        _drawClip = clip;
        if (_scenePass is { } scene)
        {
            ApplySceneViewport(scene);
        }
        else if (_pass is { } frame)
        {
            ApplyViewport(frame);
        }
    }

    // The viewport's own scissor, narrowed by the draw clip when one is set.
    private void SetClippedScissor(IRhiRenderPassEncoder pass, int x, int y, int width, int height)
    {
        if (_drawClip is { } clip)
        {
            (x, y, width, height) = clip.ToScissor(x, y, width, height);
        }

        pass.SetScissor(x, y, width, height);
    }
}

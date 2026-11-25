using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>A race's ghost, laid over the lit scene before the lens. See <see cref="GhostPass"/>.</summary>
public sealed partial class GraphicsDevice
{
    private readonly List<GhostBodyDraw> _ghostDraws = [];
    private GhostPass? _ghost;
    private GhostLook _ghostLook;
    private TextureHandle _ghostRamp;

    // The texels of the ramp a ghost drawn straight into the frame is coloured by.
    private const int GhostRampTexels = 64;

    /// <inheritdoc/>
    /// <remarks>
    /// Taken as the thermal sensor's bodies are (<see cref="DrawThermalBody"/>): the pose as drawn, the unjittered projection.
    /// Not for a shadow: a ghost casts none.
    /// </remarks>
    public void DrawGhost(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, in GhostLook look)
    {
        ThrowIfDisposed();
        if (DrawingShadow)
        {
            return;
        }

        // Nothing of it there yet, or any more.
        if (look.Presence <= 0f)
        {
            return;
        }

        // A race always draws its scene offscreen, on every preset, so the ghost looks the same on all of them. Only a frame
        // that could not be -- one drawn while the surface cache's cards are -- has it drawn straight into the frame as it
        // goes, as I.G.I. 2's infrared view draws a body: unlit, its edges bright and its middle faint, over what is behind it.
        if (!Capturing)
        {
            DrawInfrared(mesh, ranges, modelView, GhostRamp(), InfraredDraw.SeenThrough);
            return;
        }

        if (TakeBody(mesh, ranges, modelView) is not { } body)
        {
            return;
        }

        _ghostLook = look;
        _ghostDraws.Add(new GhostBodyDraw(
            body,
            new System.Numerics.Vector4(modelView.M11, modelView.M12, modelView.M13, modelView.M14),
            new System.Numerics.Vector4(modelView.M21, modelView.M22, modelView.M23, modelView.M24),
            new System.Numerics.Vector4(modelView.M31, modelView.M32, modelView.M33, modelView.M34)));
    }

    /// <summary>Lays the frame's ghost over the lit scene, where any of it was drawn.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The lit scene the lens is about to read.</param>
    /// <param name="exposure">The exposure the resolve will apply this frame.</param>
    /// <returns>The picture with the ghost on it, or null where there was none.</returns>
    private IRhiTextureView? RecordGhost(SceneTargetSet scene, IRhiTextureView colour, float exposure)
    {
        if (_ghostDraws.Count == 0)
        {
            return null;
        }

        _ghost ??= new GhostPass(_device, FramesInFlight, _retired);
        (int width, int height) = PostSize(scene);
        _ghost.RecordMask(Encoder(), width, height, _ghostDraws, _ghostLook);
        _ghostDraws.Clear();

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "ghost"),
            "ghost"));
        return _ghost.RecordPicture(
            pass,
            colour,
            scene.View(SceneTargetId.Depth),
            new GhostFrame(width, height, scene.Width, scene.Height, _unprojection, exposure),
            _ghostLook,
            FrameSlot);
    }

    /// <summary>
    /// The ramp a ghost drawn straight into the frame is coloured by, made the first time one is: across it, from a face turned to
    /// the eye to one edge-on (the infrared shader's ramp coordinate), the ghost's green (<see cref="GhostPalette"/>), near clear,
    /// brightening to its near-white and near opaque at the rim, which is the ghost's outline.
    /// </summary>
    private TextureHandle GhostRamp()
    {
        if (_ghostRamp.IsValid)
        {
            return _ghostRamp;
        }

        Span<Bgra32> texels = stackalloc Bgra32[GhostRampTexels];
        for (int i = 0; i < GhostRampTexels; i++)
        {
            float rim = MathF.Pow(i / (float)(GhostRampTexels - 1), 1.5f);
            float white = rim * rim * rim;
            texels[i] = new Bgra32(
                Byte(float.Lerp(GhostPalette.Tint.B, GhostPalette.Hot.B, white)),
                Byte(float.Lerp(GhostPalette.Tint.G, GhostPalette.Hot.G, white)),
                Byte(float.Lerp(GhostPalette.Tint.R, GhostPalette.Hot.R, white)),
                Byte(0.12f + (0.88f * rim * rim)));
        }

        _ghostRamp = _textures.Create(
            new TextureDescription(GhostRampTexels, 1, 1, TextureFilter.Linear, TextureAddressMode.Clamp),
            texels,
            "ghost.ramp");
        return _ghostRamp;

        static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
    }

    private void DisposeGhost()
    {
        _ghost?.Dispose();
        _ghost = null;
        _ghostDraws.Clear();
    }
}

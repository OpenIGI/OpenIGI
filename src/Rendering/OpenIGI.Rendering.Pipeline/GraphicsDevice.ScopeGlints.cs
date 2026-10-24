using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Snipers' scopes catching the light: stars of streaks added over the picture.</summary>
/// <remarks>
/// A full-screen pass, seven glints a draw, added with one-one blending into whatever the 2D drawing goes to -- the picture, or
/// the interface layer of a frame drawn apart -- so it only ever brightens. The glints are procedural (<c>Ui/ScopeGlint.slang</c>):
/// nothing is sampled.
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>How many glints one draw carries, as the shader's push block holds them.</summary>
    public const int GlintsPerDraw = 7;

    private FullscreenPipeline? _glint;
    private IRhiBindGroupLayout? _glintLayout;
    private ShaderBindings? _glintBindings;

    /// <inheritdoc/>
    public void DrawScopeGlints(ReadOnlySpan<ScopeGlint> glints, float seconds)
    {
        ThrowIfDisposed();
        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        if (!_inFrame || glints.IsEmpty || width <= 1 || height <= 1)
        {
            return;
        }

        BuildGlint();
        ClosePass();
        bool clearInterface = DrawsIntoLayer && _layerClearPending;
        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(FrameDrawTarget, clearInterface ? RhiLoadOp.Clear : RhiLoadOp.Load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.UserInterface, "scope.glint"),
            Label: "scope.glint")))
        {
            pass.SetViewport(0, 0, width, height, 0f, 1f);
            pass.SetScissor(0, 0, width, height);
            for (int first = 0; first < glints.Length; first += GlintsPerDraw)
            {
                GlintPush push = GlintPush.From(glints[first..Math.Min(glints.Length, first + GlintsPerDraw)], width, height, seconds);
                _glint!.Draw(pass, _glintBindings!.GroupFor(0), MemoryMarshal.AsBytes(new ReadOnlySpan<GlintPush>(in push)));
            }

            pass.EndPass();
        }

        if (clearInterface)
        {
            _layerClearPending = false;
        }
    }

    private void BuildGlint()
    {
        if (_glint is not null)
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ScopeGlint);
        _glintLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
            "scope.glint"));
        RhiBlendComponent add = new(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add);
        RhiBlendComponent keep = new(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add);
        _glint = new FullscreenPipeline(_device, ShaderModuleId.ScopeGlint, _glintLayout, PresentFormat, blend: new RhiBlendState(add, keep));
        _glintBindings = new ShaderBindings(_device, reflection, [_glintLayout], RhiShaderStages.Fragment, "scope.glint");
    }

    private void DisposeGlint()
    {
        _glintBindings?.Dispose();
        _glint?.Dispose();
        _glintLayout?.Dispose();
    }

    /// <summary>The glint pass's push block, as <c>Ui/ScopeGlint.slang</c> lays it out: the frame, then seven glints.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GlintPush
    {
        private float _width, _height, _seconds, _count;
        private Glints _glints;

        public static GlintPush From(ReadOnlySpan<ScopeGlint> glints, int width, int height, float seconds)
        {
            GlintPush push = new() { _width = width, _height = height, _seconds = seconds, _count = glints.Length };
            for (int i = 0; i < glints.Length; i++)
            {
                push._glints[i] = glints[i];
            }

            return push;
        }

        [System.Runtime.CompilerServices.InlineArray(GlintsPerDraw)]
        private struct Glints
        {
            private ScopeGlint _first;
        }
    }
}

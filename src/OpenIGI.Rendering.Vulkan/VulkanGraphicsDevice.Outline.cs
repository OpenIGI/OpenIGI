using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Meshes;
using OpenIGI.Rendering.Vulkan.Pipelines;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The selection glow: a mesh marked out by a soft rim of light around its silhouette.</summary>
/// <remarks>
/// <para>
/// The classic inverted hull, three passes deep. A mask lays the model's own silhouette into depth and
/// writes no colour; a stack of shells then draws the same model pushed outwards in screen space, each
/// one wider and fainter than the last, testing against that mask so the glow stops at the model rather
/// than washing across it. See <see cref="Shaders.OutlineShaderSource"/> for the push itself.
/// </para>
/// <para>
/// Every piece of state it needs is baked into its own pipelines rather than set on the frame's, so
/// there is nothing to put back afterwards: a pass that outlines something mid-stream leaves the frame
/// exactly as it found it, apart from the bindings the helpers already track.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    /// <summary>How many shells the glow is built from.</summary>
    private const int GlowShells = 8;

    /// <summary>How sharply the glow fades from the silhouette outwards.</summary>
    /// <remarks>
    /// Above one the light hugs the model and trails off, which is what a glow looks like; at one it is
    /// a linear ramp and reads as a flat band with a visible outer edge.
    /// </remarks>
    private const float GlowFalloff = 2.5f;

    /// <summary>The depth the mask is pinned to, in the reverse-Z the depth buffer holds.</summary>
    /// <remarks>
    /// Just <em>inside</em> the near plane, never on it. Clip space keeps what satisfies
    /// <c>0 &lt;= z &lt;= w</c>, so a triangle pinned to exactly <c>w</c> sits on the boundary and the
    /// clipper's own rounding decides whether it survives — which shows up as a mask full of holes and a
    /// glow washing across the middle of the model wherever one appears.
    /// </remarks>
    private const float MaskDepth = 1f - 1e-5f;

    /// <summary>The depth a see-through shell is pinned to.</summary>
    /// <remarks>
    /// A hair behind the mask, and that gap is the whole mechanism: the shells pass the depth test
    /// against the world, which is further back still, and fail it against the mask, which is the
    /// model's own silhouette. Sharing the mask's depth would compare equal and reject everything.
    /// </remarks>
    private const float GlowDepth = 1f - 1e-4f;

    /// <inheritdoc/>
    /// <remarks>
    /// Drawn into whichever render pass the caller is already inside — the deferred path's forward
    /// subpass over the lit scene, or the present pass on the direct path — because a marker belongs
    /// over the picture it marks. Outside any pass there is nothing to draw into and the call is a
    /// no-op rather than an error: a caller that outlines a selection has no way to know which pass the
    /// frame happens to be between.
    /// </remarks>
    public unsafe void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        ThrowIfDisposed();

        VulkanMesh resource = Resolve(mesh);
        if (ranges.IsEmpty || radius <= 0f || colour.A == 0)
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

        if (!TryGetOpenPass(out RenderPass renderPass, out uint subpass))
        {
            return;
        }

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;
        Matrix4f mvp = _projection * modelView;

        // The viewport the push measures a pixel against. The scene's own, under a render scale, rather
        // than the window's -- a rim measured against the wrong one is the wrong number of pixels wide.
        float viewportWidth = MathF.Max(1f, _deferredPassActive ? _captureWidth : _viewport.Width);
        float viewportHeight = MathF.Max(1f, _deferredPassActive ? _captureHeight : _viewport.Height);

        // A rim of a fixed pixel width is what a marker wants CLOSE UP, and the wrong thing entirely once
        // the subject is small: a soldier sixty metres off stands about thirty pixels tall, and seven
        // pixels of glow either side is a bright lozenge with a man somewhere inside it. So the width is
        // also capped against the subject's own scale -- a third of a metre at its distance, projected --
        // which leaves it untouched at the ranges the marker is read at and thins it to a proportionate
        // line at the ranges where it was swallowing what it marks.
        float viewDistance = MathF.Max(MathF.Abs(modelView.M34), 0.1f);
        float metrePixels = MathF.Abs(_projection.M22) * viewportHeight * 0.5f / viewDistance;
        float width = MathF.Min(radius, MathF.Max(metrePixels * 0.35f, 1f));

        // The glow is written into the SCENE-REFERRED lit buffer, which the resolve then exposes and tone
        // maps. A colour authored in display terms and written raw is at the mercy of the eye adaptation:
        // in a dark scene the exposure is high, and an amber marker at full strength came out white and
        // bloomed. Divided by the exposure the resolve will apply, it lands at the colour it was asked
        // for whatever the scene is doing -- the same correction the first-person rig's lighting takes,
        // and for the same reason. The direct path draws straight to a display-encoded target, where the
        // colour already means what it says.
        float exposure = _deferredPassActive
            ? MathF.Max(RenderCVars.EffectiveExposure * _lastAdaptedExposure, 1e-3f)
            : 1f;

        float* push = stackalloc float[OutlinePipeline.PushSize / sizeof(float)];
        mvp.CopyColumnMajorTo(new Span<float>(push, 16));
        push[24] = viewportWidth;
        push[25] = viewportHeight;
        push[26] = 0f;
        push[27] = 0f;

        BindMeshBuffers(resource);

        // The mask. Width zero draws the model where it really is, pinned just inside the near plane so
        // the shells behind it have something to fail against.
        BindGraphicsPipeline(_outline.ForPass(renderPass, subpass, OutlinePass.Mask));
        push[16] = 0f; push[17] = 0f; push[18] = 0f; push[19] = 0f;
        push[20] = 0f; push[21] = _poseBlend; push[22] = 1f; push[23] = MaskDepth;
        vk.CmdPushConstants(
            cmd, _outline.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)OutlinePipeline.PushSize, push);
        DrawOutlineRanges(resource, ranges);

        // Cull whichever facing the frame is currently KEEPING, so each shell contributes one layer.
        // Asking for a fixed facing would double the coverage wherever the pass in force had already
        // inverted the winding, and the glow would come out twice as strong on those models.
        OutlinePass shell = _cullMode == CullMode.Clockwise
            ? OutlinePass.ShellCullBack
            : OutlinePass.ShellCullFront;
        BindGraphicsPipeline(_outline.ForPass(renderPass, subpass, shell));

        // Pinned, the shells clear any wall between them and the camera; left at their own depth the
        // world occludes them, which is the "only when visible" mode.
        push[22] = throughWalls ? 1f : 0f;
        push[23] = GlowDepth;

        // Widest and faintest first, so each shell blends over the ones outside it. What the stack has to
        // add up to is a curve, not a set of even steps: full strength against the model and nothing at
        // all at the far edge. So this tracks how opaque the glow already is at each ring and asks the
        // next shell only for the difference -- solving a * (1 - covered) = wanted - covered. Setting
        // every shell to the same fraction instead piles opacity into the first two rings and reads as a
        // solid band with a hard edge, which is what a stack of rings looks like.
        float covered = 0f;
        for (int ring = GlowShells; ring >= 1; ring--)
        {
            float fraction = ring / (float)GlowShells;
            float wanted = MathF.Pow(1f - fraction + (1f / GlowShells), GlowFalloff);
            float step = Math.Clamp((wanted - covered) / MathF.Max(1e-3f, 1f - covered), 0f, 1f);
            covered = wanted;

            push[16] = colour.R / 255f / exposure;
            push[17] = colour.G / 255f / exposure;
            push[18] = colour.B / 255f / exposure;
            push[19] = step * (colour.A / 255f);
            push[20] = width * fraction;

            vk.CmdPushConstants(
                cmd, _outline.PipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)OutlinePipeline.PushSize, push);
            DrawOutlineRanges(resource, ranges);
        }

        // The glow binds pipelines of its own, so whatever the frame had bound is gone.
        InvalidateBoundState();
    }

    /// <summary>Issues one pass of the glow over every range of the mesh.</summary>
    private void DrawOutlineRanges(VulkanMesh mesh, ReadOnlySpan<MeshDrawRange> ranges)
    {
        foreach (MeshDrawRange range in ranges)
        {
            if (range.IndexCount <= 0)
            {
                continue;
            }

            _instance.Vk.CmdDrawIndexed(
                _frameCommandBuffer, (uint)range.IndexCount, 1, (uint)range.IndexOffset, 0, 0);
        }
    }

    /// <summary>Finds the render pass and subpass the frame is currently recording into.</summary>
    /// <param name="renderPass">Receives the pass.</param>
    /// <param name="subpass">Receives the subpass index within it.</param>
    /// <returns><see langword="false"/> when no pass is open, and there is nothing to draw into.</returns>
    /// <remarks>
    /// A pipeline is built against one render pass, and the glow is drawn from wherever the caller is —
    /// so which one it is has to be asked rather than assumed. The geometry pass is deliberately not
    /// offered: it writes a G-buffer, and a flat colour written into one is not a glow but a surface
    /// claiming to be that colour.
    /// </remarks>
    private bool TryGetOpenPass(out RenderPass renderPass, out uint subpass)
    {
        if (_deferredPassActive && _deferredStage is DeferredStage.Lighting or DeferredStage.Forward)
        {
            renderPass = _deferred.ShadingPass;
            subpass = Deferred.DeferredRenderPass.ForwardSubpass;
            return true;
        }

        if (_presentPassActive)
        {
            renderPass = _presentPassResumes ? _presentTarget.ResumeRenderPass : _presentTarget.RenderPass;
            subpass = 0;
            return true;
        }

        renderPass = default;
        subpass = 0;
        return false;
    }
}

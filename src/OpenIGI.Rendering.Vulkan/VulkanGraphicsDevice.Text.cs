using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Pipelines;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Rendering.Vulkan.Textures;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>Distance-field text for <see cref="VulkanGraphicsDevice"/>.</summary>
public sealed partial class VulkanGraphicsDevice : ISdfTextDevice
{
    /// <summary>Where the fragment stage's push block starts: after the vertex stage's projection.</summary>
    private const uint SdfPushOffset = 16;

    /// <summary>Four vec4s: range, outline, glow, glow width.</summary>
    private const uint SdfPushBytes = sizeof(float) * 16;

    private Sprite2DPipeline? _sdfText2D;

    /// <inheritdoc/>
    /// <remarks>
    /// The 2D sprite path with the distance-field fragment stage in place of the plain one: the same
    /// vertex layout, the same geometry ring, the same per-texture descriptor set. Built on first use,
    /// so a session that never draws SDF text never compiles the shader.
    /// </remarks>
    public unsafe void DrawText2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend)
    {
        ThrowIfDisposed();
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("The index count must be a multiple of three.", nameof(indices));
        }

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            return;
        }

        EnsurePresentPass();

        _sdfText2D ??= new Sprite2DPipeline(
            _instance.Vk,
            _device,
            _presentTarget.RenderPass,
            ShaderModuleId.SdfText,
            SdfPushOffset);

        int frame = _frameLoop.CurrentFrame;
        _sprite2DGeometry.Append(frame, vertices, indices, out int vertexOffset, out uint firstIndex);

        uint textureKey = atlas != TextureHandle.None && _textures.ContainsKey(atlas.Id)
            ? atlas.Id
            : _whiteTexture.Id;
        VulkanTexture resolved = ResolveTextureOrWhite(atlas);
        DescriptorSet descriptorSet = _textureDescriptors.Get(textureKey, resolved);

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _sdfText2D.ForBlend(blend));
        InvalidateBoundState();

        Buffer vertexBuffer = _sprite2DGeometry.VertexBuffer(frame);
        ulong vertexBufferOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &vertexBufferOffset);
        vk.CmdBindIndexBuffer(cmd, _sprite2DGeometry.IndexBuffer(frame), 0, IndexType.Uint16);

        vk.CmdBindDescriptorSets(
            cmd, PipelineBindPoint.Graphics, _sdfText2D.PipelineLayout, 0, 1, &descriptorSet, 0, null);

        float* projection = stackalloc float[2] { _projectionWidth, _projectionHeight };
        vk.CmdPushConstants(cmd, _sdfText2D.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 2, projection);

        float* text = stackalloc float[16];
        text[0] = parameters.PxRange;
        text[1] = parameters.Dissolve;
        text[2] = parameters.DissolveSeed;
        text[3] = 0f;
        text[4] = parameters.OutlineColor.R / 255f;
        text[5] = parameters.OutlineColor.G / 255f;
        text[6] = parameters.OutlineColor.B / 255f;
        text[7] = parameters.OutlineWidth;
        text[8] = parameters.GlowColor.R / 255f;
        text[9] = parameters.GlowColor.G / 255f;
        text[10] = parameters.GlowColor.B / 255f;
        text[11] = parameters.GlowIntensity;
        text[12] = parameters.GlowWidth;
        text[13] = 0f;
        text[14] = 0f;
        text[15] = 0f;
        vk.CmdPushConstants(cmd, _sdfText2D.PipelineLayout, ShaderStageFlags.FragmentBit, SdfPushOffset, SdfPushBytes, text);

        vk.CmdDrawIndexed(cmd, (uint)indices.Length, 1, firstIndex, vertexOffset, 0);
    }
}

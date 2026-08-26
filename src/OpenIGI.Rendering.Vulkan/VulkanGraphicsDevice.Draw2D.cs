using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The two-dimensional draw path for <see cref="VulkanGraphicsDevice"/>.</summary>
public sealed partial class VulkanGraphicsDevice
{
    private readonly HashSet<string> _warned = [];

    /// <inheritdoc/>
    public unsafe void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
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

        // Two-dimensional drawing is the HUD and menus, which land on the back buffer after the scene
        // has resolved; it opens the present pass rather than any capture pass.
        EnsurePresentPass();

        int frame = _frameLoop.CurrentFrame;
        _sprite2DGeometry.Append(frame, vertices, indices, out int vertexOffset, out uint firstIndex);

        uint textureKey = texture != TextureHandle.None && _textures.ContainsKey(texture.Id)
            ? texture.Id
            : _whiteTexture.Id;
        VulkanTexture resolved = ResolveTextureOrWhite(texture);
        DescriptorSet descriptorSet = _textureDescriptors.Get(textureKey, resolved);

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _sprite2D.ForBlend(blend));

        Buffer vertexBuffer = _sprite2DGeometry.VertexBuffer(frame);
        ulong vertexBufferOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &vertexBufferOffset);
        vk.CmdBindIndexBuffer(cmd, _sprite2DGeometry.IndexBuffer(frame), 0, IndexType.Uint16);

        vk.CmdBindDescriptorSets(
            cmd,
            PipelineBindPoint.Graphics,
            _sprite2D.PipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        float* projection = stackalloc float[2] { _projectionWidth, _projectionHeight };
        vk.CmdPushConstants(cmd, _sprite2D.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 2, projection);

        vk.CmdDrawIndexed(cmd, (uint)indices.Length, 1, firstIndex, vertexOffset, 0);
    }

    private void WarnOnce(string key, string message)
    {
        if (_warned.Add(key))
        {
            _logger.Warning("Vulkan", message);
        }
    }
}

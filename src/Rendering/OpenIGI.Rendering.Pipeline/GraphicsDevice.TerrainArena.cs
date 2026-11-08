using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The ground a texture and a pass at a time: see <see cref="ITerrainArenaDevice"/>.</summary>
public sealed partial class GraphicsDevice : ITerrainArenaDevice
{
    // Made the first time a chunk is put in, so a device that never draws the original's ground never pays for it.
    private TerrainArena? _terrainArena;
    private IRhiBindGroupLayout? _terrainArenaLayout;
    private Mesh3DPipelineCache? _terrainPipelines;

    /// <inheritdoc/>
    /// <remarks>
    /// The frame's own pass alone: a scene capture, a cascade and a card page each draw the ground their own way, and the
    /// arena's table holds the eye's placements.
    /// </remarks>
    public bool TerrainArenaReady => !_disposed && !Capturing && !DrawingShadow && !CapturingCards;

    /// <inheritdoc/>
    public int AddTerrainChunk(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices, ReadOnlySpan<TerrainArenaPart> parts)
    {
        ThrowIfDisposed();
        return Arena().Add(vertices, indices, parts);
    }

    /// <inheritdoc/>
    public void RemoveTerrainChunk(int slot)
    {
        ThrowIfDisposed();
        _terrainArena?.Remove(slot);
    }

    /// <inheritdoc/>
    public bool BeginTerrainArena()
    {
        ThrowIfDisposed();
        return TerrainArenaReady && Arena().Begin(_frameCounter);
    }

    /// <inheritdoc/>
    /// <remarks>The block is the one <see cref="DrawMesh"/> would push for the chunk, so the vertex stage sees the same bits.</remarks>
    public void PlaceTerrainChunk(int slot, in Matrix4f modelView)
    {
        ThrowIfDisposed();

        if (_terrainArena is not { } arena)
        {
            return;
        }

        Span<float> block = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(block, modelView, BlendMode.Opaque, hasLightmap: false, exposure: 1f);
        arena.Place(slot, block);
    }

    /// <inheritdoc/>
    public void DrawTerrainBucket(int bucket, TextureHandle diffuse, BlendMode blend)
    {
        ThrowIfDisposed();

        if (!TerrainArenaReady || _terrainArena is not { } arena)
        {
            return;
        }

        arena.Upload();
        if (!arena.TryBucket(bucket, out IRhiBuffer indices, out int count))
        {
            return;
        }

        // The block a chunk's own draw would push but for its transform rows, which the vertex stage takes from the
        // chunk's placement instead.
        Span<float> push = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(push, Matrix4f.Identity, blend, hasLightmap: false, exposure: 1f);
        (bool reflective, IRhiBindGroup environment) = EnvironmentFor();
        IRhiRenderPipeline pipeline = _terrainPipelines!.ForState(_depthState, _cullMode, blend, _alphaReference, reflective);

        IRhiRenderPassEncoder pass = Pass();
        pass.SetPipeline(pipeline);
        pass.SetBindGroup(0, _meshes.Material(diffuse, TextureHandle.None, DetailFor(diffuse)));
        pass.SetBindGroup(1, environment);
        pass.SetBindGroup(2, arena.Group);
        pass.SetPushData(MemoryMarshal.AsBytes(push));

        // A chunk has no previous pose: the second stream is the first again, and the delta the stage forms is zero.
        pass.SetVertexBuffer(0, arena.VertexBuffer);
        pass.SetVertexBuffer(1, arena.VertexBuffer);
        pass.SetVertexBuffer(2, arena.ChunkBuffer);
        pass.SetIndexBuffer(indices, RhiIndexFormat.Uint32);
        pass.DrawIndexed(count, 1, 0);
    }

    private TerrainArena Arena()
    {
        if (_terrainArena is { } arena)
        {
            return arena;
        }

        _terrainArenaLayout = TerrainArena.CreateLayout(_device);
        _terrainPipelines = new Mesh3DPipelineCache(
            _device,
            _materialLayout,
            _cubeLayout,
            PresentFormat,
            DepthFormat,
            _retired,
            _terrainArenaLayout);
        _terrainPipelines.SetFog(_fog.Colour, _fog.DensityPerMetre);
        _terrainPipelines.SetSun(_sceneSunAmbient, _sceneSunColour);
        _terrainArena = new TerrainArena(_device, _terrainArenaLayout, _retired);
        return _terrainArena;
    }

    private void DisposeTerrainArena()
    {
        _terrainArena?.Dispose();
        _terrainPipelines?.Dispose();
        _terrainArenaLayout?.Dispose();
    }
}

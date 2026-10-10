using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>A scene with nothing in it, for the frames before the world has one.</summary>
/// <remarks>
/// <para>
/// A shader that traces rays names an acceleration structure, and a descriptor set has no way to say
/// "there is not one yet" — every binding it declares must be written with something legal before the
/// dispatch. The world's own structure is not ready until the level has been drawn once and its geometry
/// collected, which leaves a handful of frames at the start of every level with nothing to bind.
/// </para>
/// <para>
/// So there is always this: one triangle, far enough away and small enough that no ray of any length used
/// here can reach it. Binding it is the same as binding an empty world — every trace misses — but it is a
/// real structure, which is what the descriptor needs. The alternative is a second shader for the frames
/// before the world exists, which is a great deal of machinery for two seconds of level load.
/// </para>
/// </remarks>
internal sealed class PlaceholderScene : IDisposable
{
    // Far outside any level, and degenerate besides. Nothing traces this far, and if anything did the
    // triangle has no area to hit.
    private const float Away = 1e9f;

    private readonly IRhiBuffer _vertices;
    private readonly IRhiBuffer _indices;
    private readonly RayTracingScene _scene;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="PlaceholderScene"/> class, and builds it.</summary>
    /// <param name="device">The device the structure lives on.</param>
    /// <param name="structures">Its acceleration structures.</param>
    /// <param name="clock">The frame clock the build is dated by.</param>
    public PlaceholderScene(IRhiDevice device, IRhiAccelerationStructures structures, SubmissionClock clock)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(clock);

        const RhiBufferUsage usage = RhiBufferUsage.RayTracingInput | RhiBufferUsage.MapWrite;

        _vertices = device.CreateBuffer(new RhiBufferDescriptor(3 * 3 * sizeof(float), usage, "placeholder vertices"));
        device.WriteBuffer<float>(_vertices, 0, [Away, Away, Away, Away, Away, Away, Away, Away, Away]);

        // Four-byte multiple, as a raw view of an index buffer has to be.
        _indices = device.CreateBuffer(new RhiBufferDescriptor(sizeof(uint) * 3, usage, "placeholder indices"));
        device.WriteBuffer<ushort>(_indices, 0, [0, 1, 2]);

        _scene = new RayTracingScene(device, structures, instanceCapacity: 1);

        BottomLevelStructure geometry = _scene.AddGeometry(
        [
            new RhiTriangleGeometry(
                _vertices,
                VertexStride: 3 * sizeof(float),
                VertexCount: 3,
                _indices,
                FirstIndex: 0,
                PrimitiveCount: 1),
        ]);

        _scene.SetInstances(
        [
            RhiAccelerationInstance.Create(
                Matrix4f.Identity,
                customIndex: 0,
                (byte)RayMask.All,
                RhiAccelerationInstanceOptions.TriangleCullDisable,
                geometry.InstanceReference),
        ]);

        // Built at once, on its own: the tracing shaders bind a structure from their first dispatch, before
        // the world has seen a draw.
        clock.Advance();
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("placeholder scene");
        _scene.RecordUpdate(encoder, clock);
        device.Submit([encoder.Finish()]);
    }

    /// <summary>Gets the structure, which every ray misses.</summary>
    public IRhiAccelerationStructure? TopLevel => _scene.TopLevel;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Dispose();
        _indices.Dispose();
        _vertices.Dispose();
    }
}

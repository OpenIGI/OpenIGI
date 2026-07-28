using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that records what it is asked to draw instead of drawing it.
/// </summary>
/// <remarks>
/// Everything above the backend — batching, sprite placement, text layout — is testable without a GL
/// context, and testing it here means those tests run in CI where no GPU exists.
/// </remarks>
public sealed class RecordingGraphicsDevice : IGraphicsDevice
{
    private uint _nextTexture = 1;
    private uint _nextMesh = 1;

    public event EventHandler? DeviceRestored;

    public List<DrawCall> Draws { get; } = [];

    public List<MeshDrawCall> MeshDraws { get; } = [];

    public Dictionary<uint, (Vertex3D[] Vertices, ushort[] Indices)> Meshes { get; } = [];

    public Matrix4f Projection { get; private set; } = Matrix4f.Identity;

    public DepthState DepthState { get; private set; } = DepthState.Disabled;

    public CullMode CullMode { get; private set; } = CullMode.None;

    public List<Bgra32> Clears { get; } = [];

    public int SwapCount { get; private set; }

    public GraphicsCapabilities Capabilities { get; } = new(2, 4096, true, "recording");

    public DisplayMode CurrentMode { get; private set; } = DisplayMode.Default;

    public bool VSyncEnabled { get; set; }

    public IReadOnlyList<DisplayMode> EnumerateDisplayModes() => [CurrentMode];

    public void SetMode(in DisplayMode mode) => CurrentMode = mode;

    public void UnsetMode()
    {
    }

    public void Restore() => DeviceRestored?.Invoke(this, EventArgs.Empty);

    public void Clear(Bgra32 color) => Clears.Add(color);

    public void SwapBuffers() => SwapCount++;

    public void SetViewport(in Viewport viewport)
    {
    }

    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void CaptureFrame(int width, int height, Span<Bgra32> destination) => destination.Clear();

    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels) =>
        new(_nextTexture++);

    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void DestroyTexture(TextureHandle texture)
    {
    }

    public void SetTexture(int unit, TextureHandle texture)
    {
    }

    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend) =>
        Draws.Add(new DrawCall(vertices.ToArray(), indices.ToArray(), texture, blend));

    public void Set2DProjection(int width, int height)
    {
    }

    public MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        MeshHandle handle = new(_nextMesh++);
        Meshes[handle.Id] = (vertices.ToArray(), indices.ToArray());
        return handle;
    }

    public void DestroyMesh(MeshHandle mesh) => Meshes.Remove(mesh.Id);

    public void SetDepthState(DepthState state) => DepthState = state;

    public void SetCullMode(CullMode mode) => CullMode = mode;

    public void SetProjection(in Matrix4f projection) => Projection = projection;

    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend) =>
        MeshDraws.Add(new MeshDrawCall(mesh, indexOffset, indexCount, modelView, diffuse, lightmap, blend, DepthState, CullMode));

    public void Dispose()
    {
    }

    public sealed record DrawCall(Vertex2D[] Vertices, ushort[] Indices, TextureHandle Texture, BlendMode Blend)
    {
        public int QuadCount => Vertices.Length / 4;
    }

    public sealed record MeshDrawCall(
        MeshHandle Mesh,
        int IndexOffset,
        int IndexCount,
        Matrix4f ModelView,
        TextureHandle Diffuse,
        TextureHandle Lightmap,
        BlendMode Blend,
        DepthState DepthState,
        CullMode CullMode)
    {
        public int TriangleCount => IndexCount / 3;
    }
}

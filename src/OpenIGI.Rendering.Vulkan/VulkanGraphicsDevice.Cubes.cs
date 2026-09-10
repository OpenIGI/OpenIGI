using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Textures;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The cube maps a level bakes, and the state that says which one a run of draws reflects.
/// </summary>
/// <remarks>
/// Their own partial for the reason the compute resources have theirs: a cube is a different kind of
/// resource from a two-dimensional texture — six layers, one mip, no anisotropy, faces arriving one at
/// a time — and threading that through <see cref="VulkanGraphicsDevice.CreateTexture"/> would have made
/// every caller of the common path pay attention to a case none of them have.
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    // Cubes are handed out on their own numbering so a handle that reaches the wrong resolver is
    // rejected rather than mistaken for a texture that happens to share an id.
    private const uint FirstCubeId = 30_000;

    private readonly Dictionary<uint, VulkanCubeTexture> _cubes = [];
    private uint _nextCubeId = FirstCubeId;
    private TextureHandle _environmentCube;

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name)
    {
        ThrowIfDisposed();
        if (edge <= 0)
        {
            throw new GraphicsDeviceException($"A cube '{name}' has an edge of {edge}, which is not a length.");
        }

        VulkanCubeTexture cube = new(_instance.Vk, _device, _allocator, _commands, edge);
        uint id = _nextCubeId++;
        _cubes[id] = cube;
        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        ResolveCube(cube).UpdateFace(face, pixels);
    }

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
        ThrowIfDisposed();
        _environmentCube = cube;
    }

    /// <summary>Gets the cube a reflective draw should sample, if one is set and known.</summary>
    /// <param name="cube">Receives the cube.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    /// <remarks>
    /// A binding a layout declares has to be written whether or not there is anything to say, so the
    /// caller pairs this with a placeholder — the same arrangement <c>ShelterMapView</c> uses, and for
    /// the same reason.
    /// </remarks>
    internal bool TryGetEnvironmentCube(out VulkanCubeTexture? cube)
    {
        if (_environmentCube.IsValid && _cubes.TryGetValue(_environmentCube.Id, out VulkanCubeTexture? found))
        {
            cube = found;
            return true;
        }

        cube = null;
        return false;
    }

    private void DestroyCubes()
    {
        foreach (VulkanCubeTexture cube in _cubes.Values)
        {
            cube.Dispose();
        }

        _cubes.Clear();
        _environmentCube = TextureHandle.None;
    }

    private VulkanCubeTexture ResolveCube(TextureHandle handle)
    {
        if (!handle.IsValid || !_cubes.TryGetValue(handle.Id, out VulkanCubeTexture? cube))
        {
            throw new GraphicsDeviceException($"{handle} is not a cube map of this device.");
        }

        return cube;
    }
}

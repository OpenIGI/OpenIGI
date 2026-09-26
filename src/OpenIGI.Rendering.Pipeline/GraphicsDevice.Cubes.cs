using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The cube maps a level bakes, and the state that says which one a run of draws reflects.</summary>
/// <remarks>
/// <para>
/// A cube is six faces of one edge and one level, filled a face at a time; <see cref="SetEnvironmentCube"/>
/// names the one the draws that follow reflect, and a draw reflects exactly when one is named and known --
/// the reflective variant of the forward pipeline, with the cube bound in its second group. Every other draw
/// binds the white stand-in, which no variant that does not reflect ever samples.
/// </para>
/// <para>
/// Stored as sRGB, like every texture on this path: the sampler decodes and the shader, told the sampler
/// decodes, puts the encoding back, so a reflection composites at the encoding its faces were authored in.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private const int CubeFaces = 6;

    private readonly Dictionary<uint, EnvironmentCube> _cubes = [];
    private TextureHandle _environmentCube;

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name)
    {
        ThrowIfDisposed();

        if (edge <= 0)
        {
            throw new GraphicsDeviceException($"A cube '{name}' has an edge of {edge}, which is not a length.");
        }

        IRhiTexture texture = _device.CreateTexture(new RhiTextureDescriptor(
            edge,
            edge,
            RhiTextureFormat.Bgra8UnormSrgb,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: CubeFaces,
            Dimension: RhiTextureViewDimension.Cube,
            Label: name));

        // Black until a face is written: a reflection is ADDED to the surface, and a face nobody filled
        // should add nothing.
        Bgra32[] black = new Bgra32[edge * edge];

        for (int face = 0; face < CubeFaces; face++)
        {
            WriteCubeFace(texture, face, black);
        }

        IRhiTextureView view = texture.CreateView(new RhiTextureViewDescriptor(
            ArrayLayerCount: CubeFaces,
            Dimension: RhiTextureViewDimension.Cube,
            Label: name));

        IRhiBindGroup group = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            _cubeLayout,
            [RhiBindGroupEntry.Of(0, view), RhiBindGroupEntry.Of(1, _cubeSampler)],
            name));

        // From the texture table's own sequence, so no texture handle can ever be mistaken for a cube.
        TextureHandle handle = _textures.Reserve();
        _cubes[handle.Id] = new EnvironmentCube(texture, group, edge);
        return handle;
    }

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();

        if (!cube.IsValid || !_cubes.TryGetValue(cube.Id, out EnvironmentCube found))
        {
            throw new GraphicsDeviceException($"{cube} is not a cube map of this device.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, CubeFaces);

        if (pixels.Length < found.Edge * found.Edge)
        {
            throw new ArgumentException(
                $"A face of a {found.Edge}-texel cube needs {found.Edge * found.Edge} pixels, not {pixels.Length}.",
                nameof(pixels));
        }

        WriteCubeFace(found.Texture, face, pixels[..(found.Edge * found.Edge)]);
    }

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
        ThrowIfDisposed();
        _environmentCube = cube;
    }

    /// <summary>What a forward draw reflects: whether it reflects at all, and the group it binds either way.</summary>
    /// <returns>The named cube's group when there is one, the stand-in's otherwise.</returns>
    private (bool Reflective, IRhiBindGroup Group) EnvironmentFor() =>
        _environmentCube.IsValid && _cubes.TryGetValue(_environmentCube.Id, out EnvironmentCube cube)
            ? (true, cube.Group)
            : (false, _cubeGroup);

    private void WriteCubeFace(IRhiTexture texture, int face, ReadOnlySpan<Bgra32> pixels) =>
        _device.WriteTexture(
            new RhiTextureRegion(texture, 0, new RhiOrigin3D(0, 0, face)),
            pixels,
            texture.Width * 4,
            new RhiExtent3D(texture.Width, texture.Height));

    private void DisposeCubes()
    {
        foreach (EnvironmentCube cube in _cubes.Values)
        {
            cube.Group.Dispose();
            cube.Texture.Dispose();
        }

        _cubes.Clear();
        _environmentCube = TextureHandle.None;
    }

    /// <summary>One cube: its image, the group that binds it, and its edge.</summary>
    private readonly record struct EnvironmentCube(IRhiTexture Texture, IRhiBindGroup Group, int Edge);
}

using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// How far every point in a box around a model is from that model's surface, and which side it is on.
/// </summary>
/// <remarks>
/// <para>
/// The structure the realtime GI track traces through. A ray marched against triangles has to test
/// every triangle it might cross; a ray marched against a signed distance field asks one question at
/// each step — <em>how far is the nearest surface</em> — and takes a step exactly that long, which
/// cannot overshoot. A ray crossing empty space clears it in a handful of steps regardless of how much
/// geometry is nearby, and that is what makes tracing a whole level affordable.
/// </para>
/// <para>
/// <b>Dense rather than sparse, deliberately.</b> The usual layout stores only bricks near the surface,
/// because most of a volume around an open-world mesh is empty and storing it is waste. I.G.I.'s models
/// are crates, doors, huts and gantries: at the resolution below a whole model is a few tens of
/// kilobytes, and the entire game's model set is smaller than one of its texture packs. Sparse bricks
/// would buy memory this game does not need and cost an indirection on every one of the millions of
/// samples a frame takes.
/// </para>
/// <para>
/// <b>Distances are stored as bytes.</b> A sphere trace needs to know how far it may step, not the
/// distance to the millimetre — an error of a fraction of a voxel costs a step, never correctness, and
/// the alternative is four times the memory and four times the bandwidth on the hottest read in the
/// renderer.
/// </para>
/// </remarks>
public sealed class MeshDistanceField
{
    /// <summary>Initialises a new instance of the <see cref="MeshDistanceField"/> class.</summary>
    /// <param name="origin">The volume's minimum corner, in the model's own space.</param>
    /// <param name="voxelSize">How much space one voxel spans on each axis.</param>
    /// <param name="sizeX">Voxels along x.</param>
    /// <param name="sizeY">Voxels along y.</param>
    /// <param name="sizeZ">Voxels along z.</param>
    /// <param name="range">The distance that encodes to the extremes of a byte.</param>
    /// <param name="distances">The encoded distances, x fastest then y then z.</param>
    /// <exception cref="ArgumentNullException"><paramref name="distances"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The array does not match the dimensions.</exception>
    public MeshDistanceField(
        Vector3f origin,
        Vector3f voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        float range,
        byte[] distances)
    {
        ArgumentNullException.ThrowIfNull(distances);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeZ);

        if (distances.Length != sizeX * sizeY * sizeZ)
        {
            throw new ArgumentException(
                $"A {sizeX}x{sizeY}x{sizeZ} field needs {sizeX * sizeY * sizeZ} values, got {distances.Length}.",
                nameof(distances));
        }

        Origin = origin;
        VoxelSize = voxelSize;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        Range = range;
        Distances = distances;
    }

    /// <summary>Gets the volume's minimum corner, in the model's own space.</summary>
    public Vector3f Origin { get; }

    /// <summary>Gets how much space one voxel spans on each axis.</summary>
    public Vector3f VoxelSize { get; }

    /// <summary>Gets the voxel count along x.</summary>
    public int SizeX { get; }

    /// <summary>Gets the voxel count along y.</summary>
    public int SizeY { get; }

    /// <summary>Gets the voxel count along z.</summary>
    public int SizeZ { get; }

    /// <summary>Gets the distance that encodes to the extremes of the stored byte range.</summary>
    /// <remarks>
    /// Everything beyond it is clamped, which is why a trace treats a fully saturated voxel as "at
    /// least this far" rather than as an exact answer.
    /// </remarks>
    public float Range { get; }

    /// <summary>Gets the encoded distances, x fastest then y then z.</summary>
    public byte[] Distances { get; }

    /// <summary>Gets the volume's maximum corner, in the model's own space.</summary>
    public Vector3f Extent => new(
        Origin.X + (VoxelSize.X * SizeX),
        Origin.Y + (VoxelSize.Y * SizeY),
        Origin.Z + (VoxelSize.Z * SizeZ));

    /// <summary>Gets how many voxels the field holds.</summary>
    public int VoxelCount => SizeX * SizeY * SizeZ;

    /// <summary>Encodes a signed distance into the stored byte range.</summary>
    /// <param name="distance">The signed distance. Negative is inside the surface.</param>
    /// <param name="range">The distance that maps to the extremes.</param>
    /// <returns>The encoded value.</returns>
    /// <remarks>
    /// Centred on 128 so the sign survives, which is the whole point of a <em>signed</em> field: a trace
    /// that cannot tell inside from outside cannot tell a wall from the air beside it.
    /// </remarks>
    public static byte Encode(float distance, float range)
    {
        float normalised = (Math.Clamp(distance / range, -1f, 1f) * 0.5f) + 0.5f;
        return (byte)Math.Clamp((int)MathF.Round(normalised * 255f), 0, 255);
    }

    /// <summary>Decodes a stored value back to a signed distance.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="range">The distance that maps to the extremes.</param>
    /// <returns>The signed distance. Negative is inside the surface.</returns>
    public static float Decode(byte value, float range) => (((value / 255f) * 2f) - 1f) * range;

    /// <summary>Reads the signed distance at a voxel, clamping to the volume.</summary>
    /// <param name="x">The voxel's x coordinate.</param>
    /// <param name="y">The voxel's y coordinate.</param>
    /// <param name="z">The voxel's z coordinate.</param>
    /// <returns>The signed distance. Negative is inside the surface.</returns>
    /// <remarks>
    /// Clamped rather than wrapped, which is the opposite of what the texture derivation does and for
    /// the opposite reason: a volume around a model does not tile, and the space past its edge is more
    /// of the same emptiness rather than the far side of the model.
    /// </remarks>
    public float At(int x, int y, int z)
    {
        int cx = Math.Clamp(x, 0, SizeX - 1);
        int cy = Math.Clamp(y, 0, SizeY - 1);
        int cz = Math.Clamp(z, 0, SizeZ - 1);

        return Decode(Distances[(((cz * SizeY) + cy) * SizeX) + cx], Range);
    }

    /// <summary>Samples the field at a point in the model's own space, trilinearly.</summary>
    /// <param name="point">The point to sample.</param>
    /// <returns>The signed distance there. Negative is inside the surface.</returns>
    /// <remarks>
    /// The CPU counterpart of what a <c>sampler3D</c> does for free on the GPU, and it exists for the
    /// same reason the bake is testable at all: an answer that can only be obtained by rendering a frame
    /// is an answer nothing can assert against.
    /// </remarks>
    public float Sample(Vector3f point)
    {
        float gx = ((point.X - Origin.X) / VoxelSize.X) - 0.5f;
        float gy = ((point.Y - Origin.Y) / VoxelSize.Y) - 0.5f;
        float gz = ((point.Z - Origin.Z) / VoxelSize.Z) - 0.5f;

        int x0 = (int)MathF.Floor(gx);
        int y0 = (int)MathF.Floor(gy);
        int z0 = (int)MathF.Floor(gz);

        float fx = gx - x0;
        float fy = gy - y0;
        float fz = gz - z0;

        float c000 = At(x0, y0, z0);
        float c100 = At(x0 + 1, y0, z0);
        float c010 = At(x0, y0 + 1, z0);
        float c110 = At(x0 + 1, y0 + 1, z0);
        float c001 = At(x0, y0, z0 + 1);
        float c101 = At(x0 + 1, y0, z0 + 1);
        float c011 = At(x0, y0 + 1, z0 + 1);
        float c111 = At(x0 + 1, y0 + 1, z0 + 1);

        float x00 = c000 + ((c100 - c000) * fx);
        float x10 = c010 + ((c110 - c010) * fx);
        float x01 = c001 + ((c101 - c001) * fx);
        float x11 = c011 + ((c111 - c011) * fx);

        float y0z = x00 + ((x10 - x00) * fy);
        float y1z = x01 + ((x11 - x01) * fy);

        return y0z + ((y1z - y0z) * fz);
    }

    /// <summary>Returns the field's dimensions and memory footprint.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        $"MeshDistanceField {SizeX}x{SizeY}x{SizeZ} range={Range:0.##} ({VoxelCount / 1024} KiB)";
}

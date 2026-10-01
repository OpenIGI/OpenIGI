using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Every model's distance field packed into one volume, so a shader can reach all of them.
/// </summary>
/// <remarks>
/// <para>
/// A shader cannot hold an array of textures the way the CPU holds an array of fields. It can hold one
/// volume and an index, so the fields are laid side by side in a single 3D texture and each one records
/// where its corner sits. A trace that wants the field of model seventeen reads its slot and offsets
/// every sample by it.
/// </para>
/// <para>
/// <b>A uniform grid of slots, not a tight packing.</b> The fields differ in shape — a lamp post is tall
/// and narrow, a floor panel is wide and flat — so packing them tightly would save perhaps a third of
/// the volume. It would also put a different arithmetic in the shader for every model and a search in
/// the builder, and the saving is a few megabytes of a texture that is already small. Every slot is the
/// size of the largest field, and a model that does not fill its slot leaves the rest unread.
/// </para>
/// <para>
/// <b>Voxels stay isotropic, which is not a detail.</b> Each field is baked with the same voxel spacing
/// on all three axes, so a stored distance means the same thing in every direction. Normalising models
/// into a fixed cube instead would make the spacing depend on the model's proportions, and a distance
/// measured through a stretched grid is not a distance — a sphere trace stepping by it would overshoot
/// along the squashed axis and walk through walls.
/// </para>
/// </remarks>
public sealed class DistanceFieldAtlas
{
    private DistanceFieldAtlas(
        byte[] volume, int width, int height, int depth, int slotX, int slotY, int slotZ, Slot[] slots)
    {
        Volume = volume;
        Width = width;
        Height = height;
        Depth = depth;
        SlotWidth = slotX;
        SlotHeight = slotY;
        SlotDepth = slotZ;
        Slots = slots;
    }

    /// <summary>Gets the packed distances, x fastest then y then z.</summary>
    public byte[] Volume { get; }

    /// <summary>Gets the atlas width in voxels.</summary>
    public int Width { get; }

    /// <summary>Gets the atlas height in voxels.</summary>
    public int Height { get; }

    /// <summary>Gets the atlas depth in voxels.</summary>
    public int Depth { get; }

    /// <summary>Gets the width of one slot in voxels.</summary>
    public int SlotWidth { get; }

    /// <summary>Gets the height of one slot in voxels.</summary>
    public int SlotHeight { get; }

    /// <summary>Gets the depth of one slot in voxels.</summary>
    public int SlotDepth { get; }

    /// <summary>Gets where each field sits and what a sample of it means.</summary>
    public Slot[] Slots { get; }

    /// <summary>Gets how many voxels the atlas holds.</summary>
    public int VoxelCount => Width * Height * Depth;

    /// <summary>Packs a set of baked fields into one volume.</summary>
    /// <param name="fields">The fields to pack, in the order their indices refer to them.</param>
    /// <returns>The atlas.</returns>
    /// <remarks>
    /// The slot grid is made as cubic as the field count allows, so the atlas does not become a long
    /// thin volume that a driver may refuse: a 3D texture has a per-axis limit, and laying sixty fields
    /// in a row would exceed it on exactly the axis that gained nothing by it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is <see langword="null"/>.</exception>
    public static DistanceFieldAtlas Build(IReadOnlyList<MeshDistanceField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            return new DistanceFieldAtlas([], 0, 0, 0, 0, 0, 0, []);
        }

        int slotX = 0;
        int slotY = 0;
        int slotZ = 0;

        foreach (MeshDistanceField field in fields)
        {
            slotX = Math.Max(slotX, field.SizeX);
            slotY = Math.Max(slotY, field.SizeY);
            slotZ = Math.Max(slotZ, field.SizeZ);
        }

        // As near a cube of slots as the count allows. The cube root rounded up, then the third axis
        // taken from what the first two leave.
        int columns = Math.Max((int)MathF.Ceiling(MathF.Cbrt(fields.Count)), 1);
        int rows = Math.Max((int)MathF.Ceiling(MathF.Sqrt((float)fields.Count / columns)), 1);
        int layers = Math.Max((fields.Count + (columns * rows) - 1) / (columns * rows), 1);

        int width = columns * slotX;
        int height = rows * slotY;
        int depth = layers * slotZ;

        byte[] volume = new byte[width * height * depth];

        // Filled with the value that decodes to the largest positive distance, not with zero. Zero
        // decodes to the NEGATIVE extreme -- the encoding is centred on 128 so the sign survives -- so
        // an untouched voxel would read as deep inside a wall, and the padding around every field would
        // become solid rock a trace stops dead in.
        Array.Fill(volume, byte.MaxValue);

        Slot[] slots = new Slot[fields.Count];

        for (int i = 0; i < fields.Count; i++)
        {
            MeshDistanceField field = fields[i];

            int slot = i;
            int sx = (slot % columns) * slotX;
            int sy = ((slot / columns) % rows) * slotY;
            int sz = (slot / (columns * rows)) * slotZ;

            for (int z = 0; z < field.SizeZ; z++)
            {
                for (int y = 0; y < field.SizeY; y++)
                {
                    int source = ((z * field.SizeY) + y) * field.SizeX;
                    int target = ((((sz + z) * height) + sy + y) * width) + sx;

                    Array.Copy(field.Distances, source, volume, target, field.SizeX);
                }
            }

            slots[i] = new Slot(
                new Vector3f(sx, sy, sz),
                new Vector3f(field.SizeX, field.SizeY, field.SizeZ),
                field.Origin,
                field.VoxelSize,
                field.Range);
        }

        return new DistanceFieldAtlas(volume, width, height, depth, slotX, slotY, slotZ, slots);
    }

    /// <summary>Reads a field's distance out of the atlas, as a shader would.</summary>
    /// <param name="index">Which field.</param>
    /// <param name="point">The point to sample, in that model's own space.</param>
    /// <returns>The signed distance. Negative is inside the surface.</returns>
    /// <remarks>
    /// Nearest-voxel rather than trilinear, deliberately: this exists to prove the <em>packing</em> is
    /// right, and matching the source field's own trilinear filter would hide an off-by-one in the slot
    /// arithmetic behind the interpolation. A test compares this against the unpacked field at voxel
    /// centres, where both must agree exactly.
    /// </remarks>
    public float Sample(int index, Vector3f point)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Slots.Length);

        Slot slot = Slots[index];

        int x = (int)MathF.Floor((point.X - slot.Origin.X) / slot.VoxelSize.X);
        int y = (int)MathF.Floor((point.Y - slot.Origin.Y) / slot.VoxelSize.Y);
        int z = (int)MathF.Floor((point.Z - slot.Origin.Z) / slot.VoxelSize.Z);

        x = Math.Clamp(x, 0, (int)slot.Size.X - 1);
        y = Math.Clamp(y, 0, (int)slot.Size.Y - 1);
        z = Math.Clamp(z, 0, (int)slot.Size.Z - 1);

        int at = (((((int)slot.Corner.Z + z) * Height) + (int)slot.Corner.Y + y) * Width)
            + (int)slot.Corner.X + x;

        return MeshDistanceField.Decode(Volume[at], slot.Range);
    }

    /// <summary>Returns the atlas's dimensions and footprint.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        $"DistanceFieldAtlas {Width}x{Height}x{Depth}, {Slots.Length} field(s), {VoxelCount / 1024} KiB";

    /// <summary>Where one field sits in the atlas, and what a sample of it means.</summary>
    /// <param name="Corner">The slot's minimum voxel in the atlas.</param>
    /// <param name="Size">How many voxels of that slot the field actually fills.</param>
    /// <param name="Origin">The field's minimum corner in the model's own space.</param>
    /// <param name="VoxelSize">How much model space one voxel spans.</param>
    /// <param name="Range">The distance that the stored extremes encode.</param>
    /// <remarks>
    /// Everything a shader needs to turn a point in a model's space into an atlas coordinate, and
    /// nothing it does not. Uploaded as a storage buffer beside the volume.
    /// </remarks>
    public readonly record struct Slot(
        Vector3f Corner, Vector3f Size, Vector3f Origin, Vector3f VoxelSize, float Range);
}

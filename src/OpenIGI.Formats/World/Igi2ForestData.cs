using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>One tree placed by a <c>forest_%d.dat</c> file.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Angles">
/// Its orientation as the three angles <c>Real32x9_Create</c> builds a rotation matrix from, in radians.
/// The third is the turn about the vertical that the forest task's <c>Random rotation range</c>
/// scatters, and every tree in the corpus has one. The first two are the tilt onto the ground, and they
/// are non-zero on exactly the trees whose stand has <c>Normalize objects to ground</c> set — nine
/// stands, a quarter of the corpus.
/// </param>
/// <param name="Scale">Its per-axis scale. Uniform unless the task turns <c>Isotropic scaling</c> off.</param>
/// <param name="Light">The light colour baked onto it; see <see cref="Igi2ForestData"/>.</param>
public readonly record struct Igi2ForestTree(Vector3f Position, Vector3f Angles, Vector3f Scale, Bgra32 Light);

/// <summary>
/// A level's <c>forest_%d.dat</c>: where every tree of one <c>Forest</c> task stands.
/// </summary>
/// <remarks>
/// <para>
/// A length-prefixed header string followed by a bare array of fixed-size records, and nothing else —
/// <b>the file does not say how many trees it holds</b>. The count comes from the task's
/// <c>Number of trees</c> parameter, and <c>Forest_LoadData</c> (<c>igi2.exe 0x59FBD0</c>) lowers it to
/// what the file can supply when the file is short. So the task is the authority and the file is the
/// payload, the same way round as terrain; see <see cref="Count"/>.
/// </para>
/// <para>
/// The <c>%d</c> is the task's own level id — the slot it occupies in the level's 4096-entry
/// <c>Level_atLevelQTaskID</c> table — not a parameter it declares.
/// <c>Forest_SaveData</c> (<c>0x59F980</c>) finds it by scanning that table for the task, and
/// <c>Forest_UpdateInternalDataHandler</c> (<c>0x5A2F40</c>) builds the same name to load it back.
/// </para>
/// <para>
/// There is no BSP tree in the file despite the format's own name for itself. <c>Forest_CreateBSPTree</c>
/// (<c>0x59F350</c>) builds one from the tree array at load time, and the height data under it comes
/// from <c>Forest_GenerateHeightDataRecursively</c>. The file is the placement and only the placement.
/// </para>
/// </remarks>
public sealed class Igi2ForestData
{
    /// <summary>The header string the current version writes, and the only one this reader accepts.</summary>
    /// <remarks>
    /// Written by <c>Forest_SaveData</c> and matched by <c>Forest_LoadData</c> with <c>strncmp</c> over
    /// the length the file declares — so it is compared, not parsed, and there is no NUL on disk. All 87
    /// shipped forest files carry exactly this.
    /// </remarks>
    public const string CurrentHeader = "\nVer. 3.0\n";

    /// <summary>The size of one tree record, in bytes.</summary>
    /// <remarks><c>Forest_SavedTree1_s</c>: position, angles, scale and the baked light.</remarks>
    public const int TreeStride = 40;

    /// <summary>The size of one tree record in the older version this reader does not accept.</summary>
    /// <remarks>
    /// <c>Forest_SavedTree0_s</c> — the same three vectors without the light. The original still reads it,
    /// selecting the stride by matching a second, older header; no shipped file uses it, so rather than
    /// carry a header nothing can be tested against, an unrecognised header is refused and this constant
    /// records what the alternative was.
    /// </remarks>
    public const int LegacyTreeStride = 36;

    private readonly ReadOnlyMemory<byte> _records;

    private Igi2ForestData(string header, ReadOnlyMemory<byte> records)
    {
        Header = header;
        _records = records;
    }

    /// <summary>Gets the header string the file declared.</summary>
    public string Header { get; }

    /// <summary>Gets how many whole tree records the file holds.</summary>
    /// <remarks>
    /// The number the file <em>can</em> supply, not the number the level wants. A <c>Forest</c> task
    /// declaring more trees than this gets this many, because the original clamps; one declaring fewer
    /// gets its own number and the rest of the file is never read.
    /// </remarks>
    public int Count => _records.Length / TreeStride;

    /// <summary>Gets the record bytes, <see cref="TreeStride"/> per tree.</summary>
    public ReadOnlyMemory<byte> Records => _records;

    /// <summary>Reads a <c>forest_%d.dat</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The placement.</returns>
    /// <exception cref="PackFormatException">
    /// The bytes are too short, declare a header that runs past the end, carry a header this reader does
    /// not accept, or hold a partial trailing record.
    /// </exception>
    public static Igi2ForestData Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < sizeof(int))
        {
            throw new PackFormatException($"A forest file is {span.Length} bytes, too short for its header length.");
        }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (headerLength < 0 || headerLength > span.Length - sizeof(int))
        {
            throw new PackFormatException(
                $"A forest file declares a {headerLength}-byte header but carries {span.Length - sizeof(int)} bytes after the length.");
        }

        string header = Encoding.ASCII.GetString(span.Slice(sizeof(int), headerLength));
        if (!string.Equals(header, CurrentHeader, StringComparison.Ordinal))
        {
            throw new PackFormatException(
                $"A forest file declares header '{Describe(header)}', not '{Describe(CurrentHeader)}'.");
        }

        ReadOnlyMemory<byte> records = data[(sizeof(int) + headerLength)..];
        if (records.Length % TreeStride != 0)
        {
            throw new PackFormatException(
                $"A forest file holds {records.Length} record bytes, not a whole number of {TreeStride}-byte trees.");
        }

        return new Igi2ForestData(header, records);
    }

    /// <summary>Gets one tree.</summary>
    /// <param name="index">Its position in the file, from zero.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is not one this file holds.</exception>
    /// <remarks>
    /// The light is the value the renderer reads as a colour. <c>ForestRender_Draw</c>
    /// (<c>igi2ed 0x57CA90</c>) draws foliage with the stored dword under <c>| 0xFF000000</c> and the
    /// trunk with the average of its three low bytes replicated into a grey, so the record carries
    /// <b>24 bits of colour and the fourth byte is ignored</b>. Byte 0 is blue and byte 2 is red, which
    /// is the engine's usual <see cref="Bgra32"/> layout; the alpha this exposes is whatever the file
    /// happened to store.
    /// </remarks>
    public Igi2ForestTree GetTree(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        ReadOnlySpan<byte> record = _records.Span.Slice(index * TreeStride, TreeStride);
        return new Igi2ForestTree(
            ReadVector(record),
            ReadVector(record[12..]),
            ReadVector(record[24..]),
            new Bgra32(record[36], record[37], record[38], record[39]));
    }

    /// <summary>Gets every tree, in file order.</summary>
    /// <returns>The trees.</returns>
    public IEnumerable<Igi2ForestTree> Trees()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return GetTree(i);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"forest placement, {Count} trees";

    private static Vector3f ReadVector(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadSingleLittleEndian(source),
        BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(source[8..]));

    private static string Describe(string header) => header.Replace("\n", "\\n", StringComparison.Ordinal);
}

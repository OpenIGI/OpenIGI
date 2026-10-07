using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// One I.G.I. 2 animation clip: an ILFF <c>MINA</c> form, one file per clip, under <c>anims/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The sequel replaced I.G.I. 1's per-skeleton animation sets with one file per clip, named by what it
/// shows (<c>anims/preripcord.iff</c>, <c>anims/stand_rifle_aim.iff</c>), so a clip is found by name and
/// carries its own bone hierarchy. <c>Hierarchy_LoadAnim</c> (<c>igi2 0x57F060</c>) reads four chunks:
/// </para>
/// <list type="table">
/// <item><term><c>ANHD</c></term><description>
/// The <c>AnimHeader_s</c>, used in place: version, loop flag, total time, bone count, event count,
/// attach-object count and the clip's name. Pointer fields are written by the loader and ignored here.
/// </description></item>
/// <item><term><c>HIER</c></term><description>
/// The bone hierarchy, in the layout a bone mesh shares; see <see cref="Igi2BoneHierarchy"/>. Its shape
/// is what matches the clip to a model.
/// </description></item>
/// <item><term><c>EVNT</c></term><description>
/// A stream of variable-length records (<see cref="Igi2AnimationEventType"/>) in time order, ending in
/// an end record — and then, after the end record, the <b>tail</b>: the keys that close a looping
/// track, reachable only through the <c>next</c> link of the key before them. In a 9440-unit loop the
/// last root key at 4800 links to a tail key at 9600 carrying key 0's value, which is how the track
/// wraps. The header's event count includes the tail; across all 1,244 shipped clips it is exactly the
/// stream records plus the tail records.
/// </description></item>
/// <item><term><c>ATTA</c></term><description>
/// The attach objects, 80 bytes each, present in 1,090 of the 1,244 clips.
/// </description></item>
/// </list>
/// <para>
/// A key's <c>next</c> is a dword offset into the event chunk, which the loader turns into a pointer.
/// Tracks are assembled by following those links from each track's first key, so a track is exactly the
/// chain the engine interpolates along. Every link in the corpus lands on a record of the same track
/// with a later time; this reader refuses a clip where one does not.
/// </para>
/// </remarks>
public sealed class Igi2AnimationClip
{
    /// <summary>The header chunk, <c>ANHD</c>, as its bytes appear on disk.</summary>
    public static readonly FourCc HeaderChunk = FourCc.FromString("DHNA");

    /// <summary>The hierarchy chunk, <c>HIER</c>, as its bytes appear on disk; a bone mesh has one too.</summary>
    public static readonly FourCc HierarchyChunk = Igi2BoneHierarchy.ChunkTag;

    /// <summary>The event chunk, <c>EVNT</c>, as its bytes appear on disk.</summary>
    public static readonly FourCc EventChunk = FourCc.FromString("TNVE");

    /// <summary>The attach-object chunk, <c>ATTA</c>, as its bytes appear on disk.</summary>
    public static readonly FourCc AttachChunk = FourCc.FromString("ATTA");

    /// <summary>The only clip version the game ships, and the only one this reader accepts.</summary>
    public const int SupportedVersion = 4;

    /// <summary>The size of one <c>Anim_AttachObject_t</c>.</summary>
    public const int AttachObjectSize = 80;

    private const int HeaderFieldsSize = 0x34;
    private const int EventHeaderSize = 12;
    private const int NoNext = 0;

    private readonly Igi2RotationKey[][] _rotations;

    private Igi2AnimationClip(
        string name,
        bool loops,
        int totalTime,
        Igi2BoneHierarchy hierarchy,
        int declaredEventCount,
        Igi2TranslationKey[] translation,
        Igi2RotationKey[][] rotations,
        Igi2AnimationTrigger[] triggers,
        Igi2AnimationLink[] links,
        int inertTailLinks,
        Igi2AnimationAttachObject[] attachObjects)
    {
        Name = name;
        Loops = loops;
        TotalTime = totalTime;
        Hierarchy = hierarchy;
        DeclaredEventCount = declaredEventCount;
        Translation = translation;
        _rotations = rotations;
        Triggers = triggers;
        Links = links;
        InertTailLinks = inertTailLinks;
        AttachObjects = attachObjects;
    }

    /// <summary>Gets the clip's name, as its header stores it.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the header marks the clip as looping.</summary>
    public bool Loops { get; }

    /// <summary>Gets the clip's length, in its integer time units.</summary>
    public int TotalTime { get; }

    /// <summary>Gets the number of bones the clip animates.</summary>
    public int BoneCount => Hierarchy.BoneCount;

    /// <summary>Gets the hierarchy of the rig the clip was authored on.</summary>
    /// <remarks>
    /// Its shape decides which models the clip can drive (<see cref="Igi2BoneHierarchy.HasShapeOf"/>);
    /// its translations do not pose anything, because the engine offsets bones by the model's own.
    /// </remarks>
    public Igi2BoneHierarchy Hierarchy { get; }

    /// <summary>Gets the event count the header declares, tail included.</summary>
    public int DeclaredEventCount { get; }

    /// <summary>Gets the root translation track, tail key included.</summary>
    public IReadOnlyList<Igi2TranslationKey> Translation { get; }

    /// <summary>Gets the anim triggers, in time order.</summary>
    public IReadOnlyList<Igi2AnimationTrigger> Triggers { get; }

    /// <summary>Gets the attach-object re-links, in time order.</summary>
    public IReadOnlyList<Igi2AnimationLink> Links { get; }

    /// <summary>Gets how many link records sit in the tail, where nothing ever applies them.</summary>
    /// <remarks>
    /// A link's <c>next</c> can point past the end record, as a key's does, but links are not a track:
    /// <c>AnimController_Step</c> (<c>igi2 0x5549A0</c>) applies each one as it passes it in the stream
    /// and never follows a link's <c>next</c>. Seven shipped clips carry one. They are counted so the
    /// header's event count still balances, and kept out of <see cref="Links"/> so nothing applies a
    /// link the original never does.
    /// </remarks>
    public int InertTailLinks { get; }

    /// <summary>Gets the objects the clip carries.</summary>
    public IReadOnlyList<Igi2AnimationAttachObject> AttachObjects { get; }

    /// <summary>Gets one bone's rotation track, tail key included.</summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The keys, in time order; empty when the clip leaves the bone alone.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bone is not one this clip has.</exception>
    public IReadOnlyList<Igi2RotationKey> Rotations(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _rotations.Length);
        return _rotations[bone];
    }

    /// <summary>Reads a clip file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="IlffFormatException">The bytes are not a version-4 clip, or a track is malformed.</exception>
    public static Igi2AnimationClip Read(ReadOnlyMemory<byte> data)
    {
        IlffContainer container = IlffContainer.Read(data);
        if (container.FormType != IlffTags.Mina)
        {
            throw new IlffFormatException($"An animation clip is a '{container.FormType}' form, not 'MINA'.");
        }

        ReadOnlySpan<byte> header = Require(container, HeaderChunk).Span;
        if (header.Length < HeaderFieldsSize)
        {
            throw new IlffFormatException($"An animation header is {header.Length} bytes, too short for its fields.");
        }

        int version = ReadInt(header, 0x08);
        if (version != SupportedVersion)
        {
            throw new IlffFormatException($"An animation clip is version {version}, not {SupportedVersion}.");
        }

        bool loops = ReadInt(header, 0x0C) != 0;
        int totalTime = ReadInt(header, 0x10);
        int boneCount = ReadInt(header, 0x14);
        int declaredEvents = ReadInt(header, 0x1C);
        int attachCount = ReadInt(header, 0x24);
        string name = ReadName(header[HeaderFieldsSize..]);

        Igi2BoneHierarchy hierarchy = Igi2BoneHierarchy.Read(Require(container, HierarchyChunk).Span, boneCount);

        EventStream events = EventStream.Read(Require(container, EventChunk), name);

        var rotations = new Igi2RotationKey[boneCount][];
        for (int bone = 0; bone < boneCount; bone++)
        {
            rotations[bone] = events.Track(Igi2AnimationEventType.Rotation, bone, ReadRotationKey);
        }

        Igi2AnimationAttachObject[] attachObjects = container.TryFindChunk(AttachChunk, out IlffChunk attach)
            ? ReadAttachObjects(attach.Payload.Span, attachCount, events, name)
            : [];

        return new Igi2AnimationClip(
            name,
            loops,
            totalTime,
            hierarchy,
            declaredEvents,
            events.Track(Igi2AnimationEventType.Translation, 0, ReadTranslationKey),
            rotations,
            events.Records(Igi2AnimationEventType.Trigger, ReadTrigger),
            events.Records(Igi2AnimationEventType.Link, ReadLink),
            events.TailCount(Igi2AnimationEventType.Link),
            attachObjects);
    }

    private static ReadOnlyMemory<byte> Require(IlffContainer container, FourCc chunk) =>
        container.TryFindChunk(chunk, out IlffChunk found)
            ? found.Payload
            : throw new IlffFormatException($"An animation clip has no '{chunk}' chunk.");

    private static Igi2AnimationAttachObject[] ReadAttachObjects(
        ReadOnlySpan<byte> chunk,
        int count,
        EventStream events,
        string clip)
    {
        if (count < 0 || chunk.Length < count * AttachObjectSize)
        {
            throw new IlffFormatException(
                $"Animation '{clip}' declares {count} attach objects but its chunk holds {chunk.Length} bytes.");
        }

        var objects = new Igi2AnimationAttachObject[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = chunk.Slice(i * AttachObjectSize, AttachObjectSize);
            int firstKey = ReadInt(record, 0x4C);
            objects[i] = new Igi2AnimationAttachObject(
                ReadName(record[..16]),
                ReadVector(record, 0x10),
                ReadMatrix(record, 0x1C),
                ReadInt(record, 0x44),
                ReadInt(record, 0x48),
                firstKey == NoNext ? [] : events.Chain(firstKey, Igi2AnimationEventType.AttachKey, i, ReadAttachKey));
        }

        return objects;
    }

    private static Igi2TranslationKey ReadTranslationKey(ReadOnlySpan<byte> record, int time) =>
        new(time, ReadVector(record, 0x0C));

    private static Igi2RotationKey ReadRotationKey(ReadOnlySpan<byte> record, int time) =>
        new(time, ReadQuaternion(record, 0x0C), ReadQuaternion(record, 0x20), ReadQuaternion(record, 0x34));

    private static Igi2AttachKey ReadAttachKey(ReadOnlySpan<byte> record, int time) =>
        new(time, ReadVector(record, 0x0C), ReadQuaternion(record, 0x18));

    private static Igi2AnimationTrigger ReadTrigger(ReadOnlySpan<byte> record, int time) =>
        new(time, ReadInt(record, 0x0C), BinaryPrimitives.ReadInt16LittleEndian(record[0x10..]), ReadVector(record, 0x14));

    private static Igi2AnimationLink ReadLink(ReadOnlySpan<byte> record, int time) =>
        new(time, record[1], ReadInt(record, 0x0C), ReadMatrix(record, 0x10), ReadVector(record, 0x38));

    private static int ReadInt(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    private static Vector3f ReadVector(ReadOnlySpan<byte> data, int offset) =>
        new(ReadFloat(data, offset), ReadFloat(data, offset + 4), ReadFloat(data, offset + 8));

    private static Quaternion32 ReadQuaternion(ReadOnlySpan<byte> data, int offset) =>
        new(ReadFloat(data, offset), ReadFloat(data, offset + 4), ReadFloat(data, offset + 8), ReadFloat(data, offset + 12));

    private static Matrix3f ReadMatrix(ReadOnlySpan<byte> data, int offset)
    {
        // real32x9_t: three rows of three floats, then an nOps counter that is bookkeeping.
        Span<float> values = stackalloc float[9];
        for (int i = 0; i < 9; i++)
        {
            values[i] = ReadFloat(data, offset + (i * 4));
        }

        return Matrix3f.FromRowMajor(values);
    }

    private static string ReadName(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? data : data[..end]);
    }

    /// <summary>The event chunk, indexed by dword offset so <c>next</c> links resolve.</summary>
    private sealed class EventStream
    {
        private readonly ReadOnlyMemory<byte> _chunk;
        private readonly Dictionary<int, int> _lengthAt;
        private readonly List<int> _stream;
        private readonly string _clip;

        private EventStream(ReadOnlyMemory<byte> chunk, Dictionary<int, int> lengthAt, List<int> stream, string clip)
        {
            _chunk = chunk;
            _lengthAt = lengthAt;
            _stream = stream;
            _clip = clip;
        }

        public delegate T RecordReader<out T>(ReadOnlySpan<byte> record, int time);

        public static EventStream Read(ReadOnlyMemory<byte> chunk, string clip)
        {
            ReadOnlySpan<byte> span = chunk.Span;
            Dictionary<int, int> lengthAt = [];
            List<int> stream = [];
            bool inTail = false;

            // Walked to the end of the chunk, not just to the end record: the tail keys after it are
            // records like any other, and a link into the tail has to land on one.
            int offset = 0;
            while (offset + EventHeaderSize <= span.Length)
            {
                int length = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 2)..]);
                if (length * 4 < EventHeaderSize || offset + (length * 4) > span.Length)
                {
                    throw new IlffFormatException(
                        $"Animation '{clip}' has an event of {length} dwords at byte {offset} of a {span.Length}-byte stream.");
                }

                lengthAt[offset / 4] = length;
                if (!inTail)
                {
                    stream.Add(offset / 4);
                    inTail = span[offset] == (byte)Igi2AnimationEventType.End;
                }

                offset += length * 4;
            }

            if (!inTail)
            {
                throw new IlffFormatException($"Animation '{clip}' has no end record.");
            }

            return new EventStream(chunk, lengthAt, stream, clip);
        }

        /// <summary>Counts the tail records of a type.</summary>
        public int TailCount(Igi2AnimationEventType type)
        {
            int count = 0;
            int end = _stream[^1];
            foreach ((int index, int _) in _lengthAt)
            {
                if (index > end && Record(index)[0] == (byte)type)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Reads every stream record of a type, in stream order.</summary>
        public T[] Records<T>(Igi2AnimationEventType type, RecordReader<T> read)
        {
            List<T> records = [];
            foreach (int index in _stream)
            {
                ReadOnlySpan<byte> record = Record(index);
                if (record[0] == (byte)type)
                {
                    records.Add(read(record, Time(record)));
                }
            }

            return [.. records];
        }

        /// <summary>Reads the track whose first stream record has this type and argument.</summary>
        public T[] Track<T>(Igi2AnimationEventType type, int argument, RecordReader<T> read)
        {
            foreach (int index in _stream)
            {
                ReadOnlySpan<byte> record = Record(index);
                if (record[0] == (byte)type && record[1] == argument)
                {
                    return Chain(index, type, argument, read);
                }
            }

            return [];
        }

        /// <summary>Follows <c>next</c> links from one record to the end of its track.</summary>
        public T[] Chain<T>(int first, Igi2AnimationEventType type, int argument, RecordReader<T> read)
        {
            // Zero ends a chain only as a link: the loader fixes up a non-zero next and leaves zero null,
            // so no link can point at offset zero -- but a track's first key can sit there, and does.
            List<T> keys = [];
            int previousTime = int.MinValue;
            int index = first;
            while (true)
            {
                if (!_lengthAt.ContainsKey(index))
                {
                    throw new IlffFormatException($"Animation '{_clip}' links to dword {index}, which starts no record.");
                }

                ReadOnlySpan<byte> record = Record(index);
                int time = Time(record);
                if (record[0] != (byte)type || record[1] != argument || time <= previousTime)
                {
                    throw new IlffFormatException(
                        $"Animation '{_clip}' links a {type} {argument} track to a type-{record[0]} record for {record[1]} at {time}.");
                }

                keys.Add(read(record, time));
                previousTime = time;
                index = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
                if (index == NoNext)
                {
                    return [.. keys];
                }
            }
        }

        private ReadOnlySpan<byte> Record(int index) => _chunk.Span.Slice(index * 4, _lengthAt[index] * 4);

        private static int Time(ReadOnlySpan<byte> record) => BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
    }
}

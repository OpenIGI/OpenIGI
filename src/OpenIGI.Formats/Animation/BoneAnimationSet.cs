using System.Buffers.Binary;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Iff;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// A whole <c>anims/&lt;name&gt;.iff</c> file: one skeleton and the clips that animate it.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces <c>0x4D67A0</c>. Two things about that loader shape the reader and would not be guessed
/// from the bytes: a track with a single key is read <b>short</b> (§3.1 of the note), and <c>BALH</c>'s
/// second field is an <b>id limit</b>, not a count — the table is sparse and indexed by animation id.
/// </para>
/// </remarks>
public sealed class BoneAnimationSet
{
    private readonly BoneAnimation?[] _byId;
    private readonly BoneAnimation[] _animations;

    private BoneAnimationSet(BoneSkeleton skeleton, BoneAnimation[] animations, BoneAnimation?[] byId)
    {
        Skeleton = skeleton;
        _animations = animations;
        _byId = byId;
    }

    /// <summary>Gets the skeleton the clips animate.</summary>
    public BoneSkeleton Skeleton { get; }

    /// <summary>Gets the clips, in file order.</summary>
    public IReadOnlyList<BoneAnimation> Animations => _animations;

    /// <summary>Gets the exclusive upper bound on animation ids.</summary>
    public int IdLimit => _byId.Length;

    /// <summary>Looks a clip up by id.</summary>
    /// <param name="id">The animation id.</param>
    /// <param name="animation">Receives the clip when the id is defined.</param>
    /// <returns><see langword="true"/> when the set defines that id.</returns>
    /// <remarks>
    /// The original allocates the table zeroed and never fills the gaps, so an undefined id inside the
    /// limit reads as absent rather than as an error — which is what its <c>"Animation #%d not found"</c>
    /// message reports.
    /// </remarks>
    public bool TryGet(int id, out BoneAnimation animation)
    {
        if (id < 0 || id >= _byId.Length || _byId[id] is null)
        {
            animation = null!;
            return false;
        }

        animation = _byId[id]!;
        return true;
    }

    /// <summary>Reads a set.</summary>
    /// <param name="data">The whole <c>.iff</c> file.</param>
    /// <returns>The set.</returns>
    /// <exception cref="IffFormatException">The file is not a well-formed animation set.</exception>
    public static BoneAnimationSet Read(ReadOnlyMemory<byte> data)
    {
        IffChunk root = IffFile.ReadForm(data, BoneAnimationTags.BoneObject);

        IffChunk hierarchy = root.FirstGroup(BoneAnimationTags.BoneHierarchy)
            ?? throw new IffFormatException("The set has no BOBH bone hierarchy.");
        BoneSkeleton skeleton = ReadSkeleton(hierarchy);

        IffChunk list = root.FirstGroup(BoneAnimationTags.AnimationList)
            ?? throw new IffFormatException("The set has no BOAL animation list.");
        ReadOnlySpan<byte> header = Payload(list, BoneAnimationTags.AnimationListHeader, 8);
        int animationCount = BinaryPrimitives.ReadInt32LittleEndian(header);
        int idLimit = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);

        if (animationCount < 0 || idLimit < 0)
        {
            throw new IffFormatException($"BALH declares {animationCount} animations, limit {idLimit}.");
        }

        List<BoneAnimation> animations = new(animationCount);
        BoneAnimation?[] byId = new BoneAnimation?[idLimit];

        foreach (IffChunk group in list.GroupsWithType(BoneAnimationTags.Animation))
        {
            BoneAnimation animation = ReadAnimation(group, skeleton.BoneCount);
            if (animation.Id >= idLimit)
            {
                throw new IffFormatException(
                    $"Animation id {animation.Id} is outside the declared limit of {idLimit}.");
            }

            animations.Add(animation);

            // The original writes the slot unconditionally, so a repeated id keeps the later clip.
            byId[animation.Id] = animation;
        }

        if (animations.Count != animationCount)
        {
            throw new IffFormatException(
                $"BALH declares {animationCount} animations but the list holds {animations.Count}.");
        }

        return new BoneAnimationSet(skeleton, [.. animations], byId);
    }

    private static BoneSkeleton ReadSkeleton(IffChunk hierarchy)
    {
        ReadOnlySpan<byte> header = Payload(hierarchy, BoneAnimationTags.SkeletonHeader, 8);
        int id = BinaryPrimitives.ReadInt32LittleEndian(header);
        int boneCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (boneCount <= 0)
        {
            throw new IffFormatException($"BOSH declares {boneCount} bones.");
        }

        ReadOnlySpan<byte> parentBytes = Payload(hierarchy, BoneAnimationTags.ParentList, 4 * boneCount);
        ReadOnlySpan<byte> restBytes = Payload(hierarchy, BoneAnimationTags.TranslationList, 12 * boneCount);

        int[] parents = new int[boneCount];
        Vector3f[] rest = new Vector3f[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            parents[i] = BinaryPrimitives.ReadInt32LittleEndian(parentBytes[(4 * i)..]);
            rest[i] = ReadVector3(restBytes[(12 * i)..]);
        }

        return new BoneSkeleton(id, parents, rest);
    }

    private static BoneAnimation ReadAnimation(IffChunk group, int boneCount)
    {
        ReadOnlySpan<byte> header = Payload(group, BoneAnimationTags.AnimationHeader, 12);
        float duration = BinaryPrimitives.ReadSingleLittleEndian(header);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        int id = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);

        BoneTranslationKey[] translation = ReadTranslationTrack(group);
        BoneRotationKey[][] rotations = ReadRotationTracks(group, boneCount);
        BoneAnimationEvent[] events = ReadEvents(group);

        return new BoneAnimation(id, duration, flags, translation, rotations, events);
    }

    private static BoneTranslationKey[] ReadTranslationTrack(IffChunk group)
    {
        int count = ReadCount(group, BoneAnimationTags.TranslationHeader);
        if (count == 0)
        {
            return [];
        }

        IffChunk data = group.RequireChild(BoneAnimationTags.TranslationData);
        ReadOnlySpan<byte> span = data.Payload.Span;
        bool full = count > 1;
        int stride = BoneTranslationKey.SizeInBytes;
        Require(span.Length >= stride * count, $"BOTD holds {span.Length} bytes for {count} key(s).");

        BoneTranslationKey[] keys = new BoneTranslationKey[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> key = span[(stride * i)..];
            Vector3f position = ReadVector3(key);
            float time = BinaryPrimitives.ReadSingleLittleEndian(key[12..]);

            // A one-key track is read only for its value and time; the tangents are meaningless with
            // nothing to interpolate towards, and the original never copies them.
            Vector3f outTangent = full ? ReadVector3(key[16..]) : default;
            Vector3f inTangent = full ? ReadVector3(key[28..]) : default;
            keys[i] = new BoneTranslationKey(position, time, outTangent, inTangent);
        }

        return keys;
    }

    private static BoneRotationKey[][] ReadRotationTracks(IffChunk group, int boneCount)
    {
        // The loader walks BORH/BORD as sibling pairs, one pair per bone in bone order, so the tracks
        // are matched positionally rather than by any index in the data.
        List<IffChunk> headers = [.. group.ChildrenWithId(BoneAnimationTags.RotationHeader)];
        List<IffChunk> datas = [.. group.ChildrenWithId(BoneAnimationTags.RotationData)];
        Require(
            headers.Count == boneCount && datas.Count == boneCount,
            $"An animation holds {headers.Count} BORH and {datas.Count} BORD for {boneCount} bones.");

        BoneRotationKey[][] tracks = new BoneRotationKey[boneCount][];
        for (int bone = 0; bone < boneCount; bone++)
        {
            Require(headers[bone].Payload.Length >= 4, "BORH is shorter than four bytes.");
            int count = BinaryPrimitives.ReadInt32LittleEndian(headers[bone].Payload.Span);
            Require(count >= 0, $"BORH declares {count} keys.");
            if (count == 0)
            {
                tracks[bone] = [];
                continue;
            }

            ReadOnlySpan<byte> span = datas[bone].Payload.Span;
            bool full = count > 1;
            int stride = BoneRotationKey.SizeInBytes;
            Require(span.Length >= stride * count, $"BORD holds {span.Length} bytes for {count} key(s).");

            BoneRotationKey[] keys = new BoneRotationKey[count];
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> key = span[(stride * i)..];
                Quaternion32 rotation = ReadQuaternion(key);
                float time = BinaryPrimitives.ReadSingleLittleEndian(key[16..]);
                Quaternion32 outTangent = full ? ReadQuaternion(key[20..]) : rotation;
                Quaternion32 inTangent = full ? ReadQuaternion(key[36..]) : rotation;
                keys[i] = new BoneRotationKey(rotation, time, outTangent, inTangent);
            }

            tracks[bone] = keys;
        }

        return tracks;
    }

    private static BoneAnimationEvent[] ReadEvents(IffChunk group)
    {
        int count = ReadCount(group, BoneAnimationTags.EventHeader);
        if (count == 0)
        {
            return [];
        }

        IffChunk data = group.RequireChild(BoneAnimationTags.EventData);
        ReadOnlySpan<byte> span = data.Payload.Span;
        int stride = BoneAnimationEvent.SizeInBytes;
        Require(span.Length >= stride * count, $"BOED holds {span.Length} bytes for {count} event(s).");

        BoneAnimationEvent[] events = new BoneAnimationEvent[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = span[(stride * i)..];
            events[i] = new BoneAnimationEvent(
                BinaryPrimitives.ReadInt32LittleEndian(record),
                BinaryPrimitives.ReadInt32LittleEndian(record[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(record[8..]),
                ReadVector3(record[12..]));
        }

        return events;
    }

    private static int ReadCount(IffChunk group, Core.FourCc tag)
    {
        IffChunk? header = group.FirstChild(tag);
        if (header is null)
        {
            return 0;
        }

        Require(header.Payload.Length >= 4, $"'{tag}' is shorter than four bytes.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(header.Payload.Span);
        Require(count >= 0, $"'{tag}' declares {count} entries.");
        return count;
    }

    private static ReadOnlySpan<byte> Payload(IffChunk parent, Core.FourCc tag, int minimumSize)
    {
        IffChunk chunk = parent.RequireChild(tag);
        Require(
            chunk.Payload.Length >= minimumSize,
            $"'{tag}' holds {chunk.Payload.Length} bytes, needs {minimumSize}.");
        return chunk.Payload.Span;
    }

    private static Vector3f ReadVector3(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadSingleLittleEndian(span),
        BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(span[8..]));

    private static Quaternion32 ReadQuaternion(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadSingleLittleEndian(span),
        BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(span[8..]),
        BinaryPrimitives.ReadSingleLittleEndian(span[12..]));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new IffFormatException(message);
        }
    }
}

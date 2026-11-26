using System.Buffers.Binary;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Formats.World;

/// <summary>
/// One of a model's <c>TXAN</c> chunks: an animated texture, a material slot whose texture is swapped for one of the model's
/// other slots each frame.
/// </summary>
/// <remarks>
/// <para>
/// Both games read it alike (I.G.I. 2's <c>MTP_ModelLoad</c> into <c>ModelAnimTexInfo_s</c>; Project I.G.I.'s
/// <c>Model_Load</c>): the slot to animate, the frame count, then one slot per frame. Each chunk is one animated texture.
/// The 20 bytes per animated texture are the loaded record, not the chunk.
/// </para>
/// <para>
/// The game draws frame <c>n</c> by putting the texture of slot <c>n</c> in the animated slot
/// (<c>Mesh3D_SetDrawAnimTextures</c>), so <see cref="FrameSlots"/> is read but not used for drawing. In every retail model
/// that has one, the list runs <c>0, 1, 2, …</c> anyway: only the explosion fireball <c>1006_01_1</c> has one, in both
/// games, and its <c>25, 26, 0…25</c> animates the face group's slot 25 through all of the instance's slots.
/// </para>
/// </remarks>
/// <param name="Slot">The material slot whose texture is animated, <c>iTextureToAnimate</c>.</param>
/// <param name="FrameCount">How many frames the animation has, <c>nFrames</c>.</param>
/// <param name="FrameSlots">The slot named for each frame, <c>aiFrames</c>.</param>
public sealed record MefTextureAnimation(int Slot, int FrameCount, IReadOnlyList<int> FrameSlots)
{
    /// <summary>Reads every animated texture a model carries, in file order.</summary>
    /// <param name="container">The model's container.</param>
    /// <returns>The animated textures; empty for a model with none.</returns>
    /// <exception cref="PackFormatException">A chunk is shorter than its frame count says.</exception>
    public static IReadOnlyList<MefTextureAnimation> ReadAll(IlffContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        List<MefTextureAnimation> animations = [];
        foreach (IlffChunk chunk in container.Chunks)
        {
            if (chunk.Id == MefTags.TextureAnimation)
            {
                animations.Add(Read(chunk.Payload.Span));
            }
        }

        return animations;
    }

    /// <summary>Reads one chunk's payload.</summary>
    /// <param name="payload">The <c>TXAN</c> payload.</param>
    /// <returns>The animated texture.</returns>
    /// <exception cref="PackFormatException">The payload is shorter than its frame count says.</exception>
    public static MefTextureAnimation Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            throw new PackFormatException($"The '{MefTags.TextureAnimation}' chunk is {payload.Length} bytes, too short for its header.");
        }

        int slot = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int frames = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        if (frames < 0 || (long)8 + (4L * frames) > payload.Length)
        {
            throw new PackFormatException(
                $"The '{MefTags.TextureAnimation}' chunk declares {frames} frames, which do not fit its {payload.Length} bytes.");
        }

        int[] slots = new int[frames];
        for (int i = 0; i < frames; i++)
        {
            slots[i] = BinaryPrimitives.ReadInt32LittleEndian(payload[(8 + (4 * i))..]);
        }

        return new MefTextureAnimation(slot, frames, slots);
    }
}

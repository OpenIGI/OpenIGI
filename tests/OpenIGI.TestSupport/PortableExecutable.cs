using System.Buffers.Binary;
using System.Globalization;

namespace OpenIGI.TestSupport;

/// <summary>
/// Just enough PE parsing to read data the original <c>IGI.exe</c> embeds in its image.
/// </summary>
/// <remarks>
/// The reverse-engineering notes give addresses as virtual addresses (image base <c>0x400000</c>), so
/// reading an embedded fixture means mapping a virtual address back to a file offset through the
/// section table.
/// </remarks>
public sealed class PortableExecutable
{
    private const int SectionHeaderSize = 40;

    private readonly byte[] _image;
    private readonly Section[] _sections;

    private PortableExecutable(byte[] image, ulong imageBase, Section[] sections)
    {
        _image = image;
        ImageBase = imageBase;
        _sections = sections;
    }

    /// <summary>Gets the image base recorded in the optional header.</summary>
    public ulong ImageBase { get; }

    /// <summary>Loads and parses a PE image.</summary>
    /// <param name="path">The path to the executable.</param>
    /// <returns>The parsed image.</returns>
    /// <exception cref="InvalidDataException">The file is not a PE image this reader understands.</exception>
    public static PortableExecutable Load(string path)
    {
        byte[] image = File.ReadAllBytes(path);
        ReadOnlySpan<byte> span = image;

        if (span.Length < 0x40 || span[0] != (byte)'M' || span[1] != (byte)'Z')
        {
            throw new InvalidDataException($"'{path}' does not start with an MZ header.");
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(span[0x3C..]);
        if (peOffset < 0 || peOffset + 24 > span.Length
            || span[peOffset] != (byte)'P' || span[peOffset + 1] != (byte)'E')
        {
            throw new InvalidDataException($"'{path}' has no PE signature.");
        }

        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(peOffset + 6)..]);
        int optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(span[(peOffset + 20)..]);
        int optionalHeaderOffset = peOffset + 24;

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(span[optionalHeaderOffset..]);
        ulong imageBase = magic switch
        {
            0x10B => BinaryPrimitives.ReadUInt32LittleEndian(span[(optionalHeaderOffset + 28)..]),
            0x20B => BinaryPrimitives.ReadUInt64LittleEndian(span[(optionalHeaderOffset + 24)..]),
            _ => throw new InvalidDataException($"'{path}' has an unknown optional header magic 0x{magic:X}."),
        };

        int sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
        Section[] sections = new Section[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            int offset = sectionTableOffset + (i * SectionHeaderSize);
            sections[i] = new Section(
                VirtualSize: BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]),
                VirtualAddress: BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]),
                RawSize: BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]),
                RawOffset: BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]));
        }

        return new PortableExecutable(image, imageBase, sections);
    }

    /// <summary>Reads bytes located at a virtual address.</summary>
    /// <param name="virtualAddress">The virtual address, as recorded in the reverse-engineering notes.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The bytes at that address.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The address is not inside any mapped section, or the range runs past the section.</exception>
    public ReadOnlyMemory<byte> ReadVirtual(ulong virtualAddress, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        ulong rva = virtualAddress - ImageBase;
        foreach (Section section in _sections)
        {
            ulong size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva < section.VirtualAddress || rva >= section.VirtualAddress + size)
            {
                continue;
            }

            long fileOffset = section.RawOffset + (long)(rva - section.VirtualAddress);
            if (fileOffset + count > _image.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    "The requested range runs past the end of the image.");
            }

            return _image.AsMemory((int)fileOffset, count);
        }

        throw new ArgumentOutOfRangeException(
            nameof(virtualAddress),
            virtualAddress,
            string.Create(CultureInfo.InvariantCulture, $"No section maps RVA 0x{rva:X}."));
    }

    private readonly record struct Section(uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawOffset);
}

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using OpenIGI.Core;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// A loaded QVM bytecode program: the compiled form of a <c>.qsc</c> script.
/// </summary>
/// <remarks>
/// <para>
/// Loading mirrors the original loader at <c>0x4BD220</c>: the magic must be <c>LOOP</c>, the
/// instruction-set id must be 8, and the version must be at least 5 (retail I.G.I. emits 5; I.G.I. 2
/// emits 7, which uses the same language but an incompatible binary). On top of that we validate the
/// header's self-consistency, which the original trusts blindly. See <c>docs/formats/qvm.md</c>.
/// </para>
/// <para>
/// Parsing is zero-copy for the code section; the symbol and string tables are decoded up front
/// because every consumer needs them by index.
/// </para>
/// </remarks>
public sealed class QvmProgram
{
    /// <summary>The size in bytes of a version 5 QVM header — retail Project I.G.I.</summary>
    public const int HeaderSize = Version5HeaderSize;

    /// <summary>The size in bytes of a version 5 QVM header.</summary>
    public const int Version5HeaderSize = 60;

    /// <summary>The size in bytes of a version 7 QVM header — I.G.I. 2.</summary>
    /// <remarks>
    /// Version 5's fields in the same order, plus a trailing file-info offset at <c>0x3C</c>. Nothing
    /// before that moved, which is why one reader serves both.
    /// </remarks>
    public const int Version7HeaderSize = 64;

    /// <summary>The lowest version whose header carries a file-info offset.</summary>
    public const int FirstVersionWithFileInfo = 7;

    /// <summary>The instruction-set identifier the VM accepts; any other value is rejected.</summary>
    public const int RequiredInstructionSetId = 8;

    /// <summary>The lowest bytecode version the VM accepts.</summary>
    public const int MinimumVersion = 5;

    /// <summary>The bytecode version retail Project I.G.I. ships.</summary>
    public const int IgiVersion = 5;

    /// <summary>The bytecode version I.G.I. 2 ships.</summary>
    /// <remarks>
    /// Checked across every script in a retail 1.1 installation: 1,784 files, all version 7 and all on
    /// the same instruction set as I.G.I. 1.
    /// </remarks>
    public const int Igi2Version = 7;

    /// <summary>The evaluation stack size in bytes used when the header requests none.</summary>
    public const int DefaultStackSizeBytes = 2048;

    /// <summary>The number of local/temporary slots used when the header requests none.</summary>
    public const int DefaultMaxLocals = 256;

    /// <summary>Gets the header size a bytecode version uses.</summary>
    /// <param name="version">The version word from the header.</param>
    /// <returns>The header size in bytes.</returns>
    public static int HeaderSizeFor(int version) =>
        version >= FirstVersionWithFileInfo ? Version7HeaderSize : Version5HeaderSize;

    private static readonly FourCc LoopMagic = FourCc.FromString("LOOP");

    private readonly ReadOnlyCollection<string?> _symbolNames;
    private readonly ReadOnlyCollection<string?> _strings;

    private QvmProgram(
        int instructionSetId,
        int version,
        ReadOnlyMemory<byte> code,
        int stackSizeBytes,
        int maxLocals,
        int fileInfoOffset,
        ReadOnlyCollection<string?> symbolNames,
        ReadOnlyCollection<string?> strings)
    {
        FileInfoOffset = fileInfoOffset;
        InstructionSetId = instructionSetId;
        Version = version;
        Code = code;
        StackSizeBytes = stackSizeBytes;
        MaxLocals = maxLocals;
        _symbolNames = symbolNames;
        _strings = strings;
    }

    /// <summary>Gets the instruction-set identifier from the header. Always <see cref="RequiredInstructionSetId"/>.</summary>
    public int InstructionSetId { get; }

    /// <summary>Gets the bytecode version from the header.</summary>
    public int Version { get; }

    /// <summary>Gets the offset of the trailing file-info block, or zero when the version has none.</summary>
    /// <remarks>
    /// Version 7 only. What the block holds is unread -- the interpreter does not need it -- but its
    /// offset is where the code section ends, so the loader has to know about it either way.
    /// </remarks>
    public int FileInfoOffset { get; }

    /// <summary>Gets the code section: the bytes the interpreter executes, offset zero being the entry point.</summary>
    public ReadOnlyMemory<byte> Code { get; }

    /// <summary>Gets the evaluation stack size in bytes, with the header's zero already replaced by <see cref="DefaultStackSizeBytes"/>.</summary>
    public int StackSizeBytes { get; }

    /// <summary>Gets the number of local/temporary slots, with the header's zero already replaced by <see cref="DefaultMaxLocals"/>.</summary>
    public int MaxLocals { get; }

    /// <summary>Gets the symbol names, indexed the way <see cref="QvmOpcode.PushII"/> and its narrow forms index them.</summary>
    /// <remarks>
    /// An entry is <see langword="null"/> when its table slot points outside the name pool. Retail
    /// files have no such entry; a program that indexes one faults with
    /// <see cref="Runtime.QvmError.IndexOutOfRange"/>.
    /// </remarks>
    public IReadOnlyList<string?> SymbolNames => _symbolNames;

    /// <summary>Gets the string constants, indexed the way <see cref="QvmOpcode.PushSI"/> and its narrow forms index them.</summary>
    /// <remarks>An entry is <see langword="null"/> when its table slot points outside the string pool.</remarks>
    public IReadOnlyList<string?> Strings => _strings;

    /// <summary>Determines whether <paramref name="data"/> starts with a QVM header.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the bytes begin with the <c>LOOP</c> magic and the expected instruction-set id.</returns>
    /// <remarks>
    /// <c>.spr</c> sprites share the <c>LOOP</c> magic, so the instruction-set word is part of the
    /// probe rather than the magic alone.
    /// </remarks>
    public static bool IsQvm(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize
        && FourCc.FromBytes(data) == LoopMagic
        && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == RequiredInstructionSetId;

    /// <summary>Loads a QVM program.</summary>
    /// <param name="data">The whole file. Its length must match the header exactly.</param>
    /// <returns>The loaded program.</returns>
    /// <exception cref="QvmFormatException">The bytes are not a valid QVM program.</exception>
    public static QvmProgram Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderSize)
        {
            throw new QvmFormatException($"A QVM program needs at least {HeaderSize} bytes, got {span.Length}.");
        }

        FourCc magic = FourCc.FromBytes(span);
        if (magic != LoopMagic)
        {
            throw new QvmFormatException($"Expected magic 'LOOP', found '{magic}'.");
        }

        int instructionSet = ReadHeaderField(span, 0x04, "instruction set id");
        if (instructionSet != RequiredInstructionSetId)
        {
            throw new QvmFormatException($"Expected instruction set id {RequiredInstructionSetId}, found {instructionSet}.");
        }

        int version = ReadHeaderField(span, 0x08, "version");
        if (version < MinimumVersion)
        {
            throw new QvmFormatException($"QVM version {version} is older than the minimum supported version {MinimumVersion}.");
        }

        int headerSize = HeaderSizeFor(version);
        if (span.Length < headerSize)
        {
            throw new QvmFormatException(
                $"A version {version} QVM header is {headerSize} bytes, got {span.Length}.");
        }

        int symbolTableOffset = ReadHeaderField(span, 0x0C, "symbol name table offset");
        int symbolPoolOffset = ReadHeaderField(span, 0x10, "symbol name pool offset");
        int symbolTableSize = ReadHeaderField(span, 0x14, "symbol name table size");
        int symbolPoolSize = ReadHeaderField(span, 0x18, "symbol name pool size");
        int stringTableOffset = ReadHeaderField(span, 0x1C, "string table offset");
        int stringPoolOffset = ReadHeaderField(span, 0x20, "string pool offset");
        int stringTableSize = ReadHeaderField(span, 0x24, "string table size");
        int stringPoolSize = ReadHeaderField(span, 0x28, "string pool size");
        int codeOffset = ReadHeaderField(span, 0x2C, "code offset");
        int codeSize = ReadHeaderField(span, 0x30, "code size");
        int stackSize = ReadHeaderField(span, 0x34, "stack size");
        int maxLocals = ReadHeaderField(span, 0x38, "max locals");

        // Version 7 appends a file-info block and points at it from the header. Version 5 has neither,
        // and reading 0x3C on a v5 file would read the first four bytes of the symbol table.
        int fileInfoOffset = version >= FirstVersionWithFileInfo
            ? ReadHeaderField(span, 0x3C, "file info offset")
            : 0;

        // The chained identities that hold for every retail script: the sections butt up against each
        // other in header order. The first two are structural and hold for both versions.
        RequireSum(symbolPoolOffset, symbolPoolSize, stringTableOffset, "symbol name pool", "string table offset");
        RequireSum(stringPoolOffset, stringPoolSize, codeOffset, "string pool", "code offset");

        // The third is "the code runs up to whatever comes next", which for version 5 is the end of the
        // file and for version 7 is the file-info block. Asserting the file size outright -- as this did
        // when version 5 was the only version -- rejects every I.G.I. 2 script, because the block sits
        // after the code. Checked against a retail 1.1 installation: codeOffset + codeSize is the
        // file-info offset exactly, in all 1,784 of them.
        int codeEnd = fileInfoOffset != 0 ? fileInfoOffset : span.Length;
        RequireSum(codeOffset, codeSize, codeEnd, "code section", "end of the code section");

        if (fileInfoOffset != 0)
        {
            RequireSection(span.Length, fileInfoOffset, span.Length - fileInfoOffset, "file info block");
        }

        RequireSection(span.Length, symbolTableOffset, symbolTableSize, "symbol name table");
        RequireSection(span.Length, symbolPoolOffset, symbolPoolSize, "symbol name pool");
        RequireSection(span.Length, stringTableOffset, stringTableSize, "string table");
        RequireSection(span.Length, stringPoolOffset, stringPoolSize, "string pool");
        RequireSection(span.Length, codeOffset, codeSize, "code section");

        return new QvmProgram(
            instructionSet,
            version,
            data.Slice(codeOffset, codeSize),
            stackSize == 0 ? DefaultStackSizeBytes : stackSize,
            maxLocals == 0 ? DefaultMaxLocals : maxLocals,
            fileInfoOffset,
            ReadStringTable(span, symbolTableOffset, symbolTableSize, symbolPoolOffset, symbolPoolSize),
            ReadStringTable(span, stringTableOffset, stringTableSize, stringPoolOffset, stringPoolSize));
    }

    /// <summary>Looks up a symbol name by table index.</summary>
    /// <param name="index">The index encoded in a <see cref="QvmOpcode.PushII"/> instruction.</param>
    /// <param name="name">Receives the name when the index addresses one.</param>
    /// <returns><see langword="false"/> when the index is out of range or its slot points outside the pool.</returns>
    public bool TryGetSymbolName(int index, out string name) => TryGet(_symbolNames, index, out name);

    /// <summary>Looks up a string constant by table index.</summary>
    /// <param name="index">The index encoded in a <see cref="QvmOpcode.PushSI"/> instruction.</param>
    /// <param name="value">Receives the string when the index addresses one.</param>
    /// <returns><see langword="false"/> when the index is out of range or its slot points outside the pool.</returns>
    public bool TryGetString(int index, out string value) => TryGet(_strings, index, out value);

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"QVM v{Version} code={Code.Length} symbols={_symbolNames.Count} strings={_strings.Count}");

    private static bool TryGet(ReadOnlyCollection<string?> table, int index, out string value)
    {
        // The original bounds-checks the index against the table's size in *bytes* and relies on the
        // pool-offset check to catch the rest. We check the entry count, which only differs for
        // malformed files - and there it refuses instead of reading past the table.
        if ((uint)index >= (uint)table.Count || table[index] is not { } found)
        {
            value = string.Empty;
            return false;
        }

        value = found;
        return true;
    }

    private static ReadOnlyCollection<string?> ReadStringTable(
        ReadOnlySpan<byte> data,
        int tableOffset,
        int tableSize,
        int poolOffset,
        int poolSize)
    {
        int count = tableSize / sizeof(uint);
        string?[] entries = new string?[count];
        ReadOnlySpan<byte> pool = data.Slice(poolOffset, poolSize);

        for (int i = 0; i < count; i++)
        {
            uint entryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(tableOffset + (i * sizeof(uint)))..]);
            if (entryOffset >= (uint)poolSize)
            {
                continue;
            }

            ReadOnlySpan<byte> rest = pool[(int)entryOffset..];
            int end = rest.IndexOf((byte)0);
            // Latin-1 rather than ASCII: it round-trips every byte, so string lengths and ordinal
            // comparisons match the byte-wise ones the original performs on the same pool.
            entries[i] = Encoding.Latin1.GetString(end < 0 ? rest : rest[..end]);
        }

        return new ReadOnlyCollection<string?>(entries);
    }

    private static int ReadHeaderField(ReadOnlySpan<byte> data, int offset, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        if (value > int.MaxValue)
        {
            throw new QvmFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }

    private static void RequireSum(int start, int size, int expectedEnd, string what, string expectation)
    {
        if (start + (long)size != expectedEnd)
        {
            throw new QvmFormatException(
                $"The {what} spans {start}..{start + (long)size} but the {expectation} is {expectedEnd}.");
        }
    }

    private static void RequireSection(int fileSize, int offset, int size, string what)
    {
        if (offset < 0 || size < 0 || offset + (long)size > fileSize)
        {
            throw new QvmFormatException($"The {what} at offset {offset} with size {size} does not fit in {fileSize} bytes.");
        }
    }
}

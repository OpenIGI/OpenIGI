using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// Builds a complete QVM file: header, symbol name table and pool, string table and pool, and code.
/// </summary>
/// <remarks>
/// The section layout matches what the original compiler emits, so files built here satisfy the
/// self-consistency identities the loader checks.
/// </remarks>
public sealed class QvmWriter
{
    private readonly List<string> _symbols = [];
    private readonly List<string> _strings = [];

    /// <summary>Gets the code section being assembled.</summary>
    public QvmCodeWriter Code { get; } = new();

    /// <summary>Gets or sets the bytecode version written into the header.</summary>
    public int Version { get; set; } = QvmProgram.IgiVersion;

    /// <summary>Gets or sets the instruction-set identifier written into the header.</summary>
    public int InstructionSetId { get; set; } = QvmProgram.RequiredInstructionSetId;

    /// <summary>Gets or sets the requested stack size in bytes; zero asks for the VM default.</summary>
    public int StackSizeBytes { get; set; }

    /// <summary>Gets or sets the requested local-slot count; zero asks for the VM default.</summary>
    public int MaxLocals { get; set; }

    /// <summary>Interns a symbol name and returns its table index.</summary>
    /// <param name="name">The symbol name.</param>
    /// <returns>The index a <c>PUSHII</c> should use.</returns>
    public int AddSymbol(string name) => Intern(_symbols, name);

    /// <summary>Interns a string constant and returns its table index.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The index a <c>PUSHSI</c> should use.</returns>
    public int AddString(string value) => Intern(_strings, value);

    /// <summary>Emits a <c>PUSHII</c> for a symbol, interning the name.</summary>
    /// <param name="name">The symbol name.</param>
    /// <returns>The code writer, for chaining.</returns>
    public QvmCodeWriter PushSymbol(string name) => Code.PushSymbolIndex(AddSymbol(name));

    /// <summary>Emits a <c>PUSHSI</c> for a string constant, interning the value.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The code writer, for chaining.</returns>
    public QvmCodeWriter PushString(string value) => Code.PushStringIndex(AddString(value));

    /// <summary>Assembles the file.</summary>
    /// <returns>The complete QVM image.</returns>
    public byte[] Build()
    {
        byte[] code = Code.ToArray();
        (byte[] SymbolTable, byte[] SymbolPool) symbols = BuildTable(_symbols);
        (byte[] StringTable, byte[] StringPool) strings = BuildTable(_strings);

        int symbolTableOffset = QvmProgram.HeaderSize;
        int symbolPoolOffset = symbolTableOffset + symbols.SymbolTable.Length;
        int stringTableOffset = symbolPoolOffset + symbols.SymbolPool.Length;
        int stringPoolOffset = stringTableOffset + strings.StringTable.Length;
        int codeOffset = stringPoolOffset + strings.StringPool.Length;

        byte[] image = new byte[codeOffset + code.Length];
        Span<byte> header = image.AsSpan(0, QvmProgram.HeaderSize);

        Encoding.ASCII.GetBytes("LOOP").CopyTo(header);
        Write(header, 0x04, InstructionSetId);
        Write(header, 0x08, Version);
        Write(header, 0x0C, symbolTableOffset);
        Write(header, 0x10, symbolPoolOffset);
        Write(header, 0x14, symbols.SymbolTable.Length);
        Write(header, 0x18, symbols.SymbolPool.Length);
        Write(header, 0x1C, stringTableOffset);
        Write(header, 0x20, stringPoolOffset);
        Write(header, 0x24, strings.StringTable.Length);
        Write(header, 0x28, strings.StringPool.Length);
        Write(header, 0x2C, codeOffset);
        Write(header, 0x30, code.Length);
        Write(header, 0x34, StackSizeBytes);
        Write(header, 0x38, MaxLocals);

        symbols.SymbolTable.CopyTo(image, symbolTableOffset);
        symbols.SymbolPool.CopyTo(image, symbolPoolOffset);
        strings.StringTable.CopyTo(image, stringTableOffset);
        strings.StringPool.CopyTo(image, stringPoolOffset);
        code.CopyTo(image, codeOffset);

        return image;
    }

    private static int Intern(List<string> entries, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int existing = entries.IndexOf(value);
        if (existing >= 0)
        {
            return existing;
        }

        entries.Add(value);
        return entries.Count - 1;
    }

    private static (byte[] Table, byte[] Pool) BuildTable(List<string> entries)
    {
        byte[] table = new byte[entries.Count * sizeof(uint)];
        List<byte> pool = [];

        for (int i = 0; i < entries.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(i * sizeof(uint)), pool.Count);
            pool.AddRange(Encoding.Latin1.GetBytes(entries[i]));
            pool.Add(0);
        }

        return (table, [.. pool]);
    }

    private static void Write(Span<byte> header, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(header[offset..], value);
}

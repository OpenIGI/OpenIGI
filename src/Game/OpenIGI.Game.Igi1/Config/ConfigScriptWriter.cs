using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Game.Config;

namespace OpenIGI.Game.Igi1.Config;

/// <summary>
/// Writes a configuration back out as a compiled script, in the format the original saves.
/// </summary>
/// <remarks>
/// <para>
/// Saving settings means emitting bytecode, because the save file <em>is</em> a script — see
/// <see cref="ConfigScript"/>. The body is one call per setting, in the order a retail file uses,
/// and the header's five sections are laid out exactly as the reader requires: each butts up against
/// the next and the code section ends at the end of the file.
/// </para>
/// <para>
/// A call is emitted in the shape the original's compiler produces:
/// </para>
/// <code>
/// PUSHIIB &lt;function&gt;      ; 2 bytes
/// CALL    argc=n            ; 5 + 4n, each argument an absolute code offset
/// BRA     +len              ; 5 bytes, jumps over the argument blocks
/// &lt;argument block&gt;      ; the value, then BRK
/// POP                       ; discards the result
/// </code>
/// <para>
/// The argument blocks sit between the branch and its target rather than anywhere else in the file,
/// which is what makes the offsets in the <c>CALL</c> and the displacement in the <c>BRA</c> agree.
/// </para>
/// </remarks>
public static class ConfigScriptWriter
{
    private const byte Brk = 0x00;
    private const byte Push = 0x02;
    private const byte PushB = 0x03;
    private const byte PushW = 0x04;
    private const byte PushF = 0x05;
    private const byte PushSIB = 0x09;
    private const byte PushIIB = 0x0D;
    private const byte Pop = 0x12;
    private const byte Bra = 0x14;
    private const byte CallOp = 0x18;

    /// <summary>Emits a configuration as a compiled script.</summary>
    /// <param name="config">The configuration to save.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    public static byte[] Write(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Builder builder = new();

        // The active profile index, which the loader applies after every GOPlayer has been read.
        builder.Call("GOStart", Number(config.ActiveIndex));
        builder.Call("GOContentControlPW", Text(config.ContentControlPassword));
        builder.Call("GOControlsVersion", Number(config.ControlsVersion));

        foreach (PlayerProfile profile in config.Profiles)
        {
            builder.Call("GOPlayer", Text(profile.Name));
            builder.Call("GOActiveMission", Number(profile.ActiveMission));
            builder.Call("GOInMouInv", Number(profile.InvertMouse ? 1 : 0));
            builder.Call("GOInMouSens", Real(profile.MouseSensitivity));

            foreach (InputBinding binding in profile.Bindings)
            {
                builder.Call("GOInRemap", Symbol(binding.Action), Symbol(binding.Device), Symbol(binding.Key));
            }

            builder.Call("GOGfxDisp", Number(profile.DisplayMode));
            builder.Call("GOGfxDevice", Text(profile.DeviceGuid));
            builder.Call("GOGfxGamma", Real(profile.Gamma));
            builder.Call("GOGfxPerformance", Number(profile.PerformanceFlags));
            builder.Call("GOGameLang", Number(profile.Language));
            builder.Call("GOGameDiff", Number(profile.Difficulty));
            builder.Call("GOIsBlood", Number(profile.BloodEnabled ? 1 : 0));
            builder.Call("GOSoundSpeech", Real(profile.SpeechVolume));
            builder.Call("GOSoundMusic", Real(profile.MusicVolume));
            builder.Call("GOSoundFX", Real(profile.SoundEffectsVolume));
        }

        // The script has to end with one, and a retail file does (its last two instructions are POP then
        // BRK). BRK is the only thing that stops the interpreter: the loop reads code[pc] with no bounds
        // check of any kind -- the original's is `code_base[pc]` straight into a dispatch table
        // (0x4BCFD0) -- so a script that merely runs out of instructions does not end, it carries on into
        // whatever follows the code section. Ours stopped at the last POP and faulted with an illegal
        // instruction on the byte after it; the original would have read past its own allocation.
        builder.End();

        return builder.Finish();
    }

    private static Argument Number(int value) => new(ArgumentKind.Number, value, 0f, null);

    private static Argument Real(float value) => new(ArgumentKind.Real, 0, value, null);

    private static Argument Text(string value) => new(ArgumentKind.Text, 0, 0f, value);

    private static Argument Symbol(string name) => new(ArgumentKind.Symbol, 0, 0f, name);

    private enum ArgumentKind
    {
        Number,
        Real,
        Text,
        Symbol,
    }

    private readonly record struct Argument(ArgumentKind Kind, int Number, float Real, string? Name);

    /// <summary>Accumulates the code and the two name pools.</summary>
    private sealed class Builder
    {
        private readonly List<byte> _code = [];
        private readonly List<string> _symbols = [];
        private readonly List<string> _strings = [];
        private readonly Dictionary<string, int> _symbolIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);

        public void Call(string function, params Argument[] arguments)
        {
            // The function symbol first, then the call, then a branch over the argument blocks that
            // follow it. Everything is laid out in one pass because each part's length is known.
            _code.Add(PushIIB);
            _code.Add(checked((byte)InternSymbol(function)));

            _code.Add(CallOp);
            WriteInt32(_code, arguments.Length);

            int slotBase = _code.Count;
            for (int i = 0; i < arguments.Length; i++)
            {
                WriteInt32(_code, 0);
            }

            int braOffset = _code.Count;
            _code.Add(Bra);
            WriteInt32(_code, 0);
            int blocksStart = _code.Count;

            for (int i = 0; i < arguments.Length; i++)
            {
                PatchInt32(slotBase + (i * 4), _code.Count);
                Emit(arguments[i]);
                _code.Add(Brk);
            }

            // The displacement is measured from the end of the branch, so it is exactly the length of
            // the blocks that were just written.
            PatchInt32(braOffset + 1, _code.Count - blocksStart);
            _code.Add(Pop);
        }

        /// <summary>Writes the terminating instruction, after the last statement.</summary>
        public void End() => _code.Add(Brk);

        public byte[] Finish()
        {
            byte[] symbolTable = BuildTable(_symbols, out byte[] symbolPool);
            byte[] stringTable = BuildTable(_strings, out byte[] stringPool);

            int symbolTableOffset = QvmProgram.HeaderSize;
            int symbolPoolOffset = symbolTableOffset + symbolTable.Length;
            int stringTableOffset = symbolPoolOffset + symbolPool.Length;
            int stringPoolOffset = stringTableOffset + stringTable.Length;
            int codeOffset = stringPoolOffset + stringPool.Length;

            byte[] file = new byte[codeOffset + _code.Count];
            Span<byte> span = file;

            Encoding.Latin1.GetBytes("LOOP").CopyTo(span);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x04..], QvmProgram.RequiredInstructionSetId);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x08..], Version);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x0C..], symbolTableOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x10..], symbolPoolOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x14..], symbolTable.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x18..], symbolPool.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x1C..], stringTableOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x20..], stringPoolOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x24..], stringTable.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x28..], stringPool.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x2C..], codeOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x30..], _code.Count);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x34..], StackSizeBytes);
            BinaryPrimitives.WriteInt32LittleEndian(span[0x38..], MaxLocals);

            symbolTable.CopyTo(span[symbolTableOffset..]);
            symbolPool.CopyTo(span[symbolPoolOffset..]);
            stringTable.CopyTo(span[stringTableOffset..]);
            stringPool.CopyTo(span[stringPoolOffset..]);
            CollectionsMarshal.AsSpan(_code).CopyTo(span[codeOffset..]);

            return file;
        }

        /// <summary>The version a retail configuration file carries.</summary>
        private const int Version = 5;

        /// <summary>The stack and locals a retail configuration file declares.</summary>
        private const int StackSizeBytes = 2048;

        private const int MaxLocals = 256;

        private void Emit(Argument argument)
        {
            switch (argument.Kind)
            {
                case ArgumentKind.Number when argument.Number is >= sbyte.MinValue and <= sbyte.MaxValue:
                    _code.Add(PushB);
                    _code.Add(unchecked((byte)(sbyte)argument.Number));
                    break;

                case ArgumentKind.Number when argument.Number is >= short.MinValue and <= short.MaxValue:
                {
                    _code.Add(PushW);
                    byte[] word = new byte[2];
                    BinaryPrimitives.WriteInt16LittleEndian(word, (short)argument.Number);
                    _code.AddRange(word);
                    break;
                }

                // A value too wide for a word takes the full 32-bit immediate. Widening it to a
                // float instead would be lossy above 2^24, and a retail file does carry values up
                // there — a saved display mode of 825437497 comes back as 825437504 through a float.
                case ArgumentKind.Number:
                {
                    _code.Add(Push);
                    WriteInt32(_code, argument.Number);
                    break;
                }

                case ArgumentKind.Real:
                    EmitReal(argument.Real);
                    break;

                case ArgumentKind.Text:
                    _code.Add(PushSIB);
                    _code.Add(checked((byte)InternString(argument.Name ?? string.Empty)));
                    break;

                case ArgumentKind.Symbol:
                    _code.Add(PushIIB);
                    _code.Add(checked((byte)InternSymbol(argument.Name ?? string.Empty)));
                    break;

                default:
                    break;
            }
        }

        private void EmitReal(float value)
        {
            _code.Add(PushF);
            byte[] bits = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(bits, value);
            _code.AddRange(bits);
        }

        private int InternSymbol(string name) => Intern(_symbols, _symbolIndex, name);

        private int InternString(string value) => Intern(_strings, _stringIndex, value);

        private static int Intern(List<string> pool, Dictionary<string, int> index, string value)
        {
            if (index.TryGetValue(value, out int existing))
            {
                return existing;
            }

            int added = pool.Count;
            index[value] = added;
            pool.Add(value);
            return added;
        }

        /// <summary>Builds one of the two name tables: offsets into a NUL-terminated pool.</summary>
        private static byte[] BuildTable(List<string> names, out byte[] pool)
        {
            byte[] table = new byte[names.Count * 4];
            List<byte> bytes = [];

            for (int i = 0; i < names.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(i * 4), bytes.Count);
                bytes.AddRange(Encoding.Latin1.GetBytes(names[i]));
                bytes.Add(0);
            }

            pool = [.. bytes];
            return table;
        }

        private static void WriteInt32(List<byte> code, int value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            code.AddRange(bytes);
        }

        private void PatchInt32(int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(_code)[offset..], value);
    }
}

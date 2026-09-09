using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// I.G.I. 2's bytecode container, read against a retail installation.
/// </summary>
/// <remarks>
/// <para>
/// The sequel emits version 7 where I.G.I. 1 emits 5. The <em>container</em> differs only in its
/// header: measured across retail installations of both games, all 998 I.G.I. 1 scripts put their
/// symbol table at offset 60 and all 1,784 I.G.I. 2 scripts put it at 64, the extra four bytes being a
/// file-info offset.
/// </para>
/// <para>
/// Version 7 also renumbers the opcodes — the control-flow group moves to the front and the push family
/// shifts up by six — which <see cref="QvmOpcodeEncoding"/> translates. The operand shapes do not change
/// with it, and the sweep below is what proves that.
/// </para>
/// </remarks>
public sealed class RetailIgi2QvmTests
{
    /// <summary>Every retail I.G.I. 2 script parses as a version 7 container.</summary>
    /// <remarks>
    /// The header, the three section identities and the section bounds — everything short of decoding
    /// the instructions. Before the version 7 header was understood this rejected all 1,784.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_script_loads()
    {
        IReadOnlyList<string> scripts = GameDataSource.Igi2.EnumerateFiles("*.qvm");
        Assert.NotEmpty(scripts);

        foreach (string path in scripts)
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));

            Assert.Equal(QvmProgram.Igi2Version, program.Version);
            Assert.Equal(QvmProgram.RequiredInstructionSetId, program.InstructionSetId);
            Assert.False(program.Code.IsEmpty, $"'{path}' has no code section.");
        }
    }

    /// <summary>A file-info block is rare, and the one script that carries one still loads.</summary>
    /// <remarks>
    /// Exactly one script in 1,784 has a non-zero file-info offset, and it is <c>config.qvm</c>. That
    /// single file is the reason the "code runs to the end of the file" identity had to become "code
    /// runs to whatever comes next": it is a file the game certainly loads, and the version 5 rule
    /// rejects it. A test requiring every file to carry a block would be wrong in the other direction.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void At_least_one_script_carries_a_file_info_block()
    {
        int withBlock = 0;

        foreach (string path in GameDataSource.Igi2.EnumerateFiles("*.qvm"))
        {
            if (QvmProgram.Read(File.ReadAllBytes(path)).FileInfoOffset != 0)
            {
                withBlock++;
            }
        }

        Assert.True(withBlock > 0, "No I.G.I. 2 script carried a file-info block; the version 7 path is untested.");
    }

    /// <summary>The header size follows the version, and version 5's is four bytes shorter.</summary>
    [Fact]
    public void The_header_grows_by_four_bytes_at_version_seven()
    {
        Assert.Equal(QvmProgram.Version5HeaderSize, QvmProgram.HeaderSizeFor(QvmProgram.IgiVersion));
        Assert.Equal(QvmProgram.Version7HeaderSize, QvmProgram.HeaderSizeFor(QvmProgram.Igi2Version));
        Assert.Equal(4, QvmProgram.Version7HeaderSize - QvmProgram.Version5HeaderSize);
    }

    /// <summary>A version 5 program reports no file-info block, because it has none.</summary>
    /// <remarks>
    /// Reading <c>0x3C</c> unconditionally would return the first four bytes of a version 5 file's
    /// symbol table — a plausible-looking number that would then be used as the end of the code.
    /// </remarks>
    [RequiresGameDataFact]
    public void An_igi1_script_reports_no_file_info_block()
    {
        string script = GameData.EnumerateFiles("*.qvm")[0];
        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(script));

        Assert.Equal(QvmProgram.IgiVersion, program.Version);
        Assert.Equal(0, program.FileInfoOffset);
    }

    /// <summary>Every retail I.G.I. 2 script disassembles, and the sweep lands on the section boundary.</summary>
    /// <remarks>
    /// The proof that the version 7 opcode map and every operand width are right. A linear sweep decodes
    /// each instruction by its own length and steps to the next; one wrong length desynchronises the
    /// walk and it overshoots the end of the section or stops short of it. Landing exactly on the
    /// boundary in all 1,784 scripts is a much stronger statement than any single file decoding.
    /// <para>
    /// This is the same proof the I.G.I. 1 suite uses in
    /// <c>ConfigQvmGoldenTests.EveryRetailScriptLoadsAndDisassembles</c>.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_script_disassembles_and_the_sweep_lands_exactly()
    {
        IReadOnlyList<string> scripts = GameDataSource.Igi2.EnumerateFiles("*.qvm");
        Assert.NotEmpty(scripts);

        foreach (string path in scripts)
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            List<QvmInstruction> instructions = [.. QvmDisassembler.Decode(program)];

            Assert.NotEmpty(instructions);
            Assert.All(instructions, instruction =>
                Assert.True(
                    QvmOpcodeInfo.TryGet(instruction.Opcode, out _),
                    $"{path} at {instruction.Offset}: opcode 0x{(byte)instruction.Opcode:X2} is not in the table."));

            QvmInstruction last = instructions[^1];
            Assert.True(
                last.Offset + last.Length == program.Code.Length,
                $"{path}: the sweep ended at {last.Offset + last.Length} of {program.Code.Length}.");
        }
    }

    /// <summary>The two encodings agree on the arithmetic block and disagree below it.</summary>
    /// <remarks>
    /// Read out of I.G.I. 2's <c>QVM_Open</c>, which fills its 49-slot handler table one index at a
    /// time. The shared tail is why a version 5 reader gets some way into a version 7 file before it
    /// goes wrong, which is a worse failure than not reading it at all.
    /// </remarks>
    [Fact]
    public void Version_seven_renumbers_only_the_first_twenty_five_opcodes()
    {
        for (byte value = 0x19; value <= 0x2F; value++)
        {
            Assert.True(QvmOpcodeEncoding.Version5.TryDecode(value, out QvmOpcode v5));
            Assert.True(QvmOpcodeEncoding.Version7.TryDecode(value, out QvmOpcode v7));
            Assert.Equal(v5, v7);
        }

        // CALL is the clearest of the moves: 0x18 in version 5, 0x07 in version 7 -- and 0x18 in
        // version 7 is POP, which takes no operand at all. That is the collision that desynchronises a
        // version 5 reader, because it goes looking for an argument list that is not there.
        Assert.True(QvmOpcodeEncoding.Version5.TryDecode(0x18, out QvmOpcode v5Call));
        Assert.Equal(QvmOpcode.Call, v5Call);

        Assert.True(QvmOpcodeEncoding.Version7.TryDecode(0x18, out QvmOpcode v7Pop));
        Assert.Equal(QvmOpcode.Pop, v7Pop);

        Assert.True(QvmOpcodeEncoding.Version7.TryDecode(0x07, out QvmOpcode v7Call));
        Assert.Equal(QvmOpcode.Call, v7Call);
    }

    /// <summary>Version 7 leaves RET and JSR undefined, because its own handler table does.</summary>
    /// <remarks>
    /// <c>QVM_Open</c> points both at the illegal-instruction handler, so a file carrying one is
    /// malformed rather than using something this port has not got round to.
    /// </remarks>
    [Fact]
    public void Version_seven_defines_neither_ret_nor_jsr()
    {
        Assert.False(QvmOpcodeEncoding.Version7.TryDecode(0x02, out _));
        Assert.False(QvmOpcodeEncoding.Version7.TryDecode(0x06, out _));

        Assert.False(QvmOpcodeEncoding.Version7.TryEncode(QvmOpcode.Ret, out _));
        Assert.False(QvmOpcodeEncoding.Version7.TryEncode(QvmOpcode.Jsr, out _));
    }

    /// <summary>Encoding and decoding are inverse, for both versions.</summary>
    [Fact]
    public void The_encodings_round_trip()
    {
        foreach (QvmOpcodeEncoding encoding in (QvmOpcodeEncoding[])[QvmOpcodeEncoding.Version5, QvmOpcodeEncoding.Version7])
        {
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                if (!encoding.TryDecode((byte)value, out QvmOpcode opcode))
                {
                    continue;
                }

                Assert.True(encoding.TryEncode(opcode, out byte round));
                Assert.Equal((byte)value, round);
            }
        }
    }
}

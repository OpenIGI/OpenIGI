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
/// file-info offset. That is what these tests cover, and it is all the loader currently claims.
/// </para>
/// <para>
/// <b>The instruction encoding is a separate, unfinished problem.</b> Version 7 renumbers the opcodes —
/// the control-flow group moves to the front and the push family shifts up by six — and it also changes
/// some operand widths: a linear sweep of all 1,784 scripts using the renumbering alone decodes 846 of
/// them and desynchronises on the other 938. So <see cref="QvmDisassembler"/> cannot yet read version 7,
/// and there is deliberately no test here claiming it can. The widths have to be read out of
/// <c>igi2.exe</c>'s own <c>QVM_Op*</c> handlers before any of it is written down.
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
}

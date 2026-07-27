using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class QvmDisassemblerTests
{
    [Fact]
    public void OpcodeTable_NamesEveryOpcodeTheOriginalNames()
    {
        Assert.Equal(48, QvmOpcodeInfo.Count);
        Assert.Equal(48, QvmOpcodeInfo.All().Count);
        Assert.All(QvmOpcodeInfo.All(), info => Assert.False(string.IsNullOrEmpty(info.Mnemonic)));

        Assert.True(QvmOpcodeInfo.TryParseMnemonic("pushiib", out QvmOpcode parsed));
        Assert.Equal(QvmOpcode.PushIIB, parsed);
        Assert.False(QvmOpcodeInfo.TryGet((QvmOpcode)0x30, out _));
    }

    [Fact]
    public void DecodeAt_SignExtendsTheNarrowImmediates()
    {
        byte[] code = new QvmCodeWriter().PushB(-6).PushW(-1000).ToArray();

        QvmInstruction pushB = QvmDisassembler.DecodeAt(code, 0);
        QvmInstruction pushW = QvmDisassembler.DecodeAt(code, 2);

        Assert.Equal(-6, pushB.Integer);
        Assert.Equal(2, pushB.Length);
        Assert.Equal(-1000, pushW.Integer);
        Assert.Equal(3, pushW.Length);
    }

    [Fact]
    public void DecodeAt_MeasuresAnInlineStringByItsTerminator()
    {
        byte[] code = new QvmCodeWriter().PushS("Joe").Emit(QvmOpcode.Brk).ToArray();

        QvmInstruction push = QvmDisassembler.DecodeAt(code, 0);

        Assert.Equal(QvmOpcode.PushS, push.Opcode);
        Assert.Equal("Joe", push.Text);
        Assert.Equal(5, push.Length);
        Assert.Equal(QvmOpcode.Brk, QvmDisassembler.DecodeAt(code, 5).Opcode);
    }

    [Fact]
    public void DecodeAt_ReadsACallsArgumentOffsets()
    {
        QvmCodeWriter writer = new();
        QvmLabel first = writer.CreateLabel();
        QvmLabel second = writer.CreateLabel();
        QvmLabel after = writer.CreateLabel();

        writer.PushSymbolIndex(0);
        writer.Call(first, second);
        writer.Branch(QvmOpcode.Bra, after);
        writer.MarkLabel(first).PushB(1).Emit(QvmOpcode.Brk);
        writer.MarkLabel(second).PushB(2).Emit(QvmOpcode.Brk);
        writer.MarkLabel(after).Emit(QvmOpcode.Pop);

        byte[] code = writer.ToArray();
        QvmInstruction call = QvmDisassembler.DecodeAt(code, 2);

        Assert.Equal(QvmOpcode.Call, call.Opcode);
        Assert.Equal(2, call.Integer);
        Assert.Equal(13, call.Length);
        Assert.Equal([20, 23], call.ArgumentOffsets);
    }

    [Fact]
    public void DecodeAt_MeasuresABranchFromTheEndOfTheInstruction()
    {
        QvmCodeWriter writer = new();
        QvmLabel target = writer.CreateLabel();
        writer.Branch(QvmOpcode.Bra, target);
        writer.Emit(QvmOpcode.Nop).Emit(QvmOpcode.Nop).Emit(QvmOpcode.Nop);
        writer.MarkLabel(target).Emit(QvmOpcode.Brk);

        QvmInstruction branch = QvmDisassembler.DecodeAt(writer.ToArray(), 0);

        Assert.Equal(3, branch.Integer);
        Assert.Equal(8, branch.NextOffset);
    }

    [Fact]
    public void DecodeAt_TreatsABlockSizeAsSomethingToSkip()
    {
        byte[] code = new QvmCodeWriter().Blk(4).Raw(1, 2, 3, 4).Emit(QvmOpcode.Brk).ToArray();

        QvmInstruction block = QvmDisassembler.DecodeAt(code, 0);

        Assert.Equal(4, block.Integer);
        Assert.Equal(5, block.Length);
        Assert.Equal(9, block.NextOffset);
    }

    [Fact]
    public void DecodeAt_RendersAnUnknownOpcodeAsIllegal()
    {
        QvmInstruction illegal = QvmDisassembler.DecodeAt([0x30], 0);

        Assert.Equal(1, illegal.Length);
        Assert.Contains(QvmDisassembler.IllegalMnemonic, QvmDisassembler.Format(illegal), StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeAt_RefusesAnInstructionThatRunsPastTheSection()
    {
        Assert.Throws<QvmFormatException>(() => QvmDisassembler.DecodeAt([(byte)QvmOpcode.Push, 1, 2], 0));
        Assert.Throws<QvmFormatException>(() => QvmDisassembler.DecodeAt([(byte)QvmOpcode.PushS, (byte)'x'], 0));
    }

    [Fact]
    public void Disassemble_ResolvesIndicesAgainstTheProgram()
    {
        QvmWriter writer = new();
        QvmLabel argument = writer.Code.CreateLabel();
        QvmLabel after = writer.Code.CreateLabel();

        writer.PushSymbol("GOStart");
        writer.Code.Call(argument);
        writer.Code.Branch(QvmOpcode.Bra, after);
        writer.Code.MarkLabel(argument).PushB(6).Emit(QvmOpcode.Brk);
        writer.Code.MarkLabel(after).Emit(QvmOpcode.Pop);
        writer.PushString(string.Empty);
        writer.Code.Emit(QvmOpcode.Brk);

        string[] listing = [.. QvmDisassembler.Disassemble(QvmProgram.Read(writer.Build()))];

        Assert.Equal(
            [
                "     0: PUSHIIB #0                    ; symbol \"GOStart\"",
                "     2: CALL    argc=1                ; args @16",
                "    11: BRA     +3                    ; -> 19",
                "    16: PUSHB   6",
                "    18: BRK",
                "    19: POP",
                "    20: PUSHSIB #0                    ; string \"\"",
                "    22: BRK",
            ],
            listing);
    }
}

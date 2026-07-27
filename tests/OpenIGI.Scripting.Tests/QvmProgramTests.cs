using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class QvmProgramTests
{
    [Fact]
    public void Read_ExposesSymbolsStringsAndCode()
    {
        QvmWriter writer = new();
        int symbol = writer.AddSymbol("GOStart");
        int text = writer.AddString("Joe");
        writer.Code.PushSymbolIndex(symbol).PushStringIndex(text).Emit(QvmOpcode.Brk);

        QvmProgram program = QvmProgram.Read(writer.Build());

        Assert.Equal(QvmProgram.IgiVersion, program.Version);
        Assert.Equal(QvmProgram.RequiredInstructionSetId, program.InstructionSetId);
        Assert.Equal(["GOStart"], program.SymbolNames);
        Assert.Equal(["Joe"], program.Strings);
        Assert.Equal(5, program.Code.Length);
    }

    [Fact]
    public void Read_SubstitutesTheDefaultStackAndLocalSizes()
    {
        QvmWriter writer = new();
        writer.Code.Emit(QvmOpcode.Brk);

        QvmProgram program = QvmProgram.Read(writer.Build());

        Assert.Equal(QvmProgram.DefaultStackSizeBytes, program.StackSizeBytes);
        Assert.Equal(QvmProgram.DefaultMaxLocals, program.MaxLocals);
    }

    [Fact]
    public void Read_KeepsAnExplicitStackAndLocalSize()
    {
        QvmWriter writer = new() { StackSizeBytes = 4096, MaxLocals = 64 };
        writer.Code.Emit(QvmOpcode.Brk);

        QvmProgram program = QvmProgram.Read(writer.Build());

        Assert.Equal(4096, program.StackSizeBytes);
        Assert.Equal(64, program.MaxLocals);
    }

    [Fact]
    public void Read_InternsRepeatedNames()
    {
        QvmWriter writer = new();

        Assert.Equal(0, writer.AddSymbol("Task_New"));
        Assert.Equal(0, writer.AddSymbol("Task_New"));
        Assert.Equal(1, writer.AddSymbol("BreakScript"));
    }

    [Fact]
    public void IsQvm_RequiresBothTheMagicAndTheInstructionSet()
    {
        QvmWriter writer = new();
        writer.Code.Emit(QvmOpcode.Brk);
        byte[] image = writer.Build();

        Assert.True(QvmProgram.IsQvm(image));

        // A .spr sprite shares the LOOP magic but carries a different word here.
        image[4] = 9;
        Assert.False(QvmProgram.IsQvm(image));
    }

    [Fact]
    public void Read_RejectsAForeignMagic()
    {
        byte[] image = BuildMinimal();
        image[0] = (byte)'N';

        QvmFormatException error = Assert.Throws<QvmFormatException>(() => QvmProgram.Read(image));
        Assert.Contains("LOOP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsAnUnexpectedInstructionSet()
    {
        QvmWriter writer = new() { InstructionSetId = 7 };
        writer.Code.Emit(QvmOpcode.Brk);

        QvmFormatException error = Assert.Throws<QvmFormatException>(() => QvmProgram.Read(writer.Build()));
        Assert.Contains("instruction set", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsAVersionBelowFive()
    {
        QvmWriter writer = new() { Version = 4 };
        writer.Code.Emit(QvmOpcode.Brk);

        QvmFormatException error = Assert.Throws<QvmFormatException>(() => QvmProgram.Read(writer.Build()));
        Assert.Contains("version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsAHeaderShorterThanSixtyBytes() =>
        Assert.Throws<QvmFormatException>(() => QvmProgram.Read(new byte[QvmProgram.HeaderSize - 1]));

    [Theory]
    [InlineData(0x1C)] // string table offset: must follow the symbol name pool
    [InlineData(0x2C)] // code offset: must follow the string pool
    [InlineData(0x30)] // code size: the section must end at the end of the file
    public void Read_RejectsInconsistentSectionBounds(int headerOffset)
    {
        byte[] image = BuildMinimal();
        image[headerOffset]++;

        Assert.Throws<QvmFormatException>(() => QvmProgram.Read(image));
    }

    [Fact]
    public void TryGetSymbolName_RefusesAnIndexPastTheTable()
    {
        QvmWriter writer = new();
        writer.AddSymbol("GOPlayer");
        writer.Code.Emit(QvmOpcode.Brk);
        QvmProgram program = QvmProgram.Read(writer.Build());

        Assert.True(program.TryGetSymbolName(0, out string name));
        Assert.Equal("GOPlayer", name);
        Assert.False(program.TryGetSymbolName(1, out _));
        Assert.False(program.TryGetSymbolName(-1, out _));
    }

    [Fact]
    public void TryGetString_RefusesAnIndexPastTheTable()
    {
        QvmWriter writer = new();
        writer.AddString("{00000000-0000-0000-0000-000000000000}");
        writer.Code.Emit(QvmOpcode.Brk);
        QvmProgram program = QvmProgram.Read(writer.Build());

        Assert.True(program.TryGetString(0, out string value));
        Assert.Equal("{00000000-0000-0000-0000-000000000000}", value);
        Assert.False(program.TryGetString(1, out _));
    }

    private static byte[] BuildMinimal()
    {
        QvmWriter writer = new();
        writer.AddSymbol("A");
        writer.AddString("B");
        writer.Code.Emit(QvmOpcode.Brk);
        return writer.Build();
    }
}

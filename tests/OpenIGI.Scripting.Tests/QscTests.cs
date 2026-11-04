using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>The QSC language: parsing, printing, compiling and decompiling, without game data.</summary>
public sealed class QscTests
{
    /// <summary>Control flow, operators and literals survive source → image → source unchanged.</summary>
    [Theory]
    [InlineData("Foo();\n")]
    [InlineData("Task_New(-1, \"Container\", \"Root\", 1.5, 0, 0.0,\n\tTask_New(-1, \"EditRigidObj\", \"Box\", 3, \"MODEL\"));\n")]
    [InlineData("if (AIFunction_GetCurrentEventType() == AIEVENT_IDLE)\n{\n\tAIAction_Patrol(602, 0, AIACTIONFLAG_PUSHABLE);\n}\nelse if (!A.isDead && B)\n{\n\tx = 2;\n}\nelse\n{\n\t0;\n}\n")]
    [InlineData("while (n < 10)\n{\n\tn = n + 1;\n}\n")]
    [InlineData("y = (a + b) * -c;\n")]
    [InlineData("y = a - (b - c);\n")]
    [InlineData("y = 1E-05 + 3.0 + 0.4;\n")]
    [InlineData("s = \"quote \\\" slash \\\\ tab \\t\";\n")]
    public void Source_round_trips_through_an_image(string source)
    {
        QscScript parsed = QscParser.ParseScript(source, QvmProgram.IgiVersion);
        byte[] image = QscCompiler.Compile(parsed);
        QscScript decompiled = QscDecompiler.Decompile(image);

        Assert.Equal(source, QscPrinter.Print(decompiled));
        Assert.Equal(image, QscCompiler.Compile(decompiled));
    }

    /// <summary>Both games' encodings compile and decompile; the opcode numbering is the only difference.</summary>
    [Fact]
    public void Version_seven_uses_its_own_opcode_numbering()
    {
        const string source = "if (a)\n{\n\tF(1, 2.0, \"s\");\n}\n";
        byte[] igi1 = QscCompiler.Compile(QscParser.ParseScript(source, QvmProgram.IgiVersion));
        byte[] igi2 = QscCompiler.Compile(QscParser.ParseScript(source, QvmProgram.Igi2Version));

        Assert.Equal(igi1.Length + (QvmProgram.Version7HeaderSize - QvmProgram.Version5HeaderSize), igi2.Length);
        Assert.Equal(QvmProgram.Igi2Version, QvmProgram.Read(igi2).Version);
        Assert.Equal(source, QscPrinter.Print(QscDecompiler.Decompile(igi2)));
    }

    /// <summary>Integers take the shortest push and a negative literal is a unary minus, as the original compiles them.</summary>
    [Fact]
    public void Integers_take_the_shortest_push()
    {
        QscScript script = QscParser.ParseScript("F(0, 1, 100, 1000, 100000, -1);", QvmProgram.IgiVersion);
        QvmProgram program = QvmProgram.Read(QscCompiler.Compile(script));
        QvmOpcode[] pushes = [.. QvmDisassembler.Decode(program)
            .Select(static i => i.Opcode)
            .Where(static o => o is QvmOpcode.Push0 or QvmOpcode.Push1 or QvmOpcode.PushB or QvmOpcode.PushW or QvmOpcode.Push or QvmOpcode.Minus)];

        Assert.Equal(
            [QvmOpcode.Push0, QvmOpcode.Push1, QvmOpcode.PushB, QvmOpcode.PushW, QvmOpcode.Push, QvmOpcode.Push1, QvmOpcode.Minus],
            pushes);
    }

    /// <summary>A real always prints so that it reads back as a real.</summary>
    [Theory]
    [InlineData(0f, "0.0")]
    [InlineData(18f, "18.0")]
    [InlineData(0.4f, "0.4")]
    [InlineData(1e-5f, "1E-05")]
    public void Reals_print_unambiguously(float value, string expected) =>
        Assert.Equal(expected, QscPrinter.FormatReal(value));

    /// <summary>Retail's long-form reals parse to the same 32-bit value as the short form.</summary>
    [Fact]
    public void Retail_long_reals_parse_to_the_same_value()
    {
        QscRealLiteral literal = Assert.IsType<QscRealLiteral>(QscParser.ParseExpression("0.40000000596046448"));
        Assert.Equal(0.4f, literal.Value);
    }

    /// <summary>Comments are skipped, and errors name their line and column.</summary>
    [Fact]
    public void Parse_errors_carry_a_position()
    {
        Assert.Single(QscParser.ParseScript("// a comment\n/* block\n comment */ F();", QvmProgram.IgiVersion).Statements);

        QscParseException error = Assert.Throws<QscParseException>(() => QscParser.ParseScript("F(1,\n  );", QvmProgram.IgiVersion));
        Assert.Equal(2, error.Position.Line);
        Assert.Equal(3, error.Position.Column);
    }

    /// <summary>Code the original compiler never emits is refused rather than guessed at.</summary>
    [Fact]
    public void Unknown_shapes_are_refused()
    {
        QvmWriter writer = new();
        writer.Code.Emit(QvmOpcode.Push1);
        writer.Code.Emit(QvmOpcode.Brk);

        Assert.Throws<QscDecompileException>(() => QscDecompiler.Decompile(writer.Build()));
    }
}

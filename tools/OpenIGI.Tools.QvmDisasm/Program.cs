using System.Globalization;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Tools;

return ToolHost.Run("QvmDisasm", args, Run);

static int Run(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
    {
        if (args.Count == 0)
        {
            return ctx.Usage(UsageText);
        }

        ctx.Out.WriteLine(UsageText);
        return ToolExitCode.Ok;
    }

    try
    {
        return args[0].ToUpperInvariant() switch
        {
            "DISASM" => Disassemble(ctx),
            "INFO" => Info(ctx),
            _ => ctx.Usage(UsageText, $"Unknown command '{args[0]}'."),
        };
    }
    catch (Exception error) when (error is QvmFormatException or IOException or UnauthorizedAccessException)
    {
        return ctx.Fail(error.Message, error);
    }
}

static int Disassemble(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 2)
    {
        return ctx.Usage(UsageText, "usage: QvmDisasm disasm <file.qvm>");
    }

    QvmProgram program = Load(args[1]);

    PrintHeader(ctx.Out, program);
    ctx.Out.WriteLine();

    if (program.SymbolNames.Count > 0)
    {
        ctx.Out.WriteLine("; symbols");
        PrintTable(ctx.Out, program.SymbolNames);
        ctx.Out.WriteLine();
    }

    if (program.Strings.Count > 0)
    {
        ctx.Out.WriteLine("; strings");
        PrintTable(ctx.Out, program.Strings);
        ctx.Out.WriteLine();
    }

    ctx.Out.WriteLine("; code");
    foreach (string line in QvmDisassembler.Disassemble(program))
    {
        ctx.Out.WriteLine(line);
    }

    return ToolExitCode.Ok;
}

static int Info(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 2)
    {
        return ctx.Usage(UsageText, "usage: QvmDisasm info <file.qvm>");
    }

    PrintHeader(ctx.Out, Load(args[1]));
    return ToolExitCode.Ok;
}

static QvmProgram Load(string path) => QvmProgram.Read(File.ReadAllBytes(path));

static void PrintHeader(TextWriter output, QvmProgram program) =>
    output.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"""
         ; QVM version {program.Version}, instruction set {program.InstructionSetId}
         ; {program.SymbolNames.Count} symbol(s), {program.Strings.Count} string(s), {program.Code.Length} code byte(s)
         ; stack {program.StackSizeBytes} byte(s), {program.MaxLocals} local slot(s)
         """));

static void PrintTable(TextWriter output, IReadOnlyList<string?> entries)
{
    for (int i = 0; i < entries.Count; i++)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $";{i,6}: {entries[i] ?? "<out of range>"}"));
    }
}

/// <summary>The tool's usage text.</summary>
internal static partial class Program
{
    private const string UsageText =
        """
        OpenIGI QvmDisasm - inspect and disassemble Project I.G.I. script bytecode.

        usage:
          QvmDisasm disasm <file.qvm>
          QvmDisasm info   <file.qvm>

        Prints the header, the symbol and string tables, and a listing of the code section.
        Table indices are resolved into comments; branch targets and call argument blocks are
        given as code offsets.
        """;
}

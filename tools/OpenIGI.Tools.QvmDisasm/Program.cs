using System.Globalization;
using OpenIGI.Scripting.Qvm;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return args.Length == 0 ? ExitCode.Usage : ExitCode.Ok;
    }

    try
    {
        return args[0].ToUpperInvariant() switch
        {
            "DISASM" => Disassemble(args),
            "INFO" => Info(args),
            _ => Fail($"Unknown command '{args[0]}'."),
        };
    }
    catch (Exception error) when (error is QvmFormatException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: {error.Message}");
        return ExitCode.Failure;
    }
}

static int Disassemble(string[] args)
{
    if (args.Length != 2)
    {
        return Fail("usage: QvmDisasm disasm <file.qvm>");
    }

    QvmProgram program = Load(args[1]);

    PrintHeader(program);
    Console.WriteLine();

    if (program.SymbolNames.Count > 0)
    {
        Console.WriteLine("; symbols");
        PrintTable(program.SymbolNames);
        Console.WriteLine();
    }

    if (program.Strings.Count > 0)
    {
        Console.WriteLine("; strings");
        PrintTable(program.Strings);
        Console.WriteLine();
    }

    Console.WriteLine("; code");
    foreach (string line in QvmDisassembler.Disassemble(program))
    {
        Console.WriteLine(line);
    }

    return ExitCode.Ok;
}

static int Info(string[] args)
{
    if (args.Length != 2)
    {
        return Fail("usage: QvmDisasm info <file.qvm>");
    }

    PrintHeader(Load(args[1]));
    return ExitCode.Ok;
}

static QvmProgram Load(string path) => QvmProgram.Read(File.ReadAllBytes(path));

static void PrintHeader(QvmProgram program) =>
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"""
         ; QVM version {program.Version}, instruction set {program.InstructionSetId}
         ; {program.SymbolNames.Count} symbol(s), {program.Strings.Count} string(s), {program.Code.Length} code byte(s)
         ; stack {program.StackSizeBytes} byte(s), {program.MaxLocals} local slot(s)
         """));

static void PrintTable(IReadOnlyList<string?> entries)
{
    for (int i = 0; i < entries.Count; i++)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $";{i,6}: {entries[i] ?? "<out of range>"}"));
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    PrintUsage();
    return ExitCode.Usage;
}

static void PrintUsage() =>
    Console.WriteLine(
        """
        OpenIGI QvmDisasm - inspect and disassemble Project I.G.I. script bytecode.

        usage:
          QvmDisasm disasm <file.qvm>
          QvmDisasm info   <file.qvm>

        Prints the header, the symbol and string tables, and a listing of the code section.
        Table indices are resolved into comments; branch targets and call argument blocks are
        given as code offsets.
        """);

internal static class ExitCode
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Failure = 2;
}

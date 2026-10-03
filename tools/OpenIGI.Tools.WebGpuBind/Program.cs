using System.Text;
using OpenIGI.Tools;
using OpenIGI.Tools.WebGpuBind;

// Generates src/Rendering/OpenIGI.Rendering.WebGpu.Native/Generated/ from the headers committed beside it.
//
//   dotnet run --project tools/OpenIGI.Tools.WebGpuBind            rewrite the committed bindings
//   dotnet run --project tools/OpenIGI.Tools.WebGpuBind -- --check fail if they are stale
//
// The bindings are committed rather than generated during the build, for the reason the compiled
// shaders are: a contributor should be able to build and run without the generator, and a generated
// file that nobody can read in a diff is a generated file nobody reviews.

return ToolHost.Run("WebGpuBind", args, Run);

static int Run(ToolContext ctx)
{
    // The generator itself failed (a header it cannot read, no solution above it), as distinct from --check
    // finding stale output, which is ToolExitCode.Failure.
    const int GenerationFailed = 2;

    bool checkOnly = ctx.Arguments.Contains("--check", StringComparer.Ordinal);

    try
    {
        string root = Locate();
        string project = Path.Combine(root, "src", "Rendering", "OpenIGI.Rendering.WebGpu.Native");
        string native = Path.Combine(project, "native");
        string generated = Path.Combine(project, "Generated");

        string version = File.ReadAllText(Path.Combine(native, "VERSION")).Trim();

        HeaderModel model = HeaderParser
            .Parse(File.ReadAllText(Path.Combine(native, "webgpu.h")))
            .Concat(HeaderParser.Parse(File.ReadAllText(Path.Combine(native, "wgpu.h"))));

        ctx.Info($"wgpu-native  {version}");
        ctx.Info($"headers      {native}");
        ctx.Info(
            $"declared     {model.Enums.Count} enums, {model.Structs.Count} structs, {model.Handles.Count} handles, "
            + $"{model.Callbacks.Count} callbacks, {model.Functions.Count} functions");
        ctx.Info(string.Empty);

        IReadOnlyList<GeneratedFile> files = new BindingEmitter(model, version).Emit();

        string browser = Path.Combine(root, "src", "Rendering", "OpenIGI.Rendering.WebGpu.Browser");

        if (!checkOnly)
        {
            Directory.CreateDirectory(generated);
            Directory.CreateDirectory(browser);
        }

        // The C# binding into Generated/, and the browser bridge's enum tables beside the bridge.
        (string Path, GeneratedFile File)[] outputs =
        [
            .. files.Select(file => (Path.Combine(generated, file.FileName), file)),
            (Path.Combine(browser, BrowserEnumEmitter.FileName),
                new GeneratedFile(BrowserEnumEmitter.FileName, BrowserEnumEmitter.Emit(model, version))),
        ];

        int stale = 0;
        foreach ((string path, GeneratedFile file) in outputs)
        {
            byte[] wanted = Encoding.UTF8.GetBytes(file.Text);
            bool exists = File.Exists(path);

            // Compared with line endings normalised, because the working tree's are not the repository's:
            // this repository stores LF and a Windows checkout can hold CRLF, so a byte comparison would
            // call every file stale on one machine and current on the next. What is written keeps the
            // platform's own endings; git normalises them back on the way in.
            bool same = exists && string.Equals(
                Normalise(File.ReadAllText(path)),
                Normalise(file.Text),
                StringComparison.Ordinal);

            if (!same)
            {
                stale++;
                if (!checkOnly)
                {
                    File.WriteAllBytes(path, wanted);
                }
            }

            string label = same ? "unchanged" : exists ? (checkOnly ? "STALE" : "rewritten") : (checkOnly ? "MISSING" : "added");
            ctx.Out.WriteLine($"  {label,-10} {file.FileName}");
        }

        ctx.Out.WriteLine();

        if (!checkOnly)
        {
            ctx.Info($"{outputs.Length} file(s), {stale} written.");
            return ToolExitCode.Ok;
        }

        if (stale == 0)
        {
            ctx.Info($"{outputs.Length} file(s) are current.");
            return ToolExitCode.Ok;
        }

        return ctx.Fail(
            $"{stale} generated file(s) do not match the headers. Run the tool without --check and commit what it writes.");
    }
    catch (WebGpuBindException error)
    {
        ctx.Fail(error.Message, error);
        return GenerationFailed;
    }
}

static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

static string Locate()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new WebGpuBindException("OpenIGI.slnx was not found above the tool's binary.");
}

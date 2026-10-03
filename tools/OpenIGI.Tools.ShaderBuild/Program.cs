using OpenIGI.Tools;
using OpenIGI.Tools.ShaderBuild;

// Compiles src/Rendering/OpenIGI.Shaders from Slang to SPIR-V and WGSL.
//
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild            regenerate the committed output
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild -- --check fail if the committed output is stale
//
// The committed artefacts are what the game loads; this tool is what produces them, and --check is
// what stops a shader source being edited without its compiled form following.

return ToolHost.Run("ShaderBuild", args, Run);

static int Run(ToolContext ctx)
{
    // The build itself failed (no slangc, a shader that does not compile), as distinct from --check finding
    // stale output, which is ToolExitCode.Failure.
    const int BuildFailed = 2;

    bool checkOnly = ctx.Arguments.Contains("--check", StringComparer.Ordinal);

    try
    {
        string projectDirectory = ShaderProjectLocator.Locate();
        string slangc = SlangLocator.Locate();
        SlangCompiler compiler = new(slangc);

        string version = compiler.Version();
        ctx.Info($"slangc   {slangc}");
        ctx.Info($"version  {version}");
        ctx.Info($"library  {projectDirectory}");
        ctx.Info(string.Empty);

        ShaderLibraryBuilder builder = new(compiler, projectDirectory);
        IReadOnlyList<ArtifactResult> results = builder.Build(checkOnly);

        foreach (ArtifactResult result in results)
        {
            string label = result.State switch
            {
                ArtifactState.Unchanged => "unchanged",
                ArtifactState.Changed => checkOnly ? "STALE" : "rewritten",
                _ => checkOnly ? "MISSING" : "added",
            };
            ctx.Out.WriteLine($"  {label,-10} {result.RelativePath}");
        }

        int stale = results.Count(result => result.State != ArtifactState.Unchanged);
        ctx.Out.WriteLine();

        foreach (string warning in compiler.Warnings)
        {
            ctx.Warn(warning);
        }

        if (!checkOnly)
        {
            if (builder.Pin is { } moved)
            {
                ctx.Info($"slangVersion moved to {moved}; commit ShaderModules.json with the output.");
            }

            ctx.Info($"{results.Count} artefact(s), {stale} written.");
            return ToolExitCode.Ok;
        }

        if (stale == 0)
        {
            ctx.Info($"{results.Count} artefact(s) are current.");
            return ToolExitCode.Ok;
        }

        return ctx.Fail(
            $"{stale} of {results.Count} committed artefact(s) do not match the sources. "
            + "Run `dotnet run --project tools/OpenIGI.Tools.ShaderBuild` and commit the result.");
    }
    catch (ShaderBuildException exception)
    {
        ctx.Fail(exception.Message, exception);
        return BuildFailed;
    }
}

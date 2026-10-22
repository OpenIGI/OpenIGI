using OpenIGI.Tools;
using OpenIGI.Shaders;
using OpenIGI.Tools.ShaderBuild;

// Compiles src/Rendering/OpenIGI.Shaders from Slang to SPIR-V, WGSL and DXIL.
//
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild            regenerate the committed output
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild -- --check fail if the committed output is stale
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild -- --project <mod>/shaders
//                                                                    compile a mod's shaders the same way, beside them
//
// A mod's shader folder is laid out as the library is: a ShaderModules.json, its sources under Source/, its output
// under Generated/. Its sources include the game's own library, so "Mod/PostPass.slang" -- the contract a mod's pass
// is written against -- is found from either.
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
        string libraryDirectory = ShaderProjectLocator.Locate();
        int projectArgument = ctx.Arguments.ToList().IndexOf("--project");
        string projectDirectory = projectArgument >= 0 && projectArgument + 1 < ctx.Arguments.Count
            ? Path.GetFullPath(ctx.Arguments[projectArgument + 1])
            : libraryDirectory;
        string slangc = SlangLocator.Locate();
        ShaderManifest manifest = ShaderLibraryBuilder.ReadManifest(projectDirectory);
        string repositoryRoot = Path.GetFullPath(Path.Combine(libraryDirectory, "..", "..", ".."));
        DxcInstall dxc = DxcLocator.Locate(repositoryRoot, manifest.DxcVersion);
        SlangCompiler compiler = new(slangc, dxc.LibraryDirectory)
        {
            IncludeDirectories = projectDirectory == libraryDirectory ? [] : [Path.Combine(libraryDirectory, "Source")],
        };

        string version = compiler.Version();
        ctx.Info($"slangc   {slangc}");
        ctx.Info($"version  {version}");
        ctx.Info($"dxc      {dxc.LibraryDirectory} ({dxc.Version})");
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

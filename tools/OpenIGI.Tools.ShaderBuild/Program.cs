using OpenIGI.Tools.ShaderBuild;

// Compiles src/OpenIGI.Shaders from Slang to SPIR-V and WGSL.
//
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild            regenerate the committed output
//   dotnet run --project tools/OpenIGI.Tools.ShaderBuild -- --check fail if the committed output is stale
//
// The committed artefacts are what the game loads; this tool is what produces them, and --check is
// what stops a shader source being edited without its compiled form following.

bool checkOnly = args.Contains("--check", StringComparer.Ordinal);

try
{
    string projectDirectory = ShaderProjectLocator.Locate();
    string slangc = SlangLocator.Locate();
    SlangCompiler compiler = new(slangc);

    string version = compiler.Version();
    Console.WriteLine($"slangc   {slangc}");
    Console.WriteLine($"version  {version}");
    Console.WriteLine($"library  {projectDirectory}");
    Console.WriteLine();

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
        Console.WriteLine($"  {label,-10} {result.RelativePath}");
    }

    int stale = results.Count(result => result.State != ArtifactState.Unchanged);
    Console.WriteLine();

    foreach (string warning in compiler.Warnings)
    {
        Console.WriteLine($"  {warning}");
    }

    if (compiler.Warnings.Count > 0)
    {
        Console.WriteLine();
    }


    if (!checkOnly)
    {
        if (builder.Pin is { } moved)
        {
            Console.WriteLine($"slangVersion moved to {moved}; commit ShaderModules.json with the output.");
        }

        Console.WriteLine($"{results.Count} artefact(s), {stale} written.");
        return 0;
    }

    if (stale == 0)
    {
        Console.WriteLine($"{results.Count} artefact(s) are current.");
        return 0;
    }

    Console.Error.WriteLine(
        $"{stale} of {results.Count} committed artefact(s) do not match the sources. "
        + "Run `dotnet run --project tools/OpenIGI.Tools.ShaderBuild` and commit the result.");
    return 1;
}
catch (ShaderBuildException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

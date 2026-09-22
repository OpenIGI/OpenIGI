namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Finds <c>src/OpenIGI.Shaders</c> from wherever the tool was started.</summary>
/// <remarks>
/// The tool is run through <c>dotnet run</c>, whose working directory is the caller's, so the
/// library is found by walking up to the repository root rather than by assuming one.
/// </remarks>
public static class ShaderProjectLocator
{
    private const string RelativePath = "src/OpenIGI.Shaders";

    /// <summary>Finds the shader library's project directory.</summary>
    /// <returns>The full path to <c>src/OpenIGI.Shaders</c>.</returns>
    /// <exception cref="ShaderBuildException">The directory could not be found.</exception>
    public static string Locate()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(Path.Combine(candidate, "OpenIGI.Shaders.csproj")))
                {
                    return candidate;
                }
            }
        }

        throw new ShaderBuildException(
            $"Could not find {RelativePath} above the working directory. Run the tool from inside the repository.");
    }
}

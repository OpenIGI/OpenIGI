namespace OpenIGI.Shaders.Tests;

/// <summary>Locates the shader library's sources, which some invariants are checked against.</summary>
/// <remarks>
/// The catalogue itself reads only embedded resources, so it needs no paths; these tests additionally
/// compare what is embedded against what is on disk, which is the only way to notice a source that
/// was added and never compiled.
/// </remarks>
internal static class ShaderRepository
{
    /// <summary>Gets the <c>src/OpenIGI.Shaders</c> directory.</summary>
    public static string ProjectDirectory { get; } = Path.Combine(Root(), "src", "OpenIGI.Shaders");

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("OpenIGI.slnx was not found above the test binary.");
    }
}

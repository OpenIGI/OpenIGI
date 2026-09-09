using System.Text.RegularExpressions;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>Finds the repository on disk and reads the project graph out of it.</summary>
/// <remarks>
/// The layering rules are facts about the <em>solution</em>, not about any assembly, so they are checked
/// by reading the <c>.csproj</c> files rather than by reflecting over what happens to be loaded. That
/// also means a rule can name a project no test assembly references, which is the point: the rule that
/// matters most is the one about a reference nobody has added yet.
/// </remarks>
internal static class RepositoryLayout
{
    /// <summary>Gets the repository root, found by walking up to the solution file.</summary>
    /// <exception cref="InvalidOperationException">No <c>OpenIGI.slnx</c> was found above the test binary.</exception>
    public static string Root { get; } = FindRoot();

    /// <summary>Gets every project in <c>src</c> and <c>apps</c>, by assembly name.</summary>
    public static IReadOnlyDictionary<string, ProjectInfo> Projects { get; } = ReadProjects();

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenIGI.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("OpenIGI.slnx was not found above the test binary.");
    }

    private static Dictionary<string, ProjectInfo> ReadProjects()
    {
        Dictionary<string, ProjectInfo> projects = new(StringComparer.Ordinal);

        foreach (string area in (string[])["src", "apps"])
        {
            string areaPath = Path.Combine(Root, area);
            if (!Directory.Exists(areaPath))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(areaPath, "*.csproj", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string text = File.ReadAllText(file);

                // A reference is <ProjectReference Include="..\Other\Other.csproj" />; the referenced
                // project's assembly name is its file name, which is the identity used throughout.
                List<string> references = [];
                foreach (Match match in Regex.Matches(text, @"ProjectReference\s+Include\s*=\s*""([^""]+)"""))
                {
                    references.Add(Path.GetFileNameWithoutExtension(match.Groups[1].Value.Replace('\\', '/')));
                }

                projects[name] = new ProjectInfo(name, area, Path.GetDirectoryName(file)!, references);
            }
        }

        return projects;
    }
}

/// <summary>One project in the solution.</summary>
/// <param name="Name">Its assembly name.</param>
/// <param name="Area">The top-level directory it lives under — <c>src</c> or <c>apps</c>.</param>
/// <param name="Directory">Its absolute directory.</param>
/// <param name="References">The assembly names it references, analyzers included.</param>
internal sealed record ProjectInfo(string Name, string Area, string Directory, IReadOnlyList<string> References)
{
    /// <summary>Enumerates the project's C# sources, skipping build output.</summary>
    public IEnumerable<string> Sources() =>
        System.IO.Directory.EnumerateFiles(Directory, "*.cs", SearchOption.AllDirectories)
            .Where(static p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}

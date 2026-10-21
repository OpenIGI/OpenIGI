using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>
/// No shared code starts threads or tasks of its own: work off the frame loop goes through
/// <c>IWorkScheduler</c>, which a single-threaded host runs inline.
/// </summary>
/// <remarks>
/// A browser running .NET has one thread. A <c>Task.Run</c> there does not run beside the frame; it runs
/// when the loop next yields, which a loop that is the page's frame callback never does in time -- so one
/// stray call is a feature that silently never happens, or a wait that never returns. Checked by reading
/// the sources, because the rule is about code nobody has written yet.
/// </remarks>
public sealed partial class ThreadingTests
{
    // What may start threads itself: the pool scheduler, which is the point of it, and the backends a
    // browser never loads.
    private static readonly string[] Allowed =
    [
        Path.Combine("src", "Core", "OpenIGI.Core", "Threading", "ThreadPoolWorkScheduler.cs"),
        Path.Combine("src", "Rendering", "OpenIGI.Rendering.Vulkan") + Path.DirectorySeparatorChar,
        Path.Combine("src", "Rendering", "OpenIGI.Rendering.D3D12") + Path.DirectorySeparatorChar,
        Path.Combine("src", "Platform", "OpenIGI.Platform.Desktop") + Path.DirectorySeparatorChar,
        Path.Combine("src", "Audio", "OpenIGI.Audio.OpenAL") + Path.DirectorySeparatorChar,
    ];

    [Fact]
    public void Shared_code_leaves_threads_to_the_scheduler()
    {
        string src = Path.Combine(RepositoryLayout.Root, "src");
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(RepositoryLayout.Root, file);

            if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Allowed.Any(allowed => relative.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (!line.StartsWith("//", StringComparison.Ordinal) && StartsWork().IsMatch(line))
                {
                    offenders.Add($"{relative}:{i + 1}: {line}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Shared code starts work of its own; hand it to IWorkScheduler instead:\n  " + string.Join("\n  ", offenders));
    }

    [GeneratedRegex(@"\bTask\.Run\s*[<(]|\bParallel\.For(Each)?\s*[<(]|\bnew\s+Thread\s*\(|\bThreadPool\.(Unsafe)?QueueUserWorkItem\b|\bTask\.Factory\.StartNew\b")]
    private static partial Regex StartsWork();
}

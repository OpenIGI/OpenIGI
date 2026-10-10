using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>
/// The layering rules from <c>docs/architecture.md</c>, checked by the build rather than by review.
/// </summary>
/// <remarks>
/// These were prose for the whole of the port's life and <c>apps/OpenIGI.Desktop</c> is what that cost:
/// 59,000 lines of portable game code accumulated inside the executable, one reasonable-looking commit
/// at a time, without any single one of them breaking a rule loudly enough to notice. A rule the build
/// checks is a rule; a rule in a document is a convention.
/// </remarks>
public sealed class LayeringTests
{
    /// <summary>The concrete backends. Everything below <c>apps/</c> must be free of them.</summary>
    private static readonly string[] Backends =
    [
        "OpenIGI.Rendering.Vulkan",
        "OpenIGI.Rendering.D3D12",
        "OpenIGI.Rendering.Streamline",
        "OpenIGI.Audio.OpenAL",
        "OpenIGI.Platform.Desktop",
    ];

    /// <summary>The per-title projects. No two of them may meet except in an app.</summary>
    private static readonly string[] TitleProjects =
    [
        "OpenIGI.Game.Igi1",
        "OpenIGI.Game.Igi2",
    ];

    /// <summary>Rule 1 — nothing below <c>apps/</c> names a backend.</summary>
    /// <remarks>
    /// Backends are chosen and injected by an app composition root. This mirrors the original engine's
    /// <c>RenderMode</c> seam, where "Direct3D" was one registered mode among a designed-for-many set.
    /// </remarks>
    [Fact]
    public void No_library_references_a_backend()
    {
        List<string> broken = [];

        foreach (ProjectInfo project in RepositoryLayout.Projects.Values)
        {
            if (project.Area != "src" || Backends.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string backend in project.References.Intersect(Backends, StringComparer.Ordinal))
            {
                broken.Add($"{project.Name} -> {backend}");
            }
        }

        Assert.True(
            broken.Count == 0,
            "A library references a concrete backend. Backends are wired in the app composition root:\n  "
            + string.Join("\n  ", broken));
    }

    /// <summary>Rule 2 — a title project never references another title project.</summary>
    /// <remarks>
    /// Only an app composition root may name both, and it does so through the title runtime. This is
    /// what keeps an I.G.I. 2 finding from reaching I.G.I. 1 code by the back door of a shared type.
    /// </remarks>
    [Fact]
    public void Title_projects_do_not_reference_each_other()
    {
        List<string> broken = [];

        foreach (string title in TitleProjects)
        {
            if (!RepositoryLayout.Projects.TryGetValue(title, out ProjectInfo? project))
            {
                continue;
            }

            foreach (string other in project.References.Intersect(TitleProjects, StringComparer.Ordinal))
            {
                broken.Add($"{title} -> {other}");
            }
        }

        Assert.True(broken.Count == 0, "Title projects must not meet:\n  " + string.Join("\n  ", broken));
    }

    /// <summary>Rule 2 — and nothing below an app references a title project either.</summary>
    [Fact]
    public void Only_an_app_references_a_title_project()
    {
        List<string> broken = [];

        foreach (ProjectInfo project in RepositoryLayout.Projects.Values)
        {
            if (project.Area != "src" || TitleProjects.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string title in project.References.Intersect(TitleProjects, StringComparer.Ordinal))
            {
                broken.Add($"{project.Name} -> {title}");
            }
        }

        Assert.True(
            broken.Count == 0,
            "Only an app composition root may name a title project:\n  " + string.Join("\n  ", broken));
    }

    /// <summary>Rule 4 — <c>OpenIGI.Core</c> has no dependencies at all.</summary>
    [Fact]
    public void Core_depends_on_nothing()
    {
        ProjectInfo core = RepositoryLayout.Projects["OpenIGI.Core"];

        Assert.True(
            core.References.Count == 0,
            "OpenIGI.Core must depend on nothing; everything may depend on it. Found: "
            + string.Join(", ", core.References));
    }

    /// <summary>
    /// The interface the game and the launcher share stays free of the game's renderer, so the launcher
    /// can draw it without carrying the engine's graphics stack and shaders.
    /// </summary>
    [Fact]
    public void The_shared_interface_names_no_graphics_project()
    {
        string[] shared = ["OpenIGI.UI", "OpenIGI.UI.Kit", "OpenIGI.UI.Skia", "OpenIGI.Frontend.Settings", "OpenIGI.Launcher"];
        string[] graphics = ["OpenIGI.Rendering", "OpenIGI.Rendering.Pipeline", "OpenIGI.Rendering.Rhi", "OpenIGI.Shaders", "OpenIGI.UI.Rhi"];
        List<string> broken = [];

        foreach (string name in shared)
        {
            Assert.True(RepositoryLayout.Projects.ContainsKey(name), $"'{name}' is named by the shared-interface rule but is not a project.");
            foreach (string reference in RepositoryLayout.Projects[name].References.Intersect(graphics, StringComparer.Ordinal))
            {
                broken.Add($"{name} -> {reference}");
            }
        }

        Assert.True(
            broken.Count == 0,
            "A project the launcher draws with names the game's renderer:\n  " + string.Join("\n  ", broken));
    }

    /// <summary>Every project the rules name still exists, so a rename cannot quietly disarm a rule.</summary>
    [Fact]
    public void The_rules_name_projects_that_exist()
    {
        Assert.All(Backends, b => Assert.True(
            RepositoryLayout.Projects.ContainsKey(b),
            $"'{b}' is named by the backend rule but is not a project. Rename it in the rule, or drop it."));

        Assert.Contains("OpenIGI.Presentation", RepositoryLayout.Projects.Keys);
        Assert.Contains("OpenIGI.Host", RepositoryLayout.Projects.Keys);
    }
}

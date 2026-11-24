using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>
/// The simulation reckons its sines, roots of powers and logarithms with <c>SimMath</c>, never the platform's: the same inputs
/// must give the same level on every machine, or a replay drifts and the server's check of a run turns it down.
/// </summary>
/// <remarks>
/// <see cref="Math.Sin"/> and its kin call each platform's C library, and Windows', glibc's, Apple's and the browser's differ in
/// the last bit for some arguments. Checked by reading the sources of every assembly the simulation runs in, because the rule
/// is about code nobody has written yet. What only draws or bakes -- textures, the camera, the lightmap and probe bakers --
/// keeps the platform's routines, which are faster.
/// </remarks>
public sealed partial class SimulationMathTests
{
    // The assemblies the simulation runs in.
    private static readonly string[] Simulation =
    [
        Path.Combine("src", "Core", "OpenIGI.Core"),
        Path.Combine("src", "Core", "OpenIGI.Formats"),
        Path.Combine("src", "Core", "OpenIGI.Scripting"),
        Path.Combine("src", "Engine", "OpenIGI.Engine"),
        Path.Combine("src", "Game", "OpenIGI.Game"),
        Path.Combine("src", "Game", "OpenIGI.Game.Igi1"),
        Path.Combine("src", "Game", "OpenIGI.Game.Igi2"),
    ];

    // What in them only draws or bakes, never steps the level.
    private static readonly string[] Allowed =
    [
        Path.Combine("src", "Core", "OpenIGI.Core", "Graphics") + Path.DirectorySeparatorChar,
        Path.Combine("src", "Core", "OpenIGI.Formats", "Textures") + Path.DirectorySeparatorChar,
        Path.Combine("src", "Core", "OpenIGI.Core", "Numerics", "SimMath.cs"),
        Path.Combine("src", "Core", "OpenIGI.Core", "Numerics", "SimMathF.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "DistanceFieldAtlas.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "IrradianceProbeBaker.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "LightmapBaker.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "LightmapDenoiser.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "SkyExposureProbe.cs"),
        Path.Combine("src", "Game", "OpenIGI.Game", "World", "WorldSceneDiagnostics.cs"),
    ];

    [Fact]
    public void The_simulation_uses_its_own_transcendental_functions()
    {
        List<string> offenders = [];
        foreach (string assembly in Simulation)
        {
            string directory = Path.Combine(RepositoryLayout.Root, assembly);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RepositoryLayout.Root, file);
                if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || Allowed.Any(allowed => relative.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (!line.StartsWith("//", StringComparison.Ordinal) && PlatformMath().IsMatch(line))
                    {
                        offenders.Add($"{relative}:{i + 1}: {line}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The simulation calls the platform's transcendental functions, which differ between machines; use SimMath or SimMathF:\n  "
            + string.Join("\n  ", offenders));
    }

    // Math's, MathF's and the generic-math statics' transcendental functions; Sqrt, Abs, Floor and the like are exact everywhere.
    [GeneratedRegex(@"(?<![\w.])(System\.)?(Math|MathF|double|float|Double|Single)\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Exp|Exp2|Exp10|Log|Log2|Log10|Pow|SinCos|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|Cbrt|Hypot|RootN|SinPi|CosPi|TanPi|ReciprocalEstimate|ReciprocalSqrtEstimate)\s*\(")]
    private static partial Regex PlatformMath();
}

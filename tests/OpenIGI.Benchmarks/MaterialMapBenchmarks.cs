using BenchmarkDotNet.Attributes;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Benchmarks;

/// <summary>What deriving a texture's detail maps costs.</summary>
/// <remarks>
/// <para>
/// This is the port's largest single block of CPU work outside the lightmap bake, and unlike the bake
/// it runs on every level load for every texture the pack has no cached map for. A level's worth of
/// tiles measured at most of a second, which is why <c>MaterialDetailPack</c> pushes it onto workers
/// and caches the result to disk — but a worker pool hides the cost rather than removing it, and the
/// first play of any level still pays it in full.
/// </para>
/// <para>
/// Sized 32 / 64 / 128 because the derivation's cost is superlinear in the texture's smaller side: the
/// blur radius is a FRACTION of it (<c>Relief</c>, 0.12), so a 128 does not cost four times a 64.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MaterialMapBenchmarks
{
    private Bgra32[] _pixels = [];
    private MaterialMapGenerator.Maps? _maps;
    private Bgra32[] _packed = [];

    /// <summary>Gets or sets the texture's width and height in pixels.</summary>
    [Params(32, 64, 128)]
    public int Size { get; set; }

    /// <summary>Builds the input texture once per size.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _pixels = TextureFixtures.Diffuse(Size);
        _maps = MaterialMapGenerator.Generate(_pixels, Size, Size);
        _packed = new Bgra32[Size * Size];
    }

    /// <summary>The whole derivation, which is what a level load actually pays.</summary>
    /// <returns>The derived maps.</returns>
    [Benchmark(Description = "Generate (all maps)")]
    public MaterialMapGenerator.Maps Generate() =>
        MaterialMapGenerator.Generate(_pixels, Size, Size);

    /// <summary>The derivation without the occlusion sweep, to price the sweep by difference.</summary>
    /// <remarks>
    /// A zero strength makes <c>DeriveOcclusion</c> return a filled array without sweeping, so the gap
    /// between this and <see cref="Generate"/> is what the horizon search costs. Measuring it through
    /// the public options rather than by exposing the private stage keeps the benchmark honest about
    /// what a caller can actually reach.
    /// </remarks>
    /// <returns>The derived maps.</returns>
    [Benchmark(Description = "Generate (no occlusion)")]
    public MaterialMapGenerator.Maps GenerateWithoutOcclusion() =>
        MaterialMapGenerator.Generate(
            _pixels, Size, Size, MaterialMapGenerator.Options.Default with { OcclusionStrength = 0f });

    /// <summary>Packing alone, which is the element-wise tail of the derivation.</summary>
    [Benchmark(Description = "PackDetail")]
    public void PackDetail() =>
        MaterialMapGenerator.PackDetail(_maps!, _packed);
}

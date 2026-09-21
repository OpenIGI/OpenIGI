using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a texture is judged to be made of something.
/// </summary>
/// <remarks>
/// The rules exist in this shape because I.G.I.'s textures are numbered rather than named, so the
/// usual <c>metal*</c> / <c>snow*</c> matching has nothing to match on. What is left is the one
/// meaningful suffix the packs do use, and a list of numbers someone has looked at.
/// </remarks>
public sealed class MaterialLibraryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "openigi-materials-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private MaterialLibrary Write(string json)
    {
        File.WriteAllText(_path, json);
        return MaterialLibrary.Load(_path);
    }

    [Fact]
    public void ASuffixRuleMatchesTheFamilyItNames()
    {
        MaterialLibrary library = Write("""
            { "textures": { "*_argb8888": { "roughness": 0.08 } } }
            """);

        Assert.Equal(0.08f, library.For("216_01_1_argb8888").Roughness, 4);
        Assert.Equal(SurfaceMaterial.Default.Roughness, library.For("216_01_1").Roughness, 4);
    }

    [Fact]
    public void AnExactNameBeatsASuffixRule()
    {
        // The suffix says what a family is usually made of; the name is someone having looked at this
        // one and found foliage where the rule assumed glass.
        MaterialLibrary library = Write("""
            {
              "textures": {
                "*_argb8888": { "roughness": 0.08 },
                "301_07_1_argb8888": { "roughness": 0.9 }
              }
            }
            """);

        Assert.Equal(0.9f, library.For("301_07_1_argb8888").Roughness, 4);
        Assert.Equal(0.08f, library.For("216_01_1_argb8888").Roughness, 4);
    }

    [Fact]
    public void AnEntryTakesTheFilesDefaultForWhatItDoesNotSay()
    {
        MaterialLibrary library = Write("""
            {
              "default": { "roughness": 0.55, "metallic": 0.0 },
              "textures": { "220_03_1": { "metallic": 1.0 } }
            }
            """);

        SurfaceMaterial metal = library.For("220_03_1");

        Assert.Equal(1f, metal.Metallic, 4);
        Assert.Equal(0.55f, metal.Roughness, 4);
    }

    [Fact]
    public void AnUnknownTextureTakesTheDefault()
    {
        MaterialLibrary library = Write("""{ "default": { "roughness": 0.7 } }""");

        Assert.Equal(0.7f, library.For("999_99_1").Roughness, 4);
        Assert.Equal(0.7f, library.For(null).Roughness, 4);
        Assert.Equal(0.7f, library.For(string.Empty).Roughness, 4);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        MaterialLibrary library = Write("""{ "textures": { "*_ARGB8888": { "roughness": 0.1 } } }""");

        Assert.Equal(0.1f, library.For("216_01_1_argb8888").Roughness, 4);
    }

    [Fact]
    public void AMalformedFileLeavesEverySurfaceAtTheDefault()
    {
        // A broken material file must never stop the game: without it the world draws exactly as it did
        // before materials existed.
        MaterialLibrary library = Write("{ this is not json");

        Assert.Equal(SurfaceMaterial.Default, library.For("216_01_1_argb8888"));
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        MaterialLibrary library = MaterialLibrary.Load(
            Path.Combine(Path.GetTempPath(), "no-such-materials.json"));

        Assert.Equal(SurfaceMaterial.Default, library.For("anything"));
        Assert.Equal(0, library.NamedCount);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        // The shipped file documents itself at length and is edited by hand.
        MaterialLibrary library = Write("""
            {
              // why this one is glass
              "textures": { "*_argb8888": { "roughness": 0.08, }, },
            }
            """);

        Assert.Equal(1, library.SuffixCount);
    }

    [Fact]
    public void ReflectionsRunOnTheFallbackEvenWithNoMarchingSteps()
    {
        // Switching the march off should make reflections coarse, not absent: a smooth surface
        // reflecting the sky roughly is closer to right than one reflecting nothing.
        ReflectionSettings settings = new(Quality: 0, Intensity: 1f);

        Assert.True(settings.Enabled);
        Assert.False(settings.Marches);
    }

    [Fact]
    public void ReflectionsAreOffWhenNothingCouldShow()
    {
        Assert.False(ReflectionSettings.Off.Enabled);
        Assert.False(new ReflectionSettings(Quality: 32, Intensity: 0f).Enabled);
        Assert.False(new ReflectionSettings(Quality: 32, MaxRoughness: 0f).Enabled);
    }

    /// <summary>The resolve cache survives being asked from many threads at once.</summary>
    /// <remarks>
    /// <para>
    /// It did not. <c>For</c> memoised into a plain <see cref="Dictionary{TKey, TValue}"/> and wrote to it
    /// on every miss, while the world renderer asks for a material per sub-mesh and a mission worker
    /// builds the next level's scene from its own thread. An unsynchronised write against a concurrent
    /// read can resize the buckets under the reader, which returns a wrong entry or does not return.
    /// </para>
    /// <para>
    /// The symptom was <c>WorldRendererRotorCullingTests</c> failing about one run in ten and passing
    /// alone, which is what a data race looks like from the outside. This drives the same shape
    /// deliberately: many threads, many distinct names so most calls MISS and write, and the same names
    /// asked repeatedly so the answers can be checked against a single-threaded pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResolveCacheIsSafeToAskFromManyThreads()
    {
        MaterialLibrary library = Write(
            """
            {
              "suffixes": { "_glass": { "roughness": 0.05, "metallic": 0.0 } },
              "textures": { "named01": { "roughness": 0.4, "metallic": 1.0 } }
            }
            """);

        string[] names = [.. Enumerable.Range(0, 400).Select(i => (i % 3) switch
        {
            0 => $"tex{i}_glass",
            1 => "named01",
            _ => $"plain{i}",
        })];

        // What one thread says, which is the answer every thread has to agree with.
        SurfaceMaterial[] expected = [.. names.Select(library.For)];

        SurfaceMaterial[][] answers = new SurfaceMaterial[16][];
        Parallel.For(0, answers.Length, worker =>
        {
            SurfaceMaterial[] mine = new SurfaceMaterial[names.Length];
            for (int pass = 0; pass < 8; pass++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    mine[i] = library.For(names[i]);
                }
            }

            answers[worker] = mine;
        });

        Assert.All(answers, mine => Assert.Equal(expected, mine));
    }
}

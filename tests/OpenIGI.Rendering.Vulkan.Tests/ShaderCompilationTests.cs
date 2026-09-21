using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Deferred;
using OpenIGI.Rendering.Vulkan.RayTracing;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Compiles the shader sources that read the level's shelter map, so a syntax error in them is a
/// failing test rather than a black frame.
/// </summary>
/// <remarks>
/// These strings are assembled at runtime out of several files — the wetness description is shared by
/// the pass that draws the water and the pass that reflects in it, the fog reads the same map from a
/// third, and each splices it into its own uniform layout — so nothing at build time reads them at all.
/// Handing them to <c>glslc</c> is the only way a broken splice is caught before a level is loaded.
/// </remarks>
public sealed class ShaderCompilationTests
{
    [Fact]
    public void TheUnlitFirstPersonMarkerIsNotFogged()
    {
        // Marker 2 is an unlit first-person draw. World (1) and translucent (-1) remain fogged; the
        // marker range below excludes only that rig while the dynamic first-person marker (0) was
        // already excluded.
        Assert.Contains("abs(push.uMarkerExposure.x) > 0.5 && push.uMarkerExposure.x < 1.5", Mesh3DShaderSource.Vertex);
        Assert.Contains("abs(marker) > 0.5 && marker < 1.5", Mesh3DShaderSource.Fragment);
    }

    [RequiresGlslcTheory]
    [InlineData("shading.frag", "frag")]
    [InlineData("shading-rayquery.frag", "frag")]
    [InlineData("reflect.comp", "comp")]

    // Both halves of the reflection pass. The traced variant splices in the surface cache's shared
    // lookup, which is written against the engine's OpenGL-dialect host and reaches this one through a
    // block of defines -- so a name that drifts on either side is a shader that does not compile, and
    // nothing but this would say so before a level is loaded on an adapter that traces.
    [InlineData("reflect-rayquery.comp", "comp")]
    [InlineData("fog.scatter.comp", "comp")]
    [InlineData("fog.integrate.comp", "comp")]

    // The forward mesh pair is the whole of the vanilla picture -- terrain, world, sky and rig all go
    // through it -- and it carries the level-of-detail morph, the retail distance fog and the rig's key
    // light in one push block that is exactly the guaranteed 128 bytes. Nothing at build time reads it
    // either, and a broken splice there is a black frame rather than a compile error.
    [InlineData("mesh3d.vert", "vert")]
    [InlineData("mesh3d.frag", "frag")]

    // The glazing pair, in both variants. It splices the surface cache's shared lookup and the sky-view
    // table's shared mapping into a push block and a uniform block of its own, so it has exactly the
    // failure this whole theory exists for: three separately-authored texts that have to agree about
    // every name, with nothing at build time reading any of them.
    [InlineData("glass.vert", "vert")]
    [InlineData("glass.frag", "frag")]

    // The front-layer half: the same vertex stage and the same sets, writing a colour attachment in a
    // pass that owns its depth rather than an image store from the translucency subpass.
    [InlineData("glasslayer.frag", "frag")]
    [InlineData("glassreflect.comp", "comp")]
    [InlineData("glassreflect-rayquery.comp", "comp")]
    public void TheShadersThatReadTheShelterMapCompile(string name, string stage)
    {
        string compiler = Glslc.Path;

        string source = name switch
        {
            "shading.frag" => DeferredShaderSource.LightingFragment(rayQuery: false),
            "shading-rayquery.frag" => DeferredShaderSource.LightingFragment(rayQuery: true),
            "fog.scatter.comp" => VolumetricFogShaderSource.Scatter,
            "fog.integrate.comp" => VolumetricFogShaderSource.Integrate,
            "mesh3d.vert" => Mesh3DShaderSource.Vertex,
            "mesh3d.frag" => Mesh3DShaderSource.Fragment,
            "glass.vert" => GlassShaderSource.Vertex,
            "glass.frag" => GlassShaderSource.Fragment,
            "glasslayer.frag" => GlassShaderSource.LayerFragment,
            "glassreflect.comp" => GlassReflectionShaderSource.Reflect(rayQuery: false),
            "glassreflect-rayquery.comp" => GlassReflectionShaderSource.Reflect(rayQuery: true),
            "reflect-rayquery.comp" => ReflectionShaderSource.Reflect(rayQuery: true),
            _ => ReflectionShaderSource.Reflect(rayQuery: false),
        };

        string directory = Path.Combine(Path.GetTempPath(), "openigi-shader-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string output = path + ".spv";
        File.WriteAllText(path, source);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(compiler)
            {
                ArgumentList = { $"-fshader-stage={stage}", "--target-env=vulkan1.2", "-o", output, path },
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{name} did not compile:{Environment.NewLine}{errors}");
    }

    /// <summary>The traced reflection splices in the surface cache's lookup, character for character.</summary>
    /// <remarks>
    /// The engine's indirect gather and this pass ask the same question of the same buffers — which card
    /// covers a point, and where on it — and the answer has to be the same hash and the same projection in
    /// both, or the two land on different surfaces. Neither would look broken on its own, which is exactly
    /// why this is a test: the failure mode is a reflection that quietly shows the wrong wall.
    /// </remarks>
    /// <summary>Both traced passes fire in WORLD space and look up cards in the CACHE's space.</summary>
    /// <remarks>
    /// <para>
    /// Two different offsets, and pinned because getting them the wrong way round is invisible to every
    /// signal that normally catches a mistake. It compiles, it runs, it dispatches, the debug view lights
    /// up — and every ray starts kilometres from any geometry, misses, and falls through to the sky. What
    /// you see is a surface reflecting a plausible sky instead of the room, which reads as a tuning
    /// problem rather than as a bug. The glazing shipped that way once.
    /// </para>
    /// <para>
    /// Checked per HOST rather than in the shared snippet, because the snippet reaches both through a
    /// macro: the mistake available now is not writing the wrong expression but defining
    /// <c>kWorldOrigin</c> to the wrong thing, and only the host can get that wrong.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("reflection")]
    [InlineData("glass")]
    public void BothTracedPassesFireInWorldSpaceAndLookUpCardsInTheCacheSpace(string host)
    {
        string source = host == "glass"
            ? GlassReflectionShaderSource.Reflect(rayQuery: true)
            : ReflectionShaderSource.Reflect(rayQuery: true);

        // The ray is fired from the world origin macro, and the cards are looked up from the cache's.
        Assert.Contains("originRelative + kWorldOrigin", source);
        Assert.Contains("hitRelative + p.uCacheOrigin.xyz", source);

        // And that macro must not be defined as the cache's offset, which is the mistake this guards.
        Assert.DoesNotContain("#define kWorldOrigin p.uCacheOrigin", source);
        Assert.Matches(@"#define kWorldOrigin \S", source);
    }

    /// <summary>Both traced passes splice the one copy of the hit resolve, character for character.</summary>
    /// <remarks>
    /// The screen-space reflection pass and the glazing pass ask the same question of the same buffers —
    /// can the camera see what this ray hit, and what colour is it — and they had a copy of the answer
    /// each. Copies of a depth-agreement tolerance, or of the sliver test that lets the weapon appear in a
    /// mirror, do not fail loudly when they drift: a puddle and a window simply start disagreeing about
    /// what is in the room, and neither looks wrong on its own.
    /// </remarks>
    [Fact]
    public void BothTracedPassesSpliceTheOneHitResolve()
    {
        Assert.Contains(TracedResolveShaders.Screen, ReflectionShaderSource.Reflect(rayQuery: true));
        Assert.Contains(TracedResolveShaders.Screen, GlassReflectionShaderSource.Reflect(rayQuery: true));

        Assert.Contains(TracedResolveShaders.World, ReflectionShaderSource.Reflect(rayQuery: true));
        Assert.Contains(TracedResolveShaders.World, GlassReflectionShaderSource.Reflect(rayQuery: true));

        // The ray query belongs to the shared trace alone: a host that grew its own copy back is the
        // regression this whole extraction exists to prevent.
        Assert.DoesNotContain("rayQueryInitializeEXT", ReflectionShaderSource.Reflect(rayQuery: true)
            .Replace(TracedResolveShaders.World, string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("rayQueryInitializeEXT", GlassReflectionShaderSource.Reflect(rayQuery: true)
            .Replace(TracedResolveShaders.World, string.Empty, StringComparison.Ordinal));
    }

    /// <summary>Both reflection variants resolve against the SAME cards, spliced verbatim.</summary>
    /// <remarks>
    /// The untraced one used to carry no lookup at all, on the reasoning that with no structure to trace
    /// there is no hit to resolve. That stopped being true when it gained a march: a hit found by walking
    /// the level's distance field is a hit like any other, and it resolves against exactly the cards a
    /// traced hit does. What must never happen is a second copy of the lookup — a spatial hash and a card
    /// projection that drift apart do not look like a bug, they look like the reflection landing on the
    /// wrong wall.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothReflectionVariantsSpliceTheSharedLookupVerbatim(bool rayQuery)
    {
        string source = ReflectionShaderSource.Reflect(rayQuery);

        Assert.Contains(SurfaceCacheShaders.Lookup, source, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(source, @"bool LookupCardWithin\("));
    }

    /// <summary>The untraced reflection marches the field and fills what the screen could not.</summary>
    /// <remarks>
    /// The way this failed was a silence. Screen space cannot answer a ray that leaves the frame, a ray
    /// that passes behind something nearer, or a ray aimed at what the camera never drew — and on a metal
    /// or a glossy surface there was no fallback at all, so all three came back as nothing and the metal
    /// reflected the void. A reflection that is simply absent looks like a surface that is not very
    /// reflective, which is why it stood for so long.
    /// </remarks>
    [Fact]
    public void TheUntracedReflectionMarchesWhatTheScreenCouldNotAnswer()
    {
        string source = ReflectionShaderSource.Reflect(rayQuery: false);

        Assert.Contains(DistanceFieldShaders.Surface, source, StringComparison.Ordinal);
        Assert.Contains(
            "SurfaceAt(originRelative, normalW, rayW, p.uTrace.y, hitRelative, voxel)",
            source,
            StringComparison.Ordinal);

        // It ADDS to the screen march rather than replacing it, weighted by what the screen settled.
        Assert.Contains(
            "reflection += (scene - replaced) * reflectance * (1.0 - screenWeight);",
            source,
            StringComparison.Ordinal);

        // And with the hit's own accuracy allowed for, as the glazing's march is.
        Assert.Contains("LitAtHitWithin(hitRelative, voxel, radiance)", source, StringComparison.Ordinal);
        Assert.Contains("kCardSkin + (voxel * 2.0)", source, StringComparison.Ordinal);

        // Nothing traced, on a shader compiled for an adapter that cannot.
        Assert.DoesNotContain("rayQueryEXT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("accelerationStructureEXT", source, StringComparison.Ordinal);
    }

    /// <summary>The parameter block sits at the binding after every resource, in both variants.</summary>
    /// <remarks>
    /// <para>
    /// The compute pass places its parameters at the index after the last resource it was given, and the
    /// shader has to write that number down by hand. Getting it wrong does not fail to compile — it
    /// silently overlaps a resource, and the pass reads rubbish for the whole of its parameter block.
    /// </para>
    /// <para>
    /// So this reads the numbers back out of the generated source: every binding from zero up to the
    /// parameter block's own, each declared exactly once, and nothing declared past it. Both variants,
    /// because the traced one adds seven resources and moves the block.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, 15)]
    [InlineData(true, 15)]
    public void TheParameterBlockFollowsEveryResourceBinding(bool rayQuery, int expected)
    {
        string source = ReflectionShaderSource.Reflect(rayQuery);

        List<int> declared = [];
        foreach (Match match in Regex.Matches(source, @"layout\([^)]*binding = (\d+)"))
        {
            declared.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        Assert.Equal(expected, Bindingof(source, "uniform Params"));
        Assert.Equal(Enumerable.Range(0, expected + 1), declared.Order());
        Assert.Equal(expected, ReflectionPass.BindingCount(rayQuery));
    }

    /// <summary>The glazing pass's own binding audit, for the reason the reflection pass has one.</summary>
    /// <remarks>
    /// This pass grew a resource after its parameter block had been given a fixed number, and the block
    /// has to move when it does. Nothing fails loudly when it does not: the albedo array and the block
    /// land on the same binding, the later write wins, and the shader reads a uniform block through a
    /// sampler — which on a good day is a validation message and on a bad one is a pane full of noise.
    /// </remarks>
    [Theory]
    [InlineData(false, 13)]
    [InlineData(true, 13)]
    public void TheGlazingParameterBlockFollowsEveryResourceBinding(bool rayQuery, int expected)
    {
        string source = GlassReflectionShaderSource.Reflect(rayQuery);

        List<int> declared = [];
        foreach (Match match in Regex.Matches(source, @"layout\([^)]*binding = (\d+)"))
        {
            declared.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        Assert.Equal(expected, Bindingof(source, "uniform Params"));
        Assert.Equal(Enumerable.Range(0, expected + 1), declared.Order());

        // And the PASS declares a kind for every one of them. The two lists are hand-written descriptions
        // of one descriptor set and nothing reconciles them: a resource added to the shader alone throws
        // "the pass binds N resources but N+1 were given" at the first dispatch of a level, which is a
        // crash on the player's machine rather than a red test. This shipped once, in exactly that shape.
        Assert.Equal(expected, GlassReflectionPass.BindingCount(rayQuery));
    }

    /// <summary>The untraced glazing MARCHES the field and resolves what it finds against the cards.</summary>
    /// <remarks>
    /// The whole of the software half, asserted as two facts, because the way it failed was a silence.
    /// Its resolve was once a single line returning the sky: a machine with tracing switched off saw
    /// panes that caught the daylight and showed nothing of the level — no wall behind the camera, no
    /// ground, no soldier — and nothing anywhere said so, because a reflection of the sky is a perfectly
    /// plausible thing for a window to hold. If either half of this ever goes missing again it will fail
    /// the same silent way, so it is pinned here rather than left to be noticed by looking.
    /// </remarks>
    [Fact]
    public void TheUntracedGlazingMarchesTheFieldAndResolvesAgainstTheCards()
    {
        string source = GlassReflectionShaderSource.Reflect(rayQuery: false);

        // The march itself, verbatim from the one copy of it that exists -- and the one that finds WHERE
        // a ray stopped, not the shadow test that only says whether it was blocked.
        Assert.Contains(DistanceFieldShaders.Surface, source, StringComparison.Ordinal);
        Assert.Contains("SurfaceAt(relative, normalW, rayW, p.uTrace.y, hit, voxel)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibilityAt(", source, StringComparison.Ordinal);

        // And the resolve, which is the same one the traced half uses: the lit frame where the camera can
        // see the place, the surface cache's cards where it cannot.
        Assert.Contains(SurfaceCacheShaders.Lookup, source, StringComparison.Ordinal);
        Assert.Contains("LitAtHitWithin(hit, voxel, radiance)", source, StringComparison.Ordinal);
        Assert.Contains("hit + p.uCacheOrigin.xyz", source, StringComparison.Ordinal);

        // Nothing traced, on a shader compiled for an adapter that cannot.
        Assert.DoesNotContain("rayQueryEXT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("accelerationStructureEXT", source, StringComparison.Ordinal);
    }

    /// <summary>A marched hit is resolved with its own accuracy allowed for; a traced one is not.</summary>
    /// <remarks>
    /// <para>
    /// The two shared resolves — the lit frame and the surface cache — each ask whether a hit is ON the
    /// surface being described, and both were written for a ray-triangle intersection, which is. A hit
    /// found by marching a distance field is good only to a fraction of the voxel that found it, which on
    /// the outer clipmaps is most of a metre; held to the traced tolerances it reads as "the camera drew
    /// something else here" and "no card contains this", and the software glazing came back as a hangar
    /// full of black holes with the ceiling missing from every pane.
    /// </para>
    /// <para>
    /// So the widening is asserted here, and so is its absence on the traced path: a wider tolerance
    /// there would start letting a floor resolve as the ceiling above it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMarchedResolveWidensItsToleranceByTheVoxelAndTheTracedOneDoesNot()
    {
        string marched = GlassReflectionShaderSource.Reflect(rayQuery: false);
        string traced = GlassReflectionShaderSource.Reflect(rayQuery: true);

        Assert.Contains("LitAtHitWithin(hit, voxel, radiance)", marched, StringComparison.Ordinal);
        Assert.Contains(
            "hit + p.uCacheOrigin.xyz, hitNormal, kCardSkin + (voxel * 2.0), cardRadiance",
            marched,
            StringComparison.Ordinal);

        // The declarations are shared, so what must be absent is the CALL: the traced path resolves
        // through the plain forms, which is the same thing as taking no slack -- the cards at exactly
        // the skin, which is the bias the reference gives a triangle hit.
        Assert.DoesNotContain("LitAtHitWithin(hit, ", traced, StringComparison.Ordinal);
        Assert.DoesNotContain("voxel * 2.0", traced, StringComparison.Ordinal);
        Assert.Contains("LitAtHit(hitRelative, radiance)", traced, StringComparison.Ordinal);
        Assert.Contains(
            "SampleCardsWithin(hitRelative + p.uCacheOrigin.xyz, hitNormal, kCardSkin, cardRadiance)",
            traced,
            StringComparison.Ordinal);
    }

    /// <summary>A hit the march cannot identify is estimated, and WITHOUT the sky's colour.</summary>
    /// <remarks>
    /// <para>
    /// The traced path answers an unresolvable hit with black and is right to: it can shade a hit from its
    /// own material, so one that gets that far is a genuine and rare gap. A march has no triangle and no
    /// material, so the case is ordinary and black there is a hole in the glass with a hard edge round it.
    /// </para>
    /// <para>
    /// What the estimate must not do is borrow the sky's hue. The thing being estimated is a SURFACE and
    /// its colour is exactly what is not known; the sky's is not a neutral guess but a specific wrong one.
    /// By day it passes, and at night the sky is deep blue and nothing else in the level is — measured
    /// against the traced answer in the same hangar, the glazing came back at a mean of (61, 68, 75)
    /// where the trace gives (91, 89, 86), and read as tinted glass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMarchedResolveEstimatesAHitItCannotIdentifyWithoutTheSkysColour()
    {
        string marched = GlassReflectionShaderSource.Reflect(rayQuery: false);

        Assert.Contains(
            "return vec3(dot(FallbackSky(rayW), kLuminance) * kUnknownAlbedo) + kAmbient;",
            marched,
            StringComparison.Ordinal);
        Assert.Contains("const float kUnknownAlbedo", marched, StringComparison.Ordinal);
        Assert.Contains("const vec3 kLuminance", marched, StringComparison.Ordinal);
    }

    /// <summary>A ray that leaves the field under a roof has lost a ceiling, not found the sky.</summary>
    /// <remarks>
    /// <para>
    /// The clipmaps are quarter-metre voxels at best and coarser further out; a ceiling is a thin sheet a
    /// few metres up, so the march goes through it and runs out of field on the far side. Handing that
    /// back as sky paints daylight — or, at night, a deep blue — across the ceiling of every interior in
    /// the game. Measured in a hangar, a third of every pane.
    /// </para>
    /// <para>
    /// Both marched passes have to ask, and both must read the SAME roof raster the shading and the water
    /// read, which is why <see cref="WetnessShaderSource.Shelter"/> is spliced rather than copied.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothMarchedPassesAskTheRoofMapBeforeAnsweringWithTheSky()
    {
        string glazing = GlassReflectionShaderSource.Reflect(rayQuery: false);
        string surfaces = ReflectionShaderSource.Reflect(rayQuery: false);

        foreach (string source in new[] { glazing, surfaces })
        {
            Assert.Contains(WetnessShaderSource.Shelter, source, StringComparison.Ordinal);
        }

        Assert.Contains("bool sheltered = LosesCeiling(relative, rayW);", glazing, StringComparison.Ordinal);
        Assert.Contains("bool lostCeiling = LosesCeiling(relative, rayW);", surfaces, StringComparison.Ordinal);

        // And in both, a marginal hit answers exactly as a miss does. That is what stops the march's
        // incoherent grazing hits -- one ray registers, the next does not, in fine interleaved bands --
        // from being visible at all: only a hit that truly resolves differs from its neighbours.
        Assert.Contains("return sheltered ? Unknown(rayW) : FallbackSky(rayW);", glazing, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(surfaces, @"radiance = lostCeiling"));

        // And the traced halves never ask: they have a structure, so they never lose a ceiling.
        Assert.DoesNotContain(
            "LosesCeiling(", GlassReflectionShaderSource.Reflect(rayQuery: true), StringComparison.Ordinal);
    }

    /// <summary>Splitting the roof map out of the weather left the weather's own text untouched.</summary>
    /// <remarks>
    /// Every pass that had the shelter helpers got them as the tail of one big string. Moving them into a
    /// constant of their own so the glazing can splice them alone must not change a character of what
    /// those passes compile — a drifting copy of this would not look like a bug, it would look like the
    /// water and the glass disagreeing about which floors are indoors.
    /// </remarks>
    [Fact]
    public void TheWeatherStillEndsWithTheRoofMapVerbatim()
    {
        Assert.EndsWith(WetnessShaderSource.Shelter, WetnessShaderSource.Common, StringComparison.Ordinal);
        Assert.Contains("struct ShelterMap", WetnessShaderSource.Shelter, StringComparison.Ordinal);
        Assert.Contains("bool ShelterCovers(", WetnessShaderSource.Shelter, StringComparison.Ordinal);
    }

    /// <summary>The hit shading reads the albedo array in NORMALISED coordinates, through the sampler.</summary>
    /// <remarks>
    /// <para>
    /// It used to <c>texelFetch</c>, which ignores the bound sampler and takes the nearest texel — and the
    /// pass binds a linear one, so the filtering was there and being thrown away. A reflected body at
    /// arm's length in a window is magnified many times over, so each stored texel became a hard block and
    /// the whole reflection read as low-resolution rather than as point-sampled.
    /// </para>
    /// <para>
    /// The test is on the READ, not on a resolution: normalised coordinates are resolution-independent,
    /// which is the second reason to prefer them — the array's size stopped being a constant that C# and
    /// GLSL had to agree about, so there is no longer a number here that can drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHitShadingReadsTheAlbedoArrayThroughItsSampler()
    {
        Assert.Contains("texture(uTracedAlbedo", TracedResolveShaders.Material, StringComparison.Ordinal);
        Assert.DoesNotContain("texelFetch(uTracedAlbedo", TracedResolveShaders.Material, StringComparison.Ordinal);

        // AND THE UV GOES IN WHOLE. Wrapping a tiling world texture with `fract` looks like the obvious
        // way to do it through a sampler that clamps, and it is wrong: `fract` moves the sample but it
        // cannot move the FILTER, so at every repeat the bilinear tap set straddles the jump and clamps
        // to the edge texel instead of reaching round. One texel of a layer stored at this resolution is
        // a wide stripe, so every repeat drew a hard line -- vertical marks up the side of every storage
        // tank and along every façade in the reflections. The array carries a wrapping sampler instead;
        // see VulkanTracedAlbedo.Sampler.
        Assert.Contains(
            "texture(uTracedAlbedo, vec3(uv, float(layer)))",
            TracedResolveShaders.Material,
            StringComparison.Ordinal);
        Assert.DoesNotContain("vec3(fract(uv)", TracedResolveShaders.Material, StringComparison.Ordinal);
    }

    /// <summary>The mask and the albedo read different words of the same record.</summary>
    /// <remarks>
    /// One record serves both the shadow pass's cut-out test and the reflection's hit shading, and the
    /// two reach into it by a hand-written word offset each. Landing on the same word does not fail: a
    /// cut-out surface would test its alpha against a texture layer number, so a fence would be solid or
    /// absent depending on which layer its colour happened to get.
    /// </remarks>
    [Fact]
    public void TheMaskAndTheAlbedoReadDifferentWordsOfTheRecord()
    {
        Assert.Contains("records.v[at + 5u]", RayTracedShadowPass.Source, StringComparison.Ordinal);
        Assert.Contains("records.v[at + 6u]", TracedResolveShaders.Material, StringComparison.Ordinal);
    }

    /// <summary>The glazing pass declares the hit shading before the trace that calls it.</summary>
    /// <remarks>
    /// The trace shades its hit on the way out, because the ray query is local to it — so <c>ShadeHit</c>
    /// has to be a declared function by the time <c>TraceWorld</c> is spliced. GLSL has no forward
    /// declarations for this, so the wrong order is a compile error rather than a silent fault; the test
    /// is here so that the error arrives with its reason attached rather than as a wall of parser output.
    /// </remarks>
    [Fact]
    public void TheGlazingDeclaresTheHitShadingBeforeTheTraceThatCallsIt()
    {
        string source = GlassReflectionShaderSource.Reflect(rayQuery: true);

        int shade = source.IndexOf("bool ShadeHit(", StringComparison.Ordinal);
        int trace = source.IndexOf("bool TraceWorld(", StringComparison.Ordinal);

        Assert.True(shade >= 0, "The glazing pass splices no hit shading.");
        Assert.True(trace >= 0, "The glazing pass splices no world trace.");
        Assert.True(shade < trace, "The hit shading is spliced after the trace that calls it.");
        Assert.Contains("#define kShadeHits", source, StringComparison.Ordinal);
    }

    /// <summary>Reads the binding number off the declaration that contains some text.</summary>
    private static int Bindingof(string source, string declaration)
    {
        int at = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{declaration} was not declared at all.");

        MatchCollection before = Regex.Matches(source[..at], @"layout\([^)]*binding = (\d+)");
        Assert.True(before.Count > 0, $"{declaration} carries no binding.");
        return int.Parse(before[^1].Groups[1].Value, CultureInfo.InvariantCulture);
    }

    internal static string FindGlslc()
    {
        string executable = OperatingSystem.IsWindows() ? "glslc.exe" : "glslc";

        if (Environment.GetEnvironmentVariable("VULKAN_SDK") is { Length: > 0 } sdk)
        {
            string candidate = Path.Combine(sdk, "Bin", executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips when the Vulkan SDK's compiler is not installed.</summary>
public sealed class RequiresGlslcTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresGlslcTheoryAttribute"/> class.</summary>
    public RequiresGlslcTheoryAttribute()
    {
        if (Glslc.Path.Length == 0)
        {
            Skip = "Install the Vulkan SDK, or put glslc on PATH, to compile the shaders in a test.";
        }
    }
}

/// <summary>Where the shader compiler is, looked for once.</summary>
internal static class Glslc
{
    /// <summary>The path to glslc, or empty when it is not installed.</summary>
    public static string Path { get; } = ShaderCompilationTests.FindGlslc();
}

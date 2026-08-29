using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Decides which of a level's textures are lamps, from the shape of the geometry that draws them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the lightmaps alone are not enough.</b> <see cref="EmissiveTextureSurvey"/> asks what the
/// artists BAKED around a texture, which is the strongest single signal there is — a surface painted
/// near-white everywhere it appears is a surface the artists treated as a source. But it is evidence
/// about light and not about objects, and it says yes to things that are lit rather than lighting: a
/// pale sign under a lamp, a vending machine's advert panel, a white door at the end of a sunlit
/// corridor.
/// </para>
/// <para>
/// <b>What this adds is the object.</b> A ceiling lamp is a specific shape in a specific place: small,
/// above head height, flat, facing down, and painted in near-neutral white rather than in a brand's
/// colours. None of those alone means anything — plenty of small flat things hang from ceilings — but
/// together they describe a light fixture and very little else, and they are checked against geometry
/// rather than against a bake, so they are independent evidence. This is the test the reference exporter
/// arrived at over a run of comparisons against the reference, ported here so that the port and the
/// reference agree about what a lamp is.
/// </para>
/// <para>
/// <b>Confirm once, then trust the texture.</b> A lamp texture is used dozens of times in a level and
/// only some of those instances are modelled cleanly enough to pass the shape test — one is a tidy
/// quad, the next is welded into a whole ceiling assembly whose bounds are the room. So a single
/// instance passing CONFIRMS the texture, and every place the level draws that texture is then a lamp.
/// The alternative, testing each instance on its own, lights one tube in a corridor of six.
/// </para>
/// <para>
/// The output is still a proposal. Everything here is inferred from art that was never authored to be
/// read this way, and the fixtures file exists so a wrong answer is corrected by editing a list rather
/// than by editing this class — see <see cref="LightFixtureStore"/>.
/// </para>
/// </remarks>
public static class LampDetector
{
    /// <summary>How far above the level's ground a fixture must sit to be a ceiling lamp, in metres.</summary>
    /// <remarks>
    /// Above head height, so that floor clutter — a pale crate, a white barrel — cannot qualify however
    /// bright it is painted. Measured from the level's own ground rather than from zero, because this
    /// game's world sits tens of kilometres from the origin.
    /// </remarks>
    public const double MinimumHeight = 1.8;

    /// <summary>The widest a lamp fixture is, in metres.</summary>
    /// <remarks>
    /// A fixture, not a ceiling. Three metres takes the longest fluorescent batten in the game and stops
    /// well short of the ceiling panels a lamp is mounted on, which are the thing most likely to share
    /// its texture and its height.
    /// </remarks>
    public const double MaximumWidth = 3.0;

    /// <summary>The thickest a lamp fixture is, in metres.</summary>
    public const double MaximumThickness = 1.0;

    /// <summary>How bright a lamp's own diffuse texture reads, 0 to 1.</summary>
    /// <remarks>
    /// The DIFFUSE, not the lightmap: a lamp is painted pale because it is a lamp, and this is the one
    /// piece of evidence here that does not come from the bake at all. Low, because a tube's texture
    /// page carries its dark housing as well as its bright glass.
    /// </remarks>
    public const float MinimumBrightness = 0.40f;

    /// <summary>How colourless a lamp's own diffuse texture reads, 0 (grey) to 1 (saturated).</summary>
    /// <remarks>
    /// This is the term that separates a lamp from an advert. A fluorescent fitting is white, grey and
    /// steel; a soda machine's lit panel and a warning sign are bright in the bake for the same reason a
    /// lamp is — the artist painted them so — but they are RED, and no light fixture in this game is.
    /// </remarks>
    public const float MaximumSaturation = 0.35f;

    /// <summary>The survey score below which a texture is not considered at all.</summary>
    /// <remarks>
    /// Deliberately lower than the score the fixtures were once proposed at. The shape test below is the
    /// strict half of the pair now, so the lightmap half can afford to be generous: its job is to bring
    /// candidates forward, and a real fixture lost here is invisible, whereas a wrong one is a glow in
    /// the wrong place that a reviewer can see and delete.
    /// </remarks>
    public const float MinimumScore = 0.25f;

    /// <summary>The survey score at and above which an emitter glows at full strength.</summary>
    public const float FullScore = 0.75f;

    /// <summary>How brightly a shape-confirmed lamp emits, as a multiple of its albedo.</summary>
    /// <remarks>
    /// Six, the figure the reference comparison settled on for a white tube: bright enough that the fixture
    /// reads as the source in the frame and the bloom finds it, and short of the point where its texels
    /// clip to a flat white block and it stops looking like a lamp at all.
    /// </remarks>
    public const float LampStrength = 6f;

    /// <summary>The weakest a merely-bright emitter glows, as a multiple of its albedo.</summary>
    public const float MinimumStrength = 1f;

    private const string LogCategory = "Emissive";

    /// <summary>What the detector concluded about one level.</summary>
    /// <param name="Strengths">
    /// Every texture judged to emit, and how strongly, as a multiple of its albedo. Handed to the
    /// material library so the geometry itself glows, and to the fixture extractor so that the places it
    /// is drawn become lights.
    /// </param>
    /// <param name="Confirmed">
    /// The subset of <paramref name="Strengths"/> confirmed by the shape test rather than by the bake
    /// alone — the ones that are lamps rather than merely bright. Reported for review.
    /// </param>
    public readonly record struct Result(
        IReadOnlyDictionary<string, float> Strengths,
        IReadOnlyCollection<string> Confirmed);

    /// <summary>Works out which of a level's textures emit, and how strongly.</summary>
    /// <param name="scene">The built world, walked for the shape of everything it draws.</param>
    /// <param name="resources">Where the diffuse textures are decoded from.</param>
    /// <param name="findings">The lightmap survey's ranking, strongest first.</param>
    /// <param name="logger">Where the conclusion is reported, named rather than counted.</param>
    /// <returns>The emitters and their strengths; empty when the level has none.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Result Detect(
        WorldScene scene,
        ResourceRegistry resources,
        IReadOnlyList<EmissiveTextureSurvey.Finding> findings,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(findings);

        IIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<string, TextureLook> looks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> strengths = new(StringComparer.OrdinalIgnoreCase);

        // The bake's half of the answer: everything the artists lit like a source, at a strength that
        // falls away with how sure the survey is and with how far the texture's own colour is from white.
        foreach (EmissiveTextureSurvey.Finding finding in findings)
        {
            if (finding.Score >= MinimumScore)
            {
                strengths[finding.Texture] =
                    Strength(finding.Score, Look(finding.Texture, resources, looks).Saturation);
            }
        }

        // The geometry's half: which of those, or of anything else the level draws, is SHAPED like a
        // lamp. One instance is enough, for the reason the type's remarks give.
        HashSet<string> confirmed = Confirm(scene, resources, looks, log);

        foreach (string texture in confirmed)
        {
            // A confirmed lamp emits at the lamp strength however the bake read it: the shape test is the
            // stronger evidence of the two, and a tube whose bake averaged low is still a tube.
            strengths[texture] = LampStrength;
        }

        if (strengths.Count > 0)
        {
            // Named rather than counted. This is inferred from art that was never meant to be read this
            // way, so the list is the first thing to check when a level lights wrongly.
            log.Information(
                LogCategory,
                "Emitters: " + string.Join(
                    ", ",
                    strengths.OrderByDescending(e => e.Value).Select(
                        e => $"{e.Key} x{e.Value:0.#}{(confirmed.Contains(e.Key) ? " (lamp)" : string.Empty)}")));
        }

        return new Result(strengths, confirmed);
    }

    /// <summary>How brightly a texture the bake called a source should glow.</summary>
    /// <remarks>
    /// Two terms. The survey score ramps the strength from the floor to full over the band between the
    /// gate and <see cref="FullScore"/>, so a marginal emitter glows faintly rather than arriving at the
    /// same brightness as a certain one. Whiteness then scales the whole thing down: a saturated texture
    /// that reads bright in the bake is an advert or a sign lit BY something, and a fraction of a lamp's
    /// strength is what makes it read as a lit panel instead of as a light.
    /// </remarks>
    private static float Strength(float score, float saturation)
    {
        float whiteness = Math.Clamp(1f - (2f * saturation), 0f, 1f);
        float weight = 0.12f + (0.88f * whiteness);
        float t = Math.Clamp((score - MinimumScore) / (FullScore - MinimumScore), 0f, 1f);

        return (MinimumStrength + (t * (LampStrength - MinimumStrength))) * weight;
    }

    /// <summary>Walks the level for sub-meshes shaped like a ceiling lamp, and names their textures.</summary>
    private static HashSet<string> Confirm(
        WorldScene scene,
        ResourceRegistry resources,
        Dictionary<string, TextureLook> looks,
        IIgiLogger log)
    {
        HashSet<string> confirmed = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<MefModel, MeshGeometry?> built = [];
        double ground = scene.GroundHeight;

        foreach (WorldPlacement placement in scene.Placements)
        {
            // A spline segment carries its own bent mesh, unique to the placement; everything else is the
            // model's own geometry, built once however many times the level draws it.
            MeshGeometry? geometry = placement.BentGeometry;

            if (geometry is null && !built.TryGetValue(placement.Model, out geometry))
            {
                geometry = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
                built[placement.Model] = geometry;
            }

            if (geometry is null)
            {
                continue;
            }

            foreach (MeshSubset subset in geometry.Subsets)
            {
                if (subset.IndexCount < 3
                    || !subset.HasMaterial
                    || !placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture)
                    || texture.Length == 0
                    || confirmed.Contains(texture))
                {
                    continue;
                }

                // The cheap half of the test first: a texture that is dark or coloured is not a lamp
                // whatever shape it is in, and deciding that costs one cached texture decode rather than a
                // walk over every triangle of every instance in the level.
                TextureLook look = Look(texture, resources, looks);

                if (look.Brightness <= MinimumBrightness || look.Saturation >= MaximumSaturation)
                {
                    continue;
                }

                if (IsFixtureShaped(geometry, subset, placement, ground))
                {
                    confirmed.Add(texture);
                }
            }
        }

        if (confirmed.Count > 0)
        {
            log.Information(LogCategory, $"Lamp fixtures confirmed by shape: {string.Join(", ", confirmed)}.");
        }

        return confirmed;
    }

    /// <summary>Whether one sub-mesh, where it stands, is shaped and placed like a ceiling lamp.</summary>
    /// <remarks>
    /// Small, above head height, flat, and facing along the vertical. The normal comes from the winding
    /// rather than from the authored vertex normals, which face inward on this game's interior shells;
    /// summing the raw cross products weights each face by its own area, and a fixture whose faces cancel
    /// — a closed box — fails the vertical test rather than passing it by accident.
    /// </remarks>
    private static bool IsFixtureShaped(
        MeshGeometry geometry, in MeshSubset subset, in WorldPlacement placement, double ground)
    {
        Vertex3D[] vertices = geometry.Vertices;
        ushort[] indices = geometry.Indices;
        Matrix3f drawn = placement.Orientation.Transposed();
        Vector3d origin = placement.Position;

        int start = Math.Max(subset.IndexOffset, 0);
        int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

        Vector3d low = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d high = new(double.MinValue, double.MinValue, double.MinValue);
        Vector3d facing = default;
        double height = 0.0;
        int count = 0;

        for (int at = start; at + 2 < end; at += 3)
        {
            int a = indices[at];
            int b = indices[at + 1];
            int c = indices[at + 2];

            if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
            {
                continue;
            }

            Vector3d va = ToWorld(vertices[a].Position, drawn, origin);
            Vector3d vb = ToWorld(vertices[b].Position, drawn, origin);
            Vector3d vc = ToWorld(vertices[c].Position, drawn, origin);

            facing += Vector3d.Cross(vb - va, vc - va);
            height += va.Z + vb.Z + vc.Z;
            count += 3;

            low = Lower(Lower(Lower(low, va), vb), vc);
            high = Upper(Upper(Upper(high, va), vb), vc);
        }

        if (count == 0)
        {
            return false;
        }

        // Above head height, so floor clutter cannot qualify. The centroid rather than the lowest point:
        // a tube hanging on a long stem is a lamp, and its stem reaches down towards the floor.
        if (((height / count) - ground) <= MinimumHeight)
        {
            return false;
        }

        double width = Math.Max(high.X - low.X, high.Y - low.Y);
        double thickness = high.Z - low.Z;

        if (width <= 0.05 || width >= MaximumWidth || thickness >= MaximumThickness)
        {
            return false;
        }

        // Facing along the vertical: a fixture presents its lit face to the room below, and this is what
        // rejects the wall panels and the ceiling slabs that pass every test above.
        double length = facing.Length;

        return length > 0.0 && Math.Abs(facing.Z / length) > 0.5;
    }

    /// <summary>Takes a model-space position into world space, as the renderer draws it.</summary>
    private static Vector3d ToWorld(Vector3f local, in Matrix3f drawn, in Vector3d origin)
    {
        Vector3d rotated = drawn.Transform(new Vector3d(local.X, local.Y, local.Z));

        return new Vector3d(origin.X + rotated.X, origin.Y + rotated.Y, origin.Z + rotated.Z);
    }

    private static Vector3d Lower(in Vector3d a, in Vector3d b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vector3d Upper(in Vector3d a, in Vector3d b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    /// <summary>A texture's mean brightness and mean saturation, cached per name.</summary>
    /// <remarks>
    /// <para>
    /// Brightness is the mean of each texel's largest channel, and saturation the mean of
    /// <c>(max - min) / max</c> over the texels that are not nearly black — black texels have no hue to
    /// average, and counting them would call every dark texture grey.
    /// </para>
    /// <para>
    /// Resolved through the pack's own naming. A bare texture name is not a path this game's resource
    /// layer will accept — it throws rather than returning false — which is why an earlier attempt at
    /// reading these came back white for every texture in the level.
    /// </para>
    /// </remarks>
    private static TextureLook Look(
        string texture, ResourceRegistry resources, Dictionary<string, TextureLook> cache)
    {
        if (cache.TryGetValue(texture, out TextureLook cached))
        {
            return cached;
        }

        TextureLook look = TextureLook.Unknown;

        try
        {
            if (resources.TryLoad(LoadedPack.TextureResourceName(texture), out ReadOnlyMemory<byte> data)
                && data.Length >= 4
                && TexFile.IsTexture(data.Span))
            {
                look = Measure(TexFile.Load(data.Span));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            // A texture that will not decode is one the detector has no opinion about, which the zeroes
            // above already say. Never worth taking a level load down for.
        }

        cache[texture] = look;
        return look;
    }

    private static TextureLook Measure(TextureImage image)
    {
        ReadOnlySpan<Bgra32> pixels = image.GetLevel(0);

        if (pixels.Length == 0)
        {
            return TextureLook.Unknown;
        }

        double brightness = 0.0;
        double saturation = 0.0;
        double red = 0.0;
        double green = 0.0;
        double blue = 0.0;
        int coloured = 0;

        foreach (Bgra32 pixel in pixels)
        {
            int high = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            int low = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));

            brightness += high / 255.0;
            red += pixel.R;
            green += pixel.G;
            blue += pixel.B;

            if (high > 8)
            {
                saturation += (high - low) / (double)high;
                coloured++;
            }
        }

        double colour = Math.Max(red, Math.Max(green, blue));

        // The tint, normalised on the brightest channel: a fixture's colour is a HUE, and how much light
        // it puts out is the emitting area's business rather than the texture's. A page that averaged to
        // black stays white rather than becoming a division by nothing.
        Vector3f tint = colour > 1e-3
            ? new Vector3f((float)(red / colour), (float)(green / colour), (float)(blue / colour))
            : new Vector3f(1f, 1f, 1f);

        return new TextureLook(
            (float)(brightness / pixels.Length),
            coloured > 0 ? (float)(saturation / coloured) : 0f,
            tint);
    }

    /// <summary>Gets the colour a fixture drawing the given texture should light with.</summary>
    /// <param name="resources">Where the diffuse textures are decoded from.</param>
    /// <returns>
    /// A lookup from texture name to a linear tint, normalised so its brightest channel is about one.
    /// White for anything that will not decode, which costs the tint and never the placement.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    public static Func<string, Vector3f> Tint(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        Dictionary<string, TextureLook> cache = new(StringComparer.OrdinalIgnoreCase);

        return texture => Look(texture, resources, cache).Tint;
    }

    /// <summary>What a texture looks like, averaged over its own texels.</summary>
    /// <remarks>
    /// <see cref="Unknown"/> rather than <see langword="default"/> is what an undecodable texture gets.
    /// The zero instance of a struct runs no initialiser, so a "white unless measured" rule written as a
    /// property initialiser would be skipped by exactly the case it exists for, and every fixture whose
    /// texture would not decode would come out BLACK — with a luminance of nought, and therefore with the
    /// minimum reach the radius formula can produce rather than the reach its size deserves.
    /// </remarks>
    private readonly record struct TextureLook(float Brightness, float Saturation, Vector3f Tint)
    {
        /// <summary>Gets what a texture nothing could be measured from looks like: dark, grey and white.</summary>
        public static TextureLook Unknown { get; } = new(0f, 0f, new Vector3f(1f, 1f, 1f));
    }
}

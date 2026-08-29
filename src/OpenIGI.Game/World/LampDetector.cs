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
/// <b>What this asks about instead is the object.</b> A ceiling lamp is a specific shape in a specific
/// place: small, above head height, flat, facing down, and painted in near-neutral white rather than in
/// a brand's colours. None of those alone means anything — plenty of small flat things hang from
/// ceilings — but together they describe a light fixture and very little else. This is the test the
/// reference exporter arrived at over a run of comparisons against the reference, ported here so that the
/// port and the reference agree about what a lamp is.
/// </para>
/// <para>
/// <b>The bake is not used as a second source, and that was measured rather than assumed.</b> Letting a
/// high survey score make a texture emissive on its own is what the exporter does, and here it is
/// wrong twice over. It says yes to surfaces that are bright because they are LIT — level 1's
/// highest-scoring texture of all, baked pure white across thirty uses, is a panel that skins office
/// cabinets, and it glowed. And emission means something stronger in this renderer than in the reference:
/// an emissive surface is UNLIT, it stops receiving light entirely, so a half-confident emitter does
/// not glow faintly, it turns into a flat shadeless slab.
/// </para>
/// <para>
/// Using the bake the other way round — as a veto over the shape test — fails on the same level for
/// the opposite reason: four of the fixtures this test confirms, <c>373_01_1</c> among them, score
/// exactly nought in the survey, because the artists lit those rooms without painting the fittings
/// bright. The two signals genuinely disagree about real lamps, and the geometric one is the one that
/// is right, so it decides alone.
/// </para>
/// <para>
/// <b>Confirm once, then trust the SUB-MESH — not the texture.</b> A texture is shared: the page that
/// skins a ceiling fitting also skins the panel it is bolted to and, in level 1, the side of an office
/// cabinet. Confirming the texture and glowing it everywhere is what put a blown-out wedge across a
/// garage soffit and a flat white slab down that cabinet, and a size cap on the drawing geometry does
/// not save it — the cabinet's panel is smaller than a fluorescent batten. So what is remembered is
/// which MODEL AND MATERIAL SLOT passed, and only those draw as lamps.
/// </para>
/// <para>
/// Model and slot rather than the individual placement, because that is the level the answer lives at:
/// a model is a lamp model wherever the level puts it, and a corridor of six identical tubes is six
/// placements of one model. Testing each PLACEMENT would light one tube in six, since only the tidily
/// modelled instances pass the shape test; testing each texture lights the furniture.
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

    /// <summary>The largest a fixture may be in ANY direction to count as a compact fitting, in metres.</summary>
    /// <remarks>
    /// The size that separates a wall lamp from a wall PANEL. A bracket fitting is a little box; the
    /// things a bare size test would otherwise admit are flat sheets — a cabinet's side, a door, a sign
    /// — which are small in two directions and not in the third. Six tenths of a metre is comfortably
    /// larger than any fitting in the game and smaller than any panel that was mistaken for one.
    /// </remarks>
    public const double MaximumCompactSize = 0.6;

    /// <summary>The smallest a compact fitting may be in its THINNEST direction, in metres.</summary>
    /// <remarks>
    /// A fitting is a solid little object. This is what stops a decal, a sticker or a paper-thin quad
    /// from qualifying as one by being small in all three directions at once.
    /// </remarks>
    public const double MinimumCompactSize = 0.03;

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

    /// <summary>How much light a lamp should appear to give off, whatever colour it is painted.</summary>
    /// <remarks>
    /// <para>
    /// <b>A radiance to reach, not a multiple to apply.</b> Emission is albedo times a strength, so one
    /// strength for every fixture makes a lamp as bright as its own texture happens to be: a
    /// near-white tube came out at six and clipped to a flat white block, while the grey fitting in the
    /// next room came out at two and read as a pale panel. They are the same kind of light, and how
    /// dark the artist drew the housing is not a fact about how brightly it burns.
    /// </para>
    /// <para>
    /// So each texture's strength is this divided by how bright the texture is, and every fixture in the
    /// level arrives at about the same brightness. Three, which is what a typical fitting reached under
    /// the old flat six — so the ones that already looked right are unchanged, and it is the outliers at
    /// either end that move.
    /// </para>
    /// <para>
    /// Uniform in the MEAN only. Within one fixture the bright glass still outshines its dark housing,
    /// because this scales the texture rather than replacing it.
    /// </para>
    /// </remarks>
    public const float LampRadiance = 3f;

    /// <summary>The most a lamp's own texture may be multiplied by.</summary>
    /// <remarks>
    /// A texture only just past <see cref="MinimumBrightness"/> would otherwise ask for a strength high
    /// enough to turn every speck of noise in its dark half into a light. This is the point past which a
    /// fixture is simply drawn too dark to be normalised into a lamp.
    /// </remarks>
    public const float MaximumStrength = 8f;

    private const string LogCategory = "Emissive";

    /// <summary>Which sub-meshes of a level are light fixtures.</summary>
    /// <param name="Textures">
    /// The textures those fixtures draw with, and how brightly each glows as a multiple of its albedo.
    /// The set the light extractor gathers triangles for.
    /// </param>
    /// <param name="SubMeshes">
    /// The model and material slot of every sub-mesh that passed the shape test. A draw is a lamp only
    /// if it is in here — see the type's remarks for why the texture alone is not enough.
    /// </param>
    public readonly record struct Lamps(
        IReadOnlyDictionary<string, float> Textures,
        IReadOnlySet<string> SubMeshes)
    {
        /// <summary>Gets the answer for a level that has no lamps.</summary>
        public static Lamps None { get; } = new(
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>The key a model's material slot is remembered by.</summary>
        /// <param name="instance">The drawing instance's name, as the pack records it.</param>
        /// <param name="slot">The sub-mesh's material slot.</param>
        /// <returns>The key, matching what <see cref="SubMeshes"/> holds.</returns>
        public static string Key(string instance, int slot) => instance + "#" + slot;

        /// <summary>Whether one sub-mesh of one model is a light fixture.</summary>
        /// <param name="instance">The drawing instance's name.</param>
        /// <param name="slot">The sub-mesh's material slot.</param>
        /// <returns><see langword="true"/> when this sub-mesh should draw as a lamp.</returns>
        public bool Contains(string? instance, int slot) =>
            !string.IsNullOrEmpty(instance) && SubMeshes.Contains(Key(instance, slot));
    }

    /// <summary>Works out which of a level's sub-meshes are lamps.</summary>
    /// <param name="scene">The built world, walked for the shape of everything it draws.</param>
    /// <param name="resources">Where the diffuse textures are decoded from.</param>
    /// <param name="logger">Where the conclusion is reported, named rather than counted.</param>
    /// <returns>The lamps; <see cref="Lamps.None"/> when the level has none.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Lamps Detect(WorldScene scene, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<string, TextureLook> looks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> textures = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> subMeshes = new(StringComparer.OrdinalIgnoreCase);

        Confirm(scene, resources, looks, textures, subMeshes, log);

        return new Lamps(textures, subMeshes);
    }

    /// <summary>The multiple of its own albedo that brings a texture to <see cref="LampRadiance"/>.</summary>
    private static float Strength(float brightness) =>
        Math.Clamp(LampRadiance / MathF.Max(brightness, 0.01f), 1f, MaximumStrength);

    /// <summary>Walks the level for sub-meshes shaped like a ceiling lamp, and records them.</summary>
    private static void Confirm(
        WorldScene scene,
        ResourceRegistry resources,
        Dictionary<string, TextureLook> looks,
        Dictionary<string, float> textures,
        HashSet<string> subMeshes,
        IIgiLogger log)
    {
        Dictionary<MefModel, MeshGeometry?> built = [];
        HashSet<string> tested = new(StringComparer.OrdinalIgnoreCase);
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
                    || texture.Length == 0)
                {
                    continue;
                }

                // Once per model and slot, however many times the level places it: the shape being asked
                // about is the model's, and every placement of a lamp model draws the same lamp.
                string key = Lamps.Key(placement.Instance.Name, subset.MaterialSlot);

                if (!tested.Add(key))
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
                    subMeshes.Add(key);
                    textures[texture] = Strength(look.Brightness);
                }
            }
        }

        // Named rather than counted. This is inferred from art that was never meant to be read this
        // way, so the list is the first thing to check when a level lights wrongly.
        log.Information(
            LogCategory,
            textures.Count > 0
                ? $"Lamps: {subMeshes.Count} sub-mesh(es) drawing "
                    + string.Join(", ", textures.Select(t => $"{t.Key} x{t.Value:0.#}"))
                : "Nothing in this level is shaped like a light fixture.");
    }

    /// <summary>Whether one sub-mesh, where it stands, is shaped and placed like a ceiling lamp.</summary>
    /// <remarks>
    /// Small and above head height, and then EITHER facing along the vertical — a ceiling fitting
    /// presenting its lit face to the room below — or compact in every direction, which is what a wall
    /// bracket looks like when its faces sum sideways and the vertical test cannot see it.
    /// <para>
    /// The normal comes from the winding rather than from the authored vertex normals, which face inward
    /// on this game's interior shells; summing the raw cross products weights each face by its own area,
    /// and a fixture whose faces cancel — a closed box — falls through to the compact test rather than
    /// passing the vertical one by accident.
    /// </para>
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

        // Two ways to be a fixture, because there are two kinds of them and one test cannot see both.
        //
        // A CEILING fixture presents its lit face to the room below, so its faces sum along the
        // vertical. That is the test that rejects the wall panels and ceiling slabs which pass
        // everything above, and it is the one that finds the battens and the recessed panels.
        double length = facing.Length;

        if (length > 0.0 && Math.Abs(facing.Z / length) > 0.5)
        {
            return true;
        }

        // A WALL fixture does not: a bracket lamp faces sideways into the room, so its faces sum
        // horizontally and the vertical test throws it away. Requiring the vertical alone silently lost
        // every wall-mounted fitting in the game, which is most of what lights a corridor -- one lamp
        // glowing and the identical one beside it dark.
        //
        // What stands in for the facing there is being COMPACT: small in every direction at once, and
        // not merely thin. A light fitting is a little box on a wall; the things that would otherwise
        // sneak past a bare size test are flat SHEETS -- a cabinet's side, a door panel, a sign -- which
        // are small in two dimensions and not in the third. Measuring the largest side rather than the
        // footprint is what separates them, and the smallest-side floor keeps a decal or a paper-thin
        // quad from qualifying as a solid little object.
        double smallest = Math.Min(high.X - low.X, Math.Min(high.Y - low.Y, high.Z - low.Z));
        double largest = Math.Max(high.X - low.X, Math.Max(high.Y - low.Y, high.Z - low.Z));

        return largest < MaximumCompactSize && smallest > MinimumCompactSize;
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

    /// <summary>How bright a texture's own diffuse reads, 0 to 1.</summary>
    /// <param name="texture">The texture's name, as the pack records it.</param>
    /// <param name="resources">Where the diffuse textures are decoded from.</param>
    /// <returns>The mean of each texel's largest channel; nought for anything that will not decode.</returns>
    /// <remarks>
    /// Exposed because it is the divisor behind every lamp's strength — the thing that makes a dark
    /// fitting and a white one arrive at the same <see cref="LampRadiance"/> — so a test can check the
    /// product rather than having to know the multiplier.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static float Brightness(string texture, ResourceRegistry resources)
    {
        ArgumentException.ThrowIfNullOrEmpty(texture);
        ArgumentNullException.ThrowIfNull(resources);

        return Look(texture, resources, new Dictionary<string, TextureLook>(StringComparer.OrdinalIgnoreCase))
            .Brightness;
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

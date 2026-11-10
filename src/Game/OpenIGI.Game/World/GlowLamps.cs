using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Reads a level's lamps from the glows its models carry, where the game declares them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one place a game says where its lamps are.</b> Project I.G.I. declares no lights, which is
/// why <see cref="LampDetector"/> infers them from shape and skin. The sequel's models carry a
/// <c>GLOW</c> chunk (<see cref="MefGlow"/>): a point, a colour and a size for every lamp the artists put
/// on the model, which the original draws as an additive sprite. 316 of its models have one and none of
/// the first game's do, so nothing here asks which game is running — a level whose models carry glows
/// has said where its lamps are, and a level whose models carry none is left to the detector. The
/// detector still runs beside this: what it finds from shape keeps its measured face and facing, and a
/// glow that turns out to be the same lamp gives way to it (see <c>EmissiveLightProposer</c>).
/// </para>
/// <para>
/// <b>The light comes from the glow, not from geometry.</b> Measured over every glow model in the
/// sequel: half the glows float clear of the mesh (a bulb's bloom hangs in front of its fitting, and a
/// building's lamps are often not modelled at all), and a model is usually skinned from one page, so
/// "the sub-mesh nearest the glow" is as often the whole lamp post as it is the glass. So each glow
/// becomes a <see cref="LightFixture"/> where it stands, in its own colour, and the geometry is a
/// separate and much more cautious question.
/// </para>
/// <para>
/// <b>Which geometry glows.</b> Emission is strong in this renderer — an emissive surface is unlit — so
/// a sub-mesh is only lit up when it is plainly the lamp's own glass: every vertex of it within arm's
/// reach of a glow, and drawn with a bright texture. A sub-mesh that is larger than that is a housing,
/// a post or a wall, and stays as it is; the light still falls from the glow beside it.
/// </para>
/// <para>
/// Everything but the position and the colour is soft, as it is for a fixture read from geometry: the
/// original records where a glow is and what colour, never how far it lights.
/// </para>
/// </remarks>
public static class GlowLamps
{
    /// <summary>The smallest glow that is given a light, by its sprite's radius in metres.</summary>
    /// <remarks>
    /// Below this a glow is an indicator — a camera's LED, a switch's pilot lamp, a console's row of
    /// status lights — which a level can carry hundreds of and which lights nothing. Its glass may still
    /// glow; it is not handed a light of its own.
    /// </remarks>
    public const float MinimumLightRadius = 0.1f;

    /// <summary>The largest size a glow's source is taken to be, in metres.</summary>
    /// <remarks>
    /// A glow's radius is the size of its bloom, not of its bulb: a floodlight's sprite is drawn a metre
    /// and a half across. Read as the size of the emitter that would make a searchlight of every porch
    /// lamp, so the source stops at the size of a large fitting and the bloom beyond it is taken for bloom.
    /// </remarks>
    public const float MaximumSourceRadius = 0.3f;

    /// <summary>The largest bloom a light's reach is scaled from, in metres.</summary>
    /// <remarks>Two metres is the largest bloom any retail lamp carries; past it a radius is a mistake, not a searchlight.</remarks>
    public const float MaximumReachRadius = 2f;

    /// <summary>The widest a separate piece of geometry can be and still be one light fitting, in metres.</summary>
    /// <remarks>A pendant's shade is under a metre and a ceiling batten two; a wall panel that borrows a lamp's page is several.</remarks>
    public const float MaximumFittingSpan = BakedLamps.MaximumSpan;

    /// <summary>How far under its bulb a hung lamp's light is put, beyond the bulb's own size, in metres.</summary>
    public const float HangClearance = 0.05f;

    /// <summary>The nearest a glow's reach comes to its own point when asking which glass is the lamp's, in metres.</summary>
    public const float MinimumGlassReach = 0.15f;

    /// <summary>The furthest from a glow its own glass is looked for, in metres.</summary>
    /// <remarks>
    /// Wider than the source is taken to be: a wall lamp's lens is a third of a metre across and its glow
    /// sits a hand in front of the middle of it, so the far corners are further off than the bulb is wide.
    /// </remarks>
    public const float MaximumGlassReach = 0.5f;

    /// <summary>How one-sided the geometry round a glow must be for the light to be aimed away from it.</summary>
    /// <remarks>
    /// The length of the mean of the unit vectors from the nearby vertices to the glow: one when the
    /// fitting is all on one side — a ceiling panel above its glow, a wall behind a bracket — and near
    /// nought when the glow sits inside its own shade. Half is where the two part clearly; below it the
    /// fixture faces nowhere and is lit as a point.
    /// </remarks>
    public const float MinimumSidedness = 0.5f;

    /// <summary>How far round a glow its fitting is looked for when asking which way the light faces, in metres.</summary>
    public const float FacingReach = 1f;

    private const string LogCategory = "Emissive";

    /// <summary>What a level's glows amount to.</summary>
    /// <param name="Fixtures">One light for every glow large enough to be a lamp, strongest first.</param>
    /// <param name="SubMeshes">The sub-meshes that are a glow's own glass, and the strength each emits at.</param>
    /// <param name="Textures">The textures those sub-meshes draw with.</param>
    /// <param name="Skinned">
    /// The sub-meshes of models that carry NO glow but are skinned and sized like the lamps that do, and
    /// the strength each emits at. Unlike <paramref name="SubMeshes"/> these have no glow to take a light
    /// from, so their lights are read from their geometry, as an inferred fitting's are.
    /// </param>
    /// <param name="SkinnedTextures">The textures <paramref name="Skinned"/> draw with.</param>
    /// <param name="Blooms">
    /// For each page confirmed as a glowing lamp's glass, the radius in metres of the largest bloom on a
    /// lamp that wears it — how strong the artists made that lamp, for the copies of it that carry no glow.
    /// </param>
    public readonly record struct Result(
        IReadOnlyList<LightFixture> Fixtures,
        IReadOnlyDictionary<string, float> SubMeshes,
        IReadOnlySet<string> Textures,
        IReadOnlyDictionary<string, float> Skinned,
        IReadOnlySet<string> SkinnedTextures,
        IReadOnlyDictionary<string, float> Blooms)
    {
        /// <summary>Gets the answer for a level whose models carry no glows.</summary>
        public static Result None { get; } = new(
            [],
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase));

        /// <summary>Gives a light read from a glowless lamp's geometry what its glowing twin says about it.</summary>
        /// <param name="fixture">A fixture read from geometry.</param>
        /// <returns>The fixture, hung and strengthened when it is one of <see cref="Skinned"/>'s; otherwise as it was.</returns>
        /// <remarks>
        /// <para>
        /// <b>Reach and strength from the twin.</b> A pendant's bulb is a hand across, and a reach scaled
        /// from that stops short of the floor of the hall it hangs in. The same lamp with a glow says how
        /// strong it is by the size of its bloom, so the copy is given the reach and the source a glow of
        /// that size would have.
        /// </para>
        /// <para>
        /// <b>Hung, when it faces nowhere.</b> A bulb modelled as a closed solid has faces that cancel, and
        /// its centre is inside it: lit from there it is shadowed by its own glass and the hall stays
        /// black. Nothing mounts it either, because it is part of the room it lights. A lamp that is
        /// neither on a wall nor on a stand hangs, so the light is moved to just under the bulb and aimed
        /// down.
        /// </para>
        /// </remarks>
        public LightFixture Twin(in LightFixture fixture)
        {
            if (Blooms is null
                || SkinnedTextures is null
                || !SkinnedTextures.Contains(fixture.Texture)
                || !Blooms.TryGetValue(fixture.Texture, out float bloom))
            {
                return fixture;
            }

            float source = MathF.Min(bloom, MaximumSourceRadius);
            LightFixture twin = fixture with { Area = MathF.Max(fixture.Area, MathF.PI * source * source) };

            float reach = MathF.Min(bloom, MaximumReachRadius)
                * LightFixtureExtractor.DefaultRadiusScale
                * MathF.Sqrt(MathF.Max(twin.Luminance, 0f));

            twin = twin with { Radius = MathF.Max(twin.Radius, reach) };

            if (twin.IsDirectional)
            {
                return twin;
            }

            return twin with
            {
                Position = new Vector3d(
                    twin.Position.X, twin.Position.Y, twin.Position.Z - twin.Extent - HangClearance),
                Direction = new Vector3f(0f, 0f, -1f),
            };
        }
    }

    /// <summary>Reads the lamps a level's glows declare.</summary>
    /// <param name="scene">The built world.</param>
    /// <param name="resources">Where the diffuse textures are decoded from.</param>
    /// <param name="named">
    /// What a person has named by hand. A model named there is theirs to judge: its glass is left to the
    /// detector, which has already read the entry, and only its lights are taken from the glows — unless
    /// the entry strikes the model out, when it is given neither.
    /// </param>
    /// <param name="logger">Where the conclusion is reported.</param>
    /// <returns>The lights and the glass; <see cref="Result.None"/> when no model carries a glow.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Result Propose(
        WorldScene scene,
        ResourceRegistry resources,
        IReadOnlyList<LampManifest.Entry>? named = null,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        float metres = (float)(1.0 / WorldScene.UnitsPerMetre);

        Dictionary<string, LampManifest.Entry> byModel = new(StringComparer.OrdinalIgnoreCase);

        foreach (LampManifest.Entry entry in named ?? [])
        {
            byModel[entry.Model] = entry;
        }

        Dictionary<MefModel, Glow[]> glowsOf = [];
        Dictionary<MefModel, MeshGeometry?> built = [];
        Dictionary<string, float> brightness = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tested = new(StringComparer.OrdinalIgnoreCase);

        List<LightFixture> fixtures = [];
        Dictionary<string, float> subMeshes = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> blooms = new(StringComparer.OrdinalIgnoreCase);
        int indicators = 0;
        int bent = 0;

        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!glowsOf.TryGetValue(placement.Model, out Glow[]? glows))
            {
                glows = Read(placement.Model, metres);
                glowsOf[placement.Model] = glows;
            }

            if (glows.Length == 0)
            {
                continue;
            }

            bool isNamed = byModel.TryGetValue(placement.Instance.Name, out LampManifest.Entry said);

            if (isNamed && !said.IsLamp)
            {
                // Struck out by hand, and that covers the light as well as the glass.
                continue;
            }

            // A spline segment bends its mesh along the path and the original bends the glow with it.
            // The bend is not reproduced here, so such a glow would land beside its lamp; it is counted
            // and left out rather than placed wrongly.
            if (placement.BentGeometry is not null)
            {
                bent += glows.Length;
                continue;
            }

            if (!built.TryGetValue(placement.Model, out MeshGeometry? geometry))
            {
                geometry = MefMeshBuilder.Build(placement.Model, metres);
                built[placement.Model] = geometry;
            }

            Matrix3f drawn = placement.Orientation.Transposed();

            foreach (Glow glow in glows)
            {
                if (glow.Radius < MinimumLightRadius || Luminance(glow.Colour) <= 0f)
                {
                    indicators++;
                    continue;
                }

                fixtures.Add(Describe(glow, geometry, drawn, placement));
            }

            if (geometry is null || isNamed)
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

                // Once per model and slot: the glass is the model's, wherever the level stands it. A slot
                // drawn by several sub-meshes is the lamp's glass only if every one of them is, since the
                // key cannot tell them apart.
                string key = LampDetector.Lamps.Key(placement.Instance.Name, subset.MaterialSlot);

                if (!tested.Add(key))
                {
                    continue;
                }

                if (!IsGlass(geometry, subset.MaterialSlot, glows))
                {
                    continue;
                }

                if (!brightness.TryGetValue(texture, out float bright))
                {
                    bright = LampDetector.Brightness(texture, resources);
                    brightness[texture] = bright;
                }

                // Brightness and not saturation: a red beacon's lens is as saturated as paint gets and is
                // exactly what should glow. A dark texture this close to a glow is its housing.
                if (bright <= LampDetector.MinimumBrightness)
                {
                    continue;
                }

                subMeshes[key] = LampDetector.Strength(bright, LampDetector.LampRadiance);
                textures.Add(texture);

                float largest = 0f;

                foreach (Glow glow in glows)
                {
                    largest = MathF.Max(largest, glow.Radius);
                }

                blooms[texture] = blooms.TryGetValue(texture, out float known) ? MathF.Max(known, largest) : largest;
            }
        }

        if (fixtures.Count == 0 && subMeshes.Count == 0)
        {
            return Result.None;
        }

        fixtures.Sort(LightFixtureExtractor.Compare);

        Dictionary<string, float> skinned = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> skinnedTextures = new(StringComparer.OrdinalIgnoreCase);

        if (textures.Count > 0)
        {
            Skinned(scene, textures, byModel, glowsOf, built, brightness, resources, tested, skinned, skinnedTextures);
        }

        log.Information(
            LogCategory,
            $"Glows: {fixtures.Count} light(s) and {subMeshes.Count} lit sub-mesh(es) from the models' own glows"
            + (skinned.Count > 0 ? $"; {skinned.Count} more sub-mesh(es) skinned and sized like them" : string.Empty)
            + (indicators > 0 ? $"; {indicators} too small to light anything" : string.Empty)
            + (bent > 0 ? $"; {bent} on bent spline segments left out" : string.Empty)
            + ".");

        return new Result(fixtures, subMeshes, textures, skinned, skinnedTextures, blooms);
    }

    /// <summary>Finds the lamps that carry no glow, by the pages the glowing ones are skinned with.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not every lamp has a glow.</b> A warehouse's sixteen pendant lamps are two sub-meshes of the room
    /// itself, and the room carries no glow at all: the artists lit it in the bake and left the bloom off.
    /// Left to the glows alone that hall is black. The shape test cannot find them either — a sub-mesh of
    /// sixteen lamps is as wide as the hall.
    /// </para>
    /// <para>
    /// <b>What gives them away is the page.</b> The same lamp is hung elsewhere in the level as a model of
    /// its own WITH a glow, and its glass was confirmed there. A sub-mesh drawn with that page is that
    /// lamp again — provided every separate piece of it is the size of a fitting, which is what keeps out
    /// a wall or a cabinet that merely borrows the page. The pieces are told apart by the vertices they
    /// share, so sixteen lamps in one sub-mesh are sixteen pieces and a wall is one.
    /// </para>
    /// </remarks>
    private static void Skinned(
        WorldScene scene,
        HashSet<string> glass,
        Dictionary<string, LampManifest.Entry> byModel,
        Dictionary<MefModel, Glow[]> glowsOf,
        Dictionary<MefModel, MeshGeometry?> built,
        Dictionary<string, float> brightness,
        ResourceRegistry resources,
        HashSet<string> tested,
        Dictionary<string, float> skinned,
        HashSet<string> skinnedTextures)
    {
        float metres = (float)(1.0 / WorldScene.UnitsPerMetre);

        foreach (WorldPlacement placement in scene.Placements)
        {
            // A model with glows of its own has been judged by them, and a named one by a person.
            if (placement.BentGeometry is not null
                || byModel.ContainsKey(placement.Instance.Name)
                || !glowsOf.TryGetValue(placement.Model, out Glow[]? own)
                || own.Length > 0)
            {
                continue;
            }

            if (!built.TryGetValue(placement.Model, out MeshGeometry? geometry))
            {
                geometry = MefMeshBuilder.Build(placement.Model, metres);
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
                    || !glass.Contains(texture))
                {
                    continue;
                }

                string key = LampDetector.Lamps.Key(placement.Instance.Name, subset.MaterialSlot);

                if (!tested.Add(key) || !IsFittingSized(geometry, subset.MaterialSlot))
                {
                    continue;
                }

                if (!brightness.TryGetValue(texture, out float bright))
                {
                    bright = LampDetector.Brightness(texture, resources);
                    brightness[texture] = bright;
                }

                if (bright <= LampDetector.MinimumBrightness)
                {
                    continue;
                }

                skinned[key] = LampDetector.Strength(bright, LampDetector.LampRadiance);
                skinnedTextures.Add(texture);
            }
        }
    }

    /// <summary>Whether every separate piece a model draws with one material slot is the size of a light fitting.</summary>
    private static bool IsFittingSized(MeshGeometry geometry, int slot)
    {
        Vertex3D[] vertices = geometry.Vertices;
        ushort[] indices = geometry.Indices;
        Dictionary<int, int> parent = [];

        int Find(int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.MaterialSlot != slot)
            {
                continue;
            }

            int start = Math.Max(subset.IndexOffset, 0);
            int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

            for (int at = start; at + 2 < end; at += 3)
            {
                int a = indices[at];
                int b = indices[at + 1];
                int c = indices[at + 2];

                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    return false;
                }

                parent.TryAdd(a, a);
                parent.TryAdd(b, b);
                parent.TryAdd(c, c);
                parent[Find(b)] = Find(a);
                parent[Find(c)] = Find(a);
            }
        }

        if (parent.Count == 0)
        {
            return false;
        }

        Dictionary<int, (Vector3f Low, Vector3f High)> boxes = [];

        foreach (int index in parent.Keys.ToArray())
        {
            Vector3f at = vertices[index].Position;
            int piece = Find(index);

            boxes[piece] = boxes.TryGetValue(piece, out (Vector3f Low, Vector3f High) box)
                ? (new Vector3f(MathF.Min(box.Low.X, at.X), MathF.Min(box.Low.Y, at.Y), MathF.Min(box.Low.Z, at.Z)),
                    new Vector3f(MathF.Max(box.High.X, at.X), MathF.Max(box.High.Y, at.Y), MathF.Max(box.High.Z, at.Z)))
                : (at, at);
        }

        foreach ((Vector3f low, Vector3f high) in boxes.Values)
        {
            float largest = MathF.Max(high.X - low.X, MathF.Max(high.Y - low.Y, high.Z - low.Z));

            if (largest > MaximumFittingSpan)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A model's glows, in metres.</summary>
    private static Glow[] Read(MefModel model, float metres)
    {
        IReadOnlyList<MefGlow> read = model.Glows;

        if (read.Count == 0)
        {
            return [];
        }

        Glow[] glows = new Glow[read.Count];

        for (int i = 0; i < glows.Length; i++)
        {
            MefGlow glow = read[i];

            glows[i] = new Glow(
                new Vector3f(glow.Position.X * metres, glow.Position.Y * metres, glow.Position.Z * metres),
                MathF.Abs(glow.Radius) * metres,
                new Vector3f(
                    Math.Clamp(glow.Colour.X, 0f, 1f),
                    Math.Clamp(glow.Colour.Y, 0f, 1f),
                    Math.Clamp(glow.Colour.Z, 0f, 1f)));
        }

        return glows;
    }

    /// <summary>Turns one glow, where a placement stands it, into the light it declares.</summary>
    private static LightFixture Describe(
        in Glow glow, MeshGeometry? geometry, in Matrix3f drawn, WorldPlacement placement)
    {
        Vector3d rotated = drawn.Transform(new Vector3d(glow.Position.X, glow.Position.Y, glow.Position.Z));
        Vector3d position = new(
            placement.Position.X + rotated.X,
            placement.Position.Y + rotated.Y,
            placement.Position.Z + rotated.Z);

        // The source's size, which the bloom's radius only bounds; see MaximumSourceRadius.
        float extent = MathF.Min(glow.Radius, MaximumSourceRadius);
        float area = MathF.PI * extent * extent;

        Vector3f facing = geometry is null ? default : Facing(glow, geometry);
        Vector3f direction = default;

        if ((facing.X * facing.X) + (facing.Y * facing.Y) + (facing.Z * facing.Z) > 0.25f)
        {
            Vector3d turned = drawn.Transform(new Vector3d(facing.X, facing.Y, facing.Z));
            direction = new Vector3f((float)turned.X, (float)turned.Y, (float)turned.Z);
        }

        // Named for the model it came from, so the lighting dump and the lamp readout say which fitting
        // a light belongs to. Not a texture, and nothing looks one up by it.
        LightFixture fixture = new(
            "glow:" + placement.Instance.Name,
            position,
            0f,
            Hue(glow.Colour),
            area,
            extent,
            0,
            direction);

        // The reach a fixture read from geometry is given, but from the bloom's own radius rather than
        // the capped source: the bloom is the one thing the artists sized by how strong the lamp is, a
        // floodlight's a metre and a half and a porch lamp's a fifth of one, and a floodlight on a mast
        // that reached four metres lit nothing but its own bracket.
        float reach = MathF.Min(glow.Radius, MaximumReachRadius)
            * LightFixtureExtractor.DefaultRadiusScale
            * MathF.Sqrt(MathF.Max(fixture.Luminance, 0f));

        return fixture with { Radius = MathF.Max(reach, LightFixtureExtractor.MinimumRadius) };
    }

    /// <summary>Which way a glow's light faces, in model space: away from the fitting it hangs off, or nowhere.</summary>
    private static Vector3f Facing(in Glow glow, MeshGeometry geometry)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        int count = 0;
        float reach = MathF.Max(glow.Radius, FacingReach);
        float reachSquared = reach * reach;

        foreach (Vertex3D vertex in geometry.Vertices)
        {
            float dx = glow.Position.X - vertex.Position.X;
            float dy = glow.Position.Y - vertex.Position.Y;
            float dz = glow.Position.Z - vertex.Position.Z;
            float squared = (dx * dx) + (dy * dy) + (dz * dz);

            if (squared > reachSquared || squared < 1e-8f)
            {
                continue;
            }

            float length = MathF.Sqrt(squared);

            x += dx / length;
            y += dy / length;
            z += dz / length;
            count++;
        }

        if (count == 0)
        {
            return default;
        }

        double sided = Math.Sqrt((x * x) + (y * y) + (z * z)) / count;

        if (sided < MinimumSidedness)
        {
            return default;
        }

        double scale = 1.0 / (sided * count);

        return new Vector3f((float)(x * scale), (float)(y * scale), (float)(z * scale));
    }

    /// <summary>Whether everything a model draws with one material slot lies within reach of a glow.</summary>
    private static bool IsGlass(MeshGeometry geometry, int slot, Glow[] glows)
    {
        Vertex3D[] vertices = geometry.Vertices;
        ushort[] indices = geometry.Indices;
        bool any = false;

        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.MaterialSlot != slot)
            {
                continue;
            }

            int start = Math.Max(subset.IndexOffset, 0);
            int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

            for (int at = start; at < end; at++)
            {
                int index = indices[at];

                if (index >= vertices.Length || !IsNear(vertices[index].Position, glows))
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }

    private static bool IsNear(Vector3f point, Glow[] glows)
    {
        foreach (Glow glow in glows)
        {
            float reach = Math.Clamp(glow.Radius, MinimumGlassReach, MaximumGlassReach);
            float dx = point.X - glow.Position.X;
            float dy = point.Y - glow.Position.Y;
            float dz = point.Z - glow.Position.Z;

            if ((dx * dx) + (dy * dy) + (dz * dz) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A glow's colour as a light's: the same hue with its brightest channel at one.</summary>
    /// <remarks>
    /// A glow's colour is how bright its sprite is ADDED to the picture, and most are a dim grey for that
    /// reason — a fifth of white, laid over a lamp that is already drawn. Read as the light's colour it
    /// would make every lamp a fifth as strong as one read from geometry, whose colour is normalised the
    /// same way. How strong the lamp is comes from the size of its bloom instead.
    /// </remarks>
    private static Vector3f Hue(Vector3f colour)
    {
        float brightest = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));

        return brightest > 0f
            ? new Vector3f(colour.X / brightest, colour.Y / brightest, colour.Z / brightest)
            : colour;
    }


    private static float Luminance(Vector3f colour) =>
        (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

    /// <summary>One glow in metres and in its model's space.</summary>
    private readonly record struct Glow(Vector3f Position, float Radius, Vector3f Colour);
}

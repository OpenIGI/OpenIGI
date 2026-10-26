using OpenIGI.Core.Graphics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>
/// Draws I.G.I. 2's forests: one mesh per stand, the stand's trees culled through its own index, and each
/// surviving tree issued as a trunk and a foliage pass.
/// </summary>
/// <remarks>
/// <para>
/// The state is the original's, from <c>igi2ed.exe</c>. <c>ForestRender_SetGenericState_Trunk</c>
/// (<c>0x57C5A0</c>) draws opaque with <c>CULLMODE = CCW</c> — <b>the opposite winding to the
/// terrain</b>, which is <c>CW</c> — and with the fog density at
/// <c>DrawTerrainMesh_vFogDensity * 0.8</c>, so a wood fogs a fifth less than the ground it stands on.
/// </para>
/// <para>
/// <b>The foliage is one face group, and the model says which.</b>
/// <c>ForestRender_CreateBSPModel</c> (<c>0x57D160</c>) scans a level's face groups for the first whose
/// flags byte has bit 1 set and remembers its index; <c>ForestRender_Draw</c> (<c>0x57CA90</c>) then
/// compares every group it draws against that index and sends the match through
/// <c>ForestRender_DrawFoliageFaceGroup</c> and everything else through
/// <c>ForestRender_DrawTrunkFaceGroup</c>. So the split is not a texture heuristic: it is a bit in the
/// <c>DNER</c> record, at the byte this port's face-group reader treats as material properties.
/// </para>
/// <para>
/// The two are lit differently too, which is the corroboration: the trunk gets the grey average of the
/// baked light's three bytes and the foliage gets the light itself under <c>| 0xFF000000</c>.
/// </para>
/// </remarks>
public sealed class Igi2ForestPresenter : IForestPresenter
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.forest";

    /// <summary>The alpha below which a foliage texel is cut away, out of 255.</summary>
    /// <remarks>
    /// <c>ForestRender_SetStates_Foliage</c> (<c>0x57BDF0</c>) sets <c>D3DRS_ALPHAREF</c> to this. It does
    /// so on the branch its mode word takes the value 1 on, which is the lightmap draw; the two generic
    /// state blocks it otherwise selects between (<c>0x57C770</c> alpha test, <c>0x57C900</c> alpha blend)
    /// set neither the reference nor the test themselves. The port's own looks keep it for the view.
    /// </remarks>
    public const byte FoliageAlphaReference = 32;

    /// <summary>The cut the original's view draws trunks and foliage at: alpha over 64, the shared alpha-test state's.</summary>
    /// <remarks><c>Direct3DRender_SetBlendStateAlphaTest</c>: <c>ALPHAREF</c> 64, <c>GREATER</c>; the device keeps at least its reference.</remarks>
    public const byte RetailAlphaReference = 65;

    /// <summary>The cut the original's sorted foliage blends over: alpha over one (<c>Direct3DRender_SetBlendStateTransparent</c>).</summary>
    public const byte RetailBlendedAlphaReference = 2;

    /// <summary>The face-group flag bit that marks the group as foliage rather than trunk.</summary>
    /// <remarks><c>ForestRender_CreateBSPModel</c> tests <c>*groupByte0 &amp; 2</c> and takes the first hit.</remarks>
    public const int FoliageFaceGroupFlag = 0x02;

    /// <summary>The horizontal half field-of-view tangent a draw assumes when the caller names none.</summary>
    /// <remarks>
    /// The original reads it from a global the projection also builds from
    /// (<c>flt_8DF1100</c>, used by both <c>Forest_DrawTrees</c> and <c>Forest_SetUpClippingPlanes</c>).
    /// Nothing in this port owns that number yet, so a caller that has one should pass it: this is a
    /// 90° horizontal field of view, which is wider than the game's and therefore culls less.
    /// </remarks>
    public const float DefaultHorizontalHalfTangent = 1f;

    /// <summary>The vertical half field-of-view tangent a draw assumes when the caller names none.</summary>
    /// <remarks>4:3 against <see cref="DefaultHorizontalHalfTangent"/>.</remarks>
    public const float DefaultVerticalHalfTangent = 0.75f;

    private readonly IGraphicsDevice _device;
    private readonly List<Stand> _stands = [];
    private readonly List<Visible> _visible = [];

    // One texel per distinct tree bake. A stand's trees mostly share a value, so this is a handful of
    // textures for thousands of trees rather than one each.
    private readonly Dictionary<uint, TextureHandle> _bakes = [];
    private bool _disposed;

    /// <summary>Builds the forests of one level.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="forest">The level's stands, already placed.</param>
    /// <param name="models">Resolves a stand's model name to its mesh, or null when the level lacks it.</param>
    /// <param name="textures">Resolves a model name and material slot to the texture handle it samples.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2ForestPresenter(
        IGraphicsDevice device,
        Igi2Forest forest,
        Func<string, MefModel?> models,
        Func<string, int, TextureHandle> textures,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(forest);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2ForestStand stand in forest.Stands)
        {
            if (models(stand.Model) is not { } model)
            {
                log.Warning(LogCategory, $"Forest {stand.TaskId} draws '{stand.Model}', which the level does not hold.");
                continue;
            }

            if (MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre)) is not { } geometry)
            {
                log.Warning(LogCategory, $"Forest {stand.TaskId}'s model '{stand.Model}' has no geometry.");
                continue;
            }

            (Vector3f centre, float radius, float height) = BoundsOf(model);
            int foliage = FoliageFaceGroupOf(model);

            // A tree is two materials, not one: bark and leaves, and they are different textures on
            // different slots of the same mesh. Resolved per subset for that reason -- one handle for the
            // whole stand drew the trunk in the foliage's texture, or in nothing at all.
            List<Part> trunk = [];
            List<Part> leaves = [];
            for (int i = 0; i < geometry.Subsets.Length; i++)
            {
                MeshSubset subset = geometry.Subsets[i];
                TextureHandle texture = subset.HasMaterial
                    ? textures(stand.Model, subset.MaterialSlot)
                    : TextureHandle.None;

                (i == foliage ? leaves : trunk).Add(new Part(subset, texture));
            }

            _stands.Add(new Stand(
                _device.CreateMesh(geometry.Vertices, geometry.Indices),
                [.. trunk],
                [.. leaves],
                stand.Trees,
                Igi2ForestBsp.Build(stand.Trees, stand.Centre, stand.HalfExtent, height),
                stand.Centre,
                centre,
                radius,
                height,
                Igi2ForestBsp.MarginOf(radius, stand.RandomScaleRangeX, stand.RandomScaleRangeY),
                (float)(stand.ViewCutoff * WorldScene.UnitsPerMetre)));
        }

        TreeCount = _stands.Sum(static s => s.Trees.Count);
        log.Information(
            LogCategory,
            $"Forest: {_stands.Count} stand(s), {TreeCount} trees, {_stands.Sum(static s => s.Bsp.Count)} indexed.");
    }

    /// <summary>Gets how many trees the presenter holds, culled or not.</summary>
    public int TreeCount { get; }

    /// <summary>Gets how many stands were uploaded.</summary>
    public int StandCount => _stands.Count;

    /// <inheritdoc/>
    /// <remarks>What the last <see cref="Draw(in ForestFrame)"/> kept after culling.</remarks>
    public int VisibleTreeCount { get; private set; }

    /// <summary>Gets or sets the renderer's level-of-detail bias, <c>ModelObj_GetLODBias()</c>.</summary>
    /// <remarks>Zero is neutral: it makes the cut-off compare real distances against the stand's own.</remarks>
    public float LodBias { get; set; }

    /// <summary>Gets or sets whether the foliage is alpha-blended and sorted rather than alpha-tested.</summary>
    /// <remarks>
    /// <c>ForestRender_EnableHighQualityForest</c> (<c>0x57C000</c>) sets the flag
    /// <c>ForestRender_Draw</c> reads to decide whether a foliage group goes straight out through
    /// <c>ForestRender_DrawFoliageFaceGroup</c> or through <c>SortRender_AllocItem</c> and back later as
    /// <c>ForestRender_DrawSortedFaceGroup</c> (<c>0x57E270</c>). It also decides whether the tree's level
    /// of detail comes from <c>ForestRender_GetLOD</c> or from the ordinary <c>ModelObj_GetLOD</c>.
    /// </remarks>
    public bool SortedFoliage { get; set; }

    /// <summary>Gets or sets whether the graphics options ask for high-quality forests, which the original's look sorts by.</summary>
    public Func<bool>? HighQualityForests { get; set; }

    /// <inheritdoc/>
    public string Summary => ToString();

    /// <inheritdoc/>
    public void Draw(in ForestFrame frame)
    {
        _retailLook = frame.RetailLook;
        _towardsSun = frame.TowardsSun;
        Draw(frame.Camera, frame.HorizontalHalfTangent, frame.VerticalHalfTangent);
    }

    // Whether this frame draws the trees with the original's states, and the sun they are lit by.
    private bool _retailLook;
    private Vector3f _towardsSun;

    /// <summary>Draws every tree the stand's index and the view distance keep.</summary>
    /// <param name="camera">Where the world is seen from.</param>
    /// <param name="horizontalHalfTangent">The tangent of the horizontal half field of view.</param>
    /// <param name="verticalHalfTangent">The tangent of the vertical half field of view.</param>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// Three passes, because the original has three state blocks and switches between them per face
    /// group. The trunks go first and opaque; then the foliage, which is alpha-tested and so still belongs
    /// in the opaque pass — the original's alpha-test block leaves <c>ALPHABLENDENABLE</c> off; then, when
    /// <see cref="SortedFoliage"/> is set, the foliage instead goes last and blended, back to front.
    /// </para>
    /// <para>
    /// Grouping by pass rather than by tree is a departure: <c>ForestRender_Draw</c> walks one tree's face
    /// groups and switches state between them, leaning on the batcher to coalesce. With one draw per
    /// range here and no batcher underneath, issuing all the trunks and then all the foliage is the same
    /// picture with two state changes instead of two per tree.
    /// </para>
    /// </remarks>
    public void Draw(
        in Camera3D camera,
        float horizontalHalfTangent = DefaultHorizontalHalfTangent,
        float verticalHalfTangent = DefaultVerticalHalfTangent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _visible.Clear();

        // The cull runs in engine units, because everything it measures is: a stand's centre and half
        // extent, a tree's position, and the ViewCutoff the level authored in metres and the load
        // multiplied up. So the eye is converted rather than the forest -- the same division of labour
        // the ground's quadtree makes, and for the same reason.
        Camera3D view = new(WorldScene.ToUnits(camera.Position), camera.Orientation);
        float biasScale = MathF.Sqrt(Igi2ForestLod.BiasSquaredOf(LodBias));
        float biasInverse = Igi2ForestLod.BiasInverseOf(LodBias);

        for (int s = 0; s < _stands.Count; s++)
        {
            Stand stand = _stands[s];
            Igi2ForestClipPlanes planes = Igi2ForestClipPlanes.Create(
                view,
                horizontalHalfTangent,
                verticalHalfTangent,
                stand.Cutoff);

            Vector3f offset = new(
                (float)(stand.Centre.X - view.Position.X),
                (float)(stand.Centre.Y - view.Position.Y),
                (float)(stand.Centre.Z - view.Position.Z));

            int index = s;
            stand.Bsp.Cull(
                planes,
                offset,
                stand.Margin,
                stand.Radius,
                stand.Height,
                tree => Accept(index, tree, view, biasScale, biasInverse));
        }

        VisibleTreeCount = _visible.Count;

        // CCW, which is the opposite of the terrain's winding and has to be set per pass rather than
        // once for the frame.
        _device.SetCullMode(CullMode.CounterClockwise);

        // The trunks cut out at whatever the frame is set to; a trunk texture is opaque, so the value
        // does not reach them either way. The original's look cuts both at the shared alpha-test state's 64, and sorts and
        // blends the foliage when the options ask for high-quality forests.
        if (_retailLook)
        {
            _device.SetAlphaReference(RetailAlphaReference);
            IssueLitTrunks(camera);
            if (HighQualityForests?.Invoke() != true)
            {
                Issue(camera, static s => s.Foliage, BlendMode.Opaque, doubled: true);
                _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
                return;
            }

            _visible.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
            _device.SetAlphaReference(RetailBlendedAlphaReference);
            Issue(camera, static s => s.Foliage, BlendMode.AlphaBlend, doubled: true);
            _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
            return;
        }

        Issue(camera, static s => s.Trunk, BlendMode.Opaque, doubled: false);
        if (!SortedFoliage)
        {
            // And the foliage at 32 out of 255, which is the whole point of the reference being
            // settable: a leaf sprite's alpha ramps, and the half this port used to test at eroded it to
            // a fraction of its authored shape. Set back afterwards, because it is device state and the
            // next thing drawn is not foliage.
            _device.SetAlphaReference(FoliageAlphaReference);
            Issue(camera, static s => s.Foliage, BlendMode.Opaque, doubled: true);
            _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
            return;
        }

        // Back to front, which is what SortRender_AllocItem's key buys: it takes the view-space depth of
        // the face group's first vertex and the sort renderer replays the items in that order.
        _visible.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
        Issue(camera, static s => s.Foliage, BlendMode.AlphaBlend, doubled: true);
    }

    /// <summary>Builds a tree's orientation, with its per-axis scale folded in.</summary>
    /// <param name="tree">The tree.</param>
    /// <returns>The matrix to draw it with.</returns>
    /// <remarks>
    /// Scale rides on the orientation because that is the only transform a draw carries. A quarter of
    /// the corpus's trees are tilted rather than upright — the bake leans them to the ground they stand
    /// on — so the angles are not decoration.
    /// </remarks>
    public static Matrix3f OrientationOf(Igi2Tree tree)
    {
        Matrix3f rotation = WorldScene.ToOrientation(tree.Angles);
        return new Matrix3f(
            rotation.M11 * tree.Scale.X,
            rotation.M12 * tree.Scale.X,
            rotation.M13 * tree.Scale.X,
            rotation.M21 * tree.Scale.Y,
            rotation.M22 * tree.Scale.Y,
            rotation.M23 * tree.Scale.Y,
            rotation.M31 * tree.Scale.Z,
            rotation.M32 * tree.Scale.Z,
            rotation.M33 * tree.Scale.Z);
    }

    /// <summary>Finds the face group a model draws its leaves with.</summary>
    /// <param name="model">The tree model.</param>
    /// <returns>The index into the model's draw records, or −1 when no group is flagged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The flag is byte 0 of the <c>DNER</c> face-group record, which this port's model reader steps past
    /// as material properties. It is read here from the raw chunk rather than added to the reader, so the
    /// format stays as it was measured and only the forest, which is the one thing that needs the bit,
    /// pays for knowing about it.
    /// </remarks>
    public static int FoliageFaceGroupOf(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.TryGetRawChunk(MefTags.DrawRecords, out ReadOnlyMemory<byte> payload))
        {
            return -1;
        }

        int stride = model.Format == MefMeshFormat.Lightmapped ? 28 : 32;
        ReadOnlySpan<byte> groups = payload.Span;
        int count = Math.Min(model.DrawRecords.Count, groups.Length / stride);
        for (int i = 0; i < count; i++)
        {
            if ((groups[i * stride] & FoliageFaceGroupFlag) != 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TextureHandle bake in _bakes.Values)
        {
            _device.DestroyTexture(bake);
        }

        _bakes.Clear();
        foreach (Stand stand in _stands)
        {
            _device.DestroyMesh(stand.Mesh);
        }

        _stands.Clear();
        _visible.Clear();
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 forest, {_stands.Count} stand(s), {TreeCount} trees";

    /// <summary>The model's centre, its radius about that centre, and its height, all in model units.</summary>
    /// <remarks>
    /// <c>Forest_UpdateInternalDataHandler</c> (<c>0x64CDC0</c>) computes both numbers itself rather than
    /// reading them: it walks the model's vertices, takes the greatest squared distance from the mesh's
    /// stored centre as the radius (<c>ptForest + 1560</c>) and the span of Z as the height
    /// (<c>+ 1564</c>). <b>The centre it measures from is the runtime mesh's own</b>, which this port does
    /// not read, so the vertex bounding box's centre stands in for it. The two agree for a model whose
    /// stored centre is its bounding centre and differ by however far apart they are otherwise, and the
    /// radius is only ever used as a cull margin, where erring large is safe.
    /// </remarks>
    private static (Vector3f Centre, float Radius, float Height) BoundsOf(MefModel model)
    {
        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        if (vertices.Length == 0)
        {
            return (default, 0f, 0f);
        }

        Vector3f min = vertices[0].Position;
        Vector3f max = min;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f p = vertices[i].Position;
            min = new Vector3f(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vector3f(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }

        Vector3f centre = (min + max) * 0.5f;
        float furthest = 0f;
        foreach (MefVertex vertex in vertices)
        {
            furthest = Math.Max(furthest, (vertex.Position - centre).LengthSquared);
        }

        return (centre, MathF.Sqrt(furthest), max.Z - min.Z);
    }

    private void Accept(int stand, int tree, in Camera3D camera, float biasScale, float biasInverse)
    {
        Stand owner = _stands[stand];
        Igi2Tree placed = owner.Trees[tree];

        // The model's centre is added to the camera-relative offset without being rotated by the tree,
        // which is what ForestRender_GetLOD does -- it takes the mesh centre straight off the model.
        Vector3f delta = new(
            (float)(placed.Position.X - camera.Position.X) + owner.ModelCentre.X,
            (float)(placed.Position.Y - camera.Position.Y) + owner.ModelCentre.Y,
            (float)(placed.Position.Z - camera.Position.Z) + owner.ModelCentre.Z);

        float distance = delta.Length * biasScale;
        if (owner.Cutoff > 0f)
        {
            float scale = Math.Max(placed.Scale.X, Math.Max(placed.Scale.Y, placed.Scale.Z));
            Igi2ForestLodChoice choice = Igi2ForestLod.Select(
                distance,
                scale,
                [owner.Cutoff],
                1,
                biasInverse);

            if (choice.Level == Igi2ForestLod.NotDrawn)
            {
                return;
            }
        }

        _visible.Add(new Visible(stand, tree, delta.Length));
    }

    private void Issue(in Camera3D camera, Func<Stand, Part[]> select, BlendMode blend, bool doubled)
    {
        // The bake in the second slot is a colour to multiply in, not a light to keep apart -- see
        // BakeOf. The forward path does that already; this is what tells the deferred one.
        _device.SetLightmapModulates(true);
        try
        {
            IssueTrees(camera, select, blend, doubled);
        }
        finally
        {
            _device.SetLightmapModulates(false);
        }
    }

    // ForestRender_Draw with ForestRender_SetGenericState_Trunk: a trunk is lit, its diffuse the grey of the tree's bake (the mean of its
    // three channels) and its ambient one, so it is the texture times the scene's ambient plus the sun's colour by the cosine and that
    // grey. The grey rides as the length of the sun's direction in the tree's own frame.
    private void IssueLitTrunks(in Camera3D camera)
    {
        foreach (Visible visible in _visible)
        {
            Stand stand = _stands[visible.Stand];
            if (stand.Trunk.Length == 0)
            {
                continue;
            }

            Igi2Tree tree = stand.Trees[visible.Tree];
            Matrix3f orientation = OrientationOf(tree);
            float grey = ((float)tree.Light.R + tree.Light.G + tree.Light.B) / (3f * 255f);
            Vector3f sun = orientation.Transform(_towardsSun).NormalizedOrSelf() * grey;
            _device.SetVertexSun(sun);
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(tree.Position), orientation);
            foreach (Part part in stand.Trunk)
            {
                _device.DrawMesh(stand.Mesh, part.Subset.IndexOffset, part.Subset.IndexCount, modelView, part.Texture, TextureHandle.None, BlendMode.Opaque);
            }
        }

        _device.SetVertexSun(Vector3f.Zero);
    }

    private void IssueTrees(in Camera3D camera, Func<Stand, Part[]> select, BlendMode blend, bool doubled)
    {
        foreach (Visible visible in _visible)
        {
            Stand stand = _stands[visible.Stand];
            Part[] parts = select(stand);
            if (parts.Length == 0)
            {
                continue;
            }

            Igi2Tree tree = stand.Trees[visible.Tree];
            // Drawn in metres: the frame's camera is, and so are the mesh's own vertices now.
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(tree.Position), OrientationOf(tree));
            TextureHandle bake = BakeOf(tree.Light, doubled);
            foreach (Part part in parts)
            {
                _device.DrawMesh(
                    stand.Mesh,
                    part.Subset.IndexOffset,
                    part.Subset.IndexCount,
                    modelView,
                    part.Texture,
                    bake,
                    blend);
            }
        }
    }

    /// <summary>Gets the single-texel texture holding one tree's baked colour, doubled.</summary>
    /// <remarks>
    /// <para>
    /// <b>The trees are lit by nothing but this.</b> The original's foliage stage 0 is
    /// <c>COLOROP = MODULATE2X</c> over <c>COLORARG1 = TEXTURE</c> and <c>COLORARG2 = DIFFUSE</c>
    /// (<c>ForestRender_SetTextureState_AlphaTestFoliage</c>, <c>igi2ed 0x57C4B0</c>), and
    /// <c>ForestRender_SetGenericState_AlphaTestFoliage</c> (<c>0x57C770</c>) writes
    /// <c>D3DRS_LIGHTING = 0</c> beside it — so a tree's colour is its texture times the colour the
    /// forest bake gave that tree, doubled, and nothing else touches it.
    /// </para>
    /// <para>
    /// The port read that colour into <see cref="Igi2Tree.Light"/> and then drew the trees without it,
    /// at their full albedo. On location1/level1 the bake is around (20, 35, 53) — dark, and blue over
    /// green over red, which is the night the level is authored for. Drawn without it a pine reads as
    /// bright daylight green in the middle of that night, which is exactly what it looked like.
    /// </para>
    /// <para>
    /// A single texel, because a tree is one draw and the port has no per-draw diffuse colour to put
    /// this in — the forward push block is full. The lightmap slot is free here and the forward path
    /// already multiplies it into the base colour, which is the product wanted; the deferred path is
    /// told to do the same by <see cref="IGraphicsDevice.SetLightmapModulates"/>. Doubling is baked
    /// into the texel rather than done in a shader, which is exact at these levels: the night values
    /// are all well under half.
    /// </para>
    /// </remarks>
    private TextureHandle BakeOf(Bgra32 light, bool doubled)
    {
        uint key = ((uint)light.R << 16) | ((uint)light.G << 8) | light.B | (doubled ? 0x8000_0000u : 0u);
        if (_bakes.TryGetValue(key, out TextureHandle existing))
        {
            return existing;
        }

        // THE TRUNK IS NOT DOUBLED AND THE FOLIAGE IS. They are separate state blocks in the original
        // and they disagree on exactly this: ForestRender_SetTextureState_Trunk (igi2ed 0x57C2D0) sets
        // stage 0 to COLOROP = MODULATE, while ForestRender_SetTextureState_AlphaTestFoliage (0x57C4B0)
        // sets MODULATE2X. Doubling both put every trunk at twice the brightness the original gives it,
        // which on a birch reads as a white post standing in a dark wood.
        int scale = doubled ? 2 : 1;
        Bgra32 applied = new(
            (byte)Math.Min(255, light.B * scale),
            (byte)Math.Min(255, light.G * scale),
            (byte)Math.Min(255, light.R * scale),
            255);
        TextureHandle texture = _device.CreateTexture(new TextureDescription(1, 1), [applied]);
        _bakes[key] = texture;
        return texture;
    }

    private readonly record struct Visible(int Stand, int Tree, float Depth);

    private sealed record Stand(
        MeshHandle Mesh,
        Part[] Trunk,
        Part[] Foliage,
        IReadOnlyList<Igi2Tree> Trees,
        Igi2ForestBsp Bsp,
        Vector3d Centre,
        Vector3f ModelCentre,
        float Radius,
        float Height,
        float Margin,
        float Cutoff);

    private readonly record struct Part(MeshSubset Subset, TextureHandle Texture);
}

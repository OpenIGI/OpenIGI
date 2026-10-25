using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// A whole deferred frame, described once and drawn on every backend, arriving at the same picture.
/// </summary>
/// <remarks>
/// <para>
/// The sequence the game itself drives: a scene capture opened, a geometry pass, world geometry through
/// the ordinary mesh path, the capture closed — which is where the shading runs — and a resolve onto
/// the frame. Nothing here says which backend it is for, and nothing here knows there are three passes
/// rather than one.
/// </para>
/// <para>
/// This is the gate the whole move is for. Everything else proves a pass can run; this proves the frame
/// a player would see is the same frame.
/// </para>
/// </remarks>
public sealed class DeferredFrameAgreementTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_draws_the_reference_deferred_frame(string backend)
    {
        byte[] vulkan = DrawOn(RhiBackends.Reference);
        byte[] other = DrawOn(backend);

        int differing = 0;
        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            int difference = Math.Abs(vulkan[i] - other[i]);

            if (difference > 0)
            {
                differing++;
                worst = Math.Max(worst, difference);
            }
        }

        output.WriteLine($"{differing} channel(s) differ, worst by {worst}");
        output.WriteLine($"vulkan centre {Describe(vulkan)}");
        output.WriteLine($"vulkan corner {Describe(vulkan, 2, 2)}");
        output.WriteLine($"{backend} centre {Describe(other)}");

        // One is what a different rasteriser's edge rules earn, and no more: the two run the same
        // shaders over the same geometry.
        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} by {worst}, over {differing} channel(s).");

        // And a triangle was drawn, shaded and resolved rather than the frame being one flat colour: the
        // middle is the surface's own green and the corner, which no triangle reached, is the sky. Two
        // backends agree about a uniform frame perfectly, so the comparison above is worth only as much
        // as this.
        Assert.True(vulkan[Centre + 1] > vulkan[Centre], "The middle of the frame is not the green that was drawn.");
        Assert.True(vulkan[Centre + 1] > vulkan[Centre + 2], "The middle of the frame is not the green that was drawn.");

        int corner = ((2 * Size) + 2) * 4;
        Assert.True(
            Math.Abs(vulkan[corner + 1] - vulkan[Centre + 1]) > 8,
            "The corner and the middle shaded the same, so nothing was drawn into the G-buffer.");

        // And the corner is the marched sky rather than the flat white a stand-in table gives: blue
        // before red, as a daytime sky is.
        Assert.True(
            vulkan[corner] > vulkan[corner + 2] + 8,
            $"The sky is {Describe(vulkan, 2, 2)}, which is not an atmosphere's blue.");

        // The forward pass drew over the light, in the corner it was put in.
        int forward = (((Size / 8) * Size) + (Size - (Size / 8))) * 4;
        output.WriteLine($"vulkan forward {Describe(vulkan, Size - (Size / 8), Size / 8)}");
        Assert.True(
            vulkan[forward + 2] > vulkan[forward + 1] + 32 && vulkan[forward + 2] > vulkan[forward] + 32,
            $"The forward quad's corner is {Describe(vulkan, Size - (Size / 8), Size / 8)}, not the red that was drawn.");

        // And the right way up. The triangle's apex points up the screen, so its base covers the lower
        // left of the frame and the same place mirrored into the upper half is sky. A frame turned over
        // by any of its three passes agrees with itself perfectly on every backend -- each pass reads
        // what the last wrote the same wrong way up -- so only a question about WHERE can catch it.
        int below = ((((Size * 4) / 5) * Size) + BaseColumn) * 4;
        int above = ((((Size / 5) * Size) + BaseColumn) * 4);

        output.WriteLine($"vulkan below {Describe(vulkan, BaseColumn, (Size * 4) / 5)}");
        output.WriteLine($"vulkan above {Describe(vulkan, BaseColumn, Size / 5)}");

        Assert.True(
            Math.Abs(vulkan[below + 1] - vulkan[Centre + 1]) <= 8,
            "The lower left of the frame is not the triangle's base, so the frame is upside down.");
        Assert.True(
            Math.Abs(vulkan[above + 1] - vulkan[Centre + 1]) > 8,
            "The upper left of the frame is the triangle, so the frame is upside down.");
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void A_marked_body_is_filled_and_outlined_over_the_picture_alike_on_every_backend(string backend)
    {
        byte[] plain = DrawOn(backend);
        byte[] marked = DrawOn(backend, silhouette: true);
        byte[] reference = DrawOn(RhiBackends.Reference, silhouette: true);

        output.WriteLine($"{backend} centre {Describe(plain)} -> {Describe(marked)}");

        // Inside, the faint dark fill: the green drawn there is dimmed.
        Assert.True(marked[Centre + 1] + 4 < plain[Centre + 1], $"The middle is {Describe(marked)}, not dimmed from {Describe(plain)}.");

        // About the edge, the hostile's red outline: somewhere across the triangle's left side on the middle row is redder than it
        // was. The side crosses that row at about a third of the way in.
        int brightest = 0;
        for (int x = (Size / 4) - 4; x <= (Size / 4) + 8; x++)
        {
            int at = (((Size / 2) * Size) + x) * 4;
            // Bgra: the red is the third byte.
            int gain = marked[at + 2] - plain[at + 2];
            brightest = Math.Max(brightest, gain);
        }

        output.WriteLine($"{backend} outline lifts the edge by {brightest}");
        Assert.True(brightest > 20, $"The outline lifted the triangle's edge by only {brightest}.");

        // Far from it, nothing: the sky in the corner is as it was.
        int corner = ((2 * Size) + 2) * 4;
        for (int c = 0; c < 3; c++)
        {
            Assert.InRange(marked[corner + c] - plain[corner + c], -1, 1);
        }

        // And the backend agrees with the reference about all of it.
        int worst = 0;
        for (int i = 0; i < marked.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(marked[i] - reference[i]));
        }

        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} by {worst}.");
    }

    /// <summary>Gets each compared backend, once untextured and once textured.</summary>
    public static TheoryData<string, bool> AlbedoCases
    {
        get
        {
            TheoryData<string, bool> cases = [];
            foreach (string backend in RhiBackends.ComparedWithReference)
            {
                cases.Add(backend, false);
                cases.Add(backend, true);
            }

            return cases;
        }
    }

    [AgreementTheory]
    [MemberData(nameof(AlbedoCases))]
    public void The_albedo_view_shows_the_colour_that_was_drawn(string backend, bool textured)
    {
        // Once from the vertex colour and once from a texture, because the two reach linear space by
        // different roads: the vertex colour is decoded by the shader, and a texture on this path is an
        // sRGB image the SAMPLER decodes -- which a shader written for UNORM images decoded again.
        byte[] vulkan = DrawOn(RhiBackends.Reference, DebugView.Albedo, textured);
        byte[] other = DrawOn(backend, DebugView.Albedo, textured);

        output.WriteLine($"vulkan centre {Describe(vulkan)}");
        output.WriteLine($"{backend} centre {Describe(other)}");

        // A mask view is the code value as it was stored, with no curve, grade or film on it: the colour
        // that was drawn, to within the encode and decode it went through on the way.
        foreach (byte[] frame in (byte[][])[vulkan, other])
        {
            Assert.InRange(frame[Centre], 0, 1);
            Assert.InRange(frame[Centre + 1], 219, 221);
            Assert.InRange(frame[Centre + 2], 39, 41);
        }
    }

    [ReferenceFact]
    public void The_temporal_resolve_runs_in_the_frame_and_starts_from_it()
    {
        // Vulkan alone: two of the chain's modules have no WGSL form, so WebGPU stands the chain down and
        // names r_taa instead. On its first frame the resolve has no history to weigh, so what it hands
        // the tone map is the frame itself -- to within the spatial filter's reach at an edge.
        byte[] plain = DrawOn(RhiBackends.Reference);
        byte[] resolved = DrawOn(RhiBackends.Reference, temporal: true);

        int differing = 0;
        int worst = 0;

        for (int i = 0; i < plain.Length; i++)
        {
            int difference = Math.Abs(plain[i] - resolved[i]);
            worst = Math.Max(worst, difference);
            differing += difference > 8 ? 1 : 0;
        }

        output.WriteLine($"{differing} channel(s) differ by more than 8, worst by {worst}");
        output.WriteLine($"resolved centre {Describe(resolved)}");

        Assert.True(resolved[Centre + 1] > resolved[Centre + 2], "The resolved frame lost the green that was drawn.");
        Assert.True(differing < plain.Length / 20, $"{differing} channels moved: the resolve is not starting from the frame.");
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_runs_a_mods_pass_over_the_finished_picture(string backend)
    {
        // The sample mod's greyscale, over the picture as it is shown: the tone map draws into the mods' image, the pass
        // drains it, and what it wrote is laid onto the frame -- every pixel grey, where the frame without it has the green
        // triangle and the red quad.
        PostPassModule[] greyscale = [new("sample.Greyscale", SampleGreyscale.Value, PostPassStage.Display)];
        byte[] vulkan = DrawOn(RhiBackends.Reference, passes: greyscale);
        byte[] other = DrawOn(backend, passes: greyscale);
        byte[] plain = DrawOn(RhiBackends.Reference);

        int worst = 0;
        for (int i = 0; i < vulkan.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(vulkan[i] - other[i]));
        }

        output.WriteLine($"worst difference {worst}; centre {Describe(vulkan)}, without {Describe(plain)}");
        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} about the mod's pass by {worst}.");
        Assert.True(Colourful(plain) > 0, "The frame without the pass has no colour to drain.");
        Assert.Equal(0, Colourful(vulkan));
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_spills_the_reference_bloom(string backend)
    {
        // Nineteen compute passes -- six halvings, a separable blur at each, and the sum -- bound on both
        // backends, and what they add has to be the same light. The red quad in the corner is the brightest
        // thing in the frame, so the sky beside it has to come out redder with the scatter than without.
        byte[] vulkan = DrawOn(RhiBackends.Reference, bloom: 0.5f);
        byte[] other = DrawOn(backend, bloom: 0.5f);
        byte[] plain = DrawOn(RhiBackends.Reference, bloom: 0f);

        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(vulkan[i] - other[i]));
        }

        // Just left of the quad, above the triangle's apex: sky, four pixels from the red.
        int x = (Size * 11) / 16;
        int y = Size / 8;
        int beside = ((y * Size) + x) * 4;

        output.WriteLine($"worst difference {worst}");
        output.WriteLine($"beside, bloom {Describe(vulkan, x, y)}, without {Describe(plain, x, y)}");

        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} about the bloom by {worst}.");
        Assert.True(
            vulkan[beside + 2] - vulkan[beside] > plain[beside + 2] - plain[beside],
            "The sky beside the quad is no redder with the bloom than without it.");
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_occludes_as_the_reference_does(string backend)
    {
        // The gather over the depth the triangle wrote, shown as the code values it produced: it has to
        // bind on every backend, agree, and have written something -- a view of a cleared occlusion
        // image is uniform, and two backends agree about a uniform image perfectly.
        byte[] vulkan = DrawOn(RhiBackends.Reference, DebugView.Ao);
        byte[] other = DrawOn(backend, DebugView.Ao);

        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(vulkan[i] - other[i]));
        }

        int strays = 0;
        for (int i = 0; i < vulkan.Length; i++)
        {
            if (Math.Abs(vulkan[i] - other[i]) > RhiBackends.ColourTolerance(backend))
            {
                strays++;
            }
        }

        output.WriteLine($"worst difference {worst}, {strays} channel(s) past the tolerance, centre {Describe(vulkan)}");

        // The gather tests each sample against a range, so where a backend's arithmetic rounds differently
        // a sample at a sharp corner can fall either side of it: measured on Direct3D 12, two clusters of a
        // few pixels at the triangle's corners, unoccluded where Vulkan has them one sample occluded. Within
        // the tolerance everywhere else, and past it on no more than one pixel in a hundred.
        bool sameCompiler = RhiBackends.ColourTolerance(backend) == 1;
        Assert.True(
            sameCompiler ? worst <= 1 : strays * 100 <= vulkan.Length,
            $"{backend} disagrees with {RhiBackends.Reference} about the occlusion by {worst}, on {strays} channel(s).");
        Assert.True(vulkan[Centre + 1] > 128, $"The triangle's occlusion is {Describe(vulkan)}: nothing was gathered.");
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_shadows_as_the_reference_does(string backend)
    {
        // A cascade drawn and compared against, shown as the sun's visibility: a depth array rendered
        // one layer at a time, bound as a depth texture and read through a comparison sampler, which is
        // three things WebGPU checks that Vulkan does not.
        byte[] vulkan = DrawOn(RhiBackends.Reference, DebugView.SunShadow, shadows: true);
        byte[] other = DrawOn(backend, DebugView.SunShadow, shadows: true);

        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(vulkan[i] - other[i]));
        }

        output.WriteLine($"worst difference {worst}, centre {Describe(vulkan)}, corner {Describe(vulkan, 2, 2)}");

        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} about the shadow by {worst}.");
    }

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_sees_through_a_pane_as_the_reference_does(string backend)
    {
        // The glazing's transmission: its own pipeline and its own group, laid premultiplied over the
        // lit triangle. The backends have to agree, and where the pane is the triangle has to have
        // taken its blue.
        byte[] vulkan = DrawOn(RhiBackends.Reference, glass: true);
        byte[] other = DrawOn(backend, glass: true);
        byte[] plain = DrawOn(RhiBackends.Reference);

        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(vulkan[i] - other[i]));
        }

        // Inside the pane, on the triangle.
        int x = Size / 2;
        int y = (Size * 11) / 16;
        int behind = ((y * Size) + x) * 4;

        output.WriteLine($"worst difference {worst}; through the pane {Describe(vulkan, x, y)}, without {Describe(plain, x, y)}");

        Assert.True(worst <= RhiBackends.ColourTolerance(backend), $"{backend} disagrees with {RhiBackends.Reference} about the pane by {worst}.");
        Assert.True(
            vulkan[behind] - vulkan[behind + 1] > plain[behind] - plain[behind + 1],
            "The triangle behind the pane took none of its blue.");
    }

    /// <summary>A column the triangle's base covers near the bottom of the frame and its apex does not reach near the top.</summary>
    private const int BaseColumn = 14;

    /// <summary>The frame everything here compares, written once and run twice.</summary>
    private static void DrawFrame(GraphicsDevice device, DebugView view, bool textured, bool temporal, float? bloom, bool shadows, bool glass, bool silhouette = false)
    {
        device.SetDebugView(view);

        // The occlusion, where the frame is asked to show it: the gather over the depth the geometry wrote.
        device.SetAmbientOcclusion(view is DebugView.Ao
            ? new AmbientOcclusionSettings(Quality: 1)
            : AmbientOcclusionSettings.Off);

        Assert.True(
            device.BeginSceneCapture(new SceneTargetDescription(Size, Size, Deferred: true)),
            "The capture was declined.");

        // What the game does as a capture opens: it clears the frame for the target it knows about. The
        // resolve covers the whole frame, so that clear must not outlive it.
        device.Clear(new Bgra32(255, 0, 255, 255));

        // What a frame tells the device before it draws: where the eye is, and how a depth becomes a
        // position. Both arrive with passes this path does not run, which is why they are set here
        // rather than assumed.
        // One cascade where the frame is to be shadowed: the light looking straight down the view, its
        // projection the identity, so what the triangle writes into the map is what the shading finds.
        //
        // The CASCADE, explicitly: an adapter that can trace rays traces the sun instead by default, and
        // under this frame's identity projection the traced positions do not lie on the triangle, which
        // then shadowed itself everywhere on Vulkan while WebGPU, which cannot trace, read the map. The
        // comparison is about the map -- the depth array, the depth binding and the comparison sampler.
        device.SetShadows(
            shadows ? new ShadowSettings(CascadeCount: 1, Resolution: Size) { TracedSun = false } : ShadowSettings.Off,
            shadows ? [new ShadowCascade(Matrix3f.Identity, Matrix4f.Identity, Vector3f.Zero, 1000f, 1000f, 0.1f)] : [],
            new DepthUnprojection(1f, 1f, 0.1f, 1000f),
            Matrix3f.Identity);

        device.SetTemporalAntiAliasing(
            temporal ? new TemporalAaSettings(Enabled: true) : default,
            Matrix4f.Identity,
            Vector3d.Zero,
            Matrix3f.Identity);

        // The original's surfaces, under a daytime sun. The bakes light the triangle whatever the sun
        // is; the sun is there for the atmosphere, which marches a black sky for a black one.
        device.SetLighting(SceneLighting.Baked with
        {
            // The hybrid model where the frame is shadowed, since the baked one never looks at a map.
            Model = shadows ? LightingModel.Hybrid : LightingModel.Baked,
            SunDirection = new Vector3f(0.3f, 0.9f, 0.31f).Normalized(),
            SunColour = new Vector3f(10f, 10f, 10f),
        });
        device.SetProjection(Matrix4f.Identity);
        device.SetDepthState(DepthState.TestAndWrite);
        device.SetCullMode(CullMode.None);
        device.SetModelAmbient(Vector3f.One);

        // The same green either way: carried by the vertices over no texture, or by a texture under
        // white vertices.
        Bgra32 green = new(0, 220, 40, 255);
        Bgra32 vertexColour = textured ? Bgra32.White : green;

        TextureHandle texture = textured
            ? device.CreateTexture(new TextureDescription(1, 1), [green])
            : TextureHandle.None;

        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(-0.7f, -0.7f, 0.5f), Vector3f.UnitZ, 0f, 0f, colour: vertexColour),
                new Vertex3D(new Vector3f(0.7f, -0.7f, 0.5f), Vector3f.UnitZ, 1f, 0f, colour: vertexColour),
                new Vertex3D(new Vector3f(0f, 0.7f, 0.5f), Vector3f.UnitZ, 0.5f, 1f, colour: vertexColour),
            ],
            [0, 1, 2]);

        if (shadows && device.BeginShadowCascade(0))
        {
            device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, texture, TextureHandle.None, BlendMode.Opaque);
            device.EndShadowCascade();
        }

        device.BeginPass(ScenePass.Geometry);
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, texture, TextureHandle.None, BlendMode.Opaque);
        device.EndPass();

        // And over the light, what a G-buffer cannot hold: a red quad in the top-right corner, drawn in
        // the forward pass against the depth the triangle wrote.
        Bgra32 red = new(0, 0, 200, 255);
        MeshHandle quad = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(0.5f, 0.5f, 0.5f), Vector3f.UnitZ, 0f, 0f, colour: red),
                new Vertex3D(new Vector3f(0.95f, 0.5f, 0.5f), Vector3f.UnitZ, 1f, 0f, colour: red),
                new Vertex3D(new Vector3f(0.95f, 0.95f, 0.5f), Vector3f.UnitZ, 1f, 1f, colour: red),
                new Vertex3D(new Vector3f(0.5f, 0.95f, 0.5f), Vector3f.UnitZ, 0f, 1f, colour: red),
            ],
            [0, 1, 2, 0, 2, 3]);

        device.BeginPass(ScenePass.Forward);
        device.DrawMesh(quad, 0, 6, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

        if (glass)
        {
            // A pane over the triangle's lower half: a blue texel a quarter opaque, which the glazing
            // keeps -- the geometry pass takes a pane's opaque texels and this takes the rest. At minus a
            // fifth, because the glazing's vertex stage remaps depth as half of w minus z to suit a real
            // projection: under this frame's identity that puts the pane at 0.6, in front of the triangle.
            TextureHandle pane = device.CreateTexture(new TextureDescription(1, 1), [new Bgra32(220, 60, 20, 64)]);
            MeshHandle window = device.CreateMesh(
                [
                    new Vertex3D(new Vector3f(-0.5f, -0.6f, -0.2f), Vector3f.UnitZ, 0f, 0f),
                    new Vertex3D(new Vector3f(0.5f, -0.6f, -0.2f), Vector3f.UnitZ, 1f, 0f),
                    new Vertex3D(new Vector3f(0.5f, -0.1f, -0.2f), Vector3f.UnitZ, 1f, 1f),
                    new Vertex3D(new Vector3f(-0.5f, -0.1f, -0.2f), Vector3f.UnitZ, 0f, 1f),
                ],
                [0, 1, 2, 0, 2, 3]);

            device.SetMaterialOverride(new SurfaceMaterial(Glass: true));
            device.SetDepthState(DepthState.TestOnly);
            device.DrawMesh(window, 0, 6, Matrix4f.Identity, pane, TextureHandle.None, BlendMode.AlphaBlend);
            device.SetDepthState(DepthState.TestAndWrite);
            device.SetMaterialOverride(SurfaceMaterial.Default);
        }

        // The triangle marked for the silhouette highlight, which is laid over the finished picture.
        if (silhouette)
        {
            device.DrawSilhouette(mesh, [new MeshDrawRange(0, 3)], Matrix4f.Identity, SilhouetteGroup.Hostile);
        }

        device.EndPass();

        device.EndSceneCapture();

        // Stated rather than defaulted: PostProcessSettings is a record struct, so `new()` runs the
        // ZEROING constructor and none of its declared defaults -- an exposure of nought, a saturation
        // of nought, a white point of nought. That is not "no grading"; it is a grey frame, and two
        // backends agree about a grey frame perfectly.
        device.ResolveScene(new PostProcessSettings(
            ToneMapOperator.Neutral,
            Exposure: 1f,
            WhitePoint: 1.6f,
            Contrast: 1f,
            Saturation: 1f,

            // The lens scatter is added only under a filmic curve, so a frame that says anything about
            // it -- nought included -- is resolved under one.
            BloomIntensity: bloom ?? 0f)
            with
        {
            ToneMap = bloom is null ? ToneMapOperator.Neutral : ToneMapOperator.Aces,

            // Everything spills, as ultra carries it: nothing in a frame this dim passes a threshold of one.
            BloomThreshold = 0f,
        });

        // And what it does after: the letterbox and the HUD go on top. A clear still pending from the
        // capture would be honoured by this pass and take the whole picture with it.
        device.Set2DProjection(Size, Size);
        device.DrawTriangles2D(
            [
                new Vertex2D(Size - 2, 0, 0f, 0f, Bgra32.White),
                new Vertex2D(Size, 0, 0f, 0f, Bgra32.White),
                new Vertex2D(Size, 2, 0f, 0f, Bgra32.White),
            ],
            [0, 1, 2],
            TextureHandle.None,
            BlendMode.Opaque);

        device.SwapBuffers();
    }

    private static byte[] DrawOn(
        string backend,
        DebugView view = DebugView.None,
        bool textured = false,
        bool temporal = false,
        float? bloom = null,
        bool shadows = false,
        bool glass = false,
        IReadOnlyList<PostPassModule>? passes = null,
        bool silhouette = false)
    {
        using RhiBackendHost host = RhiBackends.Open(backend, Size);

        using (GraphicsDevice device = new(host.Device, new DisplayMode(Size, Size, 32, 60)))
        {
            if (passes is not null)
            {
                device.SetPostPasses(passes);
                foreach (PostPassModule pass in passes)
                {
                    device.SetPostPassValues(pass.Name, [1f]);
                }
            }

            DrawFrame(device, view, textured, temporal, bloom, shadows, glass, silhouette);
        }

        return Read(host.Device, host.Target);
    }

    /// <summary>Brings one frame back to the host, un-padded.</summary>
    private static byte[] Read(IRhiDevice device, IRhiTexture target)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "frame.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(target),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The frame never came back.");

        ReadOnlySpan<byte> padded = data.GetData();
        byte[] pixels = new byte[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            padded.Slice(y * row, Size * 4).CopyTo(pixels.AsSpan(y * Size * 4));
        }

        return pixels;
    }

    private static int Centre => ((((Size / 2) * Size) + (Size / 2)) * 4);

    private static string Describe(byte[] pixels) => Describe(pixels, Size / 2, Size / 2);

    private static string Describe(byte[] pixels, int x, int y)
    {
        int at = (((y * Size) + x) * 4);
        return $"b{pixels[at]} g{pixels[at + 1]} r{pixels[at + 2]}";
    }

    // The sample mod's compiled greyscale, registered once a process as the game registers a mod's module.
    private static readonly Lazy<ShaderModuleId> SampleGreyscale = new(static () =>
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "OpenIGI.Modding.SampleMod", "shaders", "Generated");
            if (Directory.Exists(candidate))
            {
                return ShaderCatalog.Register(ExternalShaderModule.Read(candidate, "Greyscale"));
            }
        }

        throw new DirectoryNotFoundException("The sample mod's compiled shaders are not above the tests.");
    });

    // How many pixels have colour: channels further apart than a rounding.
    private static int Colourful(byte[] pixels)
    {
        int coloured = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int high = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
            int low = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
            if (high - low > 2)
            {
                coloured++;
            }
        }

        return coloured;
    }
}

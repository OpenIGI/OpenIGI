using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Rendering;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Traces a level once and records what each probe in an <see cref="IrradianceVolume"/> can see.
/// </summary>
/// <remarks>
/// <para>
/// The whole cost of a probe volume is the geometric question — what can this point see — and in this
/// game that answer never changes. The levels are static and each mission's sun is a fixed direction,
/// so the transfer is traced here, once, and relighting afterwards is a multiply by whatever colour
/// the sun and sky are that frame.
/// </para>
/// <para>
/// Rays go out on a Fibonacci sphere rather than a random or axis-aligned set. Random directions clump,
/// which shows up as probes that disagree with their neighbours for no reason the geometry explains;
/// axis-aligned ones miss the diagonals that matter most in a corridor. The Fibonacci spiral is the
/// cheapest construction that spaces points near-evenly on a sphere, and it is deterministic, which
/// matters because a probe bake that differs between runs is a level that lights differently each time
/// it loads.
/// </para>
/// </remarks>
public static class IrradianceProbeBaker
{
    private const string LogCategory = "GI";

    /// <summary>How many directions each probe is asked about.</summary>
    /// <remarks>
    /// Enough to resolve a doorway, few enough that a level of several thousand probes bakes in the
    /// time a level already takes to load. The result is first-order spherical harmonics, which cannot
    /// represent more detail than this anyway.
    /// </remarks>
    public const int RaysPerProbe = 64;

    /// <summary>How many times the light is allowed to bounce through the volume.</summary>
    /// <remarks>
    /// One bounce is a surface lit by the sun and the sky and nothing else, which leaves a room the sun
    /// never reaches almost dark -- the light that fills such a room is what has already reflected two,
    /// three, four times off its walls. Each extra pass re-traces every probe reading the light the last
    /// pass left on the geometry, so the series converges on the true multi-bounce answer. It is a
    /// one-time cached bake, so bounces cost nothing at runtime and the count is set for convergence, not
    /// speed. With the feedback carried at the surface's re-emitted radiance (see
    /// <see cref="IndirectFeedbackScale"/>) the per-bounce gain is albedo times how much of the gather the
    /// geometry catches, which is below one, so the light settles geometrically: at the grey retail albedo
    /// (~0.4-0.5) an enclosed room is within a per cent of its limit by the fourth pass. Five leaves a
    /// pass of headroom for a brighter-walled room without tracing the whole grid twice for a gain no
    /// surface still shows. This is the difference between an interior that reads lit and one that reads
    /// like a cave, and it is why the sun through a window fills the room rather than only its floor.
    /// </remarks>
    public const int Bounces = 5;

    /// <summary>How far, in metres, a probe looks before calling a direction open sky.</summary>
    public const float MaxTraceMetres = 30f;

    /// <summary>The most probes a bake will place, however large the level or fine the spacing.</summary>
    /// <remarks>
    /// A backstop rather than a target. The spacing is what should decide the count; this only stops a
    /// mistyped spacing from asking for a hundred million traces and appearing to hang.
    /// </remarks>
    public const int MaxProbes = 150000;

    /// <summary>How far above the level's ground the volume reaches, in metres.</summary>
    public const double VerticalReachMetres = 40.0;

    /// <summary>Bakes a volume covering a level.</summary>
    /// <param name="scene">The level, for its bounds.</param>
    /// <param name="cast">
    /// How to ask the world what a ray meets, in <em>engine units</em> — the level's objects and its
    /// terrain together, since a probe under a hill is as shut in as one inside a building.
    /// </param>
    /// <param name="sunDirection">The direction light arrives from, pointing towards the sun.</param>
    /// <param name="spacing">The distance between probes, in metres.</param>
    /// <param name="groundAlbedo">
    /// What the level bounces, per channel. One number for the whole level: the alternative is reading
    /// each hit's own material, which needs the ray to report a texture rather than a triangle.
    /// </param>
    /// <param name="logger">Where to report the bake's size and duration.</param>
    /// <param name="onBouncePass">
    /// Called with a copy of the field after each bounce pass, so a caller can show the light building up
    /// as it bakes rather than only the finished result. Null to ignore the intermediate passes.
    /// </param>
    /// <param name="work">
    /// Where the rows are traced: all at once on a scheduler that runs work concurrently, one a slice on
    /// one that does not.
    /// </param>
    /// <param name="done">
    /// Given the baked volume, or <see langword="null"/> when the level has nothing to bake around.
    /// </param>
    /// <param name="cancellationToken">
    /// Asks a bake in progress to stop — a level change or a shutdown while
    /// <see cref="IrradianceProbeBake"/> is tracing.
    /// </param>
    /// <returns>The bake, a slice at a time; see <see cref="SteppedJob{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<bool> Bake(
        WorldScene scene,
        WorldRayCast cast,
        Vector3f sunDirection,
        float spacing,
        Vector3f groundAlbedo,
        IOpenIgiLogger? logger,
        Action<IrradianceVolume>? onBouncePass,
        IWorkScheduler work,
        Action<IrradianceVolume?> done,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(cast);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(done);

        return BakeSlices(
            scene, cast, sunDirection, spacing, groundAlbedo, logger ?? NullLogger.Instance, onBouncePass, work, done, cancellationToken);
    }

    /// <summary>The bake itself, checked by <see cref="Bake"/> before it is handed out.</summary>
    private static IEnumerable<bool> BakeSlices(
        WorldScene scene,
        WorldRayCast cast,
        Vector3f sunDirection,
        float spacing,
        Vector3f groundAlbedo,
        IOpenIgiLogger log,
        Action<IrradianceVolume>? onBouncePass,
        IWorkScheduler work,
        Action<IrradianceVolume?> done,
        CancellationToken cancellationToken)
    {
        if (scene.Placements.Count == 0 || spacing <= 0f)
        {
            done(null);
            yield break;
        }

        if (!TryMeasure(scene, ref spacing, out Vector3f origin, out int countX, out int countY, out int countZ))
        {
            log.Warning(LogCategory, "The level's bounds gave no room for a probe volume.");
            done(null);
            yield break;
        }

        IrradianceVolume volume = new(origin, spacing, countX, countY, countZ);
        Vector3f[] directions = FibonacciSphere(RaysPerProbe);

        log.Information(
            LogCategory,
            $"Baking {volume.ProbeCount} probes ({countX}x{countY}x{countZ}) at {spacing:0.#} m spacing.");

        // A row per piece of work. Every probe writes only its own span of the volume and the cast reads
        // a collision index nothing is mutating, so there is no sharing to guard -- and the bake is the
        // one part of loading a level that is embarrassingly parallel, which is what makes a spacing
        // fine enough to resolve a room affordable at all. Rows rather than whole slices of the grid, so
        // that a host tracing them a slice of a frame at a time has pieces small enough to fit.
        // One core is left alone. The bake used to have the machine to itself because it blocked the
        // frame loop; now that it does not, saturating every core makes the frames it no longer blocks
        // stutter instead -- and a level that loads smoothly matters more than a field that arrives a
        // second sooner. Measured on this machine: 2.9 s inline against 5.7 s unrestrained beside a
        // live renderer, for a bake nobody is waiting on.
        int parallelism = Math.Max(1, Environment.ProcessorCount - 1);

        // One pass per bounce. The first reads no previous light (a surface lit by sun and sky alone);
        // each after it reads the light the last pass left on the geometry, so a further bounce arrives
        // and the series converges on the true multi-bounce answer. The previous pass is a snapshot the
        // whole grid reads while this pass overwrites the live volume, so the reads never race the writes.
        for (int bounce = 0; bounce < Math.Max(Bounces, 1); bounce++)
        {
            IrradianceVolume? previous = bounce == 0 ? null : volume.Clone();

            // Published partway through the pass, not only at its end, so the field the game shows updates
            // many times a second and the light resolves in CONTINUOUSLY rather than jumping once per pass.
            // A pass takes a few seconds to trace every probe; without this the room sat unchanged for all
            // of it and then stepped. Every Nth slice a worker finishes hands over a snapshot of the volume
            // so far -- on the first pass the un-traced slices still read dark, so the light sweeps across
            // the level as it fills. The render thread only ever uploads the newest snapshot (a version
            // counter), so publishing far more often than it can draw costs nothing.
            // Counted in ROWS rather than whole slices: a level is only a handful of probes deep, so a
            // slice is a big chunk of the pass and all the threads finish theirs at once -- publishing per
            // slice would hand over two or three bursts, not a steady stream. A row is fine enough that
            // whichever thread crosses the next multiple hands over a snapshot, and they come in evenly.
            int rowsDone = 0;
            int publishEvery = Math.Max(1, countY * countZ / PublishesPerPass);

            IEnumerable<bool> rows = Stepping.Range(
                work,
                countY * countZ,
                parallelism,
                row =>
                {
                    // Below the render thread, so the bake -- which runs off the main thread but still on
                    // the machine's other cores -- yields to a frame that needs a core rather than starving
                    // it. The level lights up a little slower; the frame it lights up over does not freeze.
                    // Set once per worker (the guard skips the syscall after the first row a pooled thread
                    // runs), and only where the rows run beside the frame at all.
                    if (work.IsConcurrent
                        && System.Threading.Thread.CurrentThread.Priority == System.Threading.ThreadPriority.Normal)
                    {
                        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                    }

                    int z = row / countY;
                    int y = row % countY;
                    float[] coefficients = new float[IrradianceVolume.CoefficientsPerProbe];

                    for (int x = 0; x < countX; x++)
                    {
                        TraceProbe(
                            volume,
                            volume.PositionOf(x, y, z),
                            directions,
                            cast,
                            sunDirection,
                            groundAlbedo,
                            volume.IndexOf(x, y, z),
                            coefficients,
                            previous);
                    }

                    // A copy each time: the snapshot is read on the render thread while later rows are
                    // still being written here, and only a distinct clone is safe to hand across. Cheap
                    // beside the tracing, and the render thread drops all but the latest anyway.
                    if (onBouncePass is not null && Interlocked.Increment(ref rowsDone) % publishEvery == 0)
                    {
                        onBouncePass(volume.Clone());
                    }
                },
                cancellationToken);

            foreach (bool slice in rows)
            {
                yield return slice;
            }

            // And the finished pass, always -- the partial publishes above skip the tail below the last
            // whole multiple of publishEvery, and the final pass's clone is the one the cache is written
            // from. A copy, because the next pass overwrites the live volume in place.
            onBouncePass?.Invoke(volume.Clone());
        }

        log.Information(LogCategory, $"Probe bake complete ({Bounces} bounce(s)).");
        done(volume);
    }

    /// <summary>Works out where the volume sits and how many probes it needs.</summary>
    private static bool TryMeasure(
        WorldScene scene,
        ref float spacing,
        out Vector3f origin,
        out int countX,
        out int countY,
        out int countZ)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (WorldPlacement placement in scene.Placements)
        {
            minX = Math.Min(minX, placement.Position.X);
            minY = Math.Min(minY, placement.Position.Y);
            maxX = Math.Max(maxX, placement.Position.X);
            maxY = Math.Max(maxY, placement.Position.Y);
        }

        // Vertically the volume is anchored to the ground rather than to the level's extremes. A radio
        // mast or a mountain peak would otherwise stretch it over hundreds of metres of empty air and
        // spend the whole probe budget on sky.
        // Anchored to the ground rather than to the level's vertical extremes, and generous upwards:
        // a radio mast or a mountain peak would otherwise stretch the volume over hundreds of metres of
        // empty air and spend the whole probe budget on sky, while a ceiling three storeys up still has
        // to have probes above it or the top floor is lit by the floor below.
        double ground = scene.GroundHeight;
        double minZ = ground - (spacing * 2.0);
        double maxZ = ground + VerticalReachMetres;

        countX = (int)Math.Ceiling((maxX - minX) / spacing) + 1;
        countY = (int)Math.Ceiling((maxY - minY) / spacing) + 1;
        countZ = (int)Math.Ceiling((maxZ - minZ) / spacing) + 1;

        // Coarsened rather than truncated when the level is too big for the budget: a volume that
        // covers half a level is worse than a coarser one that covers all of it, because the half it
        // misses is unlit rather than approximately lit.
        // Coarsened rather than truncated when the level is too big for the budget: a volume covering
        // half a level is worse than a coarser one covering all of it, because the half it misses is
        // unlit rather than approximately lit. The spacing goes back to the caller with the counts,
        // because a volume built with halved counts and the spacing that was asked for covers a
        // quarter of the level and believes it covers all of it.
        while ((long)countX * countY * countZ > MaxProbes)
        {
            spacing *= 2f;
            countX = (int)Math.Ceiling((maxX - minX) / spacing) + 1;
            countY = (int)Math.Ceiling((maxY - minY) / spacing) + 1;
            countZ = (int)Math.Ceiling((maxZ - minZ) / spacing) + 1;
        }

        origin = new Vector3f((float)minX, (float)minY, (float)minZ);
        return countX >= 1 && countY >= 1 && countZ >= 1;
    }

    /// <summary>Asks one probe what it can see, and records it.</summary>
    private static void TraceProbe(
        IrradianceVolume volume,
        Vector3f position,
        Vector3f[] directions,
        WorldRayCast cast,
        Vector3f sunDirection,
        Vector3f groundAlbedo,
        int probe,
        Span<float> coefficients,
        IrradianceVolume? previous)
    {
        coefficients.Clear();

        Vector3d origin = new(
            position.X * WorldScene.UnitsPerMetre,
            position.Y * WorldScene.UnitsPerMetre,
            position.Z * WorldScene.UnitsPerMetre);

        double reach = MaxTraceMetres * WorldScene.UnitsPerMetre;
        float distanceSum = 0f;
        float distanceSquaredSum = 0f;

        // Zero for a level whose sun is below the horizon, which skips the shadow rays entirely rather
        // than tracing towards nowhere.
        float sunlit = sunDirection.LengthSquared > 1e-6f ? SunlitBounce : 0f;

        foreach (Vector3f direction in directions)
        {
            Vector3d end = new(
                origin.X + (direction.X * reach),
                origin.Y + (direction.Y * reach),
                origin.Z + (direction.Z * reach));

            Vector3f arriving;
            float distance;

            if (cast(origin, end, out WorldRayHit hit))
            {
                distance = (float)(hit.Distance / WorldScene.UnitsPerMetre);

                // A blocked direction is not black. It is a surface, and that surface is lit by what it
                // can see and bounces a share of it onwards -- which is the single bounce this volume
                // carries, and the reason an interior with no window is dim rather than void.
                float exposure = 1f - (distance / MaxTraceMetres);

                // And a surface standing in the sun bounces far more than one in shadow, which is
                // where sunlight enters the INDIRECT term. It has to enter here rather than as a lobe
                // pointed at the sun: the lighting pass adds the direct, shadow-mapped sun itself, so
                // a probe carrying the sun as well counts it twice -- which is what made sunlit ground
                // blow out.
                if (sunlit > 0f)
                {
                    Vector3d hitPoint = new(
                        hit.Position.X + (hit.Normal.X * SunBiasInUnits),
                        hit.Position.Y + (hit.Normal.Y * SunBiasInUnits),
                        hit.Position.Z + (hit.Normal.Z * SunBiasInUnits));

                    Vector3d towardsSun = new(
                        hitPoint.X + (sunDirection.X * reach),
                        hitPoint.Y + (sunDirection.Y * reach),
                        hitPoint.Z + (sunDirection.Z * reach));

                    if (!cast(hitPoint, towardsSun, out _))
                    {
                        exposure *= sunlit;
                    }
                }

                // The further bounce: the surface this ray landed on is itself lit by the light the last
                // pass left on the volume, and it reflects a share of that onwards too. Sampled at the hit,
                // pushed a little off the surface along its normal so the read is of the room the surface
                // faces rather than of the wall it is part of. Null on the first pass, when there is no
                // previous light to gather -- that pass is the sun-and-sky-only bounce this one builds on.
                Vector3f indirect = default;

                if (previous is not null)
                {
                    float biasMetres = (float)(SunBiasInUnits / WorldScene.UnitsPerMetre);
                    Vector3f hitMetres = new(
                        (float)(hit.Position.X / WorldScene.UnitsPerMetre) + (hit.Normal.X * biasMetres),
                        (float)(hit.Position.Y / WorldScene.UnitsPerMetre) + (hit.Normal.Y * biasMetres),
                        (float)(hit.Position.Z / WorldScene.UnitsPerMetre) + (hit.Normal.Z * biasMetres));

                    // Brought back to the radiance the surface actually re-emits before it is bounced on.
                    // The stored field is at the SH projection's scale: Evaluate reconstructs c0 without
                    // the Y0 basis constant that Project multiplied in, so a uniform field reads back 2*sqrt(pi)
                    // -- about 3.5 -- times its radiance. Feeding that straight back multiplies the light by
                    // that factor on EVERY bounce, and an enclosed room (where nearly every gather ray lands
                    // on another lit surface) then runs away brighter each pass instead of settling. The
                    // factor belongs to storage, not to transport; dividing it out by the same Y0 leaves a
                    // per-bounce gain of albedo times geometry, which loses energy each bounce and converges.
                    indirect = previous.SampleIrradiance(hitMetres, hit.Normal) * IndirectFeedbackScale;
                }

                arriving = new Vector3f(
                    groundAlbedo.X * (exposure + indirect.X),
                    groundAlbedo.Y * (exposure + indirect.Y),
                    groundAlbedo.Z * (exposure + indirect.Z));
            }
            else
            {
                distance = MaxTraceMetres;

                // Open. The channel weights are 1 because the sky's own colour is applied per frame;
                // what is stored is how much of the sky reaches here, not what colour it is.
                arriving = new Vector3f(1f, 1f, 1f);
            }

            distanceSum += distance;
            distanceSquaredSum += distance * distance;

            Project(coefficients, direction, arriving);
        }

        float scale = 4f * MathF.PI / directions.Length;

        for (int i = 0; i < coefficients.Length; i++)
        {
            coefficients[i] *= scale;
        }

        volume.SetProbe(
            probe,
            coefficients,
            distanceSum / directions.Length,
            distanceSquaredSum / directions.Length);
    }

    /// <summary>How much more a sunlit surface bounces than a sky-lit one.</summary>
    /// <remarks>
    /// The sun is far brighter than the sky it shares the hemisphere with, so a wall in sunlight is a
    /// far stronger secondary source than the same wall in shade. This is the only route by which
    /// sunlight reaches the indirect term, and it has to be the only one: the lighting pass adds the
    /// direct sun itself, shadow-mapped, so a probe that also carried a lobe pointed at the sun would
    /// count it twice.
    /// </remarks>
    private const float SunlitBounce = 3.5f;

    /// <summary>Undoes the SH projection scale when reading light back for a further bounce.</summary>
    /// <remarks>
    /// The zeroth SH basis constant, <c>Y0 = 0.282095</c>. <see cref="IrradianceVolume.Evaluate"/> omits it,
    /// so a gathered value comes back at <c>2*sqrt(pi)</c> times the radiance it represents; multiplying by
    /// <c>Y0</c> cancels that, which is what stops the bounce feedback from compounding a factor of ~3.5 per
    /// pass and running the light away in enclosed rooms.
    /// </remarks>
    private const float IndirectFeedbackScale = 0.282095f;

    /// <summary>How many times a single bounce pass hands over its progress before it finishes.</summary>
    /// <remarks>
    /// The pass publishes a snapshot roughly this many times, spread across the rows it traces, so the field
    /// the game shows advances steadily over the pass rather than once at its end -- enough that the light
    /// reads as resolving in continuously. The render thread coalesces to the newest, so a larger number
    /// only smooths the fill; it does not cost an upload each time.
    /// </remarks>
    private const int PublishesPerPass = 24;

    /// <summary>How far off a surface a shadow ray starts, in engine units.</summary>
    /// <remarks>
    /// Without it every shadow ray begins exactly on the surface it is testing and immediately hits
    /// it, so nothing is ever found to be in sunlight.
    /// </remarks>
    private const double SunBiasInUnits = 64.0;

    /// <summary>Adds one direction's arriving light into the first-order harmonics.</summary>
    /// <remarks>
    /// The four basis functions of L1 are a constant and the three axes, with the constants folded in
    /// so that evaluation is a plain dot product rather than a dot product and three multiplies.
    /// </remarks>
    private static void Project(Span<float> coefficients, Vector3f direction, Vector3f value)
    {
        const float Constant = 0.282095f;
        const float Linear = 0.488603f;

        Span<float> basis =
        [
            Constant,
            Linear * direction.X,
            Linear * direction.Y,
            Linear * direction.Z,
        ];

        for (int channel = 0; channel < 3; channel++)
        {
            float amount = channel switch
            {
                0 => value.X,
                1 => value.Y,
                _ => value.Z,
            };

            for (int i = 0; i < 4; i++)
            {
                coefficients[(channel * 4) + i] += basis[i] * amount;
            }
        }
    }

    /// <summary>Spaces points near-evenly over a sphere.</summary>
    /// <param name="count">How many directions to produce.</param>
    /// <returns>The directions, unit length.</returns>
    /// <remarks>
    /// The golden-angle spiral: each successive point is turned by the most irrational fraction of a
    /// turn there is, which is precisely the property that stops the points ever falling into rows.
    /// </remarks>
    public static Vector3f[] FibonacciSphere(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        Vector3f[] directions = new Vector3f[count];
        float goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

        for (int i = 0; i < count; i++)
        {
            float z = 1f - ((2f * i) + 1f) / count;
            float radius = MathF.Sqrt(MathF.Max(1f - (z * z), 0f));
            float theta = goldenAngle * i;

            directions[i] = new Vector3f(MathF.Cos(theta) * radius, MathF.Sin(theta) * radius, z);
        }

        return directions;
    }
}

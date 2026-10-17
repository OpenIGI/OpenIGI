using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What the world's effects read of the host: the player's marks and walls, the materials, and the eye aimed with.</summary>
internal interface IWorldEffectsHost
{
    /// <summary>Gets whether a player stands in the level, whose marks and walls the effects use.</summary>
    bool HasPlayer { get; }

    /// <summary>Gets what a droplet or a dust puff stops against, or <see langword="null"/> while no level is up.</summary>
    WorldRayCast? Walls { get; }

    /// <summary>Gets the decals the marks go into, or <see langword="null"/> while no player stands.</summary>
    BulletMarks? Marks { get; }

    /// <summary>Gets the probe a mark checks the surface under it with.</summary>
    WorldRayCast? MarkProbe { get; }

    /// <summary>Gets the level's surface materials, once loaded.</summary>
    GameMaterialRegistry? Materials { get; }

    /// <summary>Gets what the world's trace finds, for naming the object a mark sticks to.</summary>
    WorldTraceCast? Trace { get; }

    /// <summary>Gets whether the player's profile wants blood.</summary>
    bool BloodWanted { get; }

    /// <summary>Gets the camera the frame is drawn from, which the test commands aim with.</summary>
    Camera3D Eye { get; }
}

/// <summary>
/// The world's effects: the impact bursts, the original's own dust, chips and blood, the fireball, and the smoke rockets and
/// blasts leave -- loaded with a mission, stepped on the fixed tick, drawn over the lit scene, and released with it.
/// </summary>
/// <param name="device">The device the effects are uploaded to and drawn with.</param>
/// <param name="logger">Where load problems and the test commands report.</param>
/// <param name="random">The stream the puffs' drift and life draw from, shared with the rest of the frame's effects.</param>
/// <param name="host">The player's marks and walls, the materials, and the eye.</param>
internal sealed class WorldEffects(IGraphicsDevice device, IOpenIgiLogger logger, IgiRandom random, IWorldEffectsHost host) : IHostService
{
    /// <summary>The missile's scorch half-width: <c>0x476AC0</c> hands <c>Explosion0</c> 6144.</summary>
    public const float MissileScorchHalfWidthInUnits = 6144f;

    /// <summary>The M203 round's scorch half-width: <c>0x47647B</c> hands <c>Explosion0</c> 4096.</summary>
    public const float M203ScorchHalfWidthInUnits = 4096f;

    /// <summary>Gets the bursts where rounds land, and the procedural explosions a mission without the fireball falls back to.</summary>
    public ImpactParticleRenderer? Particles { get; private set; }

    /// <summary>Gets the original's impact effects as they move: the dust and chips, and the blood.</summary>
    /// <remarks>Stepped on the fixed tick beside the particles and drawn by the title's impact presenter.</remarks>
    public ImpactEffectRuntime Impacts { get; } = new();

    private IImpactEffectPresenter? _retailImpacts;
    private ExplosionEffectRenderer? _explosions;
    private SmokeSpriteRenderer? _smoke;

    /// <inheritdoc/>
    public string Name => "World effects";

    /// <summary>Loads a mission's effects.</summary>
    /// <param name="mission">The mission, whose packs hold the fireball and chip models.</param>
    /// <param name="presenters">The title's presenters, which draw its impact effects.</param>
    /// <param name="resources">The mission's resources.</param>
    /// <param name="fileSystem">The game's files, which hold the effect sprites.</param>
    public void Load(LoadedMission mission, PresenterRegistry presenters, ResourceRegistry resources, VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(presenters);

        // The bursts where rounds land. Cheap when off: r_impact_particles 0 spawns nothing, and an empty pool
        // returns at the count check.
        Particles?.Dispose();
        Particles = new ImpactParticleRenderer(device, resources, fileSystem, MissionLoader.CommonSpritesArchive);
        if (!Particles.Load(mission))
        {
            logger.Warning("Impact", "The retail IGI1 explosion model '1009_01_1' was not bound; using the sprite fallback.");
        }

        // The original's own impact effects: concrete1.spr dust, the material's own chip model, and blood1/blood2
        // for a body. The runtime keeps the motion; this only draws it. Without it the blood is still thrown and
        // still stamps its splashes, but no drop or mist is seen.
        _retailImpacts?.Dispose();
        _retailImpacts = presenters.Create<ImpactEffectPresentationRequest, IImpactEffectPresenter>(
            new ImpactEffectPresentationRequest(device, resources, fileSystem));
        int impactModels = _retailImpacts?.Load(mission) ?? 0;
        Impacts.Clear();
        Impacts.SetMistFrameCount(_retailImpacts?.MistFrameCount ?? 1);
        if (_retailImpacts is not { SpritesAvailable: true } impacts)
        {
            logger.Warning(
                "Impact",
                "concrete1.spr, blood1.spr or blood2.spr did not resolve; the original's impact dust and blood will not draw.");
        }
        else
        {
            logger.Debug(
                "Impact",
                $"Retail impact effects bound: {impactModels} chip model(s), {impacts.MistFrameCount} blood frame(s).");
        }

        // The retail 1009_01_1 fireball, shared by every blast in the level. Loads its real texture lazily on
        // first blast; falls back to the procedural burst without it.
        _explosions?.Dispose();
        _explosions = new ExplosionEffectRenderer(device, fileSystem, MissionLoader.CommonSpritesArchive, logger);

        // The retail smoke2.spr trail behind rockets and the grey column a blast leaves.
        _smoke?.Dispose();
        _smoke = new SmokeSpriteRenderer(device, fileSystem, MissionLoader.CommonSpritesArchive, logger);
    }

    /// <summary>Frees a mission's effects, as it goes.</summary>
    public void Release()
    {
        Particles?.Dispose();
        Particles = null;
        _retailImpacts?.Dispose();
        _retailImpacts = null;
        Impacts.Clear();
        _explosions?.Dispose();
        _explosions = null;
        _smoke?.Dispose();
        _smoke = null;
    }

    /// <summary>Forgets the effects without freeing them, at a session's start.</summary>
    public void Forget()
    {
        Particles = null;
        _retailImpacts = null;
        Impacts.Clear();
        _explosions = null;
        _smoke = null;
    }

    /// <summary>Steps the bursts, the fireballs and the smoke one tick.</summary>
    public void Tick()
    {
        Particles?.Tick();
        _explosions?.Tick();
        _smoke?.Tick();
    }

    /// <summary>Draws the effects over the lit scene, in the order they composite.</summary>
    /// <param name="eye">The camera.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last two ticks.</param>
    public void Draw(in Camera3D eye, in Matrix4f projection, float blend)
    {
        // The impact bursts: dust hazes over the scene, spark light over the haze.
        Particles?.Draw(eye, projection, blend);

        // And the original's own: concrete1.spr dust, the chips it knocked loose, and blood.
        if (RenderCVars.ImpactEffects.Value)
        {
            _retailImpacts?.Draw(Impacts, eye, projection, blend);
        }

        // The rocket's smoke trail and the blast's grey column, under the fireball.
        _smoke?.Draw(eye, projection, blend);

        // The retail fireball, over the scene as light: grenade, missile and shot-out-device blasts drawn from the
        // real fireball.spr sprite.
        _explosions?.Draw(eye, projection, blend);
    }

    /// <summary>Steps the original's impact effects, and lets its blood mark what it lands on.</summary>
    /// <remarks>
    /// The droplet traces are the reason this needs the world: <c>0x4640B0</c> is a collision callback, so where a droplet
    /// stops is where the splash is stamped. The same <see cref="BulletMarks"/> the bullet holes go into takes them, which
    /// is what makes them fade out of the world together.
    /// </remarks>
    public void TickRetailImpacts()
    {
        if (!host.HasPlayer)
        {
            return;
        }

        Impacts.BloodEnabled = RenderCVars.ImpactEffects.Value && host.BloodWanted;
        Impacts.Tick(host.Walls, host.Marks, host.Materials);
    }

    /// <summary>Runs the struck material's own <c>GunShotEffect</c> for whatever landed this tick.</summary>
    /// <param name="landed">This tick's impacts: the instant rays and the ballistic arrivals alike.</param>
    /// <remarks>
    /// <para>
    /// A contact that met a human is deliberately skipped: <c>0x438458</c> asks the struck object whether it is a Human
    /// first and drops both the dust and the chips when it is -- a body throws blood instead, which the soldier and player
    /// damage paths raise directly.
    /// </para>
    /// <para>
    /// The <b>entry</b> block is the one consulted, because that is the block the shot march (<c>0x47B750</c>) dispatches
    /// for a hit on world geometry; the second block, which every retail row leaves flagless, is for the far side and
    /// produces only its mark.
    /// </para>
    /// </remarks>
    public void SpawnSurfaceImpacts(IEnumerable<GunImpact> landed)
    {
        ArgumentNullException.ThrowIfNull(landed);
        if (host.Materials is not { } materials || !RenderCVars.ImpactEffects.Value)
        {
            return;
        }

        foreach (GunImpact impact in landed)
        {
            // A knife records no impact (0x46DFB0), so it never reaches GunShotEffect's dust.
            if (!impact.HitSomething || impact.StruckHuman || impact.Melee)
            {
                continue;
            }

            if (!materials.TryGet(impact.Wall.Material, out GameMaterial? material) || material is null || !material.RecordsImpacts)
            {
                continue;
            }

            Impacts.SpawnSurfaceImpact(impact.Wall.Position, impact.Wall.Normal, impact.Direction, material.Entry, struckHuman: false);
        }
    }

    /// <summary>Throws one wound's blood, as <c>Human</c>'s damage handler does before anything else.</summary>
    /// <param name="owner">The body -- its two systems are kept against this and reused.</param>
    /// <param name="position">Where the round entered, in world units.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <returns>What the blood met behind the wound.</returns>
    /// <remarks>
    /// <c>0x45F94A</c> calls the blood <b>first</b>, before the invulnerability and instant-death gates and before any damage
    /// is applied, so a round that does nothing to a body still makes it bleed. Callers therefore raise this on contact,
    /// not on damage.
    /// </remarks>
    public BloodReport SpawnBlood(object owner, Vector3d position, Vector3f direction)
    {
        if (!host.HasPlayer || !RenderCVars.ImpactEffects.Value)
        {
            return default;
        }

        Impacts.BloodEnabled = host.BloodWanted;
        return Impacts.SpawnBlood(owner, position, direction, host.Walls, host.Marks, host.Materials);
    }

    /// <summary>Shows one blast's fireball: the real retail explosion when its texture is loaded.</summary>
    /// <param name="positionUnits">The blast centre, in world units.</param>
    /// <param name="intensity">The size and count scale; one is the grenade/missile fireball.</param>
    /// <remarks>
    /// The retail <c>1009_01_1</c> fireball is shared by every blast in the level. It is drawn from the model's own real
    /// texture when the mounted packs hold it; a mission without it falls back to the procedural burst so a blast is never
    /// invisible.
    /// </remarks>
    public void SpawnBlast(Vector3d positionUnits, float intensity)
    {
        // The pale grey puff a blast leaves, the retail ExplosionSmoke task's smoke2.spr. A few small translucent
        // puffs, not a screen-filling tower -- the size is fixed, not scaled by the particle cvar.
        if (_smoke is not null && _smoke.IsAvailable)
        {
            const int puffs = 3;
            for (int i = 0; i < puffs; i++)
            {
                Vector3f drift = new(
                    ((float)random.NextDouble() - 0.5f) * 140f,
                    ((float)random.NextDouble() - 0.5f) * 140f,
                    ((float)random.NextDouble() * 60f) + 20f);
                _smoke.SpawnPuff(
                    positionUnits,
                    drift,
                    sizeMetres: 1.0f + ((float)random.NextDouble() * 0.4f),
                    growthMetresPerTick: 0.03f,
                    lifeTicks: 28 + (int)random.NextBounded(14),
                    darkness: 0.45f);
            }
        }

        if (_explosions is not null && _explosions.Spawn(positionUnits, intensity))
        {
            return;
        }

        Particles?.SpawnExplosion(positionUnits, intensity);
    }

    /// <summary>Shows the flashbang's world smoke residue without drawing a screen whiteout.</summary>
    /// <param name="positionUnits">Where it went off, in world units.</param>
    /// <remarks>
    /// The native flash task registers <c>ExplosionSmoke</c> and uses <c>smoke2.spr</c>. Keep the smoke in the scene so it
    /// is occluded and distance-faded like the retail effect; the separate player handler owns the distance-based whiteout.
    /// </remarks>
    public void SpawnFlashbangSmoke(Vector3d positionUnits)
    {
        if (_smoke is not null && _smoke.IsAvailable)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3f drift = new(
                    ((float)random.NextDouble() - 0.5f) * 90f,
                    ((float)random.NextDouble() - 0.5f) * 90f,
                    18f + ((float)random.NextDouble() * 35f));
                _smoke.SpawnPuff(
                    positionUnits,
                    drift,
                    sizeMetres: 0.65f + ((float)random.NextDouble() * 0.2f),
                    growthMetresPerTick: 0.025f,
                    lifeTicks: 34 + (int)random.NextBounded(12),
                    darkness: 0.2f);
            }

            return;
        }

        // Keep the effect visible on a mission whose common sprite archive is incomplete. This is a world particle
        // fallback, never the old full-screen flash.
        Particles?.SpawnRocketSmoke(positionUnits, Vector3f.Zero, intensity: 1f);
    }

    /// <summary>Burns the retail <c>burn1.tex</c> scorch onto the object a blast struck.</summary>
    /// <param name="positionUnits">The impact point, in world units.</param>
    /// <param name="normal">The struck surface's normal; up when the impact recorded none.</param>
    /// <param name="halfWidthInUnits">The scorch's half-width, as the launcher passes it.</param>
    /// <remarks>
    /// <c>Explosion0</c> (<c>0x4811C9</c>) sends <c>burn1.tex</c> (<c>dword_5C1164</c>) to the struck object through the same
    /// decal message a bullet hole uses, so it lands only on an object that takes marks: never on bare terrain, never on a
    /// body. Here it joins the world's decals, drawn by the bullet-mark renderer, so it follows the same
    /// <c>r_bullet_marks</c> switch.
    /// </remarks>
    public void Scorch(Vector3d positionUnits, Vector3f normal, float halfWidthInUnits)
    {
        if (!host.HasPlayer || host.Marks is not { } marks)
        {
            return;
        }

        Vector3f n = normal;
        float length = MathF.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
        n = length < 1e-4f ? Vector3f.UnitZ : n * (1f / length);

        // Lift the scorch clear of the surface so it does not fight it for depth.
        const float liftUnits = 20.48f;
        Vector3d at = new(
            positionUnits.X + (n.X * liftUnits),
            positionUnits.Y + (n.Y * liftUnits),
            positionUnits.Z + (n.Z * liftUnits));

        if (MarkSurfaceAt(positionUnits, n, out WorldPlacement? placement) is not { } surface)
        {
            return;
        }

        // The name carries its .tex so the bullet-mark renderer's common-textures candidate matches the exact entry
        // burn1.tex is registered under (common textures, not the level's).
        marks.AddMark(at, n, "burn1.tex", halfWidthInUnits, surface, placement);
    }

    /// <summary>Lays the exhaust glow and smoke trail for every rocket still in the air.</summary>
    /// <param name="simulations">The rockets: the player's launcher's and the vehicles'.</param>
    /// <remarks>
    /// While the motor burns the rocket carries a nozzle flare (the visible half of the engine light at
    /// <c>0x004CABF0</c>) and drops a smoke puff each tick (<c>0x00428630</c>). A rocket crosses several metres a tick, so a
    /// single puff would dash; the segment it travelled is filled instead, giving the continuous trail the LAW 80 and RPG-18
    /// leave. Presentation only, on the fixed tick.
    /// </remarks>
    public void SpawnMissileTrails(params ReadOnlySpan<MissileSimulation> simulations)
    {
        bool haveSprite = _smoke is not null && _smoke.IsAvailable;
        float intensity = Math.Max(1f, RenderCVars.ImpactParticles.Value);
        foreach (MissileSimulation simulation in simulations)
        {
            SpawnMissileTrail(simulation, haveSprite, intensity);
        }
    }

    /// <summary>Registers the commands that fire the original's impact effects by hand.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// Diagnosis, and a capture handle: a headless run can put a dust puff, a spray of chips or a wound exactly where the
    /// crosshair points and photograph it, which firing a weapon on a synthetic clock cannot do reproducibly.
    /// </remarks>
    public void Register(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Commands.Add(
            "impact_test",
            "Runs the struck material's own GunShotEffect where the crosshair points.",
            context =>
            {
                if (!TryAimedSurface(out WorldRayHit hit, out Vector3f look))
                {
                    context.Warn("Nothing under the crosshair, or no level is loaded.");
                    logger.Warning("Impact", "impact_test: nothing under the crosshair.");
                    return;
                }

                if (host.Materials is not { } materials
                    || !materials.TryGet(hit.Material, out GameMaterial? material)
                    || material is null)
                {
                    context.Warn($"Material row {hit.Material} is not in the table.");
                    logger.Warning("Impact", $"impact_test: material row {hit.Material} is not in the table.");
                    return;
                }

                if (!material.RecordsImpacts)
                {
                    context.Print($"{material.Name} records no impact at all, so nothing is drawn.");
                    logger.Information("Impact", $"impact_test: {material.Name} records no impact.");
                    return;
                }

                host.Marks?.TryAdd(
                    hit,
                    MarkSurfaceAt(hit.Position, hit.Normal, out WorldPlacement? struck),
                    materials,
                    probe: host.MarkProbe,
                    placement: struck);
                Impacts.SpawnSurfaceImpact(hit.Position, hit.Normal, look, material.Entry, struckHuman: false);
                string where = FormattableString.Invariant($"{hit.Distance / WorldScene.UnitsPerMetre:0.#} m");
                string report =
                    $"{material.Name} at {where}: {Impacts.Puffs.Count} dust quad(s), "
                        + $"{Impacts.Fragments.Count} chip(s) of {material.Entry.Model}.";
                context.Print(report);
                logger.Information("Impact", report);
            });

        console.Commands.Add(
            "blood_test",
            "Throws a wound's blood where the crosshair points, as a round meeting a body does.",
            context =>
            {
                Camera3D eye = host.Eye;
                Vector3f look = eye.Orientation.Row3;
                Vector3d wound;

                // An explicit range puts the wound that many metres down the line of sight instead of at whatever the
                // crosshair reaches, so a capture can hold the spray at arm's length.
                if (context.Count > 0
                    && float.TryParse(
                        context[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float metres))
                {
                    double reach = metres * WorldScene.UnitsPerMetre;
                    Vector3d origin = eye.Position * WorldScene.UnitsPerMetre;
                    wound = new Vector3d(
                        origin.X + (look.X * reach),
                        origin.Y + (look.Y * reach),
                        origin.Z + (look.Z * reach));
                }
                else
                {
                    if (!TryAimedSurface(out WorldRayHit hit, out look))
                    {
                        context.Warn("Nothing under the crosshair, or no level is loaded.");
                        logger.Warning("Impact", "blood_test: nothing under the crosshair.");
                        return;
                    }

                    // A metre back down the line of sight, so the spray has the three metres of room the backstop
                    // trace wants and lands on the surface being aimed at.
                    wound = new Vector3d(
                        hit.Position.X - (look.X * WorldScene.UnitsPerMetre),
                        hit.Position.Y - (look.Y * WorldScene.UnitsPerMetre),
                        hit.Position.Z - (look.Z * WorldScene.UnitsPerMetre));
                }

                BloodReport blood = SpawnBlood(context, wound, look);
                string backstop = blood.BackstopHit
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"backstop {(host.Materials is { } rows && rows.TryGet(blood.BackstopMaterial, out GameMaterial? row) && row is not null ? row.Name : blood.BackstopMaterial.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
                            + $" at {blood.BackstopDistanceUnits / WorldScene.UnitsPerMetre:0.##} m, "
                            + $"{(blood.BackstopMarked ? "splashed" : "no splash -- that row marks nothing")}")
                    : "nothing within three metres behind the wound";
                int droplets = 0;
                foreach (BloodSystems systems in Impacts.Blood.Values)
                {
                    droplets += systems.Droplets.Count;
                }

                string report = Impacts.BloodEnabled
                    ? $"{droplets} droplet(s) in the air from {Impacts.Blood.Count} body(ies), "
                        + $"{host.Marks?.TotalAdded ?? 0} mark(s) recorded; {backstop}."
                    : "Blood is switched off, in the game options or by r_impact_effects.";
                context.Print(report);
                logger.Information("Impact", report);
            });
    }

    /// <inheritdoc/>
    public void Dispose() => Release();

    // Casts the frame's own eye at the world, for the impact commands.
    private bool TryAimedSurface(out WorldRayHit hit, out Vector3f look)
    {
        hit = default;
        Camera3D eye = host.Eye;
        Vector3f forward = eye.Orientation.Row3;
        look = forward;

        if (host.Walls is not { } walls)
        {
            return false;
        }

        Vector3d origin = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d end = origin + (new Vector3d(forward.X, forward.Y, forward.Z) * (60.0 * WorldScene.UnitsPerMetre));
        return walls(origin, end, out hit);
    }

    // Names the object whose surface lies at a point, for a mark to be stuck to: null for terrain, nothing, or a type that
    // takes no marks.
    private object? MarkSurfaceAt(Vector3d positionUnits, Vector3f normal, out WorldPlacement? placement)
    {
        placement = null;
        if (host.Trace is not { } trace)
        {
            return null;
        }

        const double reach = 64.0;
        Vector3d from = new(
            positionUnits.X + (normal.X * reach),
            positionUnits.Y + (normal.Y * reach),
            positionUnits.Z + (normal.Z * reach));
        Vector3d to = new(
            positionUnits.X - (normal.X * reach),
            positionUnits.Y - (normal.Y * reach),
            positionUnits.Z - (normal.Z * reach));

        if (!trace(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit hit))
        {
            return null;
        }

        placement = hit.Placement;
        return BulletMarks.SurfaceOf(hit);
    }

    private void SpawnMissileTrail(MissileSimulation simulation, bool haveSprite, float intensity)
    {
        foreach (LiveMissile missile in simulation.Missiles)
        {
            if (!missile.Powered)
            {
                continue;
            }

            // The exhaust flare at the tube's mouth, a little behind the nose along the flight line. The near cut keeps
            // it off the screen while the rocket is still at arm's length.
            if (_explosions is not null)
            {
                Vector3f v = missile.Velocity;
                float speed = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
                Vector3d nozzle = speed > 1e-3f
                    ? new Vector3d(
                        missile.Position.X - (v.X / speed * 2000.0),
                        missile.Position.Y - (v.Y / speed * 2000.0),
                        missile.Position.Z - (v.Z / speed * 2000.0))
                    : missile.Position;
                _explosions.SpawnEngineGlow(nozzle, 0.55f);
            }

            if (!missile.Config.Smoke)
            {
                continue;
            }

            Vector3d from = missile.PreviousPosition;
            Vector3d to = missile.Position;
            double travelled = (to - from).Length;

            // One puff per segment, a second only when the rocket crossed a long gap this tick, so the trail is a thin
            // line of small puffs rather than a wall of overlapping smoke.
            int puffs = Math.Clamp((int)(travelled / 6000.0) + 1, 1, 2);

            for (int i = 1; i <= puffs; i++)
            {
                double t = (double)i / puffs;
                Vector3d at = new(
                    from.X + ((to.X - from.X) * t),
                    from.Y + ((to.Y - from.Y) * t),
                    from.Z + ((to.Z - from.Z) * t));

                if (haveSprite)
                {
                    // A little of the exhaust's reverse plus a small jitter, so the column trails behind the nozzle
                    // rather than sitting dead still.
                    Vector3f drift = new(
                        (-missile.Velocity.X * 0.02f) + (((float)random.NextDouble() - 0.5f) * 60f),
                        (-missile.Velocity.Y * 0.02f) + (((float)random.NextDouble() - 0.5f) * 60f),
                        (-missile.Velocity.Z * 0.02f) + (((float)random.NextDouble() - 0.5f) * 60f) + 12f);
                    _smoke!.SpawnPuff(
                        at,
                        drift,
                        sizeMetres: 0.4f + ((float)random.NextDouble() * 0.2f),
                        growthMetresPerTick: 0.02f,
                        lifeTicks: 20 + (int)random.NextBounded(10),
                        darkness: 0.35f);
                }
                else
                {
                    Particles?.SpawnRocketSmoke(at, missile.Velocity, intensity);
                }
            }
        }
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The world's blast and rocket effects: the shared retail fireball and the launcher smoke trail. These
/// are presentation, driven off the fixed tick beside the impact particles, and kept apart from the
/// audio and detonation-resolution code that asks for them.
/// </summary>
internal sealed partial class GameHost
{
    /// <summary>Steps the original's impact effects, and lets its blood mark what it lands on.</summary>
    /// <remarks>
    /// The droplet traces are the reason this needs the world: <c>0x4640B0</c> is a collision callback,
    /// so where a droplet stops is where the splash is stamped. The same <see cref="BulletMarks"/> the
    /// bullet holes go into takes them, which is what makes them fade out of the world together.
    /// </remarks>
    private void TickRetailImpactEffects()
    {
        if (_player is null)
        {
            return;
        }

        _impactEffects.BloodEnabled = RenderCVars.ImpactEffects.Value
            && (_configuration.Active?.BloodEnabled ?? true);
        _impactEffects.Tick(_effectWalls ?? _player.Body.Walls, _player.Marks, _worldMaterials);
    }

    /// <summary>Runs the struck material's own <c>GunShotEffect</c> for whatever landed this tick.</summary>
    /// <remarks>
    /// <para>
    /// The impact list carries both the instant rays and the ballistic arrivals, so either mode kicks
    /// the same dust. A contact that met a human is deliberately skipped: <c>0x438458</c> asks the
    /// struck object whether it is a Human first and drops both the dust and the chips when it is —
    /// a body throws blood instead, which the soldier and player damage paths raise directly.
    /// </para>
    /// <para>
    /// The <b>entry</b> block is the one consulted, because that is the block the shot march
    /// (<c>0x47B750</c>) dispatches for a hit on world geometry; the second block, which every retail
    /// row leaves flagless, is for the far side and produces only its mark.
    /// </para>
    /// </remarks>
    private void SpawnRetailImpactEffects()
    {
        if (_player is null || _worldMaterials is null || !RenderCVars.ImpactEffects.Value)
        {
            return;
        }

        foreach (GunImpact impact in _player.Impacts)
        {
            // A knife records no impact (0x46DFB0), so it never reaches GunShotEffect's dust.
            if (!impact.HitSomething || impact.StruckHuman || impact.Melee)
            {
                continue;
            }

            if (!_worldMaterials.TryGet(impact.Wall.Material, out GameMaterial? material)
                || material is null
                || !material.RecordsImpacts)
            {
                continue;
            }

            _impactEffects.SpawnSurfaceImpact(
                impact.Wall.Position,
                impact.Wall.Normal,
                impact.Direction,
                material.Entry,
                struckHuman: false);
        }
    }

    /// <summary>Throws one wound's blood, as <c>Human</c>'s damage handler does before anything else.</summary>
    /// <param name="owner">The body — its two systems are kept against this and reused.</param>
    /// <param name="position">Where the round entered, in world units.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <remarks>
    /// <c>0x45F94A</c> calls the blood <b>first</b>, before the invulnerability and instant-death gates
    /// and before any damage is applied, so a round that does nothing to a body still makes it bleed.
    /// Callers therefore raise this on contact, not on damage.
    /// </remarks>
    private BloodReport SpawnRetailBlood(object owner, Vector3d position, Vector3f direction)
    {
        if (_player is null || !RenderCVars.ImpactEffects.Value)
        {
            return default;
        }

        _impactEffects.BloodEnabled = _configuration.Active?.BloodEnabled ?? true;
        return _impactEffects.SpawnBlood(
            owner,
            position,
            direction,
            _effectWalls ?? _player.Body.Walls,
            _player.Marks,
            _worldMaterials);
    }

    /// <summary>Registers the commands that fire the original's impact effects by hand.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// Diagnosis, and a capture handle: a headless run can put a dust puff, a spray of chips or a wound
    /// exactly where the crosshair points and photograph it, which firing a weapon on a synthetic clock
    /// cannot do reproducibly.
    /// </remarks>
    private void RegisterImpactCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "impact_test",
            "Runs the struck material's own GunShotEffect where the crosshair points.",
            context =>
            {
                if (!TryAimedSurface(out WorldRayHit hit, out Vector3f look))
                {
                    context.Warn("Nothing under the crosshair, or no level is loaded.");
                    _logger.Warning("Impact", "impact_test: nothing under the crosshair.");
                    return;
                }

                if (_worldMaterials is null
                    || !_worldMaterials.TryGet(hit.Material, out GameMaterial? material)
                    || material is null)
                {
                    context.Warn($"Material row {hit.Material} is not in the table.");
                    _logger.Warning("Impact", $"impact_test: material row {hit.Material} is not in the table.");
                    return;
                }

                if (!material.RecordsImpacts)
                {
                    context.Print($"{material.Name} records no impact at all, so nothing is drawn.");
                    _logger.Information("Impact", $"impact_test: {material.Name} records no impact.");
                    return;
                }

                _player?.Marks.TryAdd(
                    hit,
                    MarkSurfaceAt(hit.Position, hit.Normal),
                    _worldMaterials,
                    probe: _player?.MarkProbe);
                _impactEffects.SpawnSurfaceImpact(
                    hit.Position, hit.Normal, look, material.Entry, struckHuman: false);
                string where = FormattableString.Invariant(
                    $"{hit.Distance / WorldScene.UnitsPerMetre:0.#} m");
                string report =
                    $"{material.Name} at {where}: {_impactEffects.Puffs.Count} dust quad(s), "
                        + $"{_impactEffects.Fragments.Count} chip(s) of {material.Entry.Model}.";
                context.Print(report);
                _logger.Information("Impact", report);
            });

        console.Commands.Add(
            "blood_test",
            "Throws a wound's blood where the crosshair points, as a round meeting a body does.",
            context =>
            {
                Camera3D eye = _inspectorEye;
                Vector3f look = eye.Orientation.Row3;
                Vector3d wound;

                // An explicit range puts the wound that many metres down the line of sight instead of
                // at whatever the crosshair reaches, so a capture can hold the spray at arm's length.
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
                        _logger.Warning("Impact", "blood_test: nothing under the crosshair.");
                        return;
                    }

                    // A metre back down the line of sight, so the spray has the three metres of room the
                    // backstop trace wants and lands on the surface being aimed at.
                    wound = new Vector3d(
                        hit.Position.X - (look.X * WorldScene.UnitsPerMetre),
                        hit.Position.Y - (look.Y * WorldScene.UnitsPerMetre),
                        hit.Position.Z - (look.Z * WorldScene.UnitsPerMetre));
                }

                BloodReport blood = SpawnRetailBlood(context, wound, look);
                string backstop = blood.BackstopHit
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"backstop {(_worldMaterials is { } rows && rows.TryGet(blood.BackstopMaterial, out GameMaterial? row) && row is not null ? row.Name : blood.BackstopMaterial.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
                            + $" at {blood.BackstopDistanceUnits / WorldScene.UnitsPerMetre:0.##} m, "
                            + $"{(blood.BackstopMarked ? "splashed" : "no splash -- that row marks nothing")}")
                    : "nothing within three metres behind the wound";
                int droplets = 0;
                foreach (BloodSystems systems in _impactEffects.Blood.Values)
                {
                    droplets += systems.Droplets.Count;
                }

                string report = _impactEffects.BloodEnabled
                    ? $"{droplets} droplet(s) in the air from {_impactEffects.Blood.Count} body(ies), "
                        + $"{_player?.Marks.TotalAdded ?? 0} mark(s) recorded; {backstop}."
                    : "Blood is switched off, in the game options or by r_impact_effects.";
                context.Print(report);
                _logger.Information("Impact", report);
            });
    }

    /// <summary>Reports what a scan of the overrides folder found.</summary>
    /// <param name="scan">The scan result.</param>
    private void ReportOverrideScan(ResourceOverrides.ScanResult scan)
    {
        if (_overrides.Root is null)
        {
            return;
        }

        if (scan.Total == 0 && !scan.Moved)
        {
            _logger.Debug("Overrides", $"No overrides in '{_overrides.Root}'.");
            return;
        }

        _logger.Information("Overrides", $"{scan} in '{_overrides.Root}'.");
    }

    /// <summary>Registers the commands that manage the overrides folder.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// <para>
    /// An overrides folder that only took effect on a cold launch would make replacing an asset a
    /// compile-run-look loop with a whole game start in the middle of it. Rescanning is cheap -- a
    /// directory walk and a byte compare against what is registered -- so the loop becomes: save the
    /// file, alt-tab, type the command.
    /// </para>
    /// <para>
    /// Reloading the RESOURCES is only half of it. Everything already decoded from them -- an uploaded
    /// texture, a built mesh -- holds its own copy, so the textures are reloaded too; that is the same
    /// path r_hd uses to swap the upscaled pack in and out, and it is why this costs a hitch.
    /// </para>
    /// </remarks>
    private void RegisterOverrideCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "overrides",
            "Rescans the overrides folder and reloads whatever changed. No argument reports what is there.",
            context =>
            {
                if (_overrides.Root is null)
                {
                    context.Warn("No installation directory, so there is nowhere for an overrides folder to be.");
                    return;
                }

                if (context.Count > 0 && context[0] is "list")
                {
                    // To the log as well as the scrollback: a headless -runat run has no scrollback, and
                    // this is exactly the command somebody reaches for when a pack is not taking effect.
                    _logger.Information(
                        "Overrides",
                        $"{_overrides.Count} override(s) under '{_overrides.Root}'; "
                            + $"{_resources.OverriddenCount} standing in for a shipped asset.");

                    if (!_overrides.IsActive)
                    {
                        context.Print($"No overrides in '{_overrides.Root}'.");
                        return;
                    }

                    foreach (string listed in _overrides.Paths.Order(StringComparer.OrdinalIgnoreCase).Take(10))
                    {
                        _logger.Information("Overrides", $"  {listed}");
                    }

                    foreach (string path in _overrides.Paths.Order(StringComparer.OrdinalIgnoreCase).Take(40))
                    {
                        context.Print($"  {path}");
                    }

                    if (_overrides.Count > 40)
                    {
                        context.Print($"  ... and {_overrides.Count - 40} more.");
                    }

                    return;
                }

                ResourceOverrides.ScanResult scan = _overrides.Rescan();
                int swapped = _resources.ReapplyOverrides();

                string report =
                    $"{scan}; {swapped} registered resource(s) swapped, {_resources.OverriddenCount} now standing in.";

                // Only when something actually moved: the reload discards and re-uploads every texture in
                // the level, which is a visible hitch and is not worth paying to be told nothing changed.
                if (swapped > 0)
                {
                    _world?.ReloadTextures();
                    report += ReloadTerrainTiles() ? " Textures and ground reloaded." : " Textures reloaded.";
                    DiscardSurfaceCache();
                }

                context.Print(report);
                _logger.Information("Overrides", report);
            },
            "[list]");
    }

    /// <summary>Puts the ground back up from whatever the resource registry now hands over.</summary>
    /// <returns><see langword="true"/> when the ground was re-uploaded.</returns>
    /// <remarks>
    /// <para>
    /// A level's ground atlas is a LOOSE file, not an entry in an archive, and the two are replaced by
    /// different halves of the machinery: an archived texture is swapped in the registry and picked up by
    /// <c>WorldRenderer.ReloadTextures</c>, while a loose one is dropped from the registry and re-read on
    /// the next load. Nothing re-loads the ground by itself, because the atlas is decoded once when the
    /// level is built -- so without this the pack goes on and off every wall in the level and the ground
    /// underneath it never moves.
    /// </para>
    /// <para>
    /// Only for a mission being PLAYED. A menu backdrop's terrain was built by the preview's own private
    /// registry, and the session's is not the one holding its bytes.
    /// </para>
    /// </remarks>
    private bool ReloadTerrainTiles()
    {
        if (_terrain is null || _activeMission is not { } mission)
        {
            return false;
        }

        if (!_resources.TryLoad(mission.TerrainBasePath + ".tex", out ReadOnlyMemory<byte> bytes))
        {
            return false;
        }

        try
        {
            _terrain.ReloadTiles(TexAtlasFile.Read(bytes));
            return true;
        }
        catch (TextureFormatException error)
        {
            _logger.Warning("Overrides", $"The ground atlas did not decode: {error.Message}");
            return false;
        }
    }

    /// <summary>Registers the command that reads the camera's pose back as command-line arguments.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// A capture script needs absolute coordinates, and there is otherwise no way to get one out of the
    /// game except by writing it down from a debugger. This prints the pose in the exact spelling
    /// <c>-campos</c>, <c>-yaw</c> and <c>-pitch</c> take, so a viewpoint found by flying around is
    /// copied into a script rather than retyped and rounded. The eye is the inspector's, which is
    /// whichever camera is actually drawing -- the player's, the free camera's or a cinematic one.
    /// </remarks>
    private void RegisterCameraCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "campos",
            "Prints where the camera is, spelled as the -campos, -yaw and -pitch a capture takes.",
            context =>
            {
                Camera3D eye = _inspectorEye;
                Vector3f forward = eye.Orientation.Row3;

                // The inverse of Camera3D.LookRotation: its forward is
                // (sin yaw * cos pitch, cos yaw * cos pitch, sin pitch), so the pitch comes straight off
                // z and the yaw off the other two -- atan2 with x FIRST, because yaw here is measured
                // from +Y rather than from +X.
                float pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
                float yaw = MathF.Atan2(forward.X, forward.Y);

                string pose = FormattableString.Invariant(
                    $"-campos {eye.Position.X:0.##},{eye.Position.Y:0.##},{eye.Position.Z:0.##} -yaw {yaw:0.####} -pitch {pitch:0.####}");

                // To the log as well as the scrollback, because the caller that most wants this is a
                // headless -runat capture, which has no scrollback to read.
                context.Print(pose);
                _logger.Information("Camera", pose);
            });
    }

    /// <summary>Casts the frame's own eye at the world, for the impact commands.</summary>
    /// <param name="hit">Receives the surface under the crosshair.</param>
    /// <param name="look">Receives the direction looked along, unit length.</param>
    /// <returns><see langword="false"/> when nothing is in front of the camera.</returns>
    private bool TryAimedSurface(out WorldRayHit hit, out Vector3f look)
    {
        hit = default;
        Camera3D eye = _inspectorEye;
        Vector3f forward = eye.Orientation.Row3;
        look = forward;

        if (_effectWalls is not { } walls)
        {
            return false;
        }

        Vector3d origin = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d end = origin + (new Vector3d(forward.X, forward.Y, forward.Z) * (60.0 * WorldScene.UnitsPerMetre));
        return walls(origin, end, out hit);
    }

    /// <summary>Shows one blast's fireball: the real retail explosion when its texture is loaded.</summary>
    /// <param name="positionUnits">The blast centre, in world units.</param>
    /// <param name="intensity">The size and count scale; one is the grenade/missile fireball.</param>
    /// <remarks>
    /// The retail <c>1009_01_1</c> fireball is shared by every blast in the level. It is drawn from the
    /// model's own real texture when the mounted packs hold it; a mission without it falls back to the
    /// procedural burst so a blast is never invisible.
    /// </remarks>
    private void SpawnBlast(Vector3d positionUnits, float intensity)
    {
        // The pale grey puff a blast leaves, the retail ExplosionSmoke task's smoke2.spr. A few small
        // translucent puffs, not a screen-filling tower — the size is fixed, not scaled by the particle
        // cvar.
        if (_missileSmoke is not null && _missileSmoke.IsAvailable)
        {
            const int puffs = 3;
            for (int i = 0; i < puffs; i++)
            {
                Vector3f drift = new(
                    ((float)_soundRandom.NextDouble() - 0.5f) * 140f,
                    ((float)_soundRandom.NextDouble() - 0.5f) * 140f,
                    ((float)_soundRandom.NextDouble() * 60f) + 20f);
                _missileSmoke.SpawnPuff(
                    positionUnits,
                    drift,
                    sizeMetres: 1.0f + ((float)_soundRandom.NextDouble() * 0.4f),
                    growthMetresPerTick: 0.03f,
                    lifeTicks: 28 + (int)_soundRandom.NextBounded(14),
                    darkness: 0.45f);
            }
        }

        if (_explosions is not null && _explosions.Spawn(positionUnits, intensity))
        {
            return;
        }

        _impactParticles?.SpawnExplosion(positionUnits, intensity);
    }

    /// <summary>Shows the flashbang's world smoke residue without drawing a screen whiteout.</summary>
    /// <remarks>
    /// The native flash task registers <c>ExplosionSmoke</c> and uses <c>smoke2.spr</c>. Keep the
    /// smoke in the scene so it is occluded and distance-faded like the retail effect; the separate
    /// player handler owns the distance-based whiteout.
    /// </remarks>
    private void SpawnFlashbangSmoke(Vector3d positionUnits)
    {
        if (_missileSmoke is not null && _missileSmoke.IsAvailable)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3f drift = new(
                    ((float)_soundRandom.NextDouble() - 0.5f) * 90f,
                    ((float)_soundRandom.NextDouble() - 0.5f) * 90f,
                    18f + ((float)_soundRandom.NextDouble() * 35f));
                _missileSmoke.SpawnPuff(
                    positionUnits,
                    drift,
                    sizeMetres: 0.65f + ((float)_soundRandom.NextDouble() * 0.2f),
                    growthMetresPerTick: 0.025f,
                    lifeTicks: 34 + (int)_soundRandom.NextBounded(12),
                    darkness: 0.2f);
            }

            return;
        }

        // Keep the effect visible on a mission whose common sprite archive is incomplete. This is a
        // world particle fallback, never the old full-screen flash.
        _impactParticles?.SpawnRocketSmoke(positionUnits, Vector3f.Zero, intensity: 1f);
    }

    /// <summary>Sounds every grenade that bounced this tick, and lets the AI hear it.</summary>
    /// <remarks>
    /// The retail grenade collision (<c>0x00476CC0</c>) plays <c>grenade_bnc_1</c> at the grenade and
    /// posts a noise the AI can investigate, both debounced so a rolling grenade sounds once. The
    /// simulation already applied the debounce; here each surviving bounce is heard.
    /// </remarks>
    private void PlayGrenadeBounces()
    {
        if (_player is null)
        {
            return;
        }

        foreach (Vector3d at in _player.Grenades.Bounces)
        {
            _sounds?.PlayDefinition("grenade_bnc_1", at, priority: 120);
        }
    }

    /// <summary>The missile's scorch half-width: <c>0x476AC0</c> hands <c>Explosion0</c> 6144.</summary>
    private const float MissileScorchHalfWidthInUnits = 6144f;

    /// <summary>The M203 round's scorch half-width: <c>0x47647B</c> hands <c>Explosion0</c> 4096.</summary>
    private const float M203ScorchHalfWidthInUnits = 4096f;

    /// <summary>Burns the retail <c>burn1.tex</c> scorch onto the object a blast struck.</summary>
    /// <param name="positionUnits">The impact point, in world units.</param>
    /// <param name="normal">The struck surface's normal; up when the impact recorded none.</param>
    /// <param name="halfWidthInUnits">The scorch's half-width, as the launcher passes it.</param>
    /// <remarks>
    /// <c>Explosion0</c> (<c>0x4811C9</c>) sends <c>burn1.tex</c> (<c>dword_5C1164</c>) to the struck
    /// object through the same decal message a bullet hole uses, so it lands only on an object that
    /// takes marks: never on bare terrain, never on a body. Here it joins the world's decals, drawn by
    /// the bullet-mark renderer, so it follows the same <c>r_bullet_marks</c> switch.
    /// </remarks>
    private void ScorchSurface(Vector3d positionUnits, Vector3f normal, float halfWidthInUnits)
    {
        if (_player is null)
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

        if (MarkSurfaceAt(positionUnits, n) is not { } surface)
        {
            return;
        }

        // The name carries its .tex so the bullet-mark renderer's LOCAL:common/textures/{name}
        // candidate matches the exact entry burn1.tex is registered under (common textures, not the
        // level's).
        _player.Marks.AddMark(at, n, "burn1.tex", halfWidthInUnits, surface);
    }

    /// <summary>Names the object whose surface lies at a point, for a mark to be stuck to.</summary>
    /// <param name="positionUnits">A point on the surface.</param>
    /// <param name="normal">The surface normal there.</param>
    /// <returns>The object, or <see langword="null"/> for terrain, nothing, or a type that takes no marks.</returns>
    private object? MarkSurfaceAt(Vector3d positionUnits, Vector3f normal)
    {
        if (_worldTrace is not { } trace)
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

        return trace(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit hit)
            ? BulletMarks.SurfaceOf(hit)
            : null;
    }

    /// <summary>Lays the exhaust glow and smoke trail for every launcher rocket still in the air.</summary>
    /// <remarks>
    /// While the motor burns the rocket carries a nozzle flare (the visible half of the engine light at
    /// <c>0x004CABF0</c>) and drops a smoke puff each tick (<c>0x00428630</c>). A rocket crosses several
    /// metres a tick, so a single puff would dash; the segment it travelled is filled instead, giving the
    /// continuous trail the LAW 80 and RPG-18 leave. Presentation only, on the fixed tick.
    /// </remarks>
    private void SpawnMissileSmoke()
    {
        if (_player is null)
        {
            return;
        }

        bool haveSprite = _missileSmoke is not null && _missileSmoke.IsAvailable;
        float intensity = Math.Max(1f, RenderCVars.ImpactParticles.Value);

        SpawnMissileSmoke(_player.Missiles, haveSprite, intensity);
        SpawnMissileSmoke(_vehicleMissiles, haveSprite, intensity);
    }

    private void SpawnMissileSmoke(MissileSimulation simulation, bool haveSprite, float intensity)
    {
        foreach (LiveMissile missile in simulation.Missiles)
        {
            if (!missile.Powered)
            {
                continue;
            }

            // The exhaust flare at the tube's mouth, a little behind the nose along the flight line. The
            // near cut keeps it off the screen while the rocket is still at arm's length.
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

            // One puff per segment, a second only when the rocket crossed a long gap this tick, so the
            // trail is a thin line of small puffs rather than a wall of overlapping smoke.
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
                    // A little of the exhaust's reverse plus a small jitter, so the column trails behind
                    // the nozzle rather than sitting dead still.
                    Vector3f drift = new(
                        (-missile.Velocity.X * 0.02f) + (((float)_soundRandom.NextDouble() - 0.5f) * 60f),
                        (-missile.Velocity.Y * 0.02f) + (((float)_soundRandom.NextDouble() - 0.5f) * 60f),
                        (-missile.Velocity.Z * 0.02f) + (((float)_soundRandom.NextDouble() - 0.5f) * 60f) + 12f);
                    _missileSmoke!.SpawnPuff(
                        at,
                        drift,
                        sizeMetres: 0.4f + ((float)_soundRandom.NextDouble() * 0.2f),
                        growthMetresPerTick: 0.02f,
                        lifeTicks: 20 + (int)_soundRandom.NextBounded(10),
                        darkness: 0.35f);
                }
                else
                {
                    _impactParticles?.SpawnRocketSmoke(at, missile.Velocity, intensity);
                }
            }
        }
    }
}

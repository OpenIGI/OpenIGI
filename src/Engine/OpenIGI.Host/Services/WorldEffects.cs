using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What the world's effects read of the host: the title's own mission, whose impact effects it draws.</summary>
internal interface IWorldEffectsHost
{
    /// <summary>Gets the title's own mission, which its impact presenter draws.</summary>
    IMissionGameplay Gameplay { get; }

    /// <summary>Empties the title's own impact effects, and tells them how many frames the blood mist has.</summary>
    /// <param name="mistFrames">The mist's frame count.</param>
    void ResetTitleImpacts(int mistFrames);
}

/// <summary>
/// The world's effects: the impact bursts, the original's own dust, chips and blood, the fireball, and the smoke rockets and
/// blasts leave -- loaded with a mission, stepped on the fixed tick, drawn over the lit scene, and released with it.
/// </summary>
/// <param name="device">The device the effects are uploaded to and drawn with.</param>
/// <param name="logger">Where load problems and the test commands report.</param>
/// <param name="random">The stream the puffs' drift and life draw from, shared with the rest of the frame's effects.</param>
/// <param name="host">The title's own mission, whose impact effects are drawn among the rest.</param>
internal sealed class WorldEffects(IGraphicsDevice device, IOpenIgiLogger logger, IgiRandom random, IWorldEffectsHost host) : IHostService
{
    /// <summary>Gets the bursts where rounds land, and the procedural explosions a mission without the fireball falls back to.</summary>
    public ImpactParticleRenderer? Particles { get; private set; }

    private IMissionImpactView? _retailImpacts;
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
        _retailImpacts = presenters.Create<MissionEffectsRequest, IMissionImpactView>(
            new MissionEffectsRequest(device, resources, fileSystem, host.Gameplay));
        int impactModels = _retailImpacts?.Load(mission) ?? 0;
        host.ResetTitleImpacts(_retailImpacts?.MistFrameCount ?? 1);
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
        host.ResetTitleImpacts(1);
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
        host.ResetTitleImpacts(1);
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
        if (CombatEffectCVars.ImpactEffects.Value)
        {
            _retailImpacts?.Draw(eye, projection, blend);
        }

        // The rocket's smoke trail and the blast's grey column, under the fireball.
        _smoke?.Draw(eye, projection, blend);

        // The retail fireball, over the scene as light: grenade, missile and shot-out-device blasts drawn from the
        // real fireball.spr sprite.
        _explosions?.Draw(eye, projection, blend);
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
        float intensity = Math.Max(1f, CombatEffectCVars.ImpactParticles.Value);
        foreach (MissileSimulation simulation in simulations)
        {
            SpawnMissileTrail(simulation, haveSprite, intensity);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Release();

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

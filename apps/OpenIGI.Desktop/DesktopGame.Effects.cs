using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The world's blast and rocket effects: the shared retail fireball and the launcher smoke trail. These
/// are presentation, driven off the fixed tick beside the impact particles, and kept apart from the
/// audio and detonation-resolution code that asks for them.
/// </summary>
internal sealed partial class DesktopGame
{
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

    /// <summary>Burns the retail <c>burn1.tex</c> scorch onto the surface a blast struck.</summary>
    /// <param name="positionUnits">The impact point, in world units.</param>
    /// <param name="normal">The struck surface's normal; up when the impact recorded none.</param>
    /// <remarks>
    /// The missile impact projects <c>burn1.tex</c> (<c>dword_5C1164</c>) onto the object it hits. Here
    /// it joins the world's decals, oriented to the surface and drawn by the bullet-mark renderer, so it
    /// follows the same <c>r_bullet_marks</c> switch.
    /// </remarks>
    private void ScorchSurface(Vector3d positionUnits, Vector3f normal)
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

        // A metre-and-a-quarter scorch — larger than a bullet's mark, as a rocket's blast is. The name
        // carries its .tex so the bullet-mark renderer's LOCAL:common/textures/{name} candidate matches
        // the exact entry burn1.tex is registered under (common textures, not the level's).
        _player.Marks.AddMark(at, n, "burn1.tex", 5120f);
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

        foreach (LiveMissile missile in _player.Missiles.Missiles)
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

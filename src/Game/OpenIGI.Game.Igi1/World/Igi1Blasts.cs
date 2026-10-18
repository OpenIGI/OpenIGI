using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What the level's blasts ask of whatever shows and plays them: their sounds, their fire and smoke, and the voices they cut.</summary>
public interface IIgi1BlastHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose variables an authored blast's expression writes.</summary>
    GameLoop Game { get; }

    /// <summary>Plays a sound definition where something happened.</summary>
    /// <param name="name">The definition.</param>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="priority">Its priority.</param>
    void PlayDefinition(string name, Vector3d positionInUnits, int priority);

    /// <summary>Spawns the smoke a flashbang leaves.</summary>
    /// <param name="positionInUnits">Where it went off.</param>
    void FlashbangSmoke(Vector3d positionInUnits);

    /// <summary>Spawns the fireball a grenade, rocket or launcher round makes.</summary>
    /// <param name="positionInUnits">Where it went off.</param>
    void Fireball(Vector3d positionInUnits);

    /// <summary>Burns a rocket's scorch onto what it struck.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="normal">The struck face's normal.</param>
    void MissileScorch(Vector3d positionInUnits, Vector3f normal);

    /// <summary>Burns a launcher round's scorch onto what it struck.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="normal">The struck face's normal.</param>
    void M203Scorch(Vector3d positionInUnits, Vector3f normal);

    /// <summary>Spawns an <c>Explosion0</c>'s fire and fragments.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="fullRangeUnits">Its full range.</param>
    /// <param name="fireballs">How many fireballs.</param>
    /// <param name="fragments">How many fragments.</param>
    void Explosion(Vector3d positionInUnits, float fullRangeUnits, int fireballs, int fragments);

    /// <summary>Spawns a shot-out camera lens's or alarm light's explosion.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="isCameraLens">Whether it was a camera's lens rather than an alarm light.</param>
    void SpeakerExplosion(Vector3d positionInUnits, bool isCameraLens);

    /// <summary>Shatters a pane into its shards.</summary>
    /// <param name="placement">The pane.</param>
    /// <param name="atUnits">Where it was struck.</param>
    void GlassShatter(WorldPlacement placement, Vector3d atUnits);

    /// <summary>Cuts a shot-out speaker's siren.</summary>
    /// <param name="speaker">The speaker.</param>
    void SilenceSiren(AlarmSpeaker speaker);

    /// <summary>Cuts a destroyed camera's beep and motor.</summary>
    /// <param name="camera">The camera.</param>
    void SilenceCamera(SecurityCamera camera);
}

/// <summary>
/// The level's blasts as Project I.G.I. resolves them: grenades, rockets, launcher rounds, authored and vehicle
/// explosions, and the rounds and blasts that break panes, set off barrels and shoot out speakers -- <c>Explosion0</c>'s
/// message to everything in its reach (<c>0x47F200</c>).
/// </summary>
/// <param name="mission">The mission's world.</param>
/// <param name="harm">What a blast does to the player when it kills.</param>
/// <param name="host">What shows and plays them.</param>
public sealed class Igi1Blasts(MissionWorld mission, Igi1PlayerHarm harm, IIgi1BlastHost host)
{
    // 0x412990's shove, units a tick into the body's slide -- 758.51849.
    private const float BlastShoveUnitsPerTick = 758.51849f;

    // The explosion message's duration (+20), which 0x47F200 fills with 90 ticks.
    private const int ExplosionShakeTicks = 90;

    private readonly List<PendingGenericTbaExplosion> _pending = [];

    /// <summary>Forgets the authored explosions waiting on their delay, as a new mission starts.</summary>
    public void ClearPending() => _pending.Clear();

    /// <summary>Applies and shows whatever grenades went off this tick.</summary>
    /// <remarks>
    /// <para>
    /// The retail detonation, joined up from the reads: the blast damages every human by
    /// <see cref="ExplosionDamage.Dose"/> on <b>zone 13</b> — soldiers through the renderer's
    /// explosion path, the player through its own health — plays <c>explo_02_m</c> where it went
    /// off, and wakes the neighbourhood's AI. A flashbang damages nobody; only the player is
    /// affected (<see cref="Flashbang"/>), measured to the <b>eye</b>: any flash inside 25 m
    /// registers, one inside its own radius blinds outright.
    /// </para>
    /// <para>The visible fireball is resolved by the retail effect assets loaded by the impact renderer.</para>
    /// </remarks>
    public void ResolveGrenadeDetonations()
    {
        if (mission.Player is null)
        {
            return;
        }

        foreach (GrenadeDetonation blast in mission.Player.Grenades.Detonations)
        {
            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                // 0x00476AC0's type-1 branch spawns only the flash (0x0047E9A0) and plays explo_flash
                // (0x0047E820) -- no fireball and no explo_02_m. The world residue is a smoke task
                // (smoke2.spr), while the player handler at 0x00413090 writes the distance-based
                // whiteout fields from the player's eye. That handler does not perform a wall trace.
                host.PlayDefinition("explo_flash", blast.Position, 90);
                host.FlashbangSmoke(blast.Position);
                float eyeDistance = (float)(mission.Player.EyeUnits - blast.Position).Length;
                (float visible, float close) = Flashbang.Effect(eyeDistance, blast.RadiusUnits);
                // 0x00413090 writes the player fields only when one of its two distance tests passes.
                // The world smoke belongs to 0x47E9A0 and is deliberately independent, so a distant
                // flash event never turns into a zero-strength (but still scheduled) screen flash.
                if (visible > 0f || close > 0f)
                {
                    mission.FlashbangFlash.Apply(visible, close);
                }
                continue;
            }

            // The frag (type 0): the 1009_01_1 / explo_02_m fireball and the blast's casualties.
            host.PlayDefinition("explo_02_m", blast.Position, 90);
            host.Fireball(blast.Position);

            // AIEVENT_EXPLOSION was already posted by NotifyGrenadeAiEvents before the soldier tick.
            // Do not post it again here: leaving a second record behind makes a guard react twice to
            // one retail blast, with the duplicate consumed on the following tick.
            Explode(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, WeaponCategory.Grenade);
        }
    }

    /// <summary>Applies and shows launcher missile impacts produced by the missile task.</summary>
    public void ResolveMissileDetonations()
    {
        if (mission.Player is null)
        {
            return;
        }

        foreach (MissileDetonation blast in mission.Player.Missiles.Detonations)
        {
            ResolveMissileDetonation(blast);
        }
        foreach (MissileDetonation blast in mission.VehicleMissiles.Detonations)
        {
            ResolveMissileDetonation(blast);
        }
    }

    private void ResolveMissileDetonation(MissileDetonation blast)
    {
        if (mission.Player is null)
        {
            return;
        }

        // The missile's own impact (0x428390, at 0x42854D) sets off an Explosion0 with explo_03_l -- the launcher's
        // rocket and a vehicle's alike, since both are the same Missile task.
        host.PlayDefinition("explo_03_l", blast.Position, 90);
        host.Fireball(blast.Position);
        host.MissileScorch(blast.Position, blast.Normal);

        // The blast carries the firing weapon's record (0x428390 hands Explosion0 the missile's +0x894),
        // which is what lets a LAW rocket -- and nothing else -- blow a car up.
        Explode(
            blast.Position,
            blast.RadiusUnits,
            blast.FalloffUnits,
            blast.DamageFactor,
            WeaponCategory.Grenade,
            sourceWeaponId: blast.SourceWeaponId);
    }

    /// <summary>
    /// <c>Explosion0</c>'s message to everything in its reach (<c>0x47F200</c>): the soldiers, every authored pane and
    /// explosive object, the alarm speakers, then the player.
    /// </summary>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    /// <param name="source">The destructible that went off, which its own blast does not set off again.</param>
    /// <param name="sourceWeaponId">The weapon whose round it was, which decides what may blow a car up.</param>
    /// <remarks>
    /// The destructibles go before the speakers so a barrel-to-barrel chain is resolved in the same tick as the blast that
    /// started it.
    /// </remarks>
    public void Explode(
        Vector3d position,
        float radiusUnits,
        float falloffUnits,
        float damageFactor,
        WeaponCategory killedBy,
        DestructibleObject? source = null,
        int sourceWeaponId = 0)
    {
        mission.Soldiers?.ApplyExplosion(position, radiusUnits, falloffUnits, damageFactor);
        HandleDestructibleBlast(source, position, radiusUnits, falloffUnits, damageFactor, sourceWeaponId);
        DamageAlarmSpeakers(position, radiusUnits, falloffUnits, damageFactor);
        BlastPlayer(position, radiusUnits, falloffUnits, damageFactor, killedBy);
    }

    /// <summary>A blast's share of the player (<c>0x412990</c>): the shove and the shake, then the dose, the flash and the death.</summary>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    public void BlastPlayer(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy)
    {
        if (mission.Player is null || mission.PlayerHealth.IsDead)
        {
            return;
        }

        ReachPlayerWithBlast(position, radiusUnits, falloffUnits);
        float dose = ExplosionDamage.Dose(
            (float)(mission.Player.Body.Position - position).Length,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            radiusUnits,
            falloffUnits,
            damageFactor);
        if (dose > 0f)
        {
            mission.PlayerHealth.Apply(ExplosionDamage.Zone, dose);
            FlashPlayerForBlast();

            // A blast that zeroes the health kills exactly the way a bullet does -- without this the player stood at
            // zero for ever.
            if (mission.PlayerHealth.IsDead)
            {
                harm.BeginPlayerDeath(position, ExplosionDamage.Zone, -1, killedBy);
            }
        }
    }

    /// <summary>What any blast does to the player before its damage is counted — the head of <c>0x412990</c>.</summary>
    /// <param name="blastPosition">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The blast radius, in units.</param>
    /// <param name="falloffUnits">The blast's falloff, in units.</param>
    /// <remarks>
    /// <para>
    /// Explosion0 (<c>0x47F200</c>) hands its message to every human with no range test of its own, and the
    /// player's handler acts on distance from the eye before, and whatever, the dose: inside one and a half
    /// times radius plus falloff the body is shoved away from the blast by 758.5 units a tick (into the same
    /// slide the bullet shove uses), and the view shakes by <c>k</c> of a hit's shake — all of it inside three
    /// radii, fading to nothing at nine — for the explosion's 90 ticks.
    /// </para>
    /// <para>
    /// The same handler raises <c>Human+3528</c> within 25 m, which plays the <c>earcandy</c> loop; no shipped
    /// sound set defines it, so the original is silent there too.
    /// </para>
    /// </remarks>
    private void ReachPlayerWithBlast(Vector3d blastPosition, float radiusUnits, float falloffUnits)
    {
        if (mission.Player is null || mission.PlayerHealth.IsDead || radiusUnits <= 0f)
        {
            return;
        }

        Vector3d away = mission.Player.EyeUnits - blastPosition;
        double distance = away.Length;
        if (distance < (radiusUnits + falloffUnits) * 1.5 && distance > 0.0)
        {
            mission.Player.Body.AddHitShove(
                (float)(away.X / distance * BlastShoveUnitsPerTick),
                (float)(away.Y / distance * BlastShoveUnitsPerTick));
        }

        double beyond = distance - (radiusUnits * 3.0);
        double share = beyond < 0.0 ? 1.0 : 1.0 - (beyond / (radiusUnits * 6.0));
        if (share > 0.0)
        {
            mission.Player.InstallViewShake(
                (float)(HumanViewShake.HitPositionUnits * share),
                (float)(HumanViewShake.HitAngleRadians * share),
                ExplosionShakeTicks);
        }
    }

    /// <summary>The flash a damaging blast raises — <c>0x412990</c>'s four independent corner draws.</summary>
    private void FlashPlayerForBlast() =>
        mission.DamageFlash.HitEveryCorner(
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle());

    /// <summary>Applies M203 impacts emitted by the native-style projectile task.</summary>
    public void ResolveM203Detonations()
    {
        if (mission.Player is null)
        {
            return;
        }

        foreach (M203Detonation blast in mission.Player.M203.Detonations)
        {
            // GunM203's native projectile callback (0x476340) sends the proxy explosion tag, not the
            // hand-grenade fireball. The distinct sound/effect is audible proof that the M203 task
            // reached its own detonation path.
            host.PlayDefinition("explo_proxy", blast.Position, 90);
            host.Fireball(blast.Position);
            host.M203Scorch(blast.Position, blast.Normal);
            Explode(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, WeaponCategory.Gun);
        }
    }

    /// <summary>Consumes and presents authored GenericTBA explosions once per completed interaction.</summary>
    public void ResolveGenericTbaExplosions()
    {
        if (mission.Interactions is { } interactions)
        {
            foreach ((WorldInteraction interaction, GenericTbaExplosionParameters parameters) in interactions.ConsumeExplosions())
            {
                int delayTicks = Math.Max(0, (int)MathF.Round(
                    Math.Max(0f, parameters.ExplosionDelaySeconds) * GameClock.DefaultTickRate));
                _pending.Add(
                    new PendingGenericTbaExplosion(interaction, interaction.Anchor, parameters, delayTicks));
            }
        }

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            PendingGenericTbaExplosion pending = _pending[i];
            if (pending.RemainingTicks > 0)
            {
                pending.RemainingTicks--;
                continue;
            }

            pending.Interaction.MarkExplosionPresented(mission.Solids);
            PresentGenericTbaExplosion(pending.Position, pending.Parameters);
            _pending.RemoveAt(i);
        }
    }

    private void PresentGenericTbaExplosion(Vector3d position, GenericTbaExplosionParameters parameters)
    {
        string sound = parameters.ExplosionSound.Length > 0 ? parameters.ExplosionSound : "explo_02_m";
        host.PlayDefinition(sound, position, 90);
        // GenericTBA is an authored Explosion0 path, not a gunshot proxy. Post the same AI event
        // at presentation time as the other world blasts, including after an authored delay.
        mission.Soldiers?.NotifyExplosion(position, parameters.ExplosionRadiusUnits);
        host.Explosion(position, parameters.ExplosionRadiusUnits, parameters.FireballCount, parameters.FragmentCount);

        if (parameters.ExplosionExpression.Length > 0)
        {
            _ = new LevelExpressionRuntime(host.Game.Variables).Execute(parameters.ExplosionExpression);
        }

        float damageFactor = parameters.DamageScale * parameters.ExplosionDamageScale;
        Explode(
            position,
            parameters.ExplosionRadiusUnits,
            parameters.ExplosionFalloffUnits,
            damageFactor,
            WeaponCategory.Grenade);
    }

    /// <summary>Hands one round's contact to the exact object it struck, the way retail does.</summary>
    /// <param name="impactUnits">Where the round landed, in world units.</param>
    /// <param name="owner">The exact placed task struck by a rich trace, when available.</param>
    /// <param name="placement">The exact rendered placement struck, including an attached magic object.</param>
    /// <param name="destructibleDamage">The firing weapon row's native damage factor.</param>
    /// <remarks>
    /// The round's trace (<c>0x47B750</c>) sends its damage message to the struck task's own type
    /// handler and searches nothing around the contact. A round that named no object -- one that hit a
    /// guard or the terrain -- therefore breaks no pane, sets off no barrel and silences no speaker,
    /// however close one stands. The placement is essential for windows and barrels: their Owner is the
    /// enclosing Building task, while retail destroys only the exact attached magic object touched.
    /// </remarks>
    public void ApplyRoundContact(
        Vector3d impactUnits,
        GameTask? owner,
        WorldPlacement? placement,
        float destructibleDamage = 1f)
    {
        if (mission.Destructibles?.TryDestroy(owner, placement, destructibleDamage) is { } destructible)
        {
            HandleDestructibleImpact(destructible, impactUnits);
        }

        if (mission.AlarmSpeakers?.TryDestroy(owner, destructibleDamage) is { } speaker)
        {
            ShootOutSpeaker(speaker);
        }
    }

    /// <summary>Hands a blast to the alarm emitters: cameras add its dose, a siren or lamp inside breaks.</summary>
    /// <param name="impactUnits">Where the blast went off, in world units.</param>
    /// <param name="radiusUnits">The blast's radius.</param>
    /// <param name="falloffUnits">How far past the radius its damage takes to reach zero.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    private void DamageAlarmSpeakers(Vector3d impactUnits, float radiusUnits, float falloffUnits, float damageFactor)
    {
        if (mission.AlarmSpeakers is null)
        {
            return;
        }

        foreach (AlarmSpeaker speaker in mission.AlarmSpeakers.ApplyExplosion(impactUnits, radiusUnits, falloffUnits, damageFactor))
        {
            ShootOutSpeaker(speaker);
        }
    }

    /// <summary>Silences a destroyed alarm speaker or camera and plays its destruction.</summary>
    /// <remarks>
    /// AlarmLight_Explode (<c>0x43A5F0</c>) and the SCameraLens destruction branch (<c>0x441770</c>)
    /// permanently silence it, play its report, spawn the <c>1009_01_1</c> fireball and swap in the
    /// destroyed model.
    /// </remarks>
    private void ShootOutSpeaker(AlarmSpeaker destroyed)
    {
        // Silenced on the spot, not on the next tick's sweep -- a shot speaker cuts mid-wail.
        host.SilenceSiren(destroyed);

        if (destroyed.DestructionSound.Length > 0)
        {
            host.PlayDefinition(destroyed.DestructionSound, destroyed.PositionUnits, 60);
        }

        if (mission.SecurityCameras?.Destroy(destroyed.Task) == true
            && mission.SecurityCameras.TryGetCamera(destroyed.Task, out SecurityCamera? camera)
            && camera is not null)
        {
            host.SilenceCamera(camera);
        }

        // The SCameraLens branch (0x441770) and AlarmLight_Explode (0x43A5F0) each start their own
        // Explosion0 with fixed arguments: the lens a 2048 range, three FireBalls and seven fragments,
        // the light a 4096 range, two FireBalls and five.
        host.SpeakerExplosion(destroyed.PositionUnits, string.Equals(destroyed.Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal));
        host.Logger.Information("Ai", $"{destroyed.Task.Name} was shot out.");
    }

    /// <summary>Runs the native explosion-hit callbacks for every authored object in a blast.</summary>
    /// <remarks>
    /// Nothing is traced to. Cars (<c>0x42E160</c>), ExplodeObjects (<c>0x481780</c>) and glass
    /// (<c>0x4497F0</c>) all join Explosion0's listener list (<c>0x4C1790</c> on <c>dword_57BAA8</c>), and
    /// the fireball messages every entry on it (<c>0x480003</c>). Each handler weighs the blast by distance
    /// alone (<c>0x416C90</c>), so a wall -- or the ground over Mission 7's buried Generator_99 -- stops
    /// nothing.
    /// </remarks>
    private void HandleDestructibleBlast(
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits,
        float damageFactor = 1f,
        int sourceWeaponId = 0)
    {
        if (mission.Destructibles is not { } registry)
        {
            return;
        }

        foreach (DestructibleObject target in registry.ApplyExplosionHits(
            source,
            originUnits,
            radiusUnits,
            falloffUnits,
            damageFactor,
            target => ConditionalContainerGate.IsTaskActive(target.Owner),
            sourceWeaponId))
        {
            // Passing the blast origin is deliberate. GlassShatter uses the incoming contact to pick
            // nearby collision faces, just as Glass_ExplosionHitHandler hands the native shatter task
            // the fireball's contact point rather than the pane's origin.
            HandleDestructibleImpact(target, originUnits);
        }
    }

    /// <summary>Applies the retail presentation and blast side effects of one destroyed world object.</summary>
    public void HandleDestructibleImpact(DestructibleObject item, Vector3d? impactUnits = null)
    {
        if (item.Kind == DestructibleObjectKind.Glass)
        {
            // magicobj.qvm registers three retail glass-crush variants. Retail chooses one randomly
            // at shatter time and creates GlassShatter shards from the pane collision faces.
            int variant = (int)mission.SoundRandom.NextBounded(3u) + 1;
            host.PlayDefinition($"glass_crush_{variant}", item.PositionUnits, 55);
            if (item.Placement is { } placement)
            {
                host.GlassShatter(placement, impactUnits ?? item.PositionUnits);
            }

            // The holes in it were a child of the pane (0x47D438), so they go with it -- which is why a
            // round that breaks the glass leaves no hole hanging in the empty frame.
            if (BulletMarks.SurfaceOf(item.Owner, item.Placement) is { } shattered)
            {
                mission.Player?.Marks.Forget(shattered);
            }
            // GlassShatter_Sound is a world noise. The existing retail ground-impact route is the
            // matching AI event used for non-human world contacts; it also keeps a pane behind a wall
            // from becoming a global alert.
            mission.Soldiers?.NotifyGroundImpact(
                impactUnits ?? item.PositionUnits,
                mission.Player?.Body.Position ?? item.PositionUnits);
            host.Logger.Information("World", $"{item.TypeName} at {item.PositionUnits} shattered.");
            return;
        }

        if (mission.Destructibles is not { } registry || !registry.ScheduleExplosion(item))
        {
            return;
        }

        HandleDestructibleExplosion(item);
    }

    /// <summary>Takes every mark off a car that has just left the world.</summary>
    /// <remarks>
    /// A scorch or bullet hole is a child of the object it was stuck to (<c>0x4811C9</c> sends the decal to
    /// the struck object), and a car that has blown up deletes its children (<c>0x401780</c>) as it leaves
    /// the world cube — so the LAW's own burn mark goes with it instead of hanging where the body was.
    /// </remarks>
    public void ForgetVehicleMarks(DestructibleObjectRegistry destructibles, DestructibleObject car)
    {
        if (mission.Player is null)
        {
            return;
        }

        HashSet<object> surfaces = new(ReferenceEqualityComparer.Instance) { car.Owner };
        foreach (WorldPlacement placement in destructibles.PlacementsOf(car))
        {
            surfaces.Add(placement);
            surfaces.Add(placement.Owner);
        }

        foreach (object surface in surfaces)
        {
            mission.Player.Marks.Forget(surface);
        }

        host.Logger.Information("World", $"{car.Owner.Name} exploded and left the world.");
    }

    /// <summary>Sets off one blast of a dying car (<c>0x42E5A0</c>) once its fuse has run.</summary>
    /// <remarks>
    /// An ordinary <c>Explosion0</c>: <c>explo_03_l</c>, the 25 m explosion event, and the same message to
    /// every human, pane, barrel and speaker any other blast sends. It hands over no source record, so it
    /// sets no other car off.
    /// </remarks>
    public void PresentVehicleBlast(VehicleBlast blast)
    {
        Vector3d position = blast.PositionUnits;
        host.PlayDefinition(VehicleBlast.Sound, position, 90);
        host.Explosion(position, blast.FullRangeUnits, blast.Fireballs, blast.Fragments);
        mission.Soldiers?.NotifyExplosion(position, AiSoldier.LargeExplosionEventRadiusInUnits);
        Explode(position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, WeaponCategory.Grenade, blast.Source);

        host.Logger.Information("World", $"{blast.Source.Owner.Name} blast at {position}: radius={blast.RadiusUnits}.");
    }

    /// <summary>Applies the blast side effects after an authored explosive object's delay expires.</summary>
    private void HandleDestructibleExplosion(DestructibleObject item)
    {

        float radius = item.ExplosionRadiusUnits > 0f
            ? item.ExplosionRadiusUnits
            : ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);
        float falloff = Math.Max(0f, item.ExplosionFalloffUnits);
        float damageFactor = Math.Max(0f, item.ExplosionDamageFactor);

        if (item.ExplosionSound.Length > 0)
        {
            host.PlayDefinition(item.ExplosionSound, item.PositionUnits, 90);
        }

        // ExplodeMagicObj (0x43C0B0) and ExplodeObject (0x481A60) hand Explosion0 their authored full
        // range, fragment count and FireBall count; the look follows from those alone.
        host.Explosion(item.PositionUnits, radius, (int)item.ExplosionFireballs, item.ExplosionFragments);
        mission.Soldiers?.NotifyExplosion(item.PositionUnits, AiSoldier.LargeExplosionEventRadiusInUnits);

        // Explosion0 (0x47F200) hands its message -- the damage, the shove and the shake -- to every human
        // only when its damage is not 0. A wooden window authors 0: it splinters, and nobody is hurt.
        if (damageFactor == 0f)
        {
            host.Logger.Information("World", $"{item.TypeName} at {item.PositionUnits} broke without a blast.");
            return;
        }

        HandleDestructibleBlast(item, item.PositionUnits, radius, falloff, damageFactor);
        mission.Soldiers?.ApplyExplosion(item.PositionUnits, radius, falloff, damageFactor);

        BlastPlayer(item.PositionUnits, radius, falloff, damageFactor, WeaponCategory.Grenade);

        host.Logger.Information(
            "World",
            $"{item.TypeName} at {item.PositionUnits} exploded: radius={radius}, falloff={falloff}, damage={damageFactor}.");
    }

    private sealed class PendingGenericTbaExplosion(
        WorldInteraction interaction, Vector3d position, GenericTbaExplosionParameters parameters, int remainingTicks)
    {
        public WorldInteraction Interaction { get; } = interaction;
        public Vector3d Position { get; } = position;
        public GenericTbaExplosionParameters Parameters { get; } = parameters;
        public int RemainingTicks { get; set; } = remainingTicks;
    }

    /// <summary>Steps the destructibles a tick: their fuses, the blasts that went off, the cars that left the world, and the generators they settle.</summary>
    public void TickDestructibles()
    {
        if (mission.Destructibles is { } destructibles)
        {
            destructibles.Tick();
            foreach (DestructibleObject ready in destructibles.ConsumeReadyExplosions())
            {
                HandleDestructibleImpact(ready);
            }
            foreach (VehicleBlast blast in destructibles.ConsumeVehicleBlasts())
            {
                PresentVehicleBlast(blast);
            }
            foreach (DestructibleObject car in destructibles.ConsumeExplodedVehicles())
            {
                ForgetVehicleMarks(destructibles, car);
            }

            // A generator that exploded this tick loses its power this tick, as Generator::Run
            // (0x43B500) sees its own explosion before it reads the On expression.
            mission.Interactions?.SettleGenerators();
        }
    }

    /// <summary>Hands each of the player's rounds this tick to the exact object it struck.</summary>
    public void ApplyPlayerRounds()
    {
        if (mission.Player is not { } player)
        {
            return;
        }

        foreach (GunImpact playerImpact in player.Impacts)
        {
            if (playerImpact.HitSomething)
            {
                ApplyRoundContact(
                    playerImpact.Position,
                    playerImpact.Owner,
                    playerImpact.Placement,
                    player.HeldWeaponType?.DamageFactor ?? 1f);
            }
        }
    }
}

using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The level's blasts as Project I.G.I. resolves them: grenades, rockets, launcher rounds, authored and vehicle
/// explosions, and the rounds and blasts that break panes, set off barrels and shoot out speakers -- <c>Explosion0</c>'s
/// message to everything in its reach (<c>0x47F200</c>).
/// </summary>
/// <param name="mission">The mission's world.</param>
/// <param name="harm">What hurts a player of the mission and how it dies.</param>
/// <param name="audio">The sirens and cameras a blast silences.</param>
/// <param name="impacts">The scorch a rocket or a launcher round burns.</param>
/// <param name="host">What shows and plays them.</param>
public sealed partial class Igi1Blasts(
    MissionWorld mission, Igi1PlayerHarm harm, Igi1MissionAudio audio, Igi1ImpactEffects impacts, IBlastEffectsHost host)
{
    // The FireBalls and fragments a grenade (0x476AC0) and an M203 round (0x476340) hand Explosion0.
    private const int GrenadeFireballs = 1;
    private const int GrenadeFragments = 3;

    // How far below a grenade and a missile the line runs that finds what their blast scorches.
    private const double GrenadeScorchDropInUnits = 2048.0;
    private const double MissileScorchDropInUnits = 12288.0;

    // The flashbang's decal half-width (0x47E9A0's 2048).
    private const float FlashScorchHalfWidthInUnits = 2048f;

    // Where each task stands in the order the level made them, for the explosion list.
    private readonly Dictionary<GameTask, long> _joinOrder = [];
    private LevelTask? _orderedRoot;

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
        if (mission.Player is { } player)
        {
            ResolveGrenadeDetonations(player);
        }
    }

    /// <summary>
    /// Applies and shows what one player's last update set off: its grenades and mines, its rockets and its launcher
    /// rounds -- a co-op partner's as the mission's own player's.
    /// </summary>
    /// <param name="rig">The player.</param>
    public void ResolveDetonations(PlayerCamera rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ResolveGrenadeDetonations(rig);
        foreach (MissileDetonation blast in rig.Missiles.Detonations)
        {
            ResolveMissileDetonation(blast);
        }

        ResolveM203Detonations(rig);
    }

    private void ResolveGrenadeDetonations(PlayerCamera rig)
    {
        foreach (GrenadeDetonation blast in rig.Grenades.Detonations)
        {
            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                ShowFlashbang(blast.Position, blast.RadiusUnits);
                Record(ReplicatedBlastKind.Flashbang, blast.Position, default, blast.RadiusUnits, 0f);
                continue;
            }

            // AIEVENT_EXPLOSION was already posted by NotifyGrenadeAiEvents before the soldier tick; posting it
            // again would have a guard react twice to one retail blast.
            GrenadeDetonation frag = blast;
            ShowFrag(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor,
                () => Explode(frag.Position, frag.RadiusUnits, frag.FalloffUnits, frag.DamageFactor, WeaponCategory.Grenade));
            Record(ReplicatedBlastKind.Grenade, blast.Position, default, blast.RadiusUnits, blast.FalloffUnits);
        }

        foreach (PlacedMine mine in rig.Grenades.MineBlasts)
        {
            Vector3d at = mine.BlastPoint;
            float radius = mine.ExplosionRadiusUnits;
            ShowMine(at, radius, () => Explode(at, radius, 0f, 1f, WeaponCategory.ProximityMine));
            Record(ReplicatedBlastKind.Mine, at, default, radius, 0f);
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

        // A vehicle's rockets fly on every game's own copy of the level, so they are nobody's to send.
        foreach (MissileDetonation blast in mission.VehicleMissiles.Detonations)
        {
            ResolveMissileDetonation(blast, record: false);
        }
    }

    private void ResolveMissileDetonation(MissileDetonation blast, bool record = true)
    {
        // The missile's own impact (0x428390, at 0x42854D) sets off an Explosion0 with explo_03_l -- the launcher's
        // rocket and a vehicle's alike, since both are the same Missile task.
        // The scorch on whatever lies 12288 below, then five FireBalls and ten fragments at 10240. The blast carries
        // the firing weapon's record (0x428390 hands Explosion0 the missile's +0x894), which is what lets a LAW
        // rocket -- and nothing else -- blow a car up.
        ShowMissile(blast.Position, blast.DamageFactor, () => Explode(
            blast.Position,
            blast.RadiusUnits,
            blast.FalloffUnits,
            blast.DamageFactor,
            WeaponCategory.Grenade,
            sourceWeaponId: blast.SourceWeaponId));
        if (record)
        {
            Record(ReplicatedBlastKind.Missile, blast.Position, default, MissileExplosion.RadiusUnits, MissileExplosion.FalloffUnits);
        }
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
        Explosion0(new Igi1ExplosionHit(position, radiusUnits, falloffUnits, damageFactor, killedBy), source, sourceWeaponId);
    }

    /// <summary>The unbroken panes near a point, by their place in the level's destructibles.</summary>
    /// <param name="at">The point, in units.</param>
    /// <param name="reach">How far from it, in units.</param>
    /// <returns>Each pane's index and centre.</returns>
    public IReadOnlyList<(int Id, Vector3d At)> GlassNear(Vector3d at, double reach)
    {
        List<(int, Vector3d)> panes = [];
        if (mission.Destructibles is not { } registry)
        {
            return panes;
        }

        for (int i = 0; i < registry.Objects.Count; i++)
        {
            DestructibleObject item = registry.Objects[i];
            if (item.Kind == DestructibleObjectKind.Glass && !item.IsDestroyed
                && ConditionalContainerGate.IsTaskActive(item.Owner)
                && (item.PositionUnits - at).Length <= reach)
            {
                panes.Add((i, item.PositionUnits));
            }
        }

        return panes;
    }

    /// <summary>Shatters one pane as a blast's contact does (any blast breaks the glass it reaches), shards from the blast.</summary>
    /// <param name="id">The pane's index among the level's destructibles.</param>
    /// <param name="from">The blast's centre, in units.</param>
    public void ShatterGlass(int id, Vector3d from)
    {
        if (mission.Destructibles is not { } registry || id < 0 || id >= registry.Objects.Count)
        {
            return;
        }

        DestructibleObject item = registry.Objects[id];
        if (item.Kind == DestructibleObjectKind.Glass && !item.IsDestroyed && DestructibleObjectRegistry.Strike(item) is { } broken)
        {
            HandleDestructibleImpact(broken, from);
        }
    }

    /// <summary>A blast that reaches everything but the players: what a match's bomb does to the world, its own wave hurting them.</summary>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The falloff beyond it.</param>
    /// <param name="damageFactor">Its damage.</param>
    public void ExplodeSparingPlayers(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor) =>
        Explosion0(new Igi1ExplosionHit(position, radiusUnits, falloffUnits, damageFactor, WeaponCategory.Grenade, SparesPlayers: true), null, 0);

    /// <summary>
    /// <c>Explosion0</c>'s message (<c>0x47F200</c>): with any damage at all, <c>ExplosionHit</c> to every member of the
    /// explosion list in the order they joined it.
    /// </summary>
    /// <param name="blast">The blast.</param>
    /// <param name="source">The destructible that went off, which its own blast does not set off again.</param>
    /// <param name="sourceWeaponId">The weapon whose round it was, which decides what may blow a car up.</param>
    /// <remarks>
    /// <para>
    /// The list (<c>dword_57BAA8</c>, <c>0x4168D0</c>) is joined in each member's create handler -- every Human
    /// (<c>0x45EB30</c>), car (<c>0x42E160</c>), helicopter (<c>0x431540</c>), alarm light (<c>0x43A0E0</c>), camera
    /// (<c>0x4415C0</c>), pane (<c>0x4497F0</c>) and explosive (<c>0x481780</c>, <c>0x43BE80</c>) -- so it holds them in the
    /// order the level made them, and each weighs the blast in its own handler. A task made later joins at the end.
    /// </para>
    /// <para>
    /// A blast of no damage messages nobody: a wooden window authors 0, and it splinters with nobody hurt or shoved.
    /// </para>
    /// </remarks>
    private void Explosion0(Igi1ExplosionHit blast, DestructibleObject? source, int sourceWeaponId)
    {
        if (blast.DamageFactor == 0f)
        {
            return;
        }

        ExplosionHit shared = blast.ToShared();
        foreach (Action hit in ExplosionList(blast, shared, source, sourceWeaponId))
        {
            hit();
        }
    }

    // The explosion list's members as this blast reaches them, in the order they joined.
    private List<Action> ExplosionList(Igi1ExplosionHit blast, ExplosionHit shared, DestructibleObject? source, int sourceWeaponId)
    {
        List<(long Order, int Joined, Action Hit)> members = [];

        void Join(GameTask owner, Action hit) => members.Add((JoinOrder(owner), members.Count, hit));

        if (mission.LevelRoot is { } root)
        {
            foreach (GameTask task in root.DescendantsAndSelf())
            {
                if (task is IExplosionHitTarget target && !(blast.SparesPlayers && task is HumanPlayerTask))
                {
                    IExplosionHitTarget member = target;
                    Join(task, () => member.ExplosionHit(shared));
                }
            }
        }

        // Another game's targets joined the list after every task the level made, in the order they joined.
        ExplosionHit joinedShare = mission.Joined.ExplosionDamageScale == 1f
            ? shared
            : shared with { Damage = shared.Damage * mission.Joined.ExplosionDamageScale };
        foreach (IExplosionHitTarget joined in mission.Joined.ExplosionTargets)
        {
            IExplosionHitTarget member = joined;
            members.Add((long.MaxValue - 1, members.Count, () => member.ExplosionHit(joinedShare)));
        }

        // The laid mines joined the list as they were made, after every task the level made (0x481780) -- a co-op
        // partner's as the mission's own player's.
        foreach (PlayerCamera layer in Rigs())
        {
            foreach (PlacedMine mine in layer.Grenades.Mines)
            {
                PlacedMine member = mine;
                members.Add((long.MaxValue - 2, members.Count, () => member.Blast(ExplosionDamage.Dose(
                    (float)(member.Position - blast.Position).Length,
                    member.ContactRadiusUnits,
                    blast.RadiusUnits,
                    blast.FalloffUnits,
                    blast.DamageFactor))));
            }
        }

        if (mission.PlayerTask is null && mission.Player is not null && !blast.SparesPlayers)
        {
            // A level that declares no HumanPlayer still has the player the opening camera stood.
            members.Add((long.MaxValue, members.Count, () => SendPlayerTheBlast(blast)));
        }

        // A co-op session's other players are humans the blast reaches as it reaches the player, through the player's
        // handler; they hold no task in the level, so they come last, after it.
        foreach (Multiplayer.Igi1NetworkPlayer partner in blast.SparesPlayers ? [] : mission.NetworkPlayers)
        {
            Multiplayer.Igi1NetworkPlayer member = partner;
            members.Add((long.MaxValue, members.Count, () => BlastPlayer(
                member, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy)));
        }

        if (mission.Destructibles is { } registry)
        {
            foreach (DestructibleObject item in registry.Objects)
            {
                DestructibleObject member = item;
                Join(item.Owner, () =>
                {
                    if (DestructibleObjectRegistry.ExplosionHit(
                            member,
                            source,
                            blast.Position,
                            blast.RadiusUnits,
                            blast.FalloffUnits,
                            blast.DamageFactor,
                            target => ConditionalContainerGate.IsTaskActive(target.Owner),
                            sourceWeaponId) is { } destroyed)
                    {
                        // Passing the blast origin is deliberate. GlassShatter uses the incoming contact to pick
                        // nearby collision faces, just as Glass_ExplosionHitHandler hands the native shatter task
                        // the fireball's contact point rather than the pane's origin.
                        HandleDestructibleImpact(destroyed, blast.Position);
                    }
                });
            }
        }

        if (mission.AlarmSpeakers is { } speakers)
        {
            foreach (AlarmSpeaker speaker in speakers.Speakers)
            {
                AlarmSpeaker member = speaker;
                Join(speaker.Task, () =>
                {
                    if (AlarmSpeakerRegistry.ExplosionHit(member, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor))
                    {
                        ShootOutSpeaker(member);
                    }
                });
            }
        }

        members.Sort(static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Joined.CompareTo(b.Joined));
        return members.ConvertAll(static m => m.Hit);
    }

    // Where a task stands in the order the level made its tasks: its place in the tree as it was first walked, and a task
    // made since then after all of those, in the order it was first seen.
    private long JoinOrder(GameTask task)
    {
        if (!ReferenceEquals(_orderedRoot, mission.LevelRoot))
        {
            _joinOrder.Clear();
            _orderedRoot = mission.LevelRoot;
            if (_orderedRoot is { } root)
            {
                foreach (GameTask made in root.DescendantsAndSelf())
                {
                    _joinOrder[made] = _joinOrder.Count;
                }
            }
        }

        if (!_joinOrder.TryGetValue(task, out long order))
        {
            order = _joinOrder.Count;
            _joinOrder[task] = order;
        }

        return order;
    }

    // The blast's ExplosionHit to the player's task, whose handler is HumanPlayer's (0x412990); a level that declares no
    // HumanPlayer still has the player the opening camera stood.
    private void SendPlayerTheBlast(Igi1ExplosionHit blast)
    {
        if (mission.PlayerTask is { } task)
        {
            task.ExplosionHit(blast.ToShared());
        }
        else
        {
            BlastPlayer(harm.Own, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy);
        }
    }

    /// <summary>A blast's share of a player (<c>0x412990</c>): the shove and the shake, then the dose, the flash and the death.</summary>
    /// <param name="target">The player: the mission's own, or a co-op partner.</param>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    public void BlastPlayer(IHarmedPlayer target, Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy) =>
        harm.BlastPlayer(target, position, radiusUnits, falloffUnits, damageFactor, killedBy);

    /// <summary>Applies M203 impacts emitted by the native-style projectile task.</summary>
    public void ResolveM203Detonations()
    {
        if (mission.Player is { } player)
        {
            ResolveM203Detonations(player);
        }
    }

    private void ResolveM203Detonations(PlayerCamera rig)
    {
        foreach (M203Detonation blast in rig.M203.Detonations)
        {
            // GunM203's native projectile callback (0x476340) sends the proxy explosion tag, not the
            // hand-grenade fireball. The distinct sound/effect is audible proof that the M203 task
            // reached its own detonation path.
            M203Detonation round = blast;
            ShowM203(blast.Position, blast.Normal, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor,
                () => Explode(round.Position, round.RadiusUnits, round.FalloffUnits, round.DamageFactor, WeaponCategory.Gun));
            Record(ReplicatedBlastKind.M203, blast.Position, blast.Normal, blast.RadiusUnits, blast.FalloffUnits);
        }
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

        if (mission.Interactions?.WeaponHit(owner) == true)
        {
            host.Logger.Information("World", $"{owner!.Name} was shot and broken.");
        }

        mission.RotatingObjects?.WeaponHit(owner, placement);
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
        audio.SilenceSiren(destroyed);

        if (mission.SecurityCameras?.Destroy(destroyed.Task) == true
            && mission.SecurityCameras.TryGetCamera(destroyed.Task, out SecurityCamera? camera)
            && camera is not null)
        {
            audio.SilenceCamera(camera);
        }

        // The SCameraLens branch (0x441770) and AlarmLight_Destroy (0x43A5F0) each start their own Explosion0 with
        // fixed arguments: the lens 0.25 of damage over 2048, seven fragments and three FireBalls, heard 25 m away; the
        // light 0.05 over 4096, five and two, heard 5 m away. Both blasts reach whoever stands that close.
        bool lens = string.Equals(destroyed.Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal);
        Vector3d at = destroyed.PositionUnits;
        (float damage, float range, int fragments, int fireballs, double heard) = lens
            ? (0.25f, 2048f, 7, 3, 102400.0)
            : (0.05f, 4096f, 5, 2, 20480.0);
        mission.Soldiers?.NotifyExplosion(at, heard);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at, 1, 0, damage, range, 0f, fragments, fireballs,
            destroyed.DestructionSound,
            () => Explode(at, range, 0f, damage, WeaponCategory.Grenade),
            SoundPriority: 60));
        host.Logger.Information("Ai", $"{destroyed.Task.Name} was shot out.");
    }

    // Every player in the level: the mission's own, then a co-op session's others.
    private IEnumerable<PlayerCamera> Rigs()
    {
        if (mission.Player is { } own)
        {
            yield return own;
        }

        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            yield return partner.Player;
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
        // The radius sizes the FireBalls, the smoke and the fragments' fall as well as the blast (0x47F200).
        Vector3d position = blast.PositionUnits;
        mission.Soldiers?.NotifyExplosion(position, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            position, 1, 0, blast.DamageFactor, blast.RadiusUnits, blast.FalloffUnits, blast.Fragments, blast.Fireballs,
            VehicleBlast.Sound,
            () => Explode(position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, WeaponCategory.Grenade, blast.Source)));

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

        // ExplodeMagicObj (0x43C0B0) and ExplodeObject (0x481A60) hand Explosion0 their authored full range, fragment
        // count, FireBall count and sound. Its message -- the damage, the shove and the shake -- goes to every human only
        // when its damage is not 0 (0x47F200): a wooden window authors 0, so it splinters and nobody is hurt.
        mission.Soldiers?.NotifyExplosion(item.PositionUnits, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            item.PositionUnits, 1, 0, damageFactor, radius, falloff, item.ExplosionFragments, (int)item.ExplosionFireballs,
            item.ExplosionSound,
            () => Explosion0(new Igi1ExplosionHit(item.PositionUnits, radius, falloff, damageFactor, WeaponCategory.Grenade), item, sourceWeaponId: 0)));
        if (damageFactor == 0f)
        {
            host.Logger.Information("World", $"{item.TypeName} at {item.PositionUnits} broke without a blast.");
            return;
        }

        host.Logger.Information(
            "World",
            $"{item.TypeName} at {item.PositionUnits} exploded: radius={radius}, falloff={falloff}, damage={damageFactor}.");
    }

    /// <summary>
    /// Breaks a pane a thrown or moving body strikes (<c>Glass_Touch</c>, <c>0x449CB0</c>): its damage made up to its strength,
    /// shattered from where the body is, the body let through.
    /// </summary>
    /// <param name="pane">The pane the body's ray found.</param>
    /// <param name="from">Where the body is.</param>
    /// <returns>Whether it broke.</returns>
    public bool TouchGlass(RetailGlassPane pane, Vector3d from)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (mission.Destructibles is not { } destructibles)
        {
            return false;
        }

        // The pane's state is the destructible standing where it hangs.
        DestructibleObject? glass = null;
        double nearest = 64.0 * 64.0;
        foreach (DestructibleObject item in destructibles.Objects)
        {
            if (item.Kind != DestructibleObjectKind.Glass || item.IsDestroyed)
            {
                continue;
            }

            double d = (item.PositionUnits - pane.PositionUnits).LengthSquared;
            if (d < nearest)
            {
                nearest = d;
                glass = item;
            }
        }

        if (glass is null || destructibles.TryDestroy(glass.Owner, glass.Placement, float.MaxValue) is not { } broken)
        {
            return false;
        }

        pane.IsBroken = true;
        HandleDestructibleImpact(broken, from);
        return true;
    }

    // MineField_Explode (0x43AC40): an Explosion0 at its target, dropped to the ground beneath it (0x4149B0), that throws its
    // own fragments and FireBalls and sends no AI event.
    internal void ExplodeMineField(Igi1MineFieldTask field)
    {
        if (MineFieldTarget(field.TargetId) is not { } found)
        {
            return;
        }

        (Vector3d at, GameTask? target) = found;

        if (field.SnapToGround && mission.Trace is { } trace)
        {
            at = DropToGround(trace, at, target);
        }

        float radius = field.Radius * 4096f;
        float falloff = field.FalloffRadius * 4096f;
        float damage = field.DamageScale;
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at,
            1,
            (int)(field.Delay * 30f),
            damage,
            radius,
            falloff,
            field.Fragments,
            field.Fireballs,
            field.Sound,
            () => Explode(at, radius, falloff, damage, WeaponCategory.ProximityMine)));
    }

    // What task a minefield goes off at: the player, task 0, where its body is; any other where the task stands.
    private (Vector3d At, GameTask? Task)? MineFieldTarget(int id)
    {
        if (id == 0)
        {
            return mission.Player is { } player ? (player.Body.Position, null) : null;
        }

        return mission.LevelRoot?.Tree.FromId(id) is DeclaredTask task
            && task.TryGetValue("Position", out object? raw)
            && raw is Vector3d position
                ? (position, task)
                : null;
    }

    // 0x4149B0: straight down 400 m for the ground, else straight up, past the target itself; nothing found leaves it.
    private static Vector3d DropToGround(WorldTraceCast trace, Vector3d at, GameTask? target)
    {
        const double reach = 1638400.0;
        foreach (double dz in new[] { -reach, reach })
        {
            if (trace(new WorldTraceRequest(at, at + new Vector3d(0.0, 0.0, dz), WorldTraceMask.World, IgnoredOwner: target), out WorldTraceHit hit))
            {
                return new Vector3d(at.X, at.Y, hit.Position.Z);
            }
        }

        return at;
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
        if (mission.Player is { } player)
        {
            ApplyRounds(player);
        }
    }

    /// <summary>Hands each of one player's rounds from its last update to the exact object it struck.</summary>
    /// <param name="player">The player: the mission's own, or a co-op partner.</param>
    public void ApplyRounds(PlayerCamera player)
    {
        ArgumentNullException.ThrowIfNull(player);
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

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>
/// An I.G.I. 2 squad level running inside another game's level: its soldiers standing in for the host's humans, run where the
/// host's walk reaches them, seen, struck and drawn as the host's own.
/// </summary>
/// <remarks>
/// <para>
/// Each soldier stands for a host human (<see cref="StandFor"/>): it is in the world while that human's task is, as the host's
/// containers put it in and take it out, and the host's task dies with it -- the latch the host's scripts read. A host generator
/// spawns its later lives from a pool made with the level (<see cref="AddLives"/>).
/// </para>
/// <para>
/// A tick of the level is its walk's, then what follows the world: what the host has it do each tick, the soldiers in and out of
/// the world, the player's visibility for the next tick's detection, and the soldiers' sounds once, as the game runs its own.
/// </para>
/// </remarks>
public sealed class Igi2HostedLevel
{
    private readonly TaskTree _tasks;
    private readonly Igi2LevelRun _run;
    private readonly Igi2Sounds _sounds;
    private readonly Dictionary<HumanTask, Igi2AiSquadTask> _squads = [];
    private readonly Dictionary<HumanTask, Igi2HumanSoldier> _standIns = [];
    private readonly Dictionary<HumanTask, bool> _inWorld = [];
    private readonly HashSet<Igi2AiSquadTask> _squadsRun = [];
    private readonly List<Action> _eachTick = [];
    private readonly List<Action> _onLeave = [];

    // The host's player task and mission, once the level joined them, which I.G.I. 2's player's death is latched onto.
    private HumanTask? _hostPlayer;
    private MissionState? _mission;
    private TitleWorldModel[] _drawn = [];
    private int _drawnTick = -1;

    internal Igi2HostedLevel(
        TaskTree tasks,
        LevelTask root,
        Igi2LevelRun run,
        Igi2Sounds sounds,
        Igi2Soldiers soldiers,
        Igi2DetectionWorld detection,
        Igi2AiWorld ai,
        Igi2HostPlayerVisibility? visibility)
    {
        _tasks = tasks;
        Root = root;
        _run = run;
        _sounds = sounds;
        Soldiers = soldiers;
        Detection = detection;
        Ai = ai;
        Visibility = visibility;
    }

    /// <summary>Gets the guest's level.</summary>
    public LevelTask Root { get; }

    /// <summary>Gets the soldiers.</summary>
    public Igi2Soldiers Soldiers { get; }

    /// <summary>Gets the detectors, which list the soldiers and the host's player.</summary>
    public Igi2DetectionWorld Detection { get; }

    /// <summary>Gets the level's AI.</summary>
    public Igi2AiWorld Ai { get; }

    /// <summary>Gets how the soldiers see the host's player, or <see langword="null"/> where they see it at a cone's full length.</summary>
    public Igi2HostPlayerVisibility? Visibility { get; }

    /// <summary>Gets the weapons the soldiers have dropped, lying where they fell, or <see langword="null"/> without weapon tables.</summary>
    public Pickups.Igi2PickupList? Pickups { get; init; }

    /// <summary>Gets the level's 2-D particles: its wounded's blood, its smoke, its trails.</summary>
    public OpenIGI.Game.World.Particles.Particle2DModule? Particles { get; init; }

    /// <summary>Gets the soldiers' grenades and missiles in flight, or <see langword="null"/> without physics types.</summary>
    public Igi2Grenades? Grenades { get; init; }

    /// <summary>Gets the level's explosions: their FireBalls and thrown pieces are drawn with its models.</summary>
    public Igi2ExplosionWorld? Explosions { get; init; }

    /// <summary>Gets the host's alarms the level holds controls for (<see cref="Igi2GuestLevel.AddAlarm"/>), run each tick.</summary>
    public IReadOnlyList<Interaction.Igi2AlarmControlTask> Alarms { get; init; } = [];

    /// <summary>Gets the level's own player, I.G.I. 2's, where the host stood one (<see cref="Igi2GuestLevel.AddPlayer"/>).</summary>
    public Player.Igi2TitlePlayer? Player { get; init; }

    /// <summary>Gets the session's weapon tables, which a gun the host drops is made from; <see langword="null"/> where it has none.</summary>
    public Weapons.Igi2WeaponTables? WeaponTables { get; init; }

    /// <summary>Gets the models a dropped gun is made of, its reach their bound.</summary>
    public Func<string, Formats.World.MefModel?>? PickupModels { get; init; }

    /// <summary>
    /// Throws a gun the host's level dropped into the level's pickups as I.G.I. 2's own, as a dying soldier's gun is thrown
    /// (<c>HumanSoldier_CreateGunPickups</c>): falling from where it left the hand, a clip in it, for the level's player to take.
    /// </summary>
    /// <param name="weaponId">The weapon, by I.G.I. 2's id.</param>
    /// <param name="position">Where it leaves the hand, in world units.</param>
    /// <param name="orientation">How it lies.</param>
    /// <returns>Whether it was thrown; not a weapon I.G.I. 2 has, nor in a level with no pickups.</returns>
    public bool DropGun(string weaponId, Vector3d position, Matrix3f orientation)
    {
        ArgumentException.ThrowIfNullOrEmpty(weaponId);
        if (Pickups is not { } pickups || WeaponTables is not { } tables
            || tables.WeaponTypes.FirstOrDefault(w => string.Equals(w.WeaponId, weaponId, StringComparison.Ordinal)) is not { } weapon)
        {
            return false;
        }

        float radius = weapon.FixedModel.Length > 0 && PickupModels?.Invoke(weapon.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
        float[] rows = [orientation.M11, orientation.M12, orientation.M13, orientation.M21, orientation.M22, orientation.M23,
            orientation.M31, orientation.M32, orientation.M33];
        int clip = weapon.AmmoInfo(0, tables).RoundsPerClip;
        pickups.Add(Game.Igi2.Pickups.Igi2GunPickup.Thrown(weapon, tables, position, rows, 0f, isMoving: true, isThrowShort: true, clip, null, radius));
        return true;
    }

    /// <summary>Gets the guns mounted in the host's stead (<see cref="Igi2GuestLevel.AddStationaryGun"/>), run each tick.</summary>
    public IReadOnlyList<Weapons.Igi2StationaryGunTask> Guns { get; init; } = [];

    /// <summary>Gets the casings the soldiers' weapons have thrown.</summary>
    public Igi2GunCasings? Casings { get; init; }

    /// <summary>Gets each host human, and the I.G.I. 2 soldier that stands for it.</summary>
    public IReadOnlyDictionary<HumanTask, Igi2HumanSoldier> HandedOver => _standIns;

    /// <summary>Gets the ticks the level has run.</summary>
    public int Ticks { get; private set; }

    /// <summary>Has a soldier stand for a host human, run as its squad where the host's walk reaches that human.</summary>
    /// <param name="host">The host's human.</param>
    /// <param name="squad">The soldier's squad.</param>
    /// <param name="soldier">The soldier.</param>
    public void StandFor(HumanTask host, Igi2AiSquadTask squad, Igi2HumanSoldierTask soldier)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(soldier);
        _squads[host] = squad;
        if (soldier.Body is { } body)
        {
            _standIns[host] = body;
        }
    }

    /// <summary>
    /// Gives a host human's generator the lives it spawns: the first is the soldier standing for it, the rest wait out of the world
    /// until the generator puts them in.
    /// </summary>
    /// <param name="template">The host human the generator clones.</param>
    /// <param name="lives">Every life, the first the one standing for it, each with its squad.</param>
    /// <returns>The lives, for the host's generator to spawn; <see langword="null"/> where none has a body.</returns>
    public IGuardLives? AddLives(HumanTask template, IEnumerable<(Igi2AiSquadTask Squad, Igi2HumanSoldierTask Soldier)> lives)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(lives);
        List<(Igi2AiSquadTask, Igi2HumanSoldier)> pool = [];
        foreach ((Igi2AiSquadTask squad, Igi2HumanSoldierTask soldier) in lives)
        {
            if (soldier.Body is { } body)
            {
                pool.Add((squad, body));
                body.IsActive = false;
            }
        }

        return pool.Count == 0 ? null : new GuardLives(this, template, pool);
    }

    /// <summary>Has something done at the end of each of the level's ticks, before the soldiers follow the world.</summary>
    /// <param name="action">What is done.</param>
    public void EachTick(Action action) => _eachTick.Add(action ?? throw new ArgumentNullException(nameof(action)));

    /// <summary>Has something done as the level is left, before its tasks are deleted.</summary>
    /// <param name="action">What is done.</param>
    public void OnLeave(Action action) => _onLeave.Add(action ?? throw new ArgumentNullException(nameof(action)));

    /// <summary>Puts each soldier in the world as its host human is, as the level begins.</summary>
    public void Begin() => FollowWorld(initial: true);

    /// <summary>
    /// Puts the soldiers in the host's mission: among the bodies its shots meet, and on its explosion and human lists after its
    /// own.
    /// </summary>
    /// <param name="mission">The host's mission.</param>
    /// <param name="levelRoot">
    /// The host's level, whose humans hear the soldiers' AI events after the soldiers' own, as the soldiers hear theirs.
    /// </param>
    /// <param name="damageScale">What the host's rounds' and blasts' damage is multiplied by as it strikes a soldier.</param>
    public void JoinTo(MissionState mission, GameTask levelRoot, float damageScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        Ai.OtherHumans = [.. levelRoot.DescendantsAndSelf().OfType<IHumanAiEventTarget>()];
        mission.Bodies.Add(new Igi2ShootableSoldiers(Root.Descendants().OfType<Igi2HumanSoldierTask>()) { DamageScale = damageScale });
        mission.Joined.ExplosionDamageScale = damageScale;
        foreach (Igi2HumanSoldierTask soldier in Root.Descendants().OfType<Igi2HumanSoldierTask>())
        {
            // A soldier out of the world -- held by its container, or a generator's life not yet spawned or taken out -- is on no
            // list a blast walks.
            mission.Joined.JoinExplosions(new InWorld(soldier));
            mission.Joined.JoinHumans(soldier);
        }

        // The player's binoculars mark the soldiers as they mark the level's own.
        foreach (Soldiers.Igi2HumanSoldier body in Soldiers.Bodies)
        {
            mission.Joined.JoinSeen(body);
        }

        // I.G.I. 2's player, standing for the level's own: the level's rounds and blasts find its body as the level's player.
        if (Player is { } own && PlayerSpawn.FindGameplayTask(levelRoot) is { } hostPlayer)
        {
            mission.Bodies.Add(new Player.Igi2ShootablePlayer(hostPlayer, own.Human) { DamageScale = damageScale });
            mission.Joined.JoinExplosions(hostPlayer);
            _hostPlayer = hostPlayer;
            _mission = mission;
        }

        // It hears the level's broadcasts for the player's loud shots.
        if (Visibility is not null)
        {
            mission.Joined.JoinHumans(Visibility);
        }
    }

    /// <summary>
    /// Has the host level's walk run the soldiers: each squad where the walk first reaches a human one of its soldiers stands
    /// for, so the containers that held the host's humans hold the squad, and the level's tick finished as the walk's ends.
    /// </summary>
    /// <param name="walk">The host level's walk.</param>
    /// <remarks>
    /// A squad runs its soldiers as it runs (<c>AISquad_RunHandler</c>), so a squad of several runs once a tick, however many
    /// of its soldiers' humans the walk reaches.
    /// </remarks>
    public void BindTo(LevelRun walk)
    {
        ArgumentNullException.ThrowIfNull(walk);
        foreach (HumanTask host in _squads.Keys)
        {
            walk.Bind(host, () => RunSquadOnce(_squads[host]));
        }

        walk.AddTickEnd(EndTick);
    }

    // A squad's run, once a tick.
    private void RunSquadOnce(Igi2AiSquadTask squad)
    {
        if (_squadsRun.Add(squad))
        {
            _run.Run(squad);
        }
    }

    /// <summary>Runs the level a tick on its own: every squad and its soldier, then each soldier's body and mind.</summary>
    public void Tick()
    {
        _run.Tick();
        Finish();
    }

    /// <summary>
    /// Gets the models the level draws as of the last tick: the weapons lying where soldiers dropped them, the grenades and
    /// missiles in flight, each soldier in the world posed with its weapon in hand, and the casings.
    /// </summary>
    /// <returns>The models.</returns>
    /// <remarks>
    /// As I.G.I. 2's own level draws them (<c>Igi2TitlePlayer.WorldModels</c>), for the soldiers whose host humans are in the
    /// world: one a container holds out of it is not there to be seen.
    /// </remarks>
    public IReadOnlyList<TitleWorldModel> Models()
    {
        if (_drawnTick != Ticks)
        {
            List<TitleWorldModel> models = [];
            if (Pickups is { } lying)
            {
                models.AddRange(lying.Items.Select(static p => new TitleWorldModel(p.WeaponType.FixedModel, p.Position, Rows(p.Orientation))));
            }

            if (Grenades is { } grenades)
            {
                models.AddRange(grenades.Flying.Select(static g => new TitleWorldModel(g.Model, g.Position, Rows(g.Orientation))));
                models.AddRange(grenades.Dynamic.Tasks.OfType<Igi2ImpactGrenade>()
                    .Where(static g => g.Parameters.Model.Length != 0)
                    .Select(static g => new TitleWorldModel(g.Parameters.Model, g.Position, Rows(g.Orientation))));
                models.AddRange(grenades.Dynamic.Tasks.OfType<Igi2Missile>()
                    .Select(static m => new TitleWorldModel(m.Model, m.Position, Rows(m.Body.Orientation))));
            }

            foreach ((HumanTask host, Igi2HumanSoldier body) in _standIns)
            {
                if (_inWorld.GetValueOrDefault(host) && body.Model is { } model)
                {
                    models.AddRange(Igi2BodyFrame.Build(
                        body,
                        body.BodyPose(),
                        model.Name,
                        body.Weapon.Inventory?.Current?.WeaponType.FixedModel,
                        Ticks,
                        body.Weapon.Firing?.Flashes));
                }
            }

            if (Casings is { } casings)
            {
                models.AddRange(casings.Models());
            }

            if (Explosions is { } explosions)
            {
                models.AddRange(ExplosionPieceModels.Of(explosions, Ticks));
            }

            _drawn = [.. models];
            _drawnTick = Ticks;
        }

        return _drawn;
    }

    /// <summary>Takes the level down as its mission is left: the guest's tasks are deleted, and the ids they kept are free.</summary>
    public void Leave()
    {
        foreach (Action action in _onLeave)
        {
            action();
        }

        Visibility?.Reset();
        if (Root.Parent is not null)
        {
            _tasks.Delete(Root);
        }
    }

    private void EndTick()
    {
        _run.EndHostedTick();
        _squadsRun.Clear();
        Finish();
    }

    // A soldier is in the world while the host human it stands for is (as the host's containers put it in and take it out); the
    // body takes part as that changes, and its script may switch it between.
    private void FollowWorld(bool initial)
    {
        foreach ((HumanTask host, Igi2HumanSoldier body) in _standIns)
        {
            bool now = ConditionalContainerGate.IsTaskActive(host);
            if (initial || _inWorld.GetValueOrDefault(host) != now)
            {
                _inWorld[host] = now;
                body.IsActive = now;
            }
        }
    }

    private void Finish()
    {
        // The host's alarms, as the level's own controls run in its walk: each tells the soldiers as it starts and stops.
        foreach (Interaction.Igi2AlarmControlTask alarm in Alarms)
        {
            _run.Run(alarm);
        }

        // The guns mounted in the host's stead turn and fire as the level's own do where its walk reaches them.
        foreach (Weapons.Igi2StationaryGunTask gun in Guns)
        {
            _run.Run(gun);
        }

        foreach (Action action in _eachTick)
        {
            action();
        }

        FollowWorld(initial: false);

        // The player's factor for the tick just run, which the soldiers' detection reads on the next.
        Visibility?.Update();

        // The soldiers' sounds run once a tick after the level, as the game's own do: each follows what it hangs off, and one
        // that has ended is let go.
        // The weapons lying about, as the player's delayed pass steps them, and the casings once the level has run.
        Pickups?.Run();
        Casings?.Run();
        _sounds.Run();
        Ticks++;

        // The level's player dies with I.G.I. 2's standing for it: its scripts read the latch (HumanPlayer.isDead), and the mission
        // knows its player is down.
        if (Player is { Human.IsDead: true } && _hostPlayer is { IsDead: false } fallen)
        {
            fallen.IsDead = true;
            _mission?.IsPlayerDead = true;
        }

        // A host human dies with the soldier standing for it, the latch the host's scripts read (HumanSoldier_N.isDead).
        foreach ((HumanTask host, Igi2HumanSoldier body) in _standIns)
        {
            if (body.IsDead && !host.IsDead)
            {
                host.IsDead = true;
            }
        }
    }

    // The template's life in the world is now another: the walk runs its squad, the host human stands for its body and lives
    // again, and it is in the world as the human is.
    private void Wear(HumanTask host, Igi2AiSquadTask squad, Igi2HumanSoldier body)
    {
        _squads[host] = squad;
        _standIns[host] = body;
        host.IsDead = false;
        bool now = ConditionalContainerGate.IsTaskActive(host);
        _inWorld[host] = now;
        body.IsActive = now;
        _drawnTick = -1;
    }

    // A generator's lives of a soldier: the first is the one standing for its template, the rest wait out of the world.
    private sealed class GuardLives(Igi2HostedLevel level, HumanTask template, List<(Igi2AiSquadTask Squad, Igi2HumanSoldier Body)> lives)
        : IGuardLives
    {
        private int _current = -1;

        // Replaced once the dead body's death has played out, as a Project I.G.I. generator replaces its guard (0x44F410 takes a
        // generator's guard out of the world then).
        public bool IsDead => _current >= 0 && lives[_current].Body is { IsDead: true, IsDeathAnimationDone: true };

        public void Spawn()
        {
            // 0x487EE0: the dead life is deleted (0x4015F0) as the next is cloned (0x488000).
            if (_current >= 0)
            {
                lives[_current].Body.IsActive = false;
            }

            if (_current + 1 >= lives.Count)
            {
                return;
            }

            _current++;
            level.Wear(template, lives[_current].Squad, lives[_current].Body);
        }
    }

    // Nine floats row by row as a matrix.
    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    // A soldier as a blast finds it: only while it is in the world.
    private sealed class InWorld(Igi2HumanSoldierTask soldier) : IExplosionHitTarget
    {
        public void ExplosionHit(in ExplosionHit hit)
        {
            if (soldier.Body is { IsActive: true })
            {
                soldier.ExplosionHit(hit);
            }
        }
    }
}

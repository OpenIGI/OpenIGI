using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>A player the server runs for one of its clients: an I.G.I. 2 player whose controls are the commands the client sends.</summary>
/// <remarks>
/// <para>
/// It is the level's own kind of player, <see cref="Igi2HumanPlayer"/>, made as the level's player is, standing in every list
/// the level's player stands in, and run at the same two points of the tick (<see cref="Igi2TitlePlayer.NetworkHumans"/>). What
/// differs is only where its channel frame comes from: not the keyboard and mouse, but <see cref="Commands"/>, the client's
/// commands by tick.
/// </para>
/// <para>
/// It has no first-person view of its own on the server; its client looks through its own copy.
/// </para>
/// </remarks>
public sealed class Igi2NetworkHuman
{
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];

    internal Igi2NetworkHuman(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons, Igi2CollisionWorld world)
    {
        Human = human;
        Weapons = weapons;
        World = world;
        human.Felt = (part, direction) => _felt.Add((part, direction, Commands.LastTakenTick));
    }

    // The hits it took since the last snapshot, each with the newest of its client's commands run when it came.
    private readonly List<(Igi2DamagePart Part, Vector3f Direction, long After)> _felt = [];

    /// <summary>The sense a hit is sent as, for the player's own game to feel (<see cref="Igi2HumanPlayer.FeelHit"/>).</summary>
    internal const byte HitSense = 0;

    /// <summary>Takes the hits it has felt since the last snapshot, as senses its client's game feels.</summary>
    /// <param name="entity">Its entity in the snapshots.</param>
    /// <param name="into">Where they go.</param>
    /// <remarks>Each goes with how many of its client's commands the server has run since, as Project I.G.I.'s shove does.</remarks>
    internal void TakeFelt(int entity, List<ReplicatedSense> into)
    {
        long now = Commands.LastTakenTick;
        foreach ((Igi2DamagePart part, Vector3f direction, long after) in _felt)
        {
            int carried = now == long.MinValue || after == long.MinValue ? 0 : (int)Math.Clamp(now - after, 0, 255);
            into.Add(new ReplicatedSense(entity, HitSense, direction, A: (float)part, Ticks: carried));
        }

        _felt.Clear();
    }

    /// <summary>Gets the player.</summary>
    public Igi2HumanPlayer Human { get; }

    /// <summary>Gets what it carries, or <see langword="null"/> in a session without weapon tables.</summary>
    public Igi2PlayerWeapons? Weapons { get; }

    /// <summary>Gets its client's commands, by the tick each is for.</summary>
    public UserCommandBuffer Commands { get; } = new(Igi2Command.ChannelCount);

    // The command being run, the channel frame and the look its client began it with.
    private readonly float[] _command = new float[Igi2Command.ChannelCount];

    /// <summary>Gets the channel frame it last ran on.</summary>
    public ReadOnlySpan<float> Frame => _frame;

    /// <summary>Gets the collision world its body stands in.</summary>
    internal Igi2CollisionWorld World { get; }

    /// <summary>
    /// Gets or sets where a client puts it: another player as the snapshots show it, or <see langword="null"/> on the server,
    /// where it runs on its client's commands.
    /// </summary>
    /// <remarks>
    /// A client has no other player's commands, only where the server had it. So its copy is a puppet: each tick its body
    /// is put in the state the snapshot names and that state's clip played on, its controls never run
    /// (<see cref="Igi2HumanPlayer.PoseAsPuppet"/>), and it is stood exactly where the snapshot puts it, facing and looking as it does.
    /// </remarks>
    public ReplicatedHuman? PuppetTarget { get; set; }

    /// <summary>
    /// Its delayed half on a tick: for each of its client's commands due, its zoom, its update on the command and its delayed
    /// handler.
    /// </summary>
    /// <param name="tick">The tick being run.</param>
    /// <param name="betweenCommands">
    /// What the level does between two commands run in one tick: the shots the first fired traced, as the next tick's run
    /// handler would have traced them before the second.
    /// </param>
    /// <param name="rewind">
    /// What puts the others back where its client saw them as it made each command, for as long as the command runs: what the
    /// command strikes inside it (a knife's blow) meets them there, as Project I.G.I.'s partner's commands do.
    /// </param>
    /// <remarks>
    /// Each of its client's commands is run once, in order, so the player goes exactly where its client predicted it, as
    /// Project I.G.I.'s partner does (<see cref="UserCommandBuffer.Due"/>): none on a tick the queue is dry -- the player waits
    /// for its client -- and two on a tick it is catching up. A dry queue once repeated the last command: the server's copy
    /// walked and turned on keys nobody pressed, and as the queue never ran more than one a tick, every dry tick left it a
    /// tick further behind its client for good, until the queue was full and the client's newest commands were refused. The
    /// client was put back again and again.
    /// </remarks>
    internal void RunDelayed(long tick, Action<Igi2NetworkHuman>? betweenCommands = null, Func<Igi2NetworkHuman, IDisposable?>? rewind = null)
    {
        if (PuppetTarget is { } target)
        {
            RunPuppet(tick, target);
            return;
        }

        int due = Commands.Due();
        for (int run = 0; run < due; run++)
        {
            // The last command's shots are traced before the next is taken: taking it sets the view tick the rewind reads, and
            // the first command's rounds were put back where the second's client saw the others.
            if (run > 0)
            {
                if (Commands.Buffered == 0)
                {
                    break;
                }

                betweenCommands?.Invoke(this);
            }

            if (!Commands.TryTake(_command))
            {
                break;
            }

            using IDisposable? rewound = rewind?.Invoke(this);
            RunCommand(tick);
        }
    }

    // One of its client's commands: begun from where the client's player looked, the frame then turning it as it turned there.
    // The client's player's settings and seed are taken before it, and what its toggles came to after.
    private void RunCommand(long tick)
    {
        Igi2CommandStart? start = Igi2Command.Unpack(_command, _frame, out Igi2CommandEnd? end);
        if (start is { } began)
        {
            if (!Human.IsDead)
            {
                Human.IdealGamma = began.IdealGamma;
                Human.View.IdealPitch = began.IdealPitch;
            }

            Human.UnfilteredMouse = began.Unfiltered;
            Human.SteadyTurns = began.SteadyTurns;
            Human.InvertMouse = began.InvertMouse;
            Human.Movement = began.Movement;
            Human.Igi1Weight = began.Igi1Weight;
        }

        Human.View.RunZoom();
        Human.Ticks = (int)tick;
        if (start is { } seeded)
        {
            Igi2Command.Reseed(Human.Random, seeded.Seed);
        }

        Human.Update(_frame);
        Human.RunDelayed();
        if (end is { } left && !Human.IsDead)
        {
            Human.IsWalk = left.Walk;
            Human.View.FieldOfView = left.FieldOfView;
            Human.View.ObjectFieldOfView = left.ObjectFieldOfView;
            FollowClimb(left.Climb);
        }
    }

    /// <summary>How many commands running it may hang on a ladder or a wire its client says it is off before it lets go.</summary>
    internal const int ClimbPatience = 8;

    // The commands in a row it has hung on something its client did not.
    private int _climbApart;

    // The command says what its client hung on after it, and this copy has just run the same command: the two agree unless a
    // command was lost between them. Hanging on a ladder its client has left, it lets go, and the client's player is then put
    // where this one falls -- held there instead, every snapshot pulled the client's player back to the ladder for good.
    private void FollowClimb(int clientClimb)
    {
        _climbApart = Human.ClimbMode != 0 && clientClimb == 0 ? _climbApart + 1 : 0;
        if (_climbApart > ClimbPatience)
        {
            _climbApart = 0;
            Human.LetGoOfClimb();
        }
    }

    // What shows the rounds the server's copy fires, made once the puppet is first run.
    private Weapons.Igi2RemoteGunfire? _gunfire;

    // Whether the puppet has been posed once, its state's entry run.
    private bool _posed;

    // Whether its fall has taken the server's death clip.
    private bool _deathPosed;

    // The weapon the server's copy holds, carried and put in hand here: it is drawn in the hand, the body is posed holding it,
    // and the rounds the server's copy fires are fired again from it. Whether it was put in hand now.
    private bool Hold(int held)
    {
        if (held == 0 || Weapons is not { } weapons
            || weapons.Inventory.HeldForSnapshot == held
            || weapons.Inventory.WeaponByNumber(held - 1) is not { } item)
        {
            return false;
        }

        weapons.Inventory.EnableWeaponType(item.WeaponType.WeaponId, addAmmo: false);
        return weapons.Activate(item);
    }

    private void RunPuppet(long tick, in ReplicatedHuman target)
    {
        Array.Clear(_frame);
        Human.Ticks = (int)tick;
        bool alive = (target.Flags & ReplicatedHumanCondition.Alive) != 0;

        // Fallen on the server: it falls here, and its own death plays out where it lay; stood up there, it stands here.
        if (!alive)
        {
            if (!Human.IsDead)
            {
                Human.Damage.Damage = Human.Damage.MaxDamage + 1f;
                Human.UpdateDie();
            }

            _gunfire?.Silence();
            Human.Update(_frame);

            // The death the server's copy fell in, not one drawn here.
            if (!_deathPosed && target.ClipTime != 0
                && Human.Locomotion.CurrentState is Igi2PlayerState.Die or Igi2PlayerState.DieAlternate)
            {
                if (Human.Animation.LastState != target.ClipTime)
                {
                    Human.Animation.PlayState(target.ClipTime, 0, Igi2HumanPlayer.StateBlendSeconds);
                }

                _deathPosed = true;
            }

            Human.RunPuppetBody();
            return;
        }

        _deathPosed = false;
        if (Human.IsDead)
        {
            Human.Revive(target.Position.ToVector(), Quantize.AngleFromBits(target.Yaw));
        }

        // A weapon put in hand changes the clips every state plays (the weapon's own body clips): the state is entered again,
        // or a copy made unarmed and given its gun while it stood kept the unarmed clip, and no gun hung in its hands. So is the
        // first pose's: a copy is made standing without the stand's entry ever run (Create's TransitionTo), so no clip at all.
        bool rearmed = Hold(target.Weapon) || !_posed;
        _posed = true;
        if (Weapons?.Firing is { } firing)
        {
            (_gunfire ??= new Weapons.Igi2RemoteGunfire(firing, Human)).Take(target.Shots);
        }

        Human.PoseAsPuppet(
            target.State,
            rearmed,
            target.Action & 0xF,
            (target.Action >> 4) & 0xF,
            target.ActionSequence,
            target.Position.ToVector(),
            target.Velocity,
            Quantize.AngleFromBits(target.Yaw),
            Quantize.AngleFromBits(target.Pitch),
            target.ClipTime);
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A soldier as a co-op server has it, for a client to pose its own copy by.</summary>
/// <param name="Position">Where the soldier stands, in world units.</param>
/// <param name="Facing">Its heading, radians about the vertical.</param>
/// <param name="Animation">Its body's clip.</param>
/// <param name="UpperBody">Its torso's clip, or -1.</param>
/// <param name="LookPitch">Its look's pitch, radians.</param>
/// <param name="Alive">Whether it lives.</param>
/// <param name="Shots">Its shots so far, counted round in a byte: a change is a shot to flame and sound.</param>
public readonly record struct SoldierPuppet(Vector3d Position, float Facing, int Animation, int UpperBody, float LookPitch, bool Alive, byte Shots = 0)
{
    /// <summary>Gets the heading an orientation turns a soldier to, the inverse of <see cref="Matrix3f.RotationZ"/>.</summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The heading, radians.</returns>
    public static float FacingOf(Matrix3f orientation) => MathF.Atan2(orientation.M21, orientation.M11);
}

/// <summary>A co-op client's soldiers, posed by the server's rather than run by their own minds.</summary>
public sealed partial class SoldierSimulation
{
    private readonly Dictionary<SoldierView, SoldierPuppet> _puppets = [];

    // The puppets that fired since the last call to TakePuppetShots, for the mission to trace their rounds.
    private readonly List<(SoldierView View, OpenIGI.Game.Weapons.WeaponType? Weapon, float Facing, float Pitch)> _puppetShots = [];

    /// <summary>Takes the shots the puppeted soldiers fired since the last call: who, with what, and which way.</summary>
    /// <returns>The shots.</returns>
    /// <remarks>
    /// A client's soldiers fire no rounds of their own -- the server's do, and land them -- but each shot still draws its
    /// line through the air, which the mission traces from these.
    /// </remarks>
    public IReadOnlyList<(SoldierView View, OpenIGI.Game.Weapons.WeaponType? Weapon, float Facing, float Pitch)> TakePuppetShots()
    {
        var shots = _puppetShots.ToArray();
        _puppetShots.Clear();
        return shots;
    }

    /// <summary>Gets what a soldier holds and fires, or <see langword="null"/>.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>Its agent.</returns>
    public SoldierAgent? AgentOf(SoldierView view) => _context.FindAgent(view);

    /// <summary>Gets the level's soldiers, in no set order.</summary>
    public IEnumerable<SoldierView> SoldierViews => Brains.Keys;

    /// <summary>
    /// Gets or sets whether these soldiers are a co-op client's, every one the server's puppet: one the server hasn't posed yet
    /// stands where it is rather than thinking for itself.
    /// </summary>
    public bool PuppetsOnly
    {
        get => _context.PuppetsOnly;
        set => _context.PuppetsOnly = value;
    }

    /// <summary>
    /// Gets or sets whether the soldiers stand, for the moment, where a co-op client drew them -- the server's lag
    /// compensation of that client's round -- so a hit test meets them at the simulation's placement, not the frame's.
    /// </summary>
    public bool Rewound { get; set; }

    /// <summary>Gets a soldier's runtime id, unique to each live soldier and the same on every machine playing the level.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The id, or -1 for one with no mind.</returns>
    public int RuntimeIdOf(SoldierView view) => Brains.TryGetValue(view, out Igi1HumanAi? brain) ? brain.RuntimeId : -1;

    /// <summary>Gets the soldier with a runtime id, or <see langword="null"/>.</summary>
    /// <param name="runtimeId">The id.</param>
    /// <returns>The soldier.</returns>
    public SoldierView? SoldierByRuntimeId(int runtimeId)
    {
        if (!_context.BrainsByRuntimeId.TryGetValue(runtimeId, out Igi1HumanAi? wanted))
        {
            return null;
        }

        foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
        {
            if (ReferenceEquals(brain, wanted))
            {
                return view;
            }
        }

        return null;
    }

    /// <summary>Gets the task id of the generator whose current life a soldier is, or <see langword="null"/>.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The generator's id.</returns>
    public int? GeneratorIdOf(SoldierView view) => _generators.GeneratorOf(view)?.Id;

    /// <inheritdoc cref="GuardGeneratorSpawner.ReplaceAsServerDid"/>
    public SoldierView? ReplaceAsServerDid(int generatorId, int runtimeId) => _generators.ReplaceAsServerDid(generatorId, runtimeId);

    /// <summary>Poses a soldier from the server's copy from now on, its own mind no longer run.</summary>
    /// <param name="view">The soldier.</param>
    /// <param name="puppet">How the server has it.</param>
    /// <remarks>
    /// The original's soldiers are the one world both players fight, so only the server runs their minds; a client's copies
    /// are stood, turned and animated as the server's are, and fall when the server's fall.
    /// </remarks>
    public void Puppet(SoldierView view, in SoldierPuppet puppet)
    {
        ArgumentNullException.ThrowIfNull(view);
        _puppets[view] = puppet;
    }

    // A puppeted soldier's tick: its pose taken from the server's, its rig stepped, its mind left alone.
    private bool TickPuppet(SoldierView view)
    {
        if (!_puppets.TryGetValue(view, out SoldierPuppet puppet))
        {
            return false;
        }

        if (!puppet.Alive)
        {
            if (!view.IsDead)
            {
                view.Kill(puppet.Animation);
            }

            view.Tick();

            // Its gun stops with it. An automatic's sound is a loop held for as long as the soldier is said to be firing,
            // and is let go only when told it isn't; a dead puppet told nothing kept firing on the client's speakers.
            if (_context.FindAgent(view) is { } fallen && Brains.TryGetValue(view, out Igi1HumanAi? dead))
            {
                fallen.FiredThisTick = false;
                WeaponSound?.Invoke(dead.RuntimeId, false, fallen.WeaponType, dead.EyePosition());
            }

            return true;
        }

        view.PlaceAt(puppet.Position, Matrix3f.RotationZ(puppet.Facing));
        if (view.CurrentAnimation != puppet.Animation && puppet.Animation >= 0)
        {
            view.PlayStateAnimation(puppet.Animation, loops: true);
        }

        if (view.UpperBodyAnimation != puppet.UpperBody)
        {
            if (puppet.UpperBody >= 0)
            {
                view.PlayUpperBodyAnimation(puppet.UpperBody, loops: true);
            }
            else
            {
                view.StopUpperBodyAnimation();
            }
        }

        view.LookAlong(puppet.LookPitch);
        view.Tick();

        // The server's soldier fired since the last pose: this one's gun flames and is heard as its own would be.
        if (_context.FindAgent(view) is { } agent && Brains.TryGetValue(view, out Igi1HumanAi? brain))
        {
            bool fired = unchecked((byte)agent.ShotSequence) != puppet.Shots;

            // Lit for the tick it fired and out again after: left set, the flame burned on every tick that followed.
            agent.FiredThisTick = fired;
            if (fired)
            {
                agent.ShotSequence = puppet.Shots;
                _puppetShots.Add((view, agent.WeaponType, puppet.Facing, puppet.LookPitch));
            }

            WeaponSound?.Invoke(brain.RuntimeId, fired, agent.WeaponType, brain.EyePosition());
        }

        return true;
    }
}

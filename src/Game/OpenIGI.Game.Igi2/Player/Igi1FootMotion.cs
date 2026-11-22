using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>What Project I.G.I.'s bodies walk in an I.G.I. 2 level: one for the level, whichever of its players stand on one.</summary>
/// <param name="walls">The level's placed objects, which the wall sweep, the ground probe and the roof probe meet.</param>
/// <param name="terrainWalls">The level's terrain as the wall sweep meets it, or <see langword="null"/> for none.</param>
/// <param name="terrain">The ground a body stands on where no object is under it, or <see langword="null"/> for none.</param>
/// <param name="humans">The level's humans, whom a body cannot walk into, or <see langword="null"/> for none.</param>
public sealed class Igi1FootWorld(
    WorldRayCast? walls,
    WorldRayCast? terrainWalls,
    IGroundHeightField? terrain,
    Func<IEnumerable<Igi2Human>>? humans)
{
    /// <summary>Gets the level's placed objects as a body's sweeps and probes meet them.</summary>
    public WorldRayCast? Walls { get; } = walls;

    /// <summary>Gets the level's terrain as the wall sweep meets it.</summary>
    public WorldRayCast? TerrainWalls { get; } = terrainWalls;

    /// <summary>Gets the ground a body stands on where no object is under it.</summary>
    public IGroundHeightField? Terrain { get; } = terrain;

    /// <summary>Gets a value indicating whether a body has stepped since <see cref="TakeUse"/> last asked.</summary>
    /// <remarks>What the level's collision is kept up to date for: while no body walks it, nothing needs it moved.</remarks>
    public bool IsInUse { get; private set; }

    /// <summary>Where the living humans other than one stand, their feet in world units.</summary>
    /// <param name="self">The human asking, who is not in its own way.</param>
    /// <returns>Their feet.</returns>
    public IEnumerable<Vector3d> OthersThan(Igi2Human self) => humans is null
        ? []
        : humans().Where(h => !ReferenceEquals(h, self) && !h.IsDead).Select(static h => h.Position);

    /// <summary>Notes that a body has stepped.</summary>
    public void Use() => IsInUse = true;

    /// <summary>Whether a body has stepped since the last asking, and starts the count again.</summary>
    /// <returns><see langword="true"/> when one has.</returns>
    public bool TakeUse()
    {
        bool used = IsInUse;
        IsInUse = false;
        return used;
    }
}

/// <summary>
/// Project I.G.I.'s body under I.G.I. 2's player (<see cref="Igi2PlayerMovement.Igi1"/>): the first game's own walk, run, strafe,
/// crouch, jump and fall moving the sequel's soldier.
/// </summary>
/// <remarks>
/// <para>
/// It is the one body the first game's player stands on (<see cref="PlayerBody"/>), its state machine and its root motion
/// played from the walk carried with the code (<see cref="HumanLocomotionRootMotion.Retail"/>), so it moves exactly as it moves
/// there and needs nothing of that game's installed. I.G.I. 2's player keeps all the rest: its states choose its own clips for
/// what the body does, and its ladders, wires, fences, water, lying down and peeking are its own.
/// </para>
/// <para>
/// The body is seated where the player stands each time it takes over from those, and comes to rest there: what it was doing
/// before does not carry into what I.G.I. 2's player did since.
/// </para>
/// </remarks>
public sealed class Igi1FootMotion
{
    private readonly IGroundHeightField? _terrain;
    private HumanLocomotionAnimator _animator = new(HumanLocomotionRootMotion.Retail);
    private bool _seated;
    private HumanFallImpact? _landing;

    /// <summary>Initializes a new instance of the <see cref="Igi1FootMotion"/> class.</summary>
    /// <param name="world">What the body walks.</param>
    /// <param name="self">The player it moves, which the body does not count among the humans in its way.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public Igi1FootMotion(Igi1FootWorld world, Igi2Human self)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(self);
        World = world;
        _terrain = world.Terrain;
        Body = new PlayerBody { Walls = world.Walls, TerrainWalls = world.TerrainWalls, OtherHumans = () => world.OthersThan(self) };
        Body.SetFallImpactSink(impact => _landing = impact);
    }

    /// <summary>Gets what the body walks, which another player of the same level can stand a body of its own on.</summary>
    public Igi1FootWorld World { get; }

    /// <summary>Gets the body.</summary>
    public PlayerBody Body { get; }

    /// <summary>Gets a value indicating whether the body is moving the player: seated, and not handed back since.</summary>
    public bool IsSeated => _seated;

    /// <summary>Gets a value indicating whether the body left the ground in a jump on its last step.</summary>
    public bool TookOff => _seated && Body.TookOffThisTick;

    /// <summary>Hands the player back to I.G.I. 2's own motion; the body is seated afresh when it next steps.</summary>
    public void Unseat()
    {
        _seated = false;
        _landing = null;
    }

    /// <summary>Takes the landing the body's last step made, once.</summary>
    /// <returns>The landing, or <see langword="null"/> when the step did not land.</returns>
    public HumanFallImpact? TakeLanding()
    {
        HumanFallImpact? landing = _landing;
        _landing = null;
        return landing;
    }

    /// <summary>Steps the body a tick from where I.G.I. 2's player stands and faces.</summary>
    /// <param name="feet">Where the player's feet are, as its platform has carried them.</param>
    /// <param name="heading">Which way it faces: I.G.I. 2's gamma, which turns the body's forward axis as the first game's yaw does.</param>
    /// <param name="command">The tick's controls, the turn already taken by I.G.I. 2's player.</param>
    public void Step(Vector3d feet, float heading, in PlayerCommand command)
    {
        if (!_seated)
        {
            Body.ComeToRest();
            _animator = new HumanLocomotionAnimator(HumanLocomotionRootMotion.Retail);
            _seated = true;
        }

        // A landing the player's states did not take on the tick after it was made is not theirs to take later.
        _landing = null;
        World.Use();
        Body.Position = feet;
        Body.Yaw = heading;
        Body.TickAnimated(command, _terrain, _animator);
    }

    /// <summary>Pushes the body along a moving face it stands on, through the walls as its own step is.</summary>
    /// <param name="push">The push, in world units a tick.</param>
    public void Push(Vector3f push) => Body.SlideBy(new Vector3d(push.X, push.Y, push.Z));
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Computer;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>What the computer draws an object of the level as, which its type's map handler decides.</summary>
public enum ComputerObjectKind
{
    /// <summary>A building, terminal, cabinet, door, switch, pickup or radio: the highlight's sprite, marker and label (<c>0x443DC0</c>).</summary>
    Terminal,

    /// <summary>A soldier (<c>0x460280</c>).</summary>
    Human,

    /// <summary>The player (<c>0x413730</c>).</summary>
    Player,

    /// <summary>A helicopter or a car (<c>0x467C60</c>).</summary>
    Vehicle,

    /// <summary>A mine (<c>0x475F30</c>).</summary>
    Mine,

    /// <summary>A stationary gun's holder (<c>0x46E9F0</c>).</summary>
    GunHolder,

    /// <summary>A security camera's lens (<c>0x442C60</c>).</summary>
    Lens,
}

/// <summary>An object of the level as the computer sees it.</summary>
/// <param name="Task">Its task.</param>
/// <param name="Kind">What it is drawn as.</param>
/// <param name="Position">Where it is, world units.</param>
/// <param name="Yaw">Its heading, the third of its matrix's Euler angles.</param>
/// <param name="Radius">Its model's radius, scaled, world units.</param>
public readonly record struct ComputerObject(GameTask Task, ComputerObjectKind Kind, Vector3d Position, float Yaw, float Radius)
{
    /// <summary>Gets the team a human is on.</summary>
    public int Team { get; init; }

    /// <summary>
    /// Gets a value indicating whether it is live: a human in the world and not dead (<c>vulnerable</c>), a vehicle running
    /// (<c>+0x745</c>), a lens's camera switched on.
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>Gets a value indicating whether a mine or a camera has gone off (<c>exploded</c>).</summary>
    public bool IsExploded { get; init; }

    /// <summary>Gets a value indicating whether a mine is set off from afar (<c>remote</c>), the only kind the map shows.</summary>
    public bool IsRemote { get; init; }

    /// <summary>Gets a lens's sprite frame, which turns with it.</summary>
    public int Frame { get; init; }
}

/// <summary>A <c>ComputerHilight</c> as the computer reads it (<c>0x4687A0</c>).</summary>
/// <param name="Ids">The tasks it names.</param>
/// <param name="Sprite">The sprite it puts on them, by name in the computer archive, or empty.</param>
/// <param name="Marker">The marker shape, <c>-1</c> for none.</param>
/// <param name="Color">The marker colour, <c>-1</c> for none.</param>
/// <param name="Text">The label shown at the first close zoom, a text resource.</param>
/// <param name="Text2">The label shown closer.</param>
public readonly record struct ComputerHilight(IReadOnlyList<int> Ids, string Sprite, int Marker, int Color, string Text, string Text2)
{
    /// <summary>Gets a value indicating whether its condition holds this tick.</summary>
    public bool IsOn { get; init; }
}

/// <summary>What the computer asks of the level it runs in.</summary>
public interface IIgi1ComputerWorld
{
    /// <summary>Gets the game's random stream.</summary>
    IgiRandom Random { get; }

    /// <summary>Gets how text is measured.</summary>
    IComputerFonts? Fonts { get; }

    /// <summary>Gets the size of a sprite of the computer archive, display pixels.</summary>
    /// <param name="sprite">Its name.</param>
    /// <returns>Its first frame's size, or nothing when it is missing.</returns>
    (int Width, int Height) SpriteSize(string sprite);

    /// <summary>Plays a defined sound, unplaced (<c>0x4E7200</c>).</summary>
    /// <param name="name">The definition.</param>
    void PlaySound(string name);

    /// <summary>Finds a text resource (<c>0x4B64E0</c>, <c>0x4B5F00</c>).</summary>
    /// <param name="resource">Its name.</param>
    /// <returns>Its text, or <see langword="null"/> when there is none.</returns>
    string? FindText(string resource);

    /// <summary>Drops a point onto what is below it (<c>0x4CB9F0</c>).</summary>
    /// <param name="from">The point.</param>
    /// <param name="depth">How far down to look, world units.</param>
    /// <param name="ground">Receives where it lands.</param>
    /// <returns><see langword="true"/> when something is below within the depth.</returns>
    bool TryDrop(Vector3d from, double depth, out Vector3d ground);

    /// <summary>Casts a line at the world (<c>0x4CC2A0</c>).</summary>
    /// <param name="start">Its start.</param>
    /// <param name="finish">Its end.</param>
    /// <param name="hit">Receives where it stopped.</param>
    /// <param name="struck">Receives the task it met, when it was a task's.</param>
    /// <returns><see langword="true"/> when it met something.</returns>
    bool TryLine(Vector3d start, Vector3d finish, out Vector3d hit, out GameTask? struck);

    /// <summary>Whether a line between two points is clear for the map's eye, as a soldier's sight is (<c>0x4CD6B0</c>).</summary>
    /// <param name="from">The eye.</param>
    /// <param name="point">The point.</param>
    /// <param name="target">The task looked at, which does not block its own line.</param>
    /// <returns><see langword="true"/> when any of the line gets through.</returns>
    bool CanSee(Vector3d from, Vector3d point, GameTask target);

    /// <summary>Gets the humans, the player among them, in the order the level made them (<c>0x460C60</c>).</summary>
    /// <returns>The humans.</returns>
    IReadOnlyList<ComputerObject> Humans();

    /// <summary>Gets the helicopters and cars (<c>0x4168E0</c>).</summary>
    /// <returns>The vehicles.</returns>
    IReadOnlyList<ComputerObject> Vehicles();

    /// <summary>Gets the mines (<c>0x476030</c>).</summary>
    /// <returns>The mines.</returns>
    IReadOnlyList<ComputerObject> Mines();

    /// <summary>Gets the security cameras' lenses (<c>0x443660</c>).</summary>
    /// <returns>The lenses.</returns>
    IReadOnlyList<ComputerObject> Lenses();

    /// <summary>Gets the stationary guns' holders (<c>0x46EAD0</c>).</summary>
    /// <returns>The holders.</returns>
    IReadOnlyList<ComputerObject> GunHolders();

    /// <summary>Gets the highlights, their conditions evaluated this tick (<c>0x4688A0</c>).</summary>
    /// <returns>The highlights.</returns>
    IReadOnlyList<ComputerHilight> Hilights();

    /// <summary>Finds what a task is drawn as, by its id.</summary>
    /// <param name="id">The id.</param>
    /// <param name="found">Receives the object or objects the task stands for: a shown container's or a spawned generator's children.</param>
    void FindObjects(int id, ICollection<ComputerObject> found);

    /// <summary>Where a task is, as its position handler reports it (handler 7), for the map to look at.</summary>
    /// <param name="task">The task.</param>
    /// <param name="at">Receives where it is.</param>
    /// <returns><see langword="false"/> when it is gone or has no position.</returns>
    bool TryLocate(GameTask task, out Vector3d at);

    /// <summary>Whether the map may follow a task (the select event): a soldier of team 1, a vehicle.</summary>
    /// <param name="task">The task.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    bool CanFollow(GameTask task);

    /// <summary>Sets a mine off, as a click on it does (<c>+0x248 = 1</c>, <c>+0x23C = 9</c>).</summary>
    /// <param name="mine">The mine.</param>
    void SetOffMine(GameTask mine);

    /// <summary>
    /// Traces a camera's view as the map shows it (<c>0x442DD0</c>): twelve rays from its lens across its view, each through any
    /// glass, stopping at the first solid thing or at the camera's range.
    /// </summary>
    /// <param name="lens">The lens.</param>
    /// <returns>The fan, or <see langword="null"/> once the camera has gone off.</returns>
    ComputerViewCone? ViewCone(GameTask lens);

    /// <summary>Gets a task by its id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The task, or <see langword="null"/>.</returns>
    GameTask? TaskById(int id);
}

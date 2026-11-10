using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Saves;

/// <summary>
/// Something a save game keeps the state of, a task type with a serialize parameter list
/// (<c>Serialize_eGetSerializeParameterListQTaskEvent</c>): it writes its fields in its list's order, its base type's first, and
/// reads them back in the same order.
/// </summary>
/// <remarks>
/// The save engine both titles share is I.G.I. 2's (serialize.c). Project I.G.I. shipped without one; its states are this port's
/// own, written the same way.
/// </remarks>
public interface ISavedState
{
    /// <summary>Writes its state, <c>Serialize_WriteParameterList</c>.</summary>
    /// <param name="writer">The writer.</param>
    void WriteState(StateWriter writer);

    /// <summary>Reads its state back, <c>Serialize_ParseParameterList</c>, then fixes itself up as its deserialize handler does.</summary>
    /// <param name="reader">The reader.</param>
    void ReadState(StateReader reader);

    /// <summary>
    /// Gets a value indicating whether its serialize handler writes it at all: a type whose handler leaves some of its tasks out
    /// answers for each.
    /// </summary>
    bool IsSaved => true;

    /// <summary>
    /// Gets a value indicating whether it is written against its nearest ancestor that has a level id, rather than itself, and is
    /// written even without an id of its own (<c>SerializeInfo</c>'s <c>isUseParentToSerialize</c>).
    /// </summary>
    /// <remarks>
    /// A task with no level id and without this is not written: <c>Serialize_SerializeHandler</c> walks up only for a type that
    /// asks, or while <c>Serialize_isForceSerializeChildren</c> is set.
    /// </remarks>
    bool IsSavedWithParent => false;

    /// <summary>
    /// Gets the parts of it that are tasks of their own in the original and not here, each with its type's name: those its own
    /// serialize handler writes (<c>Serialize_SerializeChild</c>), before it.
    /// </summary>
    IReadOnlyList<SavedPart> SavedParts => [];

    /// <summary>
    /// Finishes a load once every task's state is read, <c>Serialize_eDeSerializeDoneQTaskEvent</c>: what depends on other tasks'
    /// state is put right here, children before their parents.
    /// </summary>
    void LoadDone()
    {
    }
}

/// <summary>A part of a task that the original keeps as a child task of its own type, with its own state, such as a camera's lens.</summary>
/// <param name="TypeName">The child's type.</param>
/// <param name="State">Its state.</param>
/// <param name="Id">The level id the child has in the original, or <c>-1</c> for none: a child a load made keeps its save's.</param>
public sealed record SavedPart(string TypeName, ISavedState State, int Id = GameTask.NoId);

/// <summary>The arguments of one call of a save script, whichever way the script is kept: compiled, or as text.</summary>
public interface ISaveArguments
{
    /// <summary>Gets the number of arguments.</summary>
    int Count { get; }

    /// <summary>Reads an argument as an integer.</summary>
    /// <param name="index">The argument.</param>
    /// <returns>Its value, or nought.</returns>
    int GetInt32(int index);

    /// <summary>Reads an argument as a real number.</summary>
    /// <param name="index">The argument.</param>
    /// <returns>Its value, or nought.</returns>
    double GetReal(int index);

    /// <summary>Reads an argument as text.</summary>
    /// <param name="index">The argument.</param>
    /// <returns>Its value, or empty.</returns>
    string GetText(int index);
}

/// <summary>What a level's tasks keep in a save game, as the title playing it knows them.</summary>
/// <remarks>
/// I.G.I. 2's tasks are their own states. Project I.G.I.'s state lies in the registries its level was built into, found by the
/// task; and either title has things made at run time -- a dropped weapon, a laid mine -- that the original keeps as child
/// tasks and this port does not.
/// </remarks>
public interface ISaveStates
{
    /// <summary>The state a task keeps, or <see langword="null"/> for one that keeps none.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Its state.</returns>
    ISavedState? StateOf(GameTask task) => task as ISavedState;

    /// <summary>
    /// The parts of a task that are child tasks in the original and not here: its state's own, and whatever was made beneath it
    /// at run time.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The parts, in the order the original's children would stand.</returns>
    IReadOnlyList<SavedPart> PartsOf(GameTask task) => StateOf(task)?.SavedParts ?? [];

    /// <summary>
    /// Whether a task still stands in the level as the original counts it: one the original deleted, such as a weapon picked up,
    /// is passed over when its siblings' places are counted.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns><see langword="true"/> while it stands.</returns>
    bool IsPresent(GameTask task) => true;

    /// <summary>
    /// Makes a child a save names that the level does not have, for a type that is made anew when it is not found
    /// (<c>SerializeInfo</c>'s <c>isRecreateIfNotFound</c>, <c>Serialize_GetChildTaskFromArgList</c>).
    /// </summary>
    /// <param name="parent">The task it was beneath.</param>
    /// <param name="typeName">Its type.</param>
    /// <param name="id">The level id it had, or <c>-1</c>.</param>
    /// <returns>The new child's state, or <see langword="null"/> for a type that is not made anew.</returns>
    ISavedState? Recreate(GameTask parent, string typeName, int id) => null;
}

/// <summary>The states of a level whose tasks are their own.</summary>
public sealed class TaskSaveStates : ISaveStates;

/// <summary>
/// Statements of a title's own in a save script, beside the tasks' states: I.G.I. 2's panes of glass, one
/// <c>GlassContainer("key", state);</c> each.
/// </summary>
public interface ISaveSection
{
    /// <summary>Gets the name its statements call.</summary>
    string CallName { get; }

    /// <summary>Writes its statements.</summary>
    /// <returns>The lines, each ended; empty for none.</returns>
    string Write();

    /// <summary>Takes one of its statements as a load reads it.</summary>
    /// <param name="arguments">The statement's arguments.</param>
    void Read(ISaveArguments arguments);

    /// <summary>Puts what was read into the level, once the whole script has run.</summary>
    void LoadDone();
}

/// <summary>What a save game is written and read with: the format's version, the level's states and the title's own statements.</summary>
public sealed class SaveContext
{
    /// <summary>Gets the version this game writes, and the least it reads.</summary>
    public required int Version { get; init; }

    /// <summary>Gets what the level's tasks keep.</summary>
    public ISaveStates States { get; init; } = new TaskSaveStates();

    /// <summary>Gets the title's own statements.</summary>
    public IReadOnlyList<ISaveSection> Sections { get; init; } = [];

    /// <summary>
    /// Gets the id the level's root is written under when it has none of its own, or <see langword="null"/> to give it none,
    /// as I.G.I. 2's format does. A task without an id is kept against its nearest ancestor that has one; with this, one that
    /// has no such ancestor -- a box of rounds in an unnumbered container at the top of a Project I.G.I. level -- is kept
    /// against the root rather than not at all.
    /// </summary>
    public int? RootId { get; init; }

    /// <summary>A header this game writes: this version, valid.</summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="ticks">The level's tick.</param>
    /// <returns>The header.</returns>
    public SaveInfo Info(int missionId, int ticks) => new(Version, missionId, ticks, true);
}

/// <summary>What a save game says of itself, <c>SaveGame_s</c>: its version, its mission and the level's tick it was made at.</summary>
/// <param name="Version">The format's version.</param>
/// <param name="MissionId">The mission, <c>Game_iMissionID</c>.</param>
/// <param name="Ticks">The level's tick.</param>
/// <param name="IsValid">Whether it can be loaded, <c>isValid</c>: its version is the current one or later.</param>
public readonly record struct SaveInfo(int Version, int MissionId, int Ticks, bool IsValid);

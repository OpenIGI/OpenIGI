using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Saves;

/// <summary>
/// A state's fields passed through one at a time, to a save or from one: each call takes the field as it stands and answers what
/// it is to be -- the same value while a save is written, the saved one while it is read. A state lists its fields once, and the
/// one list is both its writing and its reading, so the two cannot fall out of step.
/// </summary>
/// <remarks>
/// Not retail: the original's states are parameter lists (<c>Serialize_WriteParameterList</c>), which I.G.I. 2's tasks follow field
/// for field. This is for the states that are this port's own -- Project I.G.I.'s, which shipped with no save games.
/// </remarks>
public abstract class StateExchange
{
    /// <summary>Gets a value indicating whether a save is being read, rather than written.</summary>
    public abstract bool IsReading { get; }

    /// <summary>Passes a boolean through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public abstract bool Bool(bool value);

    /// <summary>Passes an integer through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public abstract int Int(int value);

    /// <summary>Passes a float through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public abstract float Real(float value);

    /// <summary>Passes a double through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public abstract double Real64(double value);

    /// <summary>Passes a string through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public abstract string Text(string value);

    /// <summary>Passes a reference to a task of the level through.</summary>
    /// <param name="value">The task, or <see langword="null"/>.</param>
    /// <returns>The task as it is to be.</returns>
    public abstract GameTask? Task(GameTask? value);

    /// <summary>Passes a place in the world through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public Vector3d Position(Vector3d value) => new(Real64(value.X), Real64(value.Y), Real64(value.Z));

    /// <summary>Passes three floats through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public Vector3f Vector(Vector3f value) => new(Real(value.X), Real(value.Y), Real(value.Z));

    /// <summary>Passes a frame through, its nine floats as they are: nothing is lost to angles.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public Matrix3f Frame(Matrix3f value) =>
        new(
            Real(value.M11), Real(value.M12), Real(value.M13),
            Real(value.M21), Real(value.M22), Real(value.M23),
            Real(value.M31), Real(value.M32), Real(value.M33));

    /// <summary>Passes an integer that may be absent through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public int? Int(int? value)
    {
        bool has = Bool(value.HasValue);
        int kept = Int(value ?? 0);
        return has ? kept : null;
    }

    /// <summary>Passes a boolean that may be absent through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public bool? Bool(bool? value)
    {
        bool has = Bool(value.HasValue);
        bool kept = Bool(value ?? false);
        return has ? kept : null;
    }

    /// <summary>Passes a place that may be absent through.</summary>
    /// <param name="value">The field as it stands.</param>
    /// <returns>The field as it is to be.</returns>
    public Vector3d? Position(Vector3d? value)
    {
        bool has = Bool(value.HasValue);
        Vector3d kept = Position(value ?? default);
        return has ? kept : null;
    }

    /// <summary>Passes a run of items through: how many, then each.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <param name="values">The items as they stand.</param>
    /// <param name="each">Passes one item through; it is given the default while one the level has not is read.</param>
    /// <returns>The items as they are to be: as many as the save holds.</returns>
    public T[] Run<T>(IReadOnlyList<T> values, Func<StateExchange, T, T> each)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(each);
        int count = Math.Max(Int(values.Count), 0);
        T[] kept = new T[count];
        for (int i = 0; i < count; i++)
        {
            kept[i] = each(this, i < values.Count ? values[i] : default!);
        }

        return kept;
    }

    /// <summary>An exchange that writes a save.</summary>
    /// <param name="writer">The writer.</param>
    /// <returns>The exchange.</returns>
    public static StateExchange Writing(StateWriter writer) => new Write(writer ?? throw new ArgumentNullException(nameof(writer)));

    /// <summary>An exchange that reads a save.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The exchange.</returns>
    public static StateExchange Reading(StateReader reader) => new Read(reader ?? throw new ArgumentNullException(nameof(reader)));

    private sealed class Write(StateWriter writer) : StateExchange
    {
        public override bool IsReading => false;

        public override bool Bool(bool value)
        {
            writer.Bool(value);
            return value;
        }

        public override int Int(int value)
        {
            writer.Int(value);
            return value;
        }

        public override float Real(float value)
        {
            writer.Real(value);
            return value;
        }

        public override double Real64(double value)
        {
            writer.Real64(value);
            return value;
        }

        public override string Text(string value)
        {
            writer.Text(value ?? string.Empty);
            return value ?? string.Empty;
        }

        public override GameTask? Task(GameTask? value)
        {
            writer.TaskReference(value);
            return value;
        }
    }

    private sealed class Read(StateReader reader) : StateExchange
    {
        public override bool IsReading => true;

        public override bool Bool(bool value) => reader.Bool();

        public override int Int(int value) => reader.Int();

        public override float Real(float value) => reader.Real();

        public override double Real64(double value) => reader.Real64();

        public override string Text(string value) => reader.Text();

        public override GameTask? Task(GameTask? value) => reader.TaskReference();
    }
}

/// <summary>A state that lists its fields once, for writing and reading alike (<see cref="StateExchange"/>).</summary>
public interface IExchangedState : ISavedState
{
    /// <summary>Passes its fields through, in an order that never changes.</summary>
    /// <param name="exchange">The exchange.</param>
    void Exchange(StateExchange exchange);

    /// <inheritdoc/>
    void ISavedState.WriteState(StateWriter writer) => Exchange(StateExchange.Writing(writer));

    /// <inheritdoc/>
    void ISavedState.ReadState(StateReader reader) => Exchange(StateExchange.Reading(reader));
}

/// <summary>Something built from a level's tasks that keeps state for some of them.</summary>
public interface ISavedStateSource
{
    /// <summary>The state it keeps, each by the task the level declares it with.</summary>
    /// <param name="solids">The level's collision, for a state that has to move its own at once when read; <see langword="null"/> without.</param>
    /// <returns>The tasks and their states.</returns>
    IEnumerable<KeyValuePair<GameTask, ISavedState>> SavedStates(World.ObjectCollisionIndex? solids);
}

/// <summary>Several states kept as one, each after the last: what one task keeps in more than one place.</summary>
/// <param name="states">The states, in an order that never changes.</param>
public sealed class SavedStateGroup(IEnumerable<ISavedState> states) : ISavedState
{
    private readonly List<ISavedState> _states = [.. states ?? throw new ArgumentNullException(nameof(states))];

    /// <summary>Gets the states.</summary>
    public IReadOnlyList<ISavedState> States => _states;

    /// <summary>Adds a state after the others.</summary>
    /// <param name="state">The state.</param>
    public void Add(ISavedState state) => _states.Add(state ?? throw new ArgumentNullException(nameof(state)));

    /// <inheritdoc/>
    public bool IsSavedWithParent { get; init; }

    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        foreach (ISavedState state in _states)
        {
            state.WriteState(writer);
        }
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
        foreach (ISavedState state in _states)
        {
            state.ReadState(reader);
        }
    }

    /// <inheritdoc/>
    public void LoadDone()
    {
        foreach (ISavedState state in _states)
        {
            state.LoadDone();
        }
    }
}

/// <summary>A state with something to do once a load is done, such as moving its collision to where it was read to.</summary>
/// <param name="state">The state.</param>
/// <param name="done">What is done once every state is read.</param>
public sealed class SavedStateThen(ISavedState state, Action done) : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => state.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => state.ReadState(reader);

    /// <inheritdoc/>
    public void LoadDone()
    {
        state.LoadDone();
        done();
    }
}

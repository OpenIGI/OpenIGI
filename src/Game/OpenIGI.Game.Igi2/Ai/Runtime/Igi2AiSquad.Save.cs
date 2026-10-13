using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// A squad's actions as a save game keeps them: words, each action's kind and then its own state. Retail writes its action stack's
/// memory as it lies (<c>SerializeArray_eParameterType</c>); this keeps the same things, so a save made here loads here.
/// </summary>
public sealed class Igi2AiSquadWords
{
    private readonly List<int> _words;
    private int _next;

    /// <summary>Initializes a new instance of the <see cref="Igi2AiSquadWords"/> class, empty for writing.</summary>
    public Igi2AiSquadWords() => _words = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2AiSquadWords"/> class over words read back.</summary>
    /// <param name="words">The words.</param>
    public Igi2AiSquadWords(IEnumerable<int> words) => _words = [.. words];

    /// <summary>Gets the words written.</summary>
    public IReadOnlyList<int> Words => _words;

    /// <summary>Gets a value indicating whether every word is read.</summary>
    public bool IsAtEnd => _next >= _words.Count;

    /// <summary>Writes an integer.</summary>
    /// <param name="value">The value.</param>
    public void Add(int value) => _words.Add(value);

    /// <summary>Writes a float as its bits.</summary>
    /// <param name="value">The value.</param>
    public void Add(float value) => _words.Add(BitConverter.SingleToInt32Bits(value));

    /// <summary>Writes a double as its two words.</summary>
    /// <param name="value">The value.</param>
    public void Add(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        _words.Add(unchecked((int)bits));
        _words.Add((int)(bits >> 32));
    }

    /// <summary>Writes three floats.</summary>
    /// <param name="value">The value.</param>
    public void Add(Vector3f value)
    {
        Add(value.X);
        Add(value.Y);
        Add(value.Z);
    }

    /// <summary>Writes three doubles.</summary>
    /// <param name="value">The value.</param>
    public void Add(Vector3d value)
    {
        Add(value.X);
        Add(value.Y);
        Add(value.Z);
    }

    /// <summary>Reads an integer.</summary>
    /// <returns>The value; nought past the end.</returns>
    public int Int() => IsAtEnd ? 0 : _words[_next++];

    /// <summary>Reads a float.</summary>
    /// <returns>The value.</returns>
    public float Single() => BitConverter.Int32BitsToSingle(Int());

    /// <summary>Reads a double.</summary>
    /// <returns>The value.</returns>
    public double Double()
    {
        uint low = unchecked((uint)Int());
        long high = Int();
        return BitConverter.Int64BitsToDouble((high << 32) | low);
    }

    /// <summary>Reads three floats.</summary>
    /// <returns>The value.</returns>
    public Vector3f Single3() => new(Single(), Single(), Single());

    /// <summary>Reads three doubles.</summary>
    /// <returns>The value.</returns>
    public Vector3d Double3() => new(Double(), Double(), Double());
}

/// <content>
/// Its save state, <c>AISquad_ptSerializeParameterList</c> (aisquad.c): whether it is dead, its flags, its timers and suspicion, and its
/// actions.
/// </content>
public sealed partial class Igi2AiSquad
{
    /// <summary>Writes its save state.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(IsDead);
        writer.Int(Flags);
        writer.Int(TenseTimer);
        writer.Int(RushTimer);
        writer.Int(DisallowIncreaseSuspicionTimer);
        writer.Int(SuspicionLevel);
        writer.Int(_actions.Count);
        Igi2AiSquadWords words = new();
        foreach (Igi2AiSquadAction action in _actions)
        {
            words.Add((int)action.Kind);
            action.SaveState(words, this);
        }

        writer.Array([.. words.Words]);
    }

    /// <summary>
    /// Reads its save state back, <c>AISquad_DeSerializeHandler</c>: its actions are popped without their lines or the members' stacks,
    /// the voices are free to play again, its events are dropped, and each saved action is put back by its kind with its state.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (_actions.Count > 0)
        {
            Pop(new Igi2AiSquadChange { NextAction = 0, PopMembers = false, Deserializing = true });
        }

        Array.Clear(World.VoiceLastPlayed);
        _events.Clear();
        IsDead = reader.Bool();
        Flags = reader.Int();
        TenseTimer = reader.Int();
        RushTimer = reader.Int();
        DisallowIncreaseSuspicionTimer = reader.Int();
        SuspicionLevel = reader.Int();
        int count = reader.Int();
        Igi2AiSquadWords words = new(reader.IntArray());
        for (int i = 0; i < count && !words.IsAtEnd; i++)
        {
            Igi2AiSquadAction action = NewAction((Igi2AiSquadActionKind)words.Int());
            action.LoadState(words, this);
            _actions.Add(action);
        }
    }

    // The action of a saved kind; anything else is idle.
    private static Igi2AiSquadAction NewAction(Igi2AiSquadActionKind kind) => kind switch
    {
        Igi2AiSquadActionKind.HoldArea => new Igi2AiSquadHoldArea(),
        Igi2AiSquadActionKind.Inquire => new Igi2AiSquadInquire(),
        Igi2AiSquadActionKind.SearchArea => new Igi2AiSquadSearchArea(),
        Igi2AiSquadActionKind.Move => new Igi2AiSquadMove(),
        Igi2AiSquadActionKind.Leapfrog => new Igi2AiSquadLeapfrog(),
        Igi2AiSquadActionKind.Patrol => new Igi2AiSquadPatrol(),
        Igi2AiSquadActionKind.MoveSearchInDirection => new Igi2AiSquadMoveSearchInDirection(),
        Igi2AiSquadActionKind.LookSearchInDirection => new Igi2AiSquadLookSearchInDirection(),
        Igi2AiSquadActionKind.Danger => new Igi2AiSquadDanger(),
        _ => new Igi2AiSquadIdle(),
    };
}

using OpenIGI.Net;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A level's script variables -- the <c>Task_id.field</c> values its tasks export, which every one of its scripts reads -- written
/// by a co-op server and put into each client's copy of the level, so the client's scripts read the server's world.
/// </summary>
/// <remarks>
/// <para>
/// Both machines load the same level, so both export the same names; each machine sorts them alike and the values go by place,
/// behind a count and a hash of the names that a client checks before it takes any. Integers and reals (whole, as a variable keeps them) are carried; the
/// level's few text variables are left to each machine.
/// </para>
/// <para>
/// The state is written afresh whenever a value that holds still for a while changes -- a door opened, an alarm raised, an
/// objective done -- and twice a second besides. A value that changes tick after tick (a clock) is marked as one, and its
/// changes alone write nothing new: it goes with the next write, or no later than the refresh.
/// </para>
/// <para>
/// A client writes only what differs from its own, through the variables' own setters, as a script's assignment would.
/// </para>
/// </remarks>
/// <param name="natives">The namespace the level's variables are published in.</param>
/// <param name="include">Whether a variable is the level's to share: a title leaves out what each machine keeps for itself, such as its players' and soldiers' own.</param>
public sealed class ScriptVariableMirror(NativeRegistry natives, Func<string, bool> include)
{
    // How often the whole state is written however little changed: twice a second.
    private const int RefreshTicks = 15;

    // Changes on so many ticks running make a variable a clock.
    private const int ClockRun = 3;

    private readonly BitWriter _writer = new(4096);
    private IQvmVariable[] _variables = [];
    private QvmValue[] _last = [];
    private byte[] _runs = [];
    private bool[] _clock = [];
    private uint _hash;
    private byte[]? _written;
    private long _writtenAt = long.MinValue;

    /// <summary>Gets how many variables are shared.</summary>
    public int Count => Variables().Length;

    /// <summary>Takes the level's variables as this tick leaves them, on the server.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The state as last written, a copy of its own, rewritten when it had to be.</returns>
    public byte[] Capture(long tick)
    {
        IQvmVariable[] variables = Variables();
        bool changed = false;
        for (int i = 0; i < variables.Length; i++)
        {
            QvmValue now = variables[i].Read();
            if (now == _last[i])
            {
                _runs[i] = 0;
                continue;
            }

            _last[i] = now;
            if (_runs[i] < byte.MaxValue && ++_runs[i] >= ClockRun)
            {
                _clock[i] = true;
            }

            changed |= !_clock[i];
        }

        if (_written is null || changed || tick - _writtenAt >= RefreshTicks)
        {
            _writer.Reset();
            _writer.WriteVarUInt((uint)variables.Length);
            _writer.WriteBits(_hash, 32);
            foreach (QvmValue value in _last)
            {
                Write(_writer, value);
            }

            _written = _writer.AsSpan().ToArray();
            _writtenAt = tick;
        }

        return [.. _written];
    }

    /// <summary>Puts a server's variables into this level, on a client.</summary>
    /// <param name="reader">The state, as <see cref="Capture"/> wrote it.</param>
    /// <returns><see langword="false"/> when it was another level's (its names differ), and nothing was taken.</returns>
    public bool Apply(ref BitReader reader)
    {
        IQvmVariable[] variables = Variables();
        uint count = reader.ReadVarUInt();
        uint hash = reader.ReadBits(32);
        if (count != variables.Length || hash != _hash)
        {
            return false;
        }

        for (int i = 0; i < variables.Length && !reader.IsOverflowed; i++)
        {
            if (Read(ref reader) is { } value && value != variables[i].Read())
            {
                variables[i].TryWrite(value, out _);
            }
        }

        return !reader.IsOverflowed;
    }

    // The variables shared, sorted by name, found once the level has exported them.
    private IQvmVariable[] Variables()
    {
        if (_variables.Length > 0)
        {
            return _variables;
        }

        List<(string Name, IQvmVariable Variable)> found = [];
        foreach (string name in natives.Names)
        {
            if (include(name) && natives.TryResolve(name, out QvmSymbol? symbol)
                && symbol is { Kind: QvmSymbolKind.Variable, Variable: { ValueType: QvmValueKind.Integer or QvmValueKind.Float } variable })
            {
                found.Add((name, variable));
            }
        }

        found.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        _variables = [.. found.Select(static f => f.Variable)];
        _last = [.. _variables.Select(static v => v.Read())];
        _runs = new byte[_variables.Length];
        _clock = new bool[_variables.Length];

        // FNV-1a over the names in their order: two levels that share no names, or the same level a mod changed, never match.
        uint hash = 2166136261;
        foreach ((string name, _) in found)
        {
            foreach (char c in name)
            {
                hash = (hash ^ c) * 16777619;
            }

            hash = (hash ^ 0xFF) * 16777619;
        }

        _hash = hash;
        return _variables;
    }

    private static void Write(BitWriter writer, QvmValue value)
    {
        if (value.Kind == QvmValueKind.Float)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value.ToDouble());
            writer.WriteBool(true);
            writer.WriteBits((uint)bits, 32);
            writer.WriteBits((uint)(bits >> 32), 32);
        }
        else
        {
            writer.WriteBool(false);
            writer.WriteVarInt(value.ToInt32());
        }
    }

    private static QvmValue? Read(ref BitReader reader)
    {
        if (!reader.ReadBool())
        {
            return QvmValue.FromInt32(reader.ReadVarInt());
        }

        ulong low = reader.ReadBits(32);
        ulong high = reader.ReadBits(32);
        return QvmValue.FromDouble(BitConverter.UInt64BitsToDouble(low | (high << 32)));
    }
}

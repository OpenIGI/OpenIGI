using OpenIGI.Core.Numerics;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A cursor over a level script's positional arguments, from which task parameters are parsed in order.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's parse state — a four-word block holding an argument cursor, the call context
/// and the file/line for diagnostics — that <c>Task_New</c> threads through every parameter's Parse
/// callback (<c>0x4F0780</c>, §7 of the re-note). The cursor starts at argument 3, after the id, the
/// type name and the editor label.
/// </para>
/// <para>
/// Each <c>Read</c> consumes exactly as many arguments as the corresponding callback in the original
/// does; those counts are tabulated and cross-checked against retail level data in the re-note, §6.6.
/// Two are counter-intuitive: <see cref="ReadReal32x9"/> and <see cref="ReadQuat32"/> each consume
/// <em>three</em> Euler angles rather than nine or four components.
/// </para>
/// </remarks>
public sealed class ScriptArgumentReader
{
    private readonly QvmCallContext _call;

    /// <summary>The first argument a task's parameters start at: after id, type name and label.</summary>
    public const int FirstParameterArgument = 3;

    /// <summary>Initializes a new instance of the <see cref="ScriptArgumentReader"/> class.</summary>
    /// <param name="call">The <c>Task_New</c> call being parsed.</param>
    /// <param name="position">The argument index to start at.</param>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative.</exception>
    public ScriptArgumentReader(QvmCallContext call, int position = FirstParameterArgument)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        _call = call;
        Position = position;
    }

    /// <summary>Gets the index of the next argument to read.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the number of arguments the call site passes.</summary>
    public int ArgumentCount => _call.ArgumentCount;

    /// <summary>Gets the number of arguments left unread.</summary>
    public int Remaining => Math.Max(0, _call.ArgumentCount - Position);

    /// <summary>Reads a 32-bit integer.</summary>
    /// <returns>The value.</returns>
    public int ReadInt32() => Take(1) ? _call.GetInt32(Position - 1) : 0;

    /// <summary>Reads a 16-bit integer, truncating as the original's <c>short</c> store does.</summary>
    /// <returns>The value.</returns>
    public short ReadInt16() => unchecked((short)ReadInt32());

    /// <summary>Reads a one-byte boolean.</summary>
    /// <returns><see langword="true"/> when the argument is non-zero.</returns>
    /// <remarks>The original stores the low byte of an int, so 256 reads back as <see langword="false"/>.</remarks>
    public bool ReadBool8() => unchecked((byte)ReadInt32()) != 0;

    /// <summary>Reads a single-precision scalar.</summary>
    /// <returns>The value.</returns>
    public float ReadReal32() => Take(1) ? _call.GetSingle(Position - 1) : 0f;

    /// <summary>Reads a double-precision scalar.</summary>
    /// <returns>The value.</returns>
    /// <remarks>
    /// The VM's stack is 32-bit, so the original's <c>Real64</c> callback (<c>0x4EFF30</c>) reads a float
    /// and widens it on store. The widening, not the read, is what makes world positions doubles.
    /// </remarks>
    public double ReadReal64() => ReadReal32();

    /// <summary>Reads three single-precision values.</summary>
    /// <returns>The vector.</returns>
    public Vector3f ReadReal32x3()
    {
        float x = ReadReal32();
        float y = ReadReal32();
        float z = ReadReal32();
        return new Vector3f(x, y, z);
    }

    /// <summary>Reads three double-precision values.</summary>
    /// <returns>The vector.</returns>
    /// <remarks>This is what <c>ObjectPos</c> uses: world positions are doubles, and a float port diverges.</remarks>
    public Vector3d ReadReal64x3()
    {
        double x = ReadReal64();
        double y = ReadReal64();
        double z = ReadReal64();
        return new Vector3d(x, y, z);
    }

    /// <summary>Reads the three Euler angles a <c>Real32x9</c> orientation is stored as in level data.</summary>
    /// <returns>The alpha, beta and gamma angles, in radians.</returns>
    /// <remarks>
    /// <para>
    /// Despite the name, the wire form is <em>three</em> angles, not nine matrix components: the
    /// original's callback (<c>0x4EFBA0</c>) reads three floats and hands them to <c>sub_4B38E0</c>,
    /// which concatenates three axis rotations into the in-memory 3×3 matrix.
    /// </para>
    /// <para>
    /// The angles are returned raw. The matrix expansion needs the axis order and handedness of
    /// <c>sub_4B38E0</c>, which lands with the orientation work in M6 — see the re-note, §12.
    /// </para>
    /// </remarks>
    public Vector3f ReadReal32x9() => ReadReal32x3();

    /// <summary>Reads a quaternion, which level data stores as three Euler angles.</summary>
    /// <returns>The quaternion.</returns>
    /// <remarks>
    /// Reproduces <c>sub_4B45A0</c>: half-angle sines and cosines combined in the order alpha, beta,
    /// gamma. Consumes three arguments, not four.
    /// </remarks>
    public Quaternion32 ReadQuat32()
    {
        double alpha = ReadReal32() * 0.5;
        double beta = ReadReal32() * 0.5;
        double gamma = ReadReal32() * 0.5;

        double sinA = Math.Sin(alpha);
        double cosA = Math.Cos(alpha);
        double sinB = Math.Sin(beta);
        double cosB = Math.Cos(beta);
        double sinG = Math.Sin(gamma);
        double cosG = Math.Cos(gamma);

        double ba = cosB * sinA;
        double ab = sinB * cosA;
        double cc = cosA * cosB;
        double ss = sinB * sinA;

        return new Quaternion32(
            (float)((ba * cosG) - (ab * sinG)),
            (float)((ab * cosG) + (ba * sinG)),
            (float)((cc * sinG) - (ss * cosG)),
            (float)((cc * cosG) + (ss * sinG)));
    }

    /// <summary>Reads a string.</summary>
    /// <returns>The text, or the empty string when the argument is not one.</returns>
    /// <remarks>
    /// Backs every string type. The original's fixed-width variants copy into inline buffers of 16, 32
    /// or 256 bytes and would truncate; nothing in retail level data reaches those limits, and silently
    /// truncating a mod's longer string would be worse than keeping it.
    /// </remarks>
    public string ReadString() => Take(1) ? _call.GetString(Position - 1) : string.Empty;

    /// <summary>Reads an envelope curve: eleven single-precision values.</summary>
    /// <returns>The eleven values in wire order.</returns>
    /// <remarks>
    /// The count is exact (<c>0x4EF860</c>); what the values <em>mean</em> is not yet reverse-engineered,
    /// so they are handed back raw. See the re-note, §12.
    /// </remarks>
    public float[] ReadGraphEnvelope()
    {
        var values = new float[11];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = ReadReal32();
        }

        return values;
    }

    /// <summary>Reads an animation key list: a count, then that many code/value pairs.</summary>
    /// <returns>The entries in wire order.</returns>
    /// <remarks>
    /// The only variable-length parameter type. Reproduces <c>0x4F3570</c>, including its one quirk:
    /// an entry whose code is 32 has its value truncated toward zero before it is stored.
    /// </remarks>
    public AnimDataEntry[] ReadAnimData()
    {
        int count = ReadInt32();
        if (count <= 0)
        {
            return [];
        }

        var entries = new AnimDataEntry[count];
        for (int i = 0; i < count; i++)
        {
            int code = unchecked((sbyte)ReadInt32());
            float value = ReadReal32();
            entries[i] = new AnimDataEntry(code, code == 32 ? (int)value : value);
        }

        return entries;
    }

    /// <summary>Reads a <c>Graph</c>, which consumes no arguments.</summary>
    /// <remarks>The original's Parse callback for <c>Graph</c> is a no-op, so the cursor does not move.</remarks>
    public void ReadGraph() => Skip(ScriptParamTypeInfo.GetArgumentCount(ScriptParamType.Graph));

    /// <summary>Skips a number of arguments.</summary>
    /// <param name="count">How many to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Position += count;
    }

    /// <inheritdoc/>
    public override string ToString() => $"arg {Position}/{_call.ArgumentCount}";

    private bool Take(int count)
    {
        // The original's sub_4F1C20 is an "at least n arguments remain" guard that reports
        // "Too few parameters in line %d of file '%s'" and never returns. Here a short argument list
        // yields defaults instead, so one malformed task in a mod cannot take the process down; the
        // cursor still advances so every following parameter stays on its own wire position.
        bool available = Position + count <= _call.ArgumentCount;
        Position += count;
        return available;
    }
}

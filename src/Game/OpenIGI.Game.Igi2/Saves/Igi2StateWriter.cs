using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// Something a save game keeps the state of, a task type with a serialize parameter list (<c>Serialize_eGetSerializeParameterListQTaskEvent</c>):
/// it writes its fields in its list's order, its base type's first, and reads them back in the same order.
/// </summary>
public interface IIgi2Serialized
{
    /// <summary>Writes its state, <c>Serialize_WriteParameterList</c>.</summary>
    /// <param name="writer">The writer.</param>
    void WriteState(Igi2StateWriter writer);

    /// <summary>Reads its state back, <c>Serialize_ParseParameterList</c>, then fixes itself up as its deserialize handler does.</summary>
    /// <param name="reader">The reader.</param>
    void ReadState(Igi2StateReader reader);

    /// <summary>
    /// Gets a value indicating whether it is written against its nearest ancestor that has a level id, rather than itself
    /// (<c>Serialize_SaveQTaskTypeWithParent</c>, <c>isUseParentToSerialize</c>).
    /// </summary>
    bool IsSavedWithParent => false;

    /// <summary>
    /// Gets the parts of it that retail keeps as child tasks of their own, each with its type's name, written as those children are,
    /// before it.
    /// </summary>
    IReadOnlyList<Igi2SerializedPart> SerializedParts => [];
}

/// <summary>A part of a task that retail keeps as a child task of its own type, with its own state, such as a camera's lens.</summary>
/// <param name="TypeName">The child's type.</param>
/// <param name="State">Its state.</param>
public sealed record Igi2SerializedPart(string TypeName, IIgi2Serialized State);

/// <summary>
/// Writes a task's fields as the save script's arguments, each as its parameter type prints it (<c>ParameterList_eQParameterPrintEvent</c>,
/// level.c): integers in decimal, floats to twenty significant figures, booleans as <c>TRUE</c> or <c>FALSE</c>, strings quoted.
/// </summary>
public sealed class Igi2StateWriter
{
    private readonly StringBuilder _text = new();
    private readonly Func<GameTask, string>? _taskReference;

    /// <summary>Initializes a new instance of the <see cref="Igi2StateWriter"/> class.</summary>
    /// <param name="taskReference">How a task a field refers to is written: its hierarchy, or <c>-1</c>.</param>
    public Igi2StateWriter(Func<GameTask, string>? taskReference = null) => _taskReference = taskReference;

    /// <summary>Gets the arguments written, each preceded by a comma and a space.</summary>
    public string Arguments => _text.ToString();

    /// <summary>Writes a boolean, <c>Level_Printbool8Handler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Bool(bool value) => Add(value ? "TRUE" : "FALSE");

    /// <summary>Writes an integer, <c>Level_PrintInt32Handler</c> and <c>Level_PrintInt16Handler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Int(int value) => Add(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes a float, <c>Level_PrintReal32Handler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Real(float value) => Add(Format(value));

    /// <summary>Writes a double, <c>Level_PrintReal64Handler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Real64(double value) => Add(Format(value));

    /// <summary>Writes three floats, <c>Level_PrintReal32x3Handler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Real3(Vector3f value)
    {
        Real(value.X);
        Real(value.Y);
        Real(value.Z);
    }

    /// <summary>Writes three doubles, <c>Level_PrintReal64x3Handler</c> and <c>Level_PrintObjectPosHandler</c>.</summary>
    /// <param name="value">The value.</param>
    public void Real64x3(Vector3d value)
    {
        Real64(value.X);
        Real64(value.Y);
        Real64(value.Z);
    }

    /// <summary>Writes nine floats row by row, <c>Level_PrintReal32x9ExactHandler</c>.</summary>
    /// <param name="value">The rows.</param>
    public void Real9(IReadOnlyList<float> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        for (int i = 0; i < 9; i++)
        {
            Real(value[i]);
        }
    }

    /// <summary>Writes a string, quoted with its tabs and line breaks escaped, <c>Level_PrintString</c>.</summary>
    /// <param name="value">The value.</param>
    public void Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder quoted = new("\"");
        foreach (char c in value)
        {
            quoted.Append(c switch
            {
                '\t' => "\\t",
                '\n' => "\\n",
                '\r' => "\\r",
                '"' => "\\\"",
                '\\' => "\\\\",
                _ => c.ToString(),
            });
        }

        Add(quoted.Append('"').ToString());
    }

    /// <summary>
    /// Writes raw bytes, <c>SerializeArray_ParameterPrintHandler</c>: the count of whole words and of bytes over, each word as eight hex
    /// digits read most significant byte first in memory order, then each byte over as two.
    /// </summary>
    /// <param name="bytes">The bytes, as they lie in memory.</param>
    public void Array(ReadOnlySpan<byte> bytes)
    {
        int words = bytes.Length >> 2, over = bytes.Length - (words * 4);
        Int(words);
        Int(over);
        for (int i = 0; i < words; i++)
        {
            int at = i * 4;
            uint word = ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3];
            Add("0x" + word.ToString("x8", CultureInfo.InvariantCulture));
        }

        for (int i = words * 4; i < bytes.Length; i++)
        {
            Add("0x" + bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes floats as raw bytes, as an array of them lies in memory.</summary>
    /// <param name="values">The values.</param>
    public void Array(ReadOnlySpan<float> values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), values[i]);
        }

        Array(bytes);
    }

    /// <summary>Writes integers as raw bytes, as an array of them lies in memory.</summary>
    /// <param name="values">The values.</param>
    public void Array(ReadOnlySpan<int> values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), values[i]);
        }

        Array(bytes);
    }

    /// <summary>Writes a reference to a task, <c>SerializeQTaskRef_ParameterPrintHandler</c>: its hierarchy, or <c>-1</c> for none.</summary>
    /// <param name="task">The task, or <see langword="null"/>.</param>
    public void TaskReference(GameTask? task) => Add(task is not null && _taskReference is not null ? _taskReference(task) : "-1");

    /// <summary>Writes a list, <c>SerializeList_ParameterPrintHandler</c>: its count, then each item's fields.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="write">Writes one item's fields.</param>
    public void List<T>(IReadOnlyCollection<T> items, Action<Igi2StateWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(write);
        Int(items.Count);
        foreach (T item in items)
        {
            write(this, item);
        }
    }

    /// <summary>Formats a value as C's <c>%.20G</c> does, the exponent in the runtime's three digits.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Format(double value)
    {
        string text = value.ToString("G20", CultureInfo.InvariantCulture);
        int e = text.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
        {
            return text;
        }

        // %G keeps a sign and at least three exponent digits in the original's runtime: 1E-05 is written 1E-005.
        string mantissa = text[..e];
        string exponent = text[(e + 1)..];
        char sign = exponent[0] is '-' or '+' ? exponent[0] : '+';
        string digits = exponent.TrimStart('-', '+').PadLeft(3, '0');
        return $"{mantissa}E{sign}{digits}";
    }

    private void Add(string argument) => _text.Append(", ").Append(argument);
}

/// <summary>Reads a task's fields back from the save script's arguments, in the order they were written.</summary>
public sealed class Igi2StateReader
{
    private readonly Igi2ScriptCall _call;
    private readonly Func<Igi2StateReader, GameTask?>? _taskReference;

    /// <summary>Initializes a new instance of the <see cref="Igi2StateReader"/> class.</summary>
    /// <param name="call">The <c>DefineTaskState</c> call.</param>
    /// <param name="next">The first argument of the task's fields.</param>
    /// <param name="taskReference">How a task reference is read back, from the reader's next arguments.</param>
    public Igi2StateReader(Igi2ScriptCall call, int next, Func<Igi2StateReader, GameTask?>? taskReference = null)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
        Next = next;
        _taskReference = taskReference;
    }

    /// <summary>Gets the next argument to read.</summary>
    public int Next { get; private set; }

    /// <summary>Gets a value indicating whether every argument is read.</summary>
    public bool IsAtEnd => Next >= _call.Count;

    /// <summary>Reads a boolean.</summary>
    /// <returns>The value; false past the end.</returns>
    public bool Bool() => Int() != 0;

    /// <summary>Reads an integer.</summary>
    /// <returns>The value; nought past the end.</returns>
    public int Int() => IsAtEnd ? Skip(0) : _call.GetInt32(Next++);

    /// <summary>Reads a float.</summary>
    /// <returns>The value; nought past the end.</returns>
    public float Real() => IsAtEnd ? Skip(0f) : _call.GetSingle(Next++);

    /// <summary>Reads a double.</summary>
    /// <returns>The value; nought past the end.</returns>
    public double Real64() => IsAtEnd ? Skip(0.0) : _call.GetReal(Next++);

    /// <summary>Reads three floats.</summary>
    /// <returns>The value.</returns>
    public Vector3f Real3() => new(Real(), Real(), Real());

    /// <summary>Reads three doubles.</summary>
    /// <returns>The value.</returns>
    public Vector3d Real64x3() => new(Real64(), Real64(), Real64());

    /// <summary>Reads nine floats row by row.</summary>
    /// <returns>The rows.</returns>
    public float[] Real9()
    {
        float[] rows = new float[9];
        for (int i = 0; i < 9; i++)
        {
            rows[i] = Real();
        }

        return rows;
    }

    /// <summary>Reads a string.</summary>
    /// <returns>The value; empty past the end.</returns>
    public string Text() => IsAtEnd ? Skip(string.Empty) : _call.GetText(Next++);

    /// <summary>Reads raw bytes written by <see cref="Igi2StateWriter.Array(ReadOnlySpan{byte})"/>.</summary>
    /// <returns>The bytes, as they lay in memory.</returns>
    public byte[] Array()
    {
        int words = Int(), over = Int();
        byte[] bytes = new byte[Math.Max(words, 0) * 4 + Math.Max(over, 0)];
        for (int i = 0; i < words; i++)
        {
            uint word = unchecked((uint)Int());
            bytes[i * 4] = (byte)(word >> 24);
            bytes[(i * 4) + 1] = (byte)(word >> 16);
            bytes[(i * 4) + 2] = (byte)(word >> 8);
            bytes[(i * 4) + 3] = (byte)word;
        }

        for (int i = 0; i < over; i++)
        {
            bytes[(words * 4) + i] = (byte)Int();
        }

        return bytes;
    }

    /// <summary>Reads floats written as raw bytes.</summary>
    /// <returns>The values.</returns>
    public float[] FloatArray()
    {
        byte[] bytes = Array();
        float[] values = new float[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToSingle(bytes, i * 4);
        }

        return values;
    }

    /// <summary>Reads integers written as raw bytes.</summary>
    /// <returns>The values.</returns>
    public int[] IntArray()
    {
        byte[] bytes = Array();
        int[] values = new int[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt32(bytes, i * 4);
        }

        return values;
    }

    /// <summary>Reads a task reference: <c>-1</c> for none, otherwise a hierarchy.</summary>
    /// <returns>The task, or <see langword="null"/>.</returns>
    public GameTask? TaskReference()
    {
        if (IsAtEnd)
        {
            return null;
        }

        if (_call.GetText(Next) is "-1" || (_call.GetInt32(Next) == -1 && _taskReference is null))
        {
            Next++;
            return null;
        }

        return _taskReference?.Invoke(this);
    }

    /// <summary>Reads a list written by <see cref="Igi2StateWriter.List{T}"/>.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <param name="read">Reads one item's fields.</param>
    /// <returns>The items.</returns>
    public List<T> List<T>(Func<Igi2StateReader, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        int count = Int();
        List<T> items = new(Math.Max(count, 0));
        for (int i = 0; i < count && !IsAtEnd; i++)
        {
            items.Add(read(this));
        }

        return items;
    }

    private T Skip<T>(T value)
    {
        Next++;
        return value;
    }
}

using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Editor.Core.Configs;

/// <summary>One config call of a config script, with the schema that names its values.</summary>
/// <param name="Name">The call's name (<c>DefineWeaponType</c>).</param>
/// <param name="Schema">Its schema, or null for a call without one.</param>
/// <param name="Values">Its values, as they now stand.</param>
public sealed record ConfigCall(string Name, ConfigSchema? Schema, List<QscExpression> Values);

/// <summary>
/// A config script (<c>weapons/AK47/weapon.qvm</c>) as a form edits it: nothing but calls, one after another, each value of
/// each one changed in place and compiled back to the script the game loads.
/// </summary>
/// <remarks>A script that does anything else (branches, loops, assignments) is not a config and is left to the Script panel.</remarks>
public sealed class ConfigDocument
{
    private readonly string[][] _saved;

    private readonly int _version;

    private ConfigDocument(int version, IReadOnlyList<ConfigCall> calls)
    {
        _version = version;
        Calls = calls;
        _saved = [.. calls.Select(static c => c.Values.Select(QscPrinter.Print).ToArray())];
    }

    /// <summary>Gets its calls, in order.</summary>
    public IReadOnlyList<ConfigCall> Calls { get; }

    /// <summary>Gets what the file defines, for the editor's name (<c>Weapon</c>), or <c>Config</c>.</summary>
    public string Title => Calls.Select(static c => c.Schema?.Title).FirstOrDefault(static t => t is not null) ?? "Config";

    /// <summary>Gets whether any value differs from the file as last read or saved.</summary>
    public bool IsDirty
    {
        get
        {
            for (int c = 0; c < Calls.Count; c++)
            {
                for (int i = 0; i < Calls[c].Values.Count; i++)
                {
                    if (IsModified(c, i))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Reads a compiled script as a config, if it is one.</summary>
    /// <param name="qvm">The compiled script.</param>
    /// <returns>The document, or null for a script that is not only calls, or not a script at all.</returns>
    public static ConfigDocument? TryRead(ReadOnlyMemory<byte> qvm)
    {
        if (!QvmProgram.IsQvm(qvm.Span))
        {
            return null;
        }

        QscScript script;
        try
        {
            script = QscDecompiler.Decompile(qvm);
        }
        catch (Exception error) when (error is QvmFormatException or QscDecompileException)
        {
            return null;
        }

        List<ConfigCall> calls = [];
        foreach (QscStatement statement in script.Statements)
        {
            if (statement is not QscExpressionStatement { Expression: QscCall call })
            {
                return null;
            }

            calls.Add(new ConfigCall(call.Name, ConfigSchemas.For(call.Name), [.. call.Arguments]));
        }

        return calls.Count == 0 ? null : new ConfigDocument(script.Version, calls);
    }

    /// <summary>Gets a value as the script writes it.</summary>
    /// <param name="call">The call's position.</param>
    /// <param name="index">The value's position.</param>
    /// <returns>The value's text.</returns>
    public string Text(int call, int index) => QscPrinter.Print(Calls[call].Values[index]);

    /// <summary>Sets a value.</summary>
    /// <param name="call">The call's position.</param>
    /// <param name="index">The value's position.</param>
    /// <param name="value">The new value.</param>
    public void Set(int call, int index, QscExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Calls[call].Values[index] = value;
    }

    /// <summary>Sets a value from text the script could write (<c>10 * WORLD_METER</c>).</summary>
    /// <param name="call">The call's position.</param>
    /// <param name="index">The value's position.</param>
    /// <param name="text">The text.</param>
    /// <param name="error">Receives what is wrong with text that is not an expression.</param>
    /// <returns>Whether it was set.</returns>
    public bool TrySetText(int call, int index, string text, out string? error)
    {
        try
        {
            Set(call, index, QscParser.ParseExpression(text));
            error = null;
            return true;
        }
        catch (QscParseException parse)
        {
            error = parse.Problem;
            return false;
        }
    }

    /// <summary>Gets whether a value differs from the file as last read or saved.</summary>
    /// <param name="call">The call's position.</param>
    /// <param name="index">The value's position.</param>
    /// <returns>Whether it changed.</returns>
    public bool IsModified(int call, int index) =>
        index >= _saved[call].Length || !string.Equals(_saved[call][index], Text(call, index), StringComparison.Ordinal);

    /// <summary>Puts a value back as the file last had it.</summary>
    /// <param name="call">The call's position.</param>
    /// <param name="index">The value's position.</param>
    public void Revert(int call, int index)
    {
        if (index < _saved[call].Length)
        {
            Set(call, index, QscParser.ParseExpression(_saved[call][index]));
        }
    }

    /// <summary>Puts every value back as the file last had it.</summary>
    public void RevertAll()
    {
        for (int c = 0; c < Calls.Count; c++)
        {
            for (int i = 0; i < Calls[c].Values.Count; i++)
            {
                Revert(c, i);
            }
        }
    }

    /// <summary>Compiles the script as it now stands, as the game loads it.</summary>
    /// <returns>The compiled script.</returns>
    public byte[] Compile() => QscCompiler.Compile(Script());

    /// <summary>Builds the script as it now stands.</summary>
    /// <returns>The script.</returns>
    public QscScript Script() =>
        new([.. Calls.Select(static c => (QscStatement)new QscExpressionStatement(new QscCall(c.Name, [.. c.Values])))], _version);

    /// <summary>Takes the values as they now stand as the saved ones.</summary>
    public void MarkSaved()
    {
        for (int c = 0; c < Calls.Count; c++)
        {
            _saved[c] = [.. Calls[c].Values.Select(QscPrinter.Print)];
        }
    }
}

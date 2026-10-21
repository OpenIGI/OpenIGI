using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// The sound definitions a game's <c>sounds.qvm</c> scripts declare: the tree of groups they make and the first definition of
/// every name, which both games keep the same way.
/// </summary>
/// <remarks>
/// <para>
/// What differs between the games is how a <c>Define*</c> call's arguments are read, so each game's reader parses the call
/// and hands the definition to <see cref="Add"/>. A group is the same in both: it is made under the current parent first,
/// and only then are its arguments -- the nested <c>Define*</c> calls -- forced, with the natives adding to it
/// (<see cref="DefineGroup"/>).
/// </para>
/// <para>A name resolves to its first definition, pre-order: the order the original's find walk meets them, stopping at the first.</para>
/// </remarks>
public sealed class SoundDefinitionTable : ISoundDefinitionLookup
{
    private readonly List<SoundDefinition> _roots = [];
    private readonly Dictionary<string, SoundDefinition> _byName = new(StringComparer.Ordinal);
    private readonly Stack<List<SoundDefinition>> _open = new();

    /// <summary>Gets the top-level definitions, in declaration order.</summary>
    public IReadOnlyList<SoundDefinition> Roots => _roots;

    /// <summary>Gets the number of distinct names.</summary>
    public int Count => _byName.Count;

    /// <summary>Forgets every definition.</summary>
    public void Clear()
    {
        _roots.Clear();
        _byName.Clear();
        _open.Clear();
    }

    /// <inheritdoc/>
    public bool TryResolve(string name, out SoundDefinition? definition)
    {
        if (string.IsNullOrEmpty(name))
        {
            definition = null;
            return false;
        }

        return _byName.TryGetValue(name, out definition);
    }

    /// <summary>Adds a definition under the group being defined, or at the top level when none is.</summary>
    /// <param name="definition">The definition.</param>
    public void Add(SoundDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        (_open.Count > 0 ? _open.Peek() : _roots).Add(definition);
        _byName.TryAdd(definition.Name, definition);
    }

    /// <summary>Defines a group from a <c>DefineGroup</c> call: argument 0 its name, every one after it a nested definition.</summary>
    /// <param name="call">The call.</param>
    /// <returns>The index after the last argument forced.</returns>
    /// <remarks>
    /// The group is added before its children are forced, so the lookup meets it before anything nested in it; its list is
    /// filled in place afterwards, keeping the group's own declaration order. QVM arguments are unevaluated code, which is
    /// what lets a native change what its own arguments do before asking for them.
    /// </remarks>
    public int DefineGroup(QvmCallContext call)
    {
        List<SoundDefinition> children = [];
        Add(new SoundDefinition
        {
            Name = call.GetString(0),
            BankKey = string.Empty,
            Kind = SoundDefinitionKind.Group,
            Children = children,
        });

        int argument = 1;
        _open.Push(children);
        try
        {
            for (; argument < call.ArgumentCount; argument++)
            {
                call.GetInt32(argument);
            }
        }
        finally
        {
            _open.Pop();
        }

        return argument;
    }

    /// <summary>Runs one <c>sounds.qvm</c> against natives that define into this table.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The namespace, with the game's <c>Define*</c> natives registered.</param>
    /// <param name="resource">The script's resource name.</param>
    /// <returns><see langword="false"/> when there is no such script.</returns>
    /// <exception cref="EngineFatalException">The script cannot be read, or it faults.</exception>
    public bool Run(ResourceRegistry resources, NativeRegistry natives, string resource)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentException.ThrowIfNullOrEmpty(resource);
        if (!resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            return false;
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {resource}", ex);
        }

        _open.Clear();
        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"{resource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        return true;
    }
}

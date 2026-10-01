using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// The global script namespace: every native function, constant and variable a program can name.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps one process-wide hash table that subsystems fill during start-up — around 160
/// native registrations plus the input, language and difficulty constant blocks. A registry instance
/// plays that role here, so tests and tools can build a namespace containing exactly what they want.
/// </para>
/// <para>
/// Names are case-sensitive, matching the original's lookup. Registering a name that is already present
/// rebinds it, which is how a level reload re-points the define natives and constants at fresh instances.
/// </para>
/// </remarks>
public sealed class NativeRegistry
{
    private readonly Dictionary<string, QvmSymbol> _symbols = new(StringComparer.Ordinal);

    /// <summary>Gets the number of registered symbols.</summary>
    public int Count => _symbols.Count;

    /// <summary>Gets the registered names.</summary>
    public IReadOnlyCollection<string> Names => _symbols.Keys;

    /// <summary>Registers a native function.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="function">The implementation.</param>
    /// <param name="argumentCount">The declared argument count.</param>
    /// <param name="returnType">Which representation of the result a call pushes. Defaults to an integer, as the original's registration does.</param>
    /// <param name="context">The object the native receives on every call.</param>
    /// <returns>The registered symbol.</returns>
    public QvmSymbol RegisterFunction(
        string name,
        QvmNativeFunction function,
        int argumentCount = 0,
        QvmValueKind returnType = QvmValueKind.None,
        object? context = null) =>
        Add(QvmSymbol.CreateFunction(name, function, argumentCount, returnType, context));

    /// <summary>Registers an integer constant.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The registered symbol.</returns>
    public QvmSymbol RegisterConstant(string name, int value) => Add(QvmSymbol.CreateConstant(name, value));

    /// <summary>Registers a real-valued constant — the engine's <c>sub_4B8870</c> registrar.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="value">The constant value.</param>
    /// <returns>The symbol.</returns>
    public QvmSymbol RegisterRealConstant(string name, double value) =>
        Add(QvmSymbol.CreateRealConstant(name, value));

    /// <summary>Registers a variable.</summary>
    /// <param name="name">The script-visible name.</param>
    /// <param name="variable">The storage binding.</param>
    /// <returns>The registered symbol.</returns>
    public QvmSymbol RegisterVariable(string name, IQvmVariable variable) => Add(QvmSymbol.CreateVariable(name, variable));

    /// <summary>Removes a symbol.</summary>
    /// <param name="name">The symbol to remove.</param>
    /// <returns><see langword="true"/> when the name was registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Models <c>Script_UnbindVariable</c> (<c>0x4B80A0</c>). Tasks bind named variables when they are
    /// exported and drop them again on unexport, so the namespace has to shrink as well as grow.
    /// </remarks>
    public bool Unregister(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _symbols.Remove(name);
    }

    /// <summary>Rebinds the object a symbol's native receives.</summary>
    /// <param name="name">The symbol to rebind.</param>
    /// <param name="context">The object to bind.</param>
    /// <exception cref="EngineFatalException">No such symbol is registered.</exception>
    /// <remarks>
    /// Fatal on a missing name because the original's <c>Script_SetSymbolContext</c> is: it reports
    /// "Symbol not found" and never returns. Level loading depends on this having taken effect.
    /// </remarks>
    public void SetSymbolContext(string name, object? context)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_symbols.TryGetValue(name, out QvmSymbol? symbol))
        {
            throw new EngineFatalException($"Script_SetSymbolContext(): Symbol not found: {name}");
        }

        symbol.Context = context;
    }

    /// <summary>Looks up a symbol by name.</summary>
    /// <param name="name">The name a script used.</param>
    /// <param name="symbol">Receives the symbol when one is registered.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    public bool TryResolve(string name, [NotNullWhen(true)] out QvmSymbol? symbol)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _symbols.TryGetValue(name, out symbol);
    }

    /// <summary>Determines whether a name is registered.</summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _symbols.ContainsKey(name);
    }

    private QvmSymbol Add(QvmSymbol symbol)
    {
        // Re-registering a name rebinds it rather than failing. That is what makes a second level load
        // work: the define scripts (DefineWeaponType, the WEAPON_ID_* constants, the material and player
        // tables) run again against fresh registry instances, and this re-points the symbol at the new
        // one — exactly what the original does with Script_SetSymbolContext plus re-running the defines,
        // rather than a process that can only ever load one mission.
        _symbols[symbol.Name] = symbol;
        return symbol;
    }
}

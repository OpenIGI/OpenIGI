using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Config;

/// <summary>
/// The <c>TASKTYPE_*</c> constants a configuration script names, given ids in the script namespace and remembered by id so
/// a definition's task type can be read back by name.
/// </summary>
/// <remarks>
/// <para>
/// The weapon, magic-object and animation-trigger tables each name task types by these constants, and all three share the
/// one process-wide namespace, which outlives every table: a mission restart, a level change, or simply the table loaded
/// second finds a constant already there. Such a constant keeps its id, and that id is adopted into this table's own map.
/// Skipping it instead left the map without the name -- every task type the table read back came out empty, which is what
/// made the optical weapons lose their zoom after a restart (issue #79) -- and numbering afresh from one gave two names the
/// same id.
/// </para>
/// <para>A constant new to the namespace takes the next id after the highest this table has seen.</para>
/// </remarks>
public sealed class TaskTypeConstants
{
    /// <summary>The prefix the task-type constants share.</summary>
    public const string Prefix = "TASKTYPE_";

    private readonly Dictionary<int, string> _names = [];
    private int _next = 1;

    /// <summary>Declares the task-type constants a script names that the namespace lacks, and adopts the ones it has.</summary>
    /// <param name="program">The script about to run.</param>
    /// <param name="natives">The namespace it runs against.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Declare(QvmProgram program, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(natives);

        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is null || !symbol.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (natives.TryResolve(symbol, out QvmSymbol? existing) && existing.IsReadable)
            {
                int existingId = existing.Read().ToInt32();
                _names[existingId] = symbol;
                _next = Math.Max(_next, existingId + 1);
                continue;
            }

            natives.RegisterConstant(symbol, _next);
            _names[_next] = symbol;
            _next++;
        }
    }

    /// <summary>Gets the constant a task-type id was declared as.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The constant's name, or empty for an id this table has not seen.</returns>
    public string NameOf(int id) => _names.GetValueOrDefault(id, string.Empty);

    /// <summary>Forgets every id, for a table about to be read again.</summary>
    public void Clear()
    {
        _names.Clear();
        _next = 1;
    }
}

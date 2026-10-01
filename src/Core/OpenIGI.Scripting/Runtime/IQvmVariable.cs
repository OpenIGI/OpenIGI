namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// Engine state a script can read and assign to by name.
/// </summary>
/// <remarks>
/// The original binds a variable symbol to a raw pointer plus a width, and its assignment handler
/// converts and truncates according to both. Each implementation here owns the rules for one storage
/// type so the interpreter stays free of them.
/// </remarks>
public interface IQvmVariable
{
    /// <summary>Gets the type the variable stores. An assignment of any other type fails.</summary>
    QvmValueKind ValueType { get; }

    /// <summary>Reads the current value.</summary>
    /// <returns>The value, tagged with <see cref="ValueType"/>.</returns>
    QvmValue Read();

    /// <summary>Stores a value, converting it the way the original's assignment handler does.</summary>
    /// <param name="value">The value to store.</param>
    /// <param name="stored">Receives what was actually stored, which an assignment pushes back onto the stack.</param>
    /// <returns><see langword="false"/> when <paramref name="value"/> has a type this variable cannot hold.</returns>
    bool TryWrite(QvmValue value, out QvmValue stored);
}

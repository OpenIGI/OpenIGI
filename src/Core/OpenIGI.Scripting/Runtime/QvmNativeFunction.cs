namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// The engine side of a script call.
/// </summary>
/// <param name="call">The call in progress: the bound context and the unevaluated arguments.</param>
/// <returns>
/// The result. The caller reads whichever representation the symbol's declared return type names, so
/// returning <see cref="QvmValue.Zero"/> is the way to return nothing.
/// </returns>
/// <remarks>
/// Arguments arrive unevaluated. A native may read an argument any number of times — including none —
/// and each read runs its expression again, which is what makes script-side conditionals and
/// side-effecting arguments behave the way the original's do.
/// </remarks>
public delegate QvmValue QvmNativeFunction(QvmCallContext call);

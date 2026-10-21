namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Marks a task class whose parameter list is its base class's list with its own parameters appended.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2 builds a derived type's parameter list from its base type's (<c>ParameterList_Derive</c>): a
/// <c>Terminal</c> reads every <c>ExplodeObject</c> parameter first, then its own. A class carrying this
/// attribute gets a generated binder that binds the base class's parameters before its own
/// <see cref="ScriptParamAttribute"/> members, and reports both lists as one.
/// </para>
/// <para>
/// Without the attribute a class's own parameters replace its base's, which is what a task whose
/// script declares its parameters afresh needs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DerivedScriptParametersAttribute : Attribute
{
}

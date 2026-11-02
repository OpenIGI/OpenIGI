using OpenIGI.Engine.Tasks;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>One parameter of a task type, as a level script's <c>Task_DeclareParameters</c> names it.</summary>
/// <param name="Name">The parameter's name, often prose ("Distance to first switch").</param>
/// <param name="TypeName">The value-type name exactly as written (<c>ObjectPos</c>, <c>bool8</c>, <c>RGB</c>…).</param>
public sealed record TaskParameterDeclaration(string Name, string TypeName)
{
    /// <summary>Gets the value type, or <see langword="null"/> when the name is not one the engine knows.</summary>
    public ScriptParamType? Type { get; } =
        ScriptParamTypeInfo.TryParseName(TypeName, out ScriptParamType type) ? type : null;
}

/// <summary>
/// A task type's parameter list, declared by the level script before any task of the type is created.
/// </summary>
/// <param name="TypeName">The task type's name (<c>EditRigidObj</c>, <c>HumanSoldier</c>…).</param>
/// <param name="Parameters">The parameters, in wire order.</param>
/// <remarks>
/// Declaration order is wire order: <c>Task_New</c> reads its parameters positionally from one flat argument
/// list, so this list is what splits a task's arguments into values and children.
/// </remarks>
public sealed record TaskDeclaration(string TypeName, IReadOnlyList<TaskParameterDeclaration> Parameters)
{
    /// <summary>Finds a parameter by name.</summary>
    /// <param name="name">The declared name, compared ordinally.</param>
    /// <returns>The index, or -1 when the type declares no such parameter.</returns>
    public int IndexOf(string name)
    {
        for (int i = 0; i < Parameters.Count; i++)
        {
            if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

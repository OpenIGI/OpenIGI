namespace OpenIGI.Scripting.Expressions;

/// <summary>The two name rules every level expression follows, in both games: <c>this.</c> and <c>.isExists</c>.</summary>
/// <remarks>
/// A task's expressions name its own variables as <c>this.field</c>, which reads as the task's name in its place; and any
/// name holding <c>.isExists</c> reads as true, since the task it asks about was loaded with the level.
/// </remarks>
public static class LevelExpressionNames
{
    /// <summary>The prefix that names the expression's own task.</summary>
    public const string ThisPrefix = "this.";

    /// <summary>The field that asks whether a task exists, which always reads as true.</summary>
    public const string ExistsField = ".isExists";

    /// <summary>Puts a task's name in place of a leading <c>this</c>.</summary>
    /// <param name="name">The name as written.</param>
    /// <param name="owner">The expression's own task's name.</param>
    /// <returns><c>owner.field</c> for <c>this.field</c>; the name as written otherwise.</returns>
    public static string ResolveThis(string name, string owner)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(owner);
        return name.StartsWith(ThisPrefix, StringComparison.Ordinal)
            ? string.Concat(owner, name.AsSpan(ThisPrefix.Length - 1))
            : name;
    }

    /// <summary>Gets whether a name asks whether a task exists.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether it holds <c>.isExists</c>.</returns>
    public static bool IsExistsProbe(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Contains(ExistsField, StringComparison.Ordinal);
    }
}

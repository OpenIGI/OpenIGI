using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.World;

/// <summary>One task type's exported fields as the original binds them, for types the port's tasks do not export themselves.</summary>
/// <param name="Type">The task type's name.</param>
/// <param name="Parent">The type it is registered under, whose fields it exports first, or <see langword="null"/>.</param>
/// <param name="Fields">Its own fields, in the order its export handler binds them.</param>
/// <param name="Source">Where the original binds them: the export handler's address, or its registrar's when it has no fields of its own.</param>
public sealed record TaskFieldDeclaration(string Type, string? Parent, IReadOnlyList<string> Fields, uint Source);

/// <summary>Turns a title's table of retail export handlers into each type's full field list, typed by the original's naming.</summary>
/// <remarks>
/// Both games' export handlers run their parent's handler first, then bind their own fields, so a type's list is its
/// ancestors' fields followed by its own. Both name fields after the binder they use: <c>is*</c> fields are bytes, <c>n*</c>
/// and <c>e*</c> integers, <c>v*</c> reals and <c>z*</c> text.
/// </remarks>
public static class DeclaredTaskFields
{
    /// <summary>Builds each declared type's fields, its ancestors' first.</summary>
    /// <param name="table">The title's declarations.</param>
    /// <returns>The fields, by type name.</returns>
    /// <exception cref="KeyNotFoundException">A declaration names a parent the table does not declare.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> Build(IEnumerable<TaskFieldDeclaration> table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Dictionary<string, TaskFieldDeclaration> byType = table.ToDictionary(static t => t.Type, StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<NativeParameter>> fields = new(StringComparer.Ordinal);
        foreach (string type in byType.Keys)
        {
            List<string> names = [];
            for (string? at = type; at is not null; at = byType[at].Parent)
            {
                names.InsertRange(0, byType[at].Fields);
            }

            fields[type] = [.. names.Distinct(StringComparer.Ordinal).Select(static n => new NativeParameter(n, TypeOf(n)))];
        }

        return fields;
    }

    /// <summary>The type a field has, from the binder its name follows.</summary>
    /// <param name="field">The field's name.</param>
    /// <returns>Its type.</returns>
    public static ScriptType TypeOf(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return field switch
        {
            _ when field.StartsWith("is", StringComparison.Ordinal) => ScriptType.Bool,
            _ when field[0] is 'n' or 'e' => ScriptType.Int,
            _ when field[0] == 'v' => ScriptType.Real,
            _ when field[0] == 'z' => ScriptType.Text,
            _ => ScriptType.Any,
        };
    }

    /// <summary>Adds declared fields to a vocabulary, over whatever it already has for those types.</summary>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <param name="fields">The fields, by type.</param>
    /// <returns>The vocabulary.</returns>
    public static ScriptVocabulary Add(this ScriptVocabulary vocabulary, IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> fields)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(fields);
        foreach ((string type, IReadOnlyList<NativeParameter> list) in fields)
        {
            vocabulary.AddFields(type, list);
        }

        return vocabulary;
    }
}

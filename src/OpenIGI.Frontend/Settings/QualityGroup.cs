namespace OpenIGI.Frontend.Settings;

/// <summary>One step of a coarse quality choice, and what it sets each variable under it to.</summary>
/// <param name="Name">The step's caption, as the player reads it — <c>Off</c>, <c>High</c>.</param>
/// <param name="Values">Each variable this step writes, paired with the text it writes.</param>
public sealed record QualityLevel(string Name, IReadOnlyList<(string Variable, string Value)> Values)
{
    /// <summary>Initializes a new instance of the <see cref="QualityLevel"/> class from a value list.</summary>
    /// <param name="name">The step's caption.</param>
    /// <param name="values">Each variable the step writes, paired with its value.</param>
    public QualityLevel(string name, params (string Variable, string Value)[] values)
        : this(name, (IReadOnlyList<(string, string)>)values)
    {
    }
}

/// <summary>
/// A coarse quality choice on the graphics page: one caption the player understands over several variables
/// they should not have to.
/// </summary>
/// <remarks>
/// <para>
/// A group is a named point in the space its variables span. Choosing <c>Shadow Quality: High</c> writes
/// six shadow variables at once, and the point of the type is that six is the number it takes for the
/// answer to mean anything — a shadow resolution without its cascade count and its distance is not a
/// setting, it is a third of one.
/// </para>
/// <para>
/// Nothing is hidden by this and nothing is duplicated in the settings file: every variable a group names
/// is still itself, still archived under its own name, and still reachable from the console — which is
/// where somebody goes who wants one of the six on its own.
/// </para>
/// <para>
/// It follows that a group has to be able to say "none of my steps": the moment one of those variables is
/// set by hand the group is between its steps, and reporting the step it used to be on would be a lie the
/// player can see through. That reading is <see cref="QualityBinding.Detect"/>'s, and is the same rule
/// <c>GraphicsPresetRegistry</c> applies to the overall preset.
/// </para>
/// </remarks>
/// <param name="Caption">The row's caption.</param>
/// <param name="Help">What the choice does, for the tooltip and the footer.</param>
/// <param name="Levels">The steps, in the order they are offered — quietest first.</param>
public sealed record QualityGroup(string Caption, string Help, IReadOnlyList<QualityLevel> Levels)
{
    /// <summary>Initializes a new instance of the <see cref="QualityGroup"/> class from a level list.</summary>
    /// <param name="caption">The row's caption.</param>
    /// <param name="help">What the choice does.</param>
    /// <param name="levels">The steps, quietest first.</param>
    public QualityGroup(string caption, string help, params QualityLevel[] levels)
        : this(caption, help, (IReadOnlyList<QualityLevel>)levels)
    {
    }

    /// <summary>Gets every variable any of the steps writes, once each, in the order first named.</summary>
    public IReadOnlyList<string> Variables
    {
        get
        {
            var names = new List<string>();
            foreach (QualityLevel level in Levels)
            {
                foreach ((string variable, _) in level.Values)
                {
                    if (!names.Contains(variable))
                    {
                        names.Add(variable);
                    }
                }
            }

            return names;
        }
    }
}

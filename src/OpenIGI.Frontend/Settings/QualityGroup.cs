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
/// A coarse quality choice on the basic graphics page: one caption the player understands over several
/// variables they should not have to.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole basic/advanced relationship in one type. Every variable a group names is still on the
/// advanced page as its own row — nothing is hidden and nothing is duplicated in the settings file — and
/// the group is a named point in the space those rows span. Choosing <c>Shadow Quality: High</c> writes the
/// five shadow variables at once; opening the advanced page afterwards shows exactly what it wrote.
/// </para>
/// <para>
/// It follows that a group has to be able to say "none of my steps": the moment one of those five rows is
/// moved by hand the group is between its steps, and reporting the step it used to be on would be a lie the
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

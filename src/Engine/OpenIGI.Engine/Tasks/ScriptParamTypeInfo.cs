namespace OpenIGI.Engine.Tasks;

/// <summary>
/// What each <see cref="ScriptParamType"/> is called in level data, what it derives from, and how many
/// script arguments its parse callback consumes.
/// </summary>
/// <remarks>
/// The argument counts are the load-bearing part: the <c>Task_New</c> script command parses parameters
/// positionally from one flat argument list, so a wrong count silently shifts every following field and
/// swallows nested child tasks. See the re-note, §7.
/// </remarks>
public static class ScriptParamTypeInfo
{
    private static readonly Dictionary<ScriptParamType, (string Name, ScriptParamType Base, int Arity)> Table = new()
    {
        // Quat32 and Real32x9 both parse THREE Euler angles and expand them in memory (0x4EFCE0,
        // 0x4EFBA0) — the storage width is not the wire width.
        [ScriptParamType.Quat32] = ("Quat32", ScriptParamType.Quat32, 3),
        [ScriptParamType.Real32] = ("Real32", ScriptParamType.Real32, 1),
        [ScriptParamType.Real32x3] = ("Real32x3", ScriptParamType.Real32x3, 3),
        [ScriptParamType.Real32x9] = ("Real32x9", ScriptParamType.Real32x9, 3),
        [ScriptParamType.Real64] = ("Real64", ScriptParamType.Real64, 1),
        [ScriptParamType.Real64x3] = ("Real64x3", ScriptParamType.Real64x3, 3),
        [ScriptParamType.Int32] = ("Int32", ScriptParamType.Int32, 1),
        [ScriptParamType.Int16] = ("Int16", ScriptParamType.Int16, 1),
        [ScriptParamType.VarString] = ("VarString", ScriptParamType.VarString, 1),
        [ScriptParamType.String16] = ("String16", ScriptParamType.String16, 1),
        [ScriptParamType.String32] = ("String32", ScriptParamType.String32, 1),
        [ScriptParamType.String256] = ("String256", ScriptParamType.String256, 1),
        [ScriptParamType.LevelExpression] = ("LevelExpression", ScriptParamType.LevelExpression, 1),
        [ScriptParamType.QTaskNote] = ("QTaskNote", ScriptParamType.QTaskNote, 0),
        [ScriptParamType.Bool8] = ("bool8", ScriptParamType.Bool8, 1),
        [ScriptParamType.Graph] = ("Graph", ScriptParamType.Graph, 3),
        [ScriptParamType.GraphEnvelope] = ("GraphEnvelope", ScriptParamType.GraphEnvelope, 11),
        [ScriptParamType.ScriptFile] = ("ScriptFile", ScriptParamType.String256, 1),
        [ScriptParamType.Bitfield] = ("Bitfield", ScriptParamType.Int32, 1),
        [ScriptParamType.PitchGraph] = ("PitchGraph", ScriptParamType.GraphEnvelope, 11),
        [ScriptParamType.Rgb] = ("RGB", ScriptParamType.Real32x3, 3),
        [ScriptParamType.Angle] = ("Angle", ScriptParamType.Real32, 1),
        [ScriptParamType.Degrees] = ("Degrees", ScriptParamType.Real32, 1),
        [ScriptParamType.ObjectPos] = ("ObjectPos", ScriptParamType.Real64x3, 3),
        [ScriptParamType.PushButton] = ("PushButton", ScriptParamType.Bool8, 1),
        [ScriptParamType.DropDownCombo] = ("DropDownCombo", ScriptParamType.VarString, 1),

        // Registered by subsystems rather than by 0x4EE970: the enum aliases (0x4F1E30),
        // RangeReal32 (0x520BB0), WaterLevel (0x518EF0) and AnimData (0x4F3440).
        [ScriptParamType.EnumInt32] = ("EnumInt32", ScriptParamType.Int32, 1),
        [ScriptParamType.EnumReal32] = ("EnumReal32", ScriptParamType.Real32, 1),
        [ScriptParamType.EnumString16] = ("EnumString16", ScriptParamType.String16, 1),
        [ScriptParamType.EnumString32] = ("EnumString32", ScriptParamType.String32, 1),
        [ScriptParamType.EnumString256] = ("EnumString256", ScriptParamType.String256, 1),
        [ScriptParamType.RangeReal32] = ("RangeReal32", ScriptParamType.Real32, 1),
        [ScriptParamType.WaterLevel] = ("WaterLevel", ScriptParamType.Real32, 1),
        [ScriptParamType.AnimData] = ("AnimData", ScriptParamType.AnimData, VariableArgumentCount),
    };

    /// <summary>The argument count reported for a type whose parse callback consumes a variable number.</summary>
    public const int VariableArgumentCount = -1;

    private static readonly Dictionary<string, ScriptParamType> ByName = BuildNameIndex();

    /// <summary>Gets the name the original registers for a type, as it appears in level data.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The registered name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered type.</exception>
    public static string GetName(ScriptParamType type) => Lookup(type).Name;

    /// <summary>Gets the base type a derived type parses as; a base type maps to itself.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The base type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered type.</exception>
    public static ScriptParamType GetBaseType(ScriptParamType type) => Lookup(type).Base;

    /// <summary>Gets how many positional script arguments a value of this type consumes.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The argument count; zero for types whose parse callback is a no-op.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered type.</exception>
    /// <exception cref="NotSupportedException">The type consumes a variable number of arguments.</exception>
    public static int GetArgumentCount(ScriptParamType type)
    {
        int arity = Lookup(type).Arity;
        if (arity == VariableArgumentCount)
        {
            throw new NotSupportedException(
                $"The script parameter type '{GetName(type)}' consumes a variable number of arguments; " +
                $"check {nameof(IsVariableLength)} first.");
        }

        return arity;
    }

    /// <summary>Determines whether a type's parse callback consumes a data-dependent number of arguments.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> only for <see cref="ScriptParamType.AnimData"/>.</returns>
    public static bool IsVariableLength(ScriptParamType type) =>
        Table.TryGetValue(type, out var entry) && entry.Arity == VariableArgumentCount;

    /// <summary>Resolves the type a level-data type name refers to.</summary>
    /// <param name="name">The name as it appears in <c>Task_DeclareParameters</c>.</param>
    /// <param name="type">Receives the type.</param>
    /// <returns><see langword="true"/> when the name is a known type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool TryParseName(string name, out ScriptParamType type)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByName.TryGetValue(name, out type);
    }

    private static (string Name, ScriptParamType Base, int Arity) Lookup(ScriptParamType type)
    {
        if (!Table.TryGetValue(type, out var entry))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a registered script parameter type.");
        }

        return entry;
    }

    private static Dictionary<string, ScriptParamType> BuildNameIndex()
    {
        // Level data spells these exactly as the original registers them, so the lookup is ordinal.
        var index = new Dictionary<string, ScriptParamType>(StringComparer.Ordinal);
        foreach ((ScriptParamType type, (string name, _, _)) in Table)
        {
            index[name] = type;
        }

        return index;
    }
}

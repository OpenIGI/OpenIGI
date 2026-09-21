namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One of the value types a task parameter can have.
/// </summary>
/// <remarks>
/// <para>
/// The original registers these as property-type descriptors in <c>0x4EE970</c>: 17 base types plus 9
/// derived ones that inherit the parent descriptor's callbacks verbatim. The derived names are not
/// cosmetic — retail level data uses <c>ObjectPos</c> and <c>RGB</c> constantly — but they parse exactly
/// like their parent, so they exist here to keep declarations readable and to let a declaration be
/// checked byte-for-byte against retail <c>Task_DeclareParameters</c> output.
/// </para>
/// <para>
/// Names match the strings the original registers, so a declaration reads the same as level data does.
/// </para>
/// </remarks>
public enum ScriptParamType
{
    /// <summary>Unset. Rejected, matching the original's fatal <c>"Undefined parameter type."</c>.</summary>
    None = 0,

    /// <summary>A quaternion; four <see cref="float"/> values.</summary>
    Quat32,

    /// <summary>A single-precision scalar.</summary>
    Real32,

    /// <summary>Three single-precision values.</summary>
    Real32x3,

    /// <summary>A 3×3 matrix; nine single-precision values.</summary>
    Real32x9,

    /// <summary>A double-precision scalar.</summary>
    Real64,

    /// <summary>Three double-precision values.</summary>
    Real64x3,

    /// <summary>A 32-bit integer.</summary>
    Int32,

    /// <summary>A 16-bit integer.</summary>
    Int16,

    /// <summary>A heap-allocated string, used for script expressions.</summary>
    VarString,

    /// <summary>A string in a 16-byte inline buffer.</summary>
    String16,

    /// <summary>A string in a 32-byte inline buffer.</summary>
    String32,

    /// <summary>A string in a 256-byte inline buffer.</summary>
    String256,

    /// <summary>A level expression source string.</summary>
    LevelExpression,

    /// <summary>Editor-only annotation. Registers no parse callback and consumes no script argument.</summary>
    QTaskNote,

    /// <summary>A one-byte boolean.</summary>
    Bool8,

    /// <summary>Editor curve data. It stores three 32-bit floats.</summary>
    Graph,

    /// <summary>An envelope curve.</summary>
    GraphEnvelope,

    /// <summary>A script file path. Derived from <see cref="String256"/>.</summary>
    ScriptFile,

    /// <summary>A bit set. Derived from <see cref="Int32"/>.</summary>
    Bitfield,

    /// <summary>A pitch envelope. Derived from <see cref="GraphEnvelope"/>.</summary>
    PitchGraph,

    /// <summary>A colour. Derived from <see cref="Real32x3"/>.</summary>
    Rgb,

    /// <summary>An angle in radians. Derived from <see cref="Real32"/>.</summary>
    Angle,

    /// <summary>An angle in degrees. Derived from <see cref="Real32"/>.</summary>
    Degrees,

    /// <summary>A world position. Derived from <see cref="Real64x3"/>; positions are doubles.</summary>
    ObjectPos,

    /// <summary>An editor push button. Derived from <see cref="Bool8"/>.</summary>
    PushButton,

    /// <summary>An editor combo box. Derived from <see cref="VarString"/>.</summary>
    DropDownCombo,

    /// <summary>An integer restricted to named values. Derived from <see cref="Int32"/>.</summary>
    EnumInt32,

    /// <summary>A scalar restricted to named values. Derived from <see cref="Real32"/>.</summary>
    EnumReal32,

    /// <summary>A 16-byte string restricted to named values. Derived from <see cref="String16"/>.</summary>
    EnumString16,

    /// <summary>A 32-byte string restricted to named values. Derived from <see cref="String32"/>.</summary>
    EnumString32,

    /// <summary>A 256-byte string restricted to named values. Derived from <see cref="String256"/>.</summary>
    EnumString256,

    /// <summary>A scalar with editor range metadata. Derived from <see cref="Real32"/>.</summary>
    RangeReal32,

    /// <summary>A water height. Derived from <see cref="Real32"/>.</summary>
    WaterLevel,

    /// <summary>
    /// An animation key list: a count followed by that many (code, value) pairs.
    /// </summary>
    /// <remarks>
    /// The only <strong>variable-length</strong> parameter type. Its parse callback
    /// (<c>0x4F3570</c>, registered by the <c>AnimTask</c> registrar <c>0x4F3440</c>) reads a count and
    /// then consumes two arguments per entry, so a task using it has no fixed argument count.
    /// </remarks>
    AnimData,
}

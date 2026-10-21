using System.Collections.Generic;

namespace OpenIGI.Generators;

/// <summary>
/// Maps each <c>ScriptParamType</c> member name onto the reader call that parses it and the CLR type it
/// produces.
/// </summary>
/// <remarks>
/// The generator cannot reference the engine, so the mapping is keyed by enum member name. It must stay
/// in step with <c>OpenIGI.Engine.Tasks.ScriptParamTypeInfo</c>; the retail-corpus tests catch drift,
/// because a wrong reader consumes the wrong number of arguments and the task tree comes out malformed.
/// </remarks>
internal static class ScriptParamTypeMap
{
    /// <summary>The reader method for a type, and the CLR type its result must be assigned to.</summary>
    internal readonly struct Entry
    {
        internal Entry(string method, string clrType)
        {
            Method = method;
            ClrType = clrType;
        }

        /// <summary>The <c>ScriptArgumentReader</c> method to call.</summary>
        internal string Method { get; }

        /// <summary>The CLR type the call returns, or <see langword="null"/> when it returns nothing.</summary>
        internal string ClrType { get; }
    }

    private static readonly Dictionary<string, Entry> Entries = new(System.StringComparer.Ordinal)
    {
        ["Int32"] = new Entry("ReadInt32", "int"),
        ["Bitfield"] = new Entry("ReadInt32", "int"),
        ["Int16"] = new Entry("ReadInt16", "short"),
        ["Bool8"] = new Entry("ReadBool8", "bool"),
        ["PushButton"] = new Entry("ReadBool8", "bool"),
        ["Real32"] = new Entry("ReadReal32", "float"),
        ["Angle"] = new Entry("ReadReal32", "float"),
        ["Degrees"] = new Entry("ReadReal32", "float"),
        ["Real64"] = new Entry("ReadReal64", "double"),
        ["Real32x3"] = new Entry("ReadReal32x3", "global::OpenIGI.Core.Numerics.Vector3f"),
        ["Rgb"] = new Entry("ReadReal32x3", "global::OpenIGI.Core.Numerics.Vector3f"),

        // Three Euler angles on the wire, not nine components.
        ["Real32x9"] = new Entry("ReadReal32x9", "global::OpenIGI.Core.Numerics.Vector3f"),

        ["Real64x3"] = new Entry("ReadReal64x3", "global::OpenIGI.Core.Numerics.Vector3d"),
        ["ObjectPos"] = new Entry("ReadReal64x3", "global::OpenIGI.Core.Numerics.Vector3d"),
        ["Quat32"] = new Entry("ReadQuat32", "global::OpenIGI.Core.Numerics.Quaternion32"),
        ["GraphEnvelope"] = new Entry("ReadGraphEnvelope", "float[]"),
        ["PitchGraph"] = new Entry("ReadGraphEnvelope", "float[]"),

        ["VarString"] = new Entry("ReadString", "string"),
        ["String16"] = new Entry("ReadString", "string"),
        ["String32"] = new Entry("ReadString", "string"),
        ["String256"] = new Entry("ReadString", "string"),
        ["EnumString16"] = new Entry("ReadString", "string"),
        ["EnumString32"] = new Entry("ReadString", "string"),
        ["EnumString256"] = new Entry("ReadString", "string"),
        ["EnumInt32"] = new Entry("ReadInt32", "int"),
        ["EnumReal32"] = new Entry("ReadReal32", "float"),
        ["RangeReal32"] = new Entry("ReadReal32", "float"),
        ["WaterLevel"] = new Entry("ReadReal32", "float"),

        // Variable length: a count followed by that many code/value pairs.
        ["AnimData"] = new Entry("ReadAnimData", "global::OpenIGI.Engine.Tasks.AnimDataEntry[]"),
        ["ScriptFile"] = new Entry("ReadString", "string"),
        ["LevelExpression"] = new Entry("ReadString", "string"),
        ["DropDownCombo"] = new Entry("ReadString", "string"),

        ["Graph"] = new Entry("ReadGraph", "float[]"),
        ["QTaskNote"] = new Entry(null!, null!),
    };

    internal static bool TryGet(string typeName, out Entry entry) => Entries.TryGetValue(typeName, out entry);
}

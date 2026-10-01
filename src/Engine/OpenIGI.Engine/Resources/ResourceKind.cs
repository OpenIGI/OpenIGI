namespace OpenIGI.Engine.Resources;

/// <summary>What a <see cref="ResourceEntry"/> holds.</summary>
/// <remarks>
/// The numeric values are the resource types the original passes to <c>Res_CreateEntry</c>
/// (<c>0x4B59F0</c>); <see cref="ContextString"/> has no original number and is a port-level
/// distinction, because context strings carry the localized text of the language archives.
/// </remarks>
public enum ResourceKind
{
    /// <summary>An archive that was exploded into other entries.</summary>
    Container = 0,

    /// <summary>A <c>BODY</c> payload from inside an archive.</summary>
    Body = 1,

    /// <summary>A file registered whole, from disk or from a caller-supplied buffer.</summary>
    RawFile = 2,

    /// <summary>A <c>PATH</c> node from inside an archive.</summary>
    Path = 4,

    /// <summary>A <c>CSTR</c> context string from inside an archive.</summary>
    ContextString = 5,
}

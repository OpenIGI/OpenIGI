namespace OpenIGI.Core.Configuration;

/// <summary>
/// Marks a static class whose fields declare console variables or console commands.
/// </summary>
/// <remarks>
/// <para>
/// A static field only runs its initialiser once something touches the class, so declarations in a
/// subsystem nobody has called yet would be missing from the registry exactly when the settings file
/// is read. The source generator emits a module initialiser that touches every class carrying this
/// attribute, which makes registration complete and ordering-independent without any runtime
/// reflection.
/// </para>
/// <para>
/// The attribute itself carries no data; it exists to be found.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CVarContainerAttribute : Attribute;

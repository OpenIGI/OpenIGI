namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Marks a task property as one the level hands it as the level is brought up: a table, a world or a sink the task uses, looked
/// up by the property's type.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[LevelService]</c> source generator emits an override of <see cref="GameTask"/>'s <c>BindLevelServices</c> that sets
/// each attributed property from the level's services, so no system has to find the task and set it by hand, and a mod's own
/// task type gets the same services by declaring them. <see cref="TaskServiceBinder"/> runs the bindings over a level's tree.
/// </para>
/// <para>
/// A property whose type the level does not provide is left as it was. The task type must be <see langword="partial"/>, and the
/// property settable and of a reference type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed partial class Igi2SwitchTask : Igi2Task
/// {
///     [LevelService] public Func&lt;string, MefModel?&gt;? Models { get; set; }
///     [LevelService] public Igi2Sounds? Sounds { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LevelServiceAttribute : Attribute
{
}

namespace OpenIGI.Scripting.Vocabulary;

/// <summary>What making one of a task type's conditions hold does, in plain words.</summary>
/// <remarks>
/// The games have no commands for most of what a level does: a message is shown because its <c>Send</c> condition holds, a
/// mission ends because <c>Complete</c> does. A title names what each such condition does, so an editor can offer "Show the
/// message" where the level data has only a parameter name.
/// </remarks>
/// <param name="TaskType">The task type.</param>
/// <param name="Parameter">The condition parameter, as level data spells it.</param>
/// <param name="Verb">What it does while it holds, as an instruction (<c>Show the message</c>).</param>
public sealed record TaskAction(string TaskType, string Parameter, string Verb);

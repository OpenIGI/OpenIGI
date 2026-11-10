namespace OpenIGI.Engine.Tasks;

/// <summary>
/// What a task event carries: the event's context, whose type is the event.
/// </summary>
/// <remarks>
/// <para>
/// The original names an event by a slot in its handler table (<c>QTask_NewQTaskEvent</c>, <c>0x4017C0</c>) and hands every
/// handler one untyped pointer, the context each event defines for itself: a round's hit, an explosion's dose, an activation's
/// query. Here the context's type stands for the slot, so a handler cannot be given another event's context, and a new event
/// is a new type -- a mod's as much as the engine's.
/// </para>
/// <para>
/// A context is a struct passed by reference: a handler writes its answer into it, as the original's handlers write through
/// the pointer, and nothing is allocated for an event. An event that carries nothing is an empty struct.
/// </para>
/// </remarks>
public interface ITaskEvent;

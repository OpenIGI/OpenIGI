using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2;

/// <summary>
/// A task of I.G.I. 2's level tree: the root of every type the title gives a class, as <c>QTask</c> is the root of the
/// original's types.
/// </summary>
/// <remarks>
/// <para>
/// The original declares each type with a parent (<c>QTask_NewQTaskType(name, size, parent)</c>) and copies the parent's
/// handler table into it, so a type that links no handler of its own for an event answers it with its parent's. The
/// title's classes follow the same tree, and an event handler is a virtual
/// member here: a class overrides the handlers its retail type links and inherits the rest.
/// </para>
/// <para>
/// A parameter list is inherited only where the original derives one (<c>ParameterList_Derive</c>); such a class carries
/// <see cref="DerivedScriptParametersAttribute"/>.
/// </para>
/// </remarks>
public abstract class Igi2Task : GameTask
{
    /// <summary>Runs the task one tick: its run handler (event 1).</summary>
    /// <param name="level">The level it runs in.</param>
    /// <remarks>
    /// The base type links no run handler, so a task that does not override this does nothing, and neither do its
    /// children: the run pass is not a walk, a task's children run only when its own handler runs them.
    /// </remarks>
    public virtual void Run(Igi2LevelRun level)
    {
    }
}

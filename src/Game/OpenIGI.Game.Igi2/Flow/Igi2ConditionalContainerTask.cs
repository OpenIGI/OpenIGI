using OpenIGI.Game.Level;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>I.G.I. 2's <c>ConditionalContainer</c>: the shared <see cref="ConditionalContainerTask"/>, with the title's save state.</summary>
/// <remarks>
/// It runs the shared handler under <see cref="Igi2LevelRun"/>'s rules. Its <c>isRun</c> is nought until something writes it
/// or its condition first runs; nothing settles it before the first tick.
/// </remarks>
public sealed partial class Igi2ConditionalContainerTask : ConditionalContainerTask
{
    /// <summary>Initializes a new instance of the <see cref="Igi2ConditionalContainerTask"/> class.</summary>
    public Igi2ConditionalContainerTask() => SetRunByte(0);

    /// <inheritdoc/>
    public override bool SettlesOutsideTheTick => false;

    /// <inheritdoc/>
    /// <remarks>Any other title's run sees it as it sees an <see cref="Igi2Task"/>: a container whose children it walks.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level is Igi2LevelRun)
        {
            base.Run(level);
            return;
        }

        level.RunChildren(this);
    }
}

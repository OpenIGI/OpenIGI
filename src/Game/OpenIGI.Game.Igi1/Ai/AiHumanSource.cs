using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A soldier as the other soldiers' scans refer to it: the original's task reference (<c>TaskRef_Add</c>).</summary>
/// <param name="view">The soldier's body.</param>
/// <param name="brain">Its brain.</param>
internal sealed class AiHumanSource(SoldierView view, Igi1HumanAi brain)
{
    /// <summary>Gets the soldier's body.</summary>
    public SoldierView View { get; } = view;

    /// <summary>Gets or sets its brain.</summary>
    public Igi1HumanAi Brain { get; set; } = brain;

    /// <summary>Gets or sets its damage state as of its last offer: <see cref="SoldierContext.HealthOf"/>, looked up once a tick.</summary>
    public HumanHealth Health { get; set; } = null!;

    /// <summary>Gets the soldier's id.</summary>
    public int Id => View.Soldier.Id;
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>Who is noticing the player, as a heads-up display marks them.</content>
public sealed partial class SoldierSimulation
{
    private readonly List<(Vector3d Position, float Awareness)> _threats = [];

    /// <summary>
    /// The living soldiers that can see the player now, with where they stand. Only a sighting counts: one alerted by an
    /// alarm or a sighting it has lost, or one behind a wall, gives the player nothing the player could not know.
    /// </summary>
    /// <returns>The soldiers, refilled each call.</returns>
    public IReadOnlyList<(Vector3d Position, float Awareness)> Threats()
    {
        _threats.Clear();
        foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
        {
            if (view.IsDead || !brain.PlayerIsEnemy)
            {
                continue;
            }

            if (brain.SeesPlayer)
            {
                _threats.Add((view.Position, 1f));
            }
        }

        return _threats;
    }
}

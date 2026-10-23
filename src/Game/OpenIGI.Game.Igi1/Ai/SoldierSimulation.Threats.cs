using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>Who is noticing the player, as a heads-up display marks them.</content>
public sealed partial class SoldierSimulation
{
    private readonly List<(Vector3d Position, float Awareness)> _threats = [];

    /// <summary>
    /// The living soldiers noticing an enemy, with where they stand and how much: one that sees its enemy has seen, one only
    /// alerted -- by an alarm, or a sighting it has lost -- is wondering.
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

            if (brain.SeesEnemy)
            {
                _threats.Add((view.Position, 1f));
            }
            else if (brain.Alerted)
            {
                _threats.Add((view.Position, 0.45f));
            }
        }

        return _threats;
    }
}

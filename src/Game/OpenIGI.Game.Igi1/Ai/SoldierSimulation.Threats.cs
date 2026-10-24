using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>Who is noticing the player, as a heads-up display marks them.</content>
public sealed partial class SoldierSimulation
{
    /// <summary>The ticks a soldier must hold the player in sight for before the mark counts him sure: a second and a half.</summary>
    public const int SureTicks = 45;

    private readonly List<(Vector3d Position, float Awareness)> _threats = [];
    private readonly Dictionary<Igi1HumanAi, int> _seenSince = [];

    /// <summary>
    /// The living soldiers that can see the player now, with where they stand and how long they have: from half as the sighting
    /// begins to whole after <see cref="SureTicks"/> ticks of it unbroken. Only a sighting counts: one alerted by an alarm or a
    /// sighting it has lost, or one behind a wall, gives the player nothing the player could not know.
    /// </summary>
    /// <returns>The soldiers, refilled each call.</returns>
    public IReadOnlyList<(Vector3d Position, float Awareness)> Threats()
    {
        _threats.Clear();
        foreach ((SoldierView view, Igi1HumanAi brain) in Brains)
        {
            if (view.IsDead || !brain.PlayerIsEnemy || !brain.SeesPlayer)
            {
                _seenSince.Remove(brain);
                continue;
            }

            if (!_seenSince.TryGetValue(brain, out int since))
            {
                since = TickCount;
                _seenSince[brain] = since;
            }

            _threats.Add((view.Position, 0.5f + (0.5f * Math.Clamp((float)(TickCount - since) / SureTicks, 0f, 1f))));
        }

        return _threats;
    }
}

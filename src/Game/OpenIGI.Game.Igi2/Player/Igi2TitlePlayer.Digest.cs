using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Replays;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>What of I.G.I. 2's running mission its fingerprint folds (<see cref="ISimulationDigest"/>).</summary>
/// <remarks>
/// The ticks run, the player's and the AI's random streams, the player as the simulation last left it, and every soldier in the
/// order the soldiers are stepped, with the mission's outcome so far.
/// </remarks>
public sealed partial class Igi2TitlePlayer : ISimulationDigest
{
    /// <inheritdoc/>
    public void Fold(ref SimulationHash hash)
    {
        hash.Add(_ticks);
        hash.Add(Human.Random);
        hash.Add(Ai?.Random);
        FoldHuman(Human, ref hash);
        hash.Add(Weapons?.IsHolding ?? false);

        IReadOnlyList<Soldiers.Igi2HumanSoldier> bodies = Soldiers?.Bodies ?? [];
        hash.Add(bodies.Count);
        foreach (Soldiers.Igi2HumanSoldier soldier in bodies)
        {
            FoldHuman(soldier, ref hash);
            hash.Add(soldier.LifeState);
            hash.Add((int)soldier.UpperBodyState);
        }

        hash.Add((int)Outcome);
    }

    private static void FoldHuman(Igi2Human human, ref SimulationHash hash)
    {
        hash.Add(human.Position);
        hash.Add(human.View.Yaw);
        hash.Add(human.View.Pitch);
        hash.Add(human.Damage.Damage);
        hash.Add(human.IsDead);
    }
}

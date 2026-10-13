using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Its save state as a human: its difficulty, how well it hits and how well it is seen are a soldier's own.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>Writes the human part of its save state.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteSoldierState(Igi2StateWriter writer) => WriteHumanState(writer, new Igi2HumanSaveExtras
    {
        Difficulty = (int)Difficulty,
        WeaponHitProbability = WeaponHitProbability,
        VisibilityFactor = VisibilityFactor,
    });

    /// <summary>Reads the human part of its save state back.</summary>
    /// <param name="reader">The reader.</param>
    public void ReadSoldierState(Igi2StateReader reader)
    {
        Igi2HumanSaveExtras extras = ReadHumanState(reader);
        WeaponHitProbability = extras.WeaponHitProbability;
        VisibilityFactor = extras.VisibilityFactor;
    }
}

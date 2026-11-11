using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>The vehicles' and gun holders' places in the level's walk.</summary>
public sealed partial class CutsceneRuntime
{
    private readonly CutsceneGunHolders _gunHolders;

    /// <summary>
    /// Gets or sets every human an <c>AIStationaryGunHolder</c>'s record is offered, in the global detection list's order:
    /// the player, a co-op session's other players, then the soldiers.
    /// </summary>
    public Func<IReadOnlyList<AiDetectable>>? GunHolderSubjects
    {
        get => _gunHolders.Subjects;
        set => _gunHolders.Subjects = value;
    }

    /// <summary>Gets the level's <c>AIStationaryGunHolder</c> tasks.</summary>
    public IEnumerable<GameTask> GunHolders => _gunHolders.Holders;

    /// <inheritdoc cref="CutsceneGunHolders.StateOf"/>
    public (int Team, bool On)? GunHolderState(GameTask holder) => _gunHolders.StateOf(holder);

    /// <inheritdoc cref="CutsceneGunHolders.RecordOf"/>
    public AiDetectionRecord? GunHolderRecord(GameTask holder) => _gunHolders.RecordOf(holder);

    // A Car, Heli or AIStationaryGunHolder updates where the walk reaches it; a holder's record takes the phase the load
    // gave it (0x46E7F0 initialises it after its children post-load).
    private void BindVehicleUpdates(GameTask levelRoot, IReadOnlyDictionary<GameTask, int> visionPhases)
    {
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (string.Equals(task.Type.Name, CarTask.TypeName, StringComparison.Ordinal)
                || string.Equals(task.Type.Name, HeliTask.TypeName, StringComparison.Ordinal)
                || string.Equals(task.Type.Name, CarAiTask.TypeName, StringComparison.Ordinal))
            {
                Adopt(task);
            }
            else if (string.Equals(task.Type.Name, CutsceneGunHolders.TypeName, StringComparison.Ordinal))
            {
                _gunHolders.Register(task, visionPhases.TryGetValue(task, out int phase) ? phase : _armament.NextVisionPhase());
                Adopt(task);
            }
        }
    }
}

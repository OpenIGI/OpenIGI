using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What a <see cref="PlayerInteractionDriver"/> asks of the player it drives: the weapon machine it puts
/// away, the view it re-commits, and the notices an interaction raises.
/// </summary>
internal interface IPlayerInteractionHost
{
    /// <summary>Gets the weapon machine, once the player has one.</summary>
    PlayerWeapons? Weapons { get; }

    /// <summary>Re-binds the first-person rig to the body.</summary>
    void SyncView();

    /// <summary>Records that the rig's pose has changed.</summary>
    void MarkPoseChanged();

    /// <summary>Drops the rig's tick pair, so the next frame is not interpolated across a jump.</summary>
    void DropViewTickPair();

    /// <summary>The world cast state 36 plants along, or <see langword="null"/> when there is no world.</summary>
    WorldTraceCast? PlantTrace();

    /// <summary>A task was pressed or a held interaction completed.</summary>
    void OnTaskActivated(int taskId);

    /// <summary>The player mounted a stationary gun whose weapon was never bound.</summary>
    void OnUnarmedStationaryGun(AiStationaryGun gun);

    /// <summary>A powered electric fence contact passed the retail 45-tick guard.</summary>
    void OnElectrifiedFenceTouched(WorldInteraction fence);

    /// <summary>The player made a noise the AI hears.</summary>
    void OnAiNoise(AiEvent noise);
}

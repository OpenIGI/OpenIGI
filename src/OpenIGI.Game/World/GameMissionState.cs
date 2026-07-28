using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>The mission the game will start when it next leaves the menus.</summary>
/// <remarks>
/// <para>
/// The original keeps this in a single global (<c>0x539560</c>). <c>Game_SetMission</c>
/// (<c>0x415890</c>) writes the id into it and does nothing else; <c>Mission_Load</c> reads the same
/// global back as the very first thing it does. The Select Mission screen's OK button is exactly this
/// pair: <c>Game_SetMission(this.nActiveID), MenuManager_LeaveMenus(-1, MENUMANAGER_STARTGAME)</c>.
/// </para>
/// <para>
/// Modelled as an object rather than a static so a test can drive two games without them sharing state.
/// </para>
/// </remarks>
public sealed class GameMissionState
{
    /// <summary>The native the menus call to choose a mission.</summary>
    public const string SetMissionNative = "Game_SetMission";

    /// <summary>Gets the mission id chosen, or zero when none has been.</summary>
    public int PendingMissionId { get; private set; }

    /// <summary>Registers <c>Game_SetMission</c>.</summary>
    /// <param name="natives">The script namespace the menu scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        natives.RegisterFunction(
            SetMissionNative,
            call =>
            {
                PendingMissionId = call.GetInt32(0);
                return QvmValue.FromInt32(1);
            },
            1);
    }

    /// <inheritdoc/>
    public override string ToString() => $"pending mission {PendingMissionId}";
}

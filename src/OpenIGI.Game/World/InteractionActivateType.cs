namespace OpenIGI.Game.World;

/// <summary>
/// The <c>GetActivate</c> type an activatable world task reports — the tag the human's interaction
/// dispatcher switches on to decide what "use" does.
/// </summary>
/// <remarks>
/// <para>
/// Every activatable task (ladder, door, fence, drawer, …) registers a <c>GetActivate</c> message
/// handler that writes one of these values; the ladder's (<c>0x4406C0</c>) writes <see cref="Ladder"/>.
/// The human's action handler (<c>0x40E9C0</c>) reads the resolved activatable's type and hands it to
/// the dispatcher <c>0x40CD30</c> (see <see cref="OpenIGI.Game.Player.HumanInteractionDispatch"/>).
/// </para>
/// <para>
/// The numeric values are the ones the dispatcher's <c>switch</c> tests, transcribed from IGI1
/// <c>sub_40CD30</c>. Values whose producing task is not yet pinned are left commented in that function
/// rather than invented here.
/// </para>
/// </remarks>
public enum InteractionActivateType
{
    /// <summary>Nothing to activate.</summary>
    None = 0,

    /// <summary>A drawer — pull it open immediately (<c>sub_444BC0</c>).</summary>
    Drawer = 2,

    /// <summary>A door — open it, or pick its lock (state 30) when it is locked.</summary>
    Door = 3,

    /// <summary>A computer / keypad — enter the <c>typecomp_loop</c> state (32).</summary>
    Computer = 5,

    /// <summary>A fence — vault or mantle it (<c>sub_43F8A0</c>).</summary>
    Fence = 9,

    /// <summary>A ladder — request a climb; the mount runs on the next tick.</summary>
    Ladder = 10,
}

namespace OpenIGI.Game.World;

/// <summary>
/// The <c>GetActivate</c> type an activatable world task reports — the tag the human's interaction
/// dispatcher switches on to decide what "use" does.
/// </summary>
/// <remarks>
/// <para>
/// Every activatable task (ladder, door, fence, cabinet, …) registers a <c>GetActivate</c> message
/// handler that writes one of these values; the ladder's (<c>0x4406C0</c>) writes <see cref="Ladder"/>.
/// The human's action handler (<c>0x40E9C0</c>) reads the resolved activatable's type and hands it to
/// the dispatcher <c>0x40CD30</c> (see <c>HumanInteractionDispatch</c>).
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

    /// <summary>A wall switch or alarm/gate button — emit its three-tick press pulse.</summary>
    Switch = 2,

    /// <summary>A door — open it, or pick its lock (state 30) when it is locked.</summary>
    Door = 3,

    /// <summary>A cabinet — enter the timed search state (31).</summary>
    Cabinet = 4,

    /// <summary>A computer / keypad — enter the <c>typecomp_loop</c> state (32).</summary>
    Terminal = 5,

    /// <summary>A mounted stationary gun — enter state 33.</summary>
    StationaryGun = 6,

    /// <summary>An engine-defined mounted-gun hand-off — state 34.</summary>
    MountedGunTransition = 7,

    /// <summary>A wire — drag along it or slide down it according to its slope.</summary>
    Wire = 9,

    /// <summary>A ladder — request a climb; the mount runs on the next tick.</summary>
    Ladder = 10,

    /// <summary>An ordinary fence — climb it with locomotion animation 171.</summary>
    Fence = 11,

    /// <summary>A generic authored timed interaction — state 35.</summary>
    Generic = 12,

    /// <summary>Place an explosive at an authored task target — state 36.</summary>
    PlaceExplosive = 13,
}

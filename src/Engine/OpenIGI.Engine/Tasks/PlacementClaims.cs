namespace OpenIGI.Engine.Tasks;

/// <summary>A count that moves whenever anything that can take over where a placed object is drawn gains or loses one.</summary>
/// <remarks>
/// <para>
/// The original draws a placed object from its own position and matrix (<c>Direct3DRender_DrawRigidMesh</c>, <c>0x49DF60</c>):
/// a door, a lift or a cut-scene that moves one writes the move into the object itself, and the renderer asks nothing. The port
/// keeps those moves in their registries instead, and a frame asked every one of them about every placement in the level -- on
/// a browser, a tenth of the frame. What none of them claims can be drawn where the level put it without asking; this count is
/// how a frame knows its record of who claims what is still true. Every claim -- a door's or a lift's task, a task reparented
/// under a lift, a cut-scene taking a task or attaching one, a vehicle's gun or plane part, a rotating object, an alarm light's
/// reflector -- advances it when it is made or let go.
/// </para>
/// <para>Main thread only, as the registries are; it only ever increases, and an advance costs only a re-check.</para>
/// </remarks>
public static class PlacementClaims
{
    private static int _epoch;

    /// <summary>Gets the current count.</summary>
    public static int Epoch => Volatile.Read(ref _epoch);

    /// <summary>Says a claim was made or let go.</summary>
    public static void Changed() => Interlocked.Increment(ref _epoch);
}

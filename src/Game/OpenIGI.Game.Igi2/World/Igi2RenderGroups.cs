namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Which of the frame's sixteen queues each kind of object is drawn in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The frame has no hardcoded pass order; it has queues.</b> <c>World_Draw</c>
/// (<c>igi2ed 0x417F50</c>) draws nothing itself — it initialises sixteen call queues, walks the world
/// cube tree so the traversal <em>enqueues</em> each visible object, and then <c>QCube_CallHandlers</c>
/// (<c>0x4171B0</c>) empties them <b>0 to 15</b>, flushing shadow volumes after each and bracketing the
/// lot with QTask events 17 and 18. The queue an object goes in is
/// <c>DynCubeObj_s::iRenderGroup</c>, and the enqueue is one line
/// (<c>0x41784F</c>): <c>CallQueue_QueueEvent(8 * group + &amp;QCube_atCallQueues, object, 5, …)</c>.
/// Event five is the draw event, which is why terrain, water, forest and the sky all reach the screen
/// through their own <c>*_EnterHandler</c> and nothing calls those directly.
/// </para>
/// <para>
/// The numbers below are read out of the create handlers rather than measured from level data, because
/// most of them are the engine's choice and not an author's. A level <em>may</em> override its rigid
/// objects — I.G.I. 2 registers <c>RenderGroup</c> as a level parameter and all 24 levels use it — but
/// what it mostly ships is the value the engine would have picked anyway.
/// </para>
/// <para>
/// <b>A caution about offsets.</b> The dedicated server's symbols put <c>iRenderGroup</c> at
/// <c>0x6C</c> of a 112-byte <c>DynCubeObj_s</c>; the editor's code reads it at <c>124</c>. Both are
/// right — the editor's layout is the server's plus sixteen bytes, which the list link confirms
/// independently (server <c>ptNext</c> at <c>0x60</c>, editor at <c>0x70</c>, the <c>+112</c>
/// <c>World_Draw</c> itself chains by). The two binaries' structures are not interchangeable, and the
/// only one with type information is the one without a renderer.
/// </para>
/// </remarks>
public static class Igi2RenderGroups
{
    /// <summary>How many queues the frame has.</summary>
    /// <remarks>
    /// Sixteen, and arithmetic rather than a guess: the flush calls
    /// <c>CallQueue_CallHandlers(8 * i + 0x788700)</c>, and <c>0x788700</c> to <c>0x788780</c> is
    /// exactly sixteen queues of eight bytes.
    /// </remarks>
    public const int Count = 16;

    /// <summary>The sky. <c>FlatSky_CreateHandler</c> (<c>0x654560</c>).</summary>
    /// <remarks>
    /// Nought, the first queue emptied — and registered through <c>World_RegisterManualObj</c> rather
    /// than into the cube tree, so it is enqueued before the traversal runs at all. Both halves say the
    /// same thing: a backdrop is drawn before everything, which is what lets it be drawn with no depth.
    /// </remarks>
    public const int Sky = 0;

    /// <summary>The sector object the camera is standing inside. <c>World_Draw</c> forces it here.</summary>
    /// <remarks>
    /// The interior you are in draws before any other, ahead even of the terrain. Set on
    /// <c>QCube_ptInsideSectorObject</c> at the top of the frame and left at
    /// <see cref="SectorObject"/> for every other.
    /// </remarks>
    public const int InsideSectorObject = 1;

    /// <summary>The ground. <c>Terrain_CreateHandler</c> (<c>0x62D5A0</c>).</summary>
    /// <remarks>
    /// Also the queue whose flush dispatches the manually registered sector objects, before its own
    /// handlers run.
    /// </remarks>
    public const int Terrain = 2;

    /// <summary>A sector object. <c>SectorObject_CreateHandler</c> (<c>0x4293F1</c>).</summary>
    public const int SectorObject = 3;

    /// <summary>Everything else. <c>World_InitDynCubeObj</c> (<c>0x412C90</c>) writes it into every object.</summary>
    /// <remarks>The forest keeps it, and so does a rigid object whose level does not say otherwise.</remarks>
    public const int Default = 5;

    /// <summary>A vehicle. <c>Vehicle_Init</c> (<c>0x63BA78</c>).</summary>
    public const int Vehicle = 7;

    /// <summary>A human, and the first-person view. <c>Human_CreateHandler</c>, <c>HumanView_CreateHandler</c>.</summary>
    public const int Human = 9;

    /// <summary>A sector, and anything registered as interior to one.</summary>
    /// <remarks>
    /// <c>Sector_CreateHandler</c> (<c>0x429A66</c>) and
    /// <c>SectorObject_RegisterInteriorDynCubeObjSpecificSectorObject</c> (<c>0x427B63</c>) — so an
    /// object moved inside a sector is <em>re-grouped</em> at runtime, and
    /// <c>SectorObject_RemoveInteriorDynCubeObj</c> puts it back to <see cref="Default"/> when it
    /// leaves. A level's authored value is therefore a starting point rather than the last word.
    /// </remarks>
    public const int SectorInterior = 12;

    /// <summary>Water. <c>Water_CreateHandler</c> (<c>0x6469EF</c>).</summary>
    /// <remarks>
    /// Fourteen, second from last, which is the whole opaque world drawn before a translucent surface
    /// that has to sort against it.
    /// </remarks>
    public const int Water = 14;

    /// <summary>Gets whether a value is one of the frame's queues.</summary>
    /// <param name="group">The group.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValid(int group) => group >= 0 && group < Count;
}

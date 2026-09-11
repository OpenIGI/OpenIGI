namespace OpenIGI.Rendering;

/// <summary>The camera's clip planes, as the original's Direct3D setup publishes them.</summary>
/// <remarks>
/// <para>
/// <c>sub_493580</c> (<c>0x493580</c>) initialises the renderer's plane globals once, in game units —
/// 4096 to the metre:
/// </para>
/// <list type="table">
/// <item>
/// <term><c>dword_BCADC0</c> (<c>0x49370B</c>)</term>
/// <description><c>409.6</c> units — <see cref="NearMetres"/>, the world layer's near plane. Scripts
/// can move it through <c>SetNearClippingPlane</c> (<c>0x497D00</c>); nothing retail does.</description>
/// </item>
/// <item>
/// <term><c>dword_BCADDC</c> (<c>0x4936F7</c>)</term>
/// <description><c>204.8</c> units — half of it, the first-person layer's near plane; see
/// <c>ViewModelProjection</c>.</description>
/// </item>
/// <item>
/// <term><c>dword_BCADCC</c> (<c>0x493715</c>)</term>
/// <description><c>409,600,000</c> units — <see cref="FarMetres"/>. It is the far end of the object
/// frustum test in <c>sub_49FEB0</c> and of the terrain draw walk's own reject at <c>0x5131A9</c>,
/// and the same figure feeds the depth encoding <c>sub_512C30</c> builds at <c>0x512C3A</c>.</description>
/// </item>
/// </list>
/// <para>
/// <b>A hundred kilometres is not a rounding of four.</b> The far plane stood at 4000 m from the first
/// fly-through until it was measured — twenty-five times short — and everything past it was clipped:
/// the mountain ranges that surround missions 5, 9 and 11 ended in a straight line at the horizon, and a
/// view from any height showed the ground beyond four kilometres simply absent. The terrain descent had
/// been emitting those chunks all along and the renderer had been drawing them; the projection threw
/// them away. Restoring the retail distance costs about 5% of the frame's GPU time on mission 5 at ultra
/// and no depth precision at all: the port renders reverse-Z into a 32-bit float depth buffer, where the
/// mapping is <c>near / z</c> to within a millionth and the near plane, not the far one, sets the
/// resolution.
/// </para>
/// </remarks>
public static class ViewPlanes
{
    /// <summary>The world layer's near plane in metres — <c>409.6</c> game units.</summary>
    public const float NearMetres = 0.1f;

    /// <summary>The far plane in metres — <c>409,600,000</c> game units.</summary>
    public const float FarMetres = 100_000f;
}

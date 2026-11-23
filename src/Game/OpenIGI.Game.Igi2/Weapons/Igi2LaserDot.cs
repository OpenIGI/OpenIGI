using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// A laser sight's dot where the weapon points, the <c>LaserDot</c> task each weapon makes (<c>Weapon_CreateHandler</c>,
/// <c>igi2 0x51C320</c>) and places as its status is shown (<c>Weapon_StatusDisplayHandler</c>, weapon.c:863).
/// </summary>
/// <remarks>
/// <para>
/// The weapon in hand shows its dot when its type has one (<c>isLaserDot</c>) and either the dot is switched on
/// (<c>isLaserDotOn</c>, the <c>LaserDotSwitch</c> fire type's release, or a laser designator's trigger held) or the type keeps it on
/// (<c>isLaserDotAlwaysOn</c>). A line from where the weapon fires, along the frame's forward axis for 409600 units, past the
/// holder, puts the dot where it meets the world, in the plane of the face it meets; meeting nothing hides it.
/// </para>
/// <para>
/// The dot is a square of 204.8 units either way of the point, one unit off the face (<c>LaserDot_CreateHandler</c>), textured
/// <c>laserdot.tga</c> and added to the picture at its alpha (the <c>PolyListDynCubeObj</c> flag 8: <c>SRCALPHA, ONE</c>). Its
/// colour is 128 a channel at full alpha, which the draw scales by 255 and packs with shifts that let each overflowing channel
/// spill into the next: red and green come out full and blue a half (<c>PolyListDynCubeObj_Direct3DDrawHandler</c>).
/// </para>
/// <para>
/// The original asks for the fire frame with the holder's inaccuracy, which draws from the run stream once a drawn frame. That
/// stream is the gameplay's own here, so the dot is aimed true instead: a picture's rate must not move the level's draws.
/// </para>
/// </remarks>
public sealed class Igi2LaserDot
{
    /// <summary>The dot's texture, <c>LaserDot_Open</c>'s <c>GameFunctions_RegisterGameTexture</c>.</summary>
    public const string Texture = "laserdot.tga";

    /// <summary>Half the dot's width, in world units.</summary>
    public const float HalfSize = 204.8f;

    /// <summary>How far off the face the dot is drawn, in world units.</summary>
    public const float Hover = 1f;

    /// <summary>How far the dot's line runs, in world units.</summary>
    public const double Range = 409600.0;

    /// <summary>The colour the dot's texture is multiplied by, as the draw packs it.</summary>
    public static readonly Bgra32 Colour = Bgra32.FromRgba(255, 255, 128, 255);

    private readonly WorldLineTrace _trace = new(1);

    /// <summary>Whether a weapon shows its dot.</summary>
    /// <param name="item">The weapon in hand, or none.</param>
    /// <returns><see langword="true"/> when its type has a dot that is on or always on.</returns>
    public static bool IsShown(Igi2WeaponItem? item) =>
        item is { WeaponType.LaserDot: true } && (item.IsLaserDotOn || item.WeaponType.LaserDotAlwaysOn);

    /// <summary>Places the dot of the weapon in hand, if it shows one.</summary>
    /// <param name="firing">The weapon.</param>
    /// <returns>The dot as a mark on the face its line meets, or <see langword="null"/> when there is none to draw.</returns>
    public TitleSurfaceMark? Place(Igi2WeaponFiring firing)
    {
        ArgumentNullException.ThrowIfNull(firing);
        if (!IsShown(firing.Inventory.Current) || firing.Owner is not { HasFireFrame: true } owner || firing.World is not { } world)
        {
            return null;
        }

        Igi2FireFrame frame = owner.GetFireFrame(addInaccuracy: false);
        float[] m = frame.Orientation;
        Vector3d from = frame.Position;
        Vector3d to = new(from.X + (m[1] * Range), from.Y + (m[4] * Range), from.Z + (m[7] * Range));
        WorldIntersection hit = Igi2WeaponSightLine.Cast(world, _trace, from, to, owner);
        if (!hit.IsHit)
        {
            return null;
        }

        return new TitleSurfaceMark("LOCAL:common/textures/" + Texture, hit.Position, hit.Normal, HalfSize, Hover, Colour, IsAdditive: true);
    }
}

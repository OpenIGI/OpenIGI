using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Pickups;

/// <summary>A weapon lying in the level for a human to pick up, as the level declares it.</summary>
/// <remarks>
/// <c>GunPickup_Open</c> (<c>igi2 0x46C780</c>) gives the type four parameters: where it lies (<c>+0x158</c>, copied to
/// the fall position by <c>GunPickup_UpdateInternalDataHandler</c>, <c>0x46BA00</c>), its orientation (<c>+0x7C</c>), the
/// weapon it gives by its <c>WEAPON_ID_*</c> name (<c>+0x1AC</c>) and how long it takes to come back (<c>+0x1CE</c>), which
/// only a multiplayer game counts out. Its model is no parameter: it is the weapon's <c>Fixed Model</c>.
/// </remarks>
public sealed partial class Igi2GunPickupTask : GameTask
{
    /// <summary>The name levels declare the type by.</summary>
    public const string TypeName = "GunPickup";

    /// <summary>Gets or sets where the pickup lies.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the pickup's orientation, as the three angles the level writes it with.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the weapon the pickup gives, by its <c>WEAPON_ID_*</c> name.</summary>
    [ScriptParam(ScriptParamType.EnumString32, "ID")]
    public string WeaponId { get; set; } = string.Empty;

    /// <summary>Gets or sets how long the pickup takes to come back, as an expression: a multiplayer game's.</summary>
    [ScriptParam(ScriptParamType.String32, "Respawn Time")]
    public string RespawnTime { get; set; } = string.Empty;
}

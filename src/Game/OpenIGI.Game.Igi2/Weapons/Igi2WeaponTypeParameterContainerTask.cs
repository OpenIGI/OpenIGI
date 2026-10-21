using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A numbered set of tagged values a weapon carries, <c>WeaponTypeParameterContainer</c>: its primary and secondary parameters.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). Its children are <c>TagItem</c>s.
/// </remarks>
public sealed partial class Igi2WeaponTypeParameterContainerTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "WeaponTypeParameterContainer";

    /// <summary>Gets or sets the <c>Parameter ID</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Parameter ID")]
    public int ParameterId { get; set; }
}

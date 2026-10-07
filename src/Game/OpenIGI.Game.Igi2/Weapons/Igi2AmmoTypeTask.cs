using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>One kind of ammunition, as <c>WEAPONS/ammo.qvm</c> declares it: <c>AmmoType</c> (<c>AmmoType_Open</c>, <c>igi2 0x43B080</c>).</summary>
/// <remarks>
/// <para>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/ammo.qvm</c>).
/// </para>
/// <para>
/// Each one takes the next number of a session-wide count as it is made (<c>AmmoType_CreateHandler</c>, <c>0x43AE20</c>),
/// and its <c>Ammo ID</c> becomes a script constant with that number (<c>AmmoType_UpdateInternalDataHandler</c>,
/// <c>0x43AF10</c>); see <see cref="Igi2WeaponTables"/>.
/// </para>
/// </remarks>
public sealed partial class Igi2AmmoTypeTask : GameTask
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "AmmoType";

    /// <summary>Gets or sets the <c>Ammo ID</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Ammo ID")]
    public string AmmoId { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Casing Model</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Casing Model")]
    public string CasingModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Model (Used for ammopickup)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Model (Used for ammopickup)")]
    public string ModelForPickup { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Enable Tracer</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Enable Tracer")]
    public bool EnableTracer { get; set; }

    /// <summary>Gets or sets the <c>Used for Accuracy-Score</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Used for Accuracy-Score")]
    public bool UsedForAccuracyScore { get; set; }

    /// <summary>Gets or sets the <c>Tracer Start R</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer Start R")]
    public int TracerStartR { get; set; }

    /// <summary>Gets or sets the <c>Tracer Start G</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer Start G")]
    public int TracerStartG { get; set; }

    /// <summary>Gets or sets the <c>Tracer Start B</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer Start B")]
    public int TracerStartB { get; set; }

    /// <summary>Gets or sets the <c>Tracer End R</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer End R")]
    public int TracerEndR { get; set; }

    /// <summary>Gets or sets the <c>Tracer End G</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer End G")]
    public int TracerEndG { get; set; }

    /// <summary>Gets or sets the <c>Tracer End B</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Tracer End B")]
    public int TracerEndB { get; set; }

    /// <summary>Gets or sets the <c>Shop price</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Shop price")]
    public int ShopPrice { get; set; }

    /// <summary>Gets or sets the <c>Shop amount</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Shop amount")]
    public int ShopAmount { get; set; }

    /// <summary>Gets or sets the <c>Shop Name Resource</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Shop Name Resource")]
    public string ShopNameResource { get; set; } = string.Empty;
}

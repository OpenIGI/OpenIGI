using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A named whole number in a weapon's parameter container, <c>TagItemInt32</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>).
/// </remarks>
public sealed partial class Igi2TagItemInt32Task : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "TagItemInt32";

    /// <summary>Gets or sets the <c>Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Value</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Value")]
    public int Value { get; set; }
}

/// <summary>A named real number in a weapon's parameter container, <c>TagItemReal32</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>).
/// </remarks>
public sealed partial class Igi2TagItemReal32Task : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "TagItemReal32";

    /// <summary>Gets or sets the <c>Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Value</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Value")]
    public float Value { get; set; }
}

/// <summary>A named short string in a weapon's parameter container, <c>TagItemString16</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>).
/// </remarks>
public sealed partial class Igi2TagItemString16Task : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "TagItemString16";

    /// <summary>Gets or sets the <c>Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Value</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String16, "Value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>A named flag in a weapon's parameter container, <c>TagItemBool8</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>).
/// </remarks>
public sealed partial class Igi2TagItemBool8Task : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "TagItemBool8";

    /// <summary>Gets or sets the <c>Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Value</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Value")]
    public bool Value { get; set; }
}

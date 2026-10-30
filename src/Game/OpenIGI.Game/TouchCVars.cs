using OpenIGI.Core.Configuration;

namespace OpenIGI.Game;

/// <summary>A touch screen's own settings: the gyroscope aim a phone offers on top of the touch controls.</summary>
/// <remarks>
/// <para>
/// Not <c>g_</c>: the Gameplay page builds itself from that prefix on every platform, and these do nothing without a touch
/// screen. The Controls page shows them under Touch once the screen has been touched.
/// </para>
/// <para>
/// Presentation of the player's own input only, like a mouse's sensitivity: the gyroscope turns the view by mouse counts, so
/// nothing the simulation records is reached any other way.
/// </para>
/// </remarks>
[CVarContainer]
public static class TouchCVars
{
    /// <summary>When turning the phone turns the view.</summary>
    /// <remarks>Off by default: a player lying down or on a bus would find the view turning on its own.</remarks>
    public static readonly EnumCVar<GyroAim> Gyro = CVar.Choice(
        "touch_gyro",
        GyroAim.Off,
        CVarOptions.Archive,
        "When turning the phone turns the view: off, scope (through a sight or the binoculars) or always.");

    /// <summary>How far the view turns for a turn of the phone: one turns it as far as the phone turned.</summary>
    public static readonly NumericCVar<float> GyroSensitivity = CVar.Float(
        "touch_gyro_sensitivity",
        1f,
        0.2f,
        4f,
        CVarOptions.Archive,
        "How far the view turns for a turn of the phone. 1 turns it as far as the phone turned.");

    /// <summary>Whether tilting the phone's top away looks down rather than up.</summary>
    public static readonly BoolCVar GyroInvert = CVar.Bool(
        "touch_gyro_invert",
        false,
        CVarOptions.Archive,
        "Tilting the top of the phone away looks down instead of up.");
}

/// <summary>When the gyroscope aims.</summary>
public enum GyroAim
{
    /// <summary>Never: the view turns by the fingers alone.</summary>
    Off,

    /// <summary>Through a sight or the binoculars, where small corrections matter most.</summary>
    Scope,

    /// <summary>Always, alongside the fingers.</summary>
    Always,
}

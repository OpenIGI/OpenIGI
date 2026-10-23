using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

/// <summary>A begin or compose of the remastered heads-up display's layer, with how many 2D draws had been recorded.</summary>
/// <param name="Composed">Whether this was the compose rather than the begin.</param>
/// <param name="DrawsBefore">The 2D draws recorded before it.</param>
/// <param name="Fx">The effects the compose was handed; none for a begin.</param>
public sealed record HudLayerEvent(bool Composed, int DrawsBefore, HudFxSettings Fx);

public sealed partial class RecordingGraphicsDevice
{
    private bool _hudLayerOpen;

    /// <summary>Gets the HUD layer's begins and composes, in order.</summary>
    public List<HudLayerEvent> HudLayerEvents { get; } = [];

    /// <summary>Gets or sets a value indicating whether the device offers a HUD layer at all.</summary>
    public bool SupportsHudLayer { get; set; } = true;

    /// <inheritdoc/>
    public bool BeginHudLayer()
    {
        if (!SupportsHudLayer || _hudLayerOpen)
        {
            return false;
        }

        _hudLayerOpen = true;
        HudLayerEvents.Add(new HudLayerEvent(false, Draws.Count, HudFxSettings.None));
        return true;
    }

    /// <inheritdoc/>
    public void ComposeHudLayer(in HudFxSettings fx)
    {
        if (!_hudLayerOpen)
        {
            return;
        }

        _hudLayerOpen = false;
        HudLayerEvents.Add(new HudLayerEvent(true, Draws.Count, fx));
    }
}

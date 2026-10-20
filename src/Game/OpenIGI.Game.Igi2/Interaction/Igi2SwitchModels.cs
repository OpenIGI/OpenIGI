using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>The model each switch wears, as its update last picked it (<c>Switch_RunHandler</c>): on, on and pressed, off, off and pressed, destroyed.</summary>
/// <param name="switches">The level's switches, by task id.</param>
public sealed class Igi2SwitchModels(IReadOnlyDictionary<int, Igi2SwitchTask> switches) : IPlacementVariant
{
    /// <inheritdoc/>
    public int VariantOf(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement.Variants is not null && switches.TryGetValue(placement.LevelId, out Igi2SwitchTask? button)
            ? button.ModelSlot
            : -1;
    }
}

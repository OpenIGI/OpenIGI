using OpenIGI.Game.World;
using OpenIGI.Scripting.Vocabulary;
using static OpenIGI.Game.World.MissionScriptVocabulary;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The natives Project I.G.I.'s level expressions call beyond the shared ones.</summary>
/// <remarks>
/// Retail's levels call six natives from expressions in all, four of them shared with I.G.I. 2; these are the other two,
/// both cut-scene housekeeping in run-at-start/stop strings.
/// </remarks>
public static class Igi1MissionScriptVocabulary
{
    /// <summary>Gets the natives, shared ones first.</summary>
    public static IReadOnlyList<NativeSignature> Signatures { get; } =
    [
        .. MissionScriptVocabulary.Signatures,
        Action("Game_CutsceneDelete", [], "Removes the cut-scene that is playing."),
        Action("StatusMessageScreen_ClearMessages", [], "Clears the status messages on screen."),
    ];
}

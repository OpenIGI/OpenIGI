using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.World;

/// <summary>
/// The natives a mission's level expressions call in both games (conditions and run-at-start/stop strings), and the
/// constants every level expression can name.
/// </summary>
/// <remarks>
/// Each title adds its own natives beside these (<c>Igi1MissionScriptVocabulary</c>, <c>Igi2MissionScriptVocabulary</c>). A
/// native is listed here only when both games register it with the same reads, so a graph that uses it compiles the same for
/// either.
/// </remarks>
public static class MissionScriptVocabulary
{
    /// <summary>The family of <c>TRUE</c> and <c>FALSE</c>.</summary>
    public const string BoolFamily = "Bool";

    /// <summary>The palette category of the mission natives.</summary>
    public const string Category = "Mission";

    /// <summary>Gets the natives both games' level expressions call.</summary>
    public static IReadOnlyList<NativeSignature> Signatures { get; } =
    [
        Action(GameMusicNatives.EnableNative, [], "Lets the level's music play again."),
        Action(GameMusicNatives.DisableNative, [], "Stops the level's music, as a cut-scene does while it runs."),
        Action("MenuManager_SetEnabled", [new("On", ScriptType.Bool)], "Lets the player open the menus, or not; a cut-scene turns it off."),
        Pure("LevelFlow_GetBreakCutSceneKey", ScriptType.Bool, "Whether the key that skips a cut-scene is held."),
    ];

    /// <summary>Gets the constants every level expression can name, as the level expression compiler resolves them.</summary>
    /// <remarks><c>GAME_FREQUENCY</c> is the tick rate, so <c>9*GAME_FREQUENCY</c> is nine seconds.</remarks>
    public static IReadOnlyList<ConstantFamily> Families { get; } =
    [
        new(BoolFamily, [new("FALSE", 0), new("TRUE", 1)]),
        new("Time", [new("GAME_FREQUENCY", LevelExpressionRuntime.Constant("GAME_FREQUENCY") ?? 30)]),
    ];

    /// <summary>Gets the read-only reals the game loop registers for both games' expressions.</summary>
    public static IReadOnlyList<NativeParameter> Globals { get; } =
    [
        new("WORLD_METER", ScriptType.Real, "Engine units in a metre (4096)."),
        new("MATH_PI", ScriptType.Real),
    ];

    /// <summary>A native that does something.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="doc">What it does.</param>
    /// <returns>The signature.</returns>
    public static NativeSignature Action(string name, NativeParameter[] parameters, string doc) =>
        new(name, parameters, null, NativeEffect.Action, ScriptContexts.Mission) { Category = Category, Doc = doc };

    /// <summary>A native that only reads.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="returns">What it returns.</param>
    /// <param name="doc">What it answers.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <returns>The signature.</returns>
    public static NativeSignature Pure(string name, ScriptType returns, string doc, params NativeParameter[] parameters) =>
        new(name, parameters, returns, NativeEffect.Pure, ScriptContexts.Mission) { Category = Category, Doc = doc };
}

using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi1;

/// <summary>Project I.G.I., as the engine runs it.</summary>
public sealed class Igi1Runtime : IGameTitleRuntime
{
    /// <summary>Initializes a new instance of the <see cref="Igi1Runtime"/> class, the bomb's clips added to the human sets.</summary>
    public Igi1Runtime() => Multiplayer.Igi1BombAnimations.Register();

    /// <inheritdoc/>
    public GameTitle Title => GameTitle.Igi1;

    /// <inheritdoc/>
    public ITitleDataLayout Layout { get; } = new Igi1DataLayout();

    /// <inheritdoc/>
    public AssetFormatProfile Formats => AssetFormatProfile.Igi1;

    /// <inheritdoc/>
    public IReadOnlyList<IContentModule> Modules => Igi1ContentModules.All;

    /// <inheritdoc/>
    /// <remarks>The MP5 SD3 Jones starts every mission with.</remarks>
    public string? CoopPreviewWeapon => "WEAPON_ID_MP5SD";

    /// <inheritdoc/>
    /// <remarks>
    /// Task fields come from the retail table (<see cref="World.Igi1TaskFields"/>) over what a scratch export finds: the
    /// first game's systems bind most fields during mission setup, where no scratch instance reaches.
    /// </remarks>
    public ScriptVocabulary CreateScriptVocabulary() => new ScriptVocabulary()
        .Add(AiScriptVocabulary.Signatures)
        .Add(World.Igi1MissionScriptVocabulary.Signatures)
        .Add(AiScriptVocabulary.Families)
        .Add(MissionScriptVocabulary.Families)
        .Add(TitleScriptVocabulary.RecordTaskFields(Modules))
        .Add(World.Igi1TaskFields.Fields)
        .AddGlobals(MissionScriptVocabulary.Globals);
}

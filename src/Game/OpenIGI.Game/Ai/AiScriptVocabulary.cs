using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Ai;

/// <summary>Project I.G.I.'s AI script API as the editor's vocabulary: <see cref="AiScriptApi"/>'s table, typed for pins.</summary>
/// <remarks>
/// Nothing here is a second table. Parameters come straight from <see cref="AiNative.Parameters"/>; this adds only what the
/// registrar's three return tags cannot say: which integer results are task ids, events, flags or truth values, and which
/// calls change something. Two getters do: <c>AIFunction_GetRandomValue</c> and <c>AIFunction_GetAnimationToPlay</c> draw from
/// the level's random stream, so evaluating one twice is not the same as once, and they are exec nodes with a result.
/// </remarks>
public static class AiScriptVocabulary
{
    private const string ActionsCategory = "AI|Actions";
    private const string FunctionsCategory = "AI|Functions";

    /// <summary>Gets every native's signature, in registration order.</summary>
    public static IReadOnlyList<NativeSignature> Signatures { get; } = [.. AiScriptApi.Natives.Select(ToSignature)];

    /// <summary>Gets the AI module's constants, grouped into families.</summary>
    public static IReadOnlyList<ConstantFamily> Families { get; } = BuildFamilies();

    /// <summary>The family a constant belongs to, by its prefix.</summary>
    /// <param name="name">The constant's name.</param>
    /// <returns>The family's name.</returns>
    public static string FamilyOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name switch
        {
            _ when name.StartsWith("AIEVENT_", StringComparison.Ordinal) => AiScriptApi.EventFamily,
            _ when name.StartsWith("AITYPE_", StringComparison.Ordinal) => AiScriptApi.TypeFamily,
            _ when name.StartsWith("AIACTIONFLAG_", StringComparison.Ordinal) => AiScriptApi.ActionFlagFamily,
            _ when name.StartsWith("AIALARMACCESS_", StringComparison.Ordinal) => AiScriptApi.AlarmAccessFamily,
            _ => AiScriptApi.RangeFamily,
        };
    }

    private static NativeSignature ToSignature(AiNative native) =>
        new(native.Name, native.Parameters, ReturnOf(native), EffectOf(native), ScriptContexts.Ai)
        {
            Category = native.IsAction ? ActionsCategory : FunctionsCategory,
        };

    private static ScriptType? ReturnOf(AiNative native) => native.Name switch
    {
        "AIFunction_GetCurrentEventType" => ScriptType.EnumOf(AiScriptApi.EventFamily),
        "AIFunction_GetAlarmAccess" => ScriptType.EnumOf(AiScriptApi.AlarmAccessFamily),
        "AIFunction_GetAlarmTriggerID" or "AIFunction_GetAlarmControlID" or "AIFunction_GetGunnerID" => ScriptType.TaskRef,
        "AIFunction_GetAlarmControlStatus" or "AIFunction_GetGunnerStatus" or "AIFunction_IsEventBehind" => ScriptType.Bool,
        _ => native.Return switch
        {
            AiNativeReturn.Int => ScriptType.Int,
            AiNativeReturn.Real => ScriptType.Real,
            _ => null,
        },
    };

    private static NativeEffect EffectOf(AiNative native) =>
        native.Return == AiNativeReturn.Void
        || native.Name is "AIFunction_GetRandomValue" or "AIFunction_GetAnimationToPlay"
            ? NativeEffect.Action
            : NativeEffect.Pure;

    private static ConstantFamily[] BuildFamilies()
    {
        string[] order =
        [
            AiScriptApi.EventFamily, AiScriptApi.ActionFlagFamily, AiScriptApi.TypeFamily, AiScriptApi.AlarmAccessFamily,
            AiScriptApi.RangeFamily,
        ];

        ILookup<string, KeyValuePair<string, int>> grouped = AiScriptApi.Constants.ToLookup(static c => FamilyOf(c.Key));
        return [.. order.Select(family => new ConstantFamily(family, [.. grouped[family].OrderBy(static c => c.Value)]))];
    }
}

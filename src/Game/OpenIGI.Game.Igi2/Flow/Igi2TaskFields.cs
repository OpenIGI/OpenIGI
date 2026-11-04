using OpenIGI.Game.World;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The fields I.G.I. 2 task types export that the port's tasks do not bind yet, from each type's retail
/// <c>RegisterScriptSymbolsHandler</c>.
/// </summary>
/// <remarks>
/// Every other I.G.I. 2 type exports from its own task class, which a scratch export records. These three are the
/// multiplayer and C4 types the port declares without behaviour; retail levels read their fields, so the editor needs the
/// pins before the port implements them.
/// </remarks>
public static class Igi2TaskFields
{
    private static readonly TaskFieldDeclaration[] Table =
    [
        new("C4BombArea", null, ["isInside", "isExploded", "isDisarmed"], 0x4F4540),
        new("Network_Objective", null, ["isObjectiveComplete", "isObjectiveFailed"], 0x4D12D0),
        new("Network_Mission", null, ["isMissionComplete", "isMissionFailed"], 0x4D13F0),
    ];

    /// <summary>Gets each type's fields, by type name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> Fields { get; } = DeclaredTaskFields.Build(Table);
}

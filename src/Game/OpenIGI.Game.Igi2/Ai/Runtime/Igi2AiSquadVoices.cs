using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>The squad voice lines, <c>AISquad_aazVoiceTable</c> as <c>AISquad_InitVoiceTable</c> fills it: four takes of each of twenty lines.</summary>
public static class Igi2AiSquadVoices
{
    private static readonly string[] Table =
    [
        "GV_01", "GV_02", "GV_01", "GV_02", "GV_05", "GV_06", "GV_07", "GV_05", "GV_11", "GV_11", "GV_11", "GV_11",
        "GV_13", "GV_13", "GV_13", "GV_13", "GV_15", "GV_16", "GV_17", "GV_15", "GV_19", "GV_19", "GV_19", "GV_19",
        "GV_26", "GV_27", "GV_26", "GV_27", "GV_30", "GV_31", "GV_30", "GV_31", "GV_34", "GV_35", "GV_36", "GV_34",
        "GV_40", "GV_41", "GV_40", "GV_41", "GV_47", "GV_48", "GV_47", "GV_48", "GV_62", "GV_63", "GV_64", "GV_66",
        "GV_75", "GV_76", "GV_75", "GV_76", "GV_80", "GV_81", "GV_82", "GV_80", "GV_84", "GV_84", "GV_84", "GV_84",
        "GV_49", "GV_49", "GV_49", "GV_49", "GV_87", "GV_88", "GV_89", "GV_90", "GV_91", "GV_92", "GV_93", "GV_94",
        "GV_60", "GV_60", "GV_60", "GV_60", string.Empty, string.Empty, string.Empty, string.Empty,
    ];

    private static readonly bool[] Lone =
    [
        true, true, true, true, false, true, false, false, false, false, false, true, true, false, true, false, false, false, false, false,
    ];

    /// <summary>A take of a line.</summary>
    /// <param name="sample">The line.</param>
    /// <param name="take">The take, nought to three.</param>
    /// <returns>The sound's name, without its voice.</returns>
    public static string Take(int sample, int take) => Table[(4 * sample) + take];

    /// <summary>Whether a lone soldier keeps a line to himself, <c>AISquad_aisVoiceDontPlayForSingleAI</c>.</summary>
    /// <param name="sample">The line.</param>
    /// <returns><see langword="true"/> when he does.</returns>
    public static bool NotForLoneSoldier(int sample) => Lone[sample];
}

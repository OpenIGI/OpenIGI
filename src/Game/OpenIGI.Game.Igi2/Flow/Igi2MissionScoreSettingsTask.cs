using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// What each rank asks of a mission, I.G.I. 2's <c>MissionScoreSettings</c> (missionscoresettings.c): for the ranks Bodyguard, Secret
/// Agent and IGI Operative, the time, times spotted, loads, health and accuracy required, as the level's parameters set them. It
/// does nothing as the level runs; the mission's end weighs the player's score against them (<see cref="MissionScoring"/>).
/// </summary>
public sealed partial class Igi2MissionScoreSettingsTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MissionScoreSettings";

    /// <summary>Gets the requirements, by rank (nought Bodyguard to two IGI Operative) then item, <c>atMissionRankRequirement</c>.</summary>
    public MissionRankRequirements Requirements { get; } = new();

    /// <summary>Gets or sets the most seconds Bodyguard allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (Bodyguard)")]
    public float TimeBodyguard { get => Requirements[0, MissionRankItem.Time]; set => Requirements[0, MissionRankItem.Time] = value; }

    /// <summary>Gets or sets the most seconds Secret Agent allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (Secret Agent)")]
    public float TimeSecretAgent { get => Requirements[1, MissionRankItem.Time]; set => Requirements[1, MissionRankItem.Time] = value; }

    /// <summary>Gets or sets the most seconds IGI Operative allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time Required (secs) (IGI Operative)")]
    public float TimeOperative { get => Requirements[2, MissionRankItem.Time]; set => Requirements[2, MissionRankItem.Time] = value; }

    /// <summary>Gets or sets the most times spotted Bodyguard allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (Bodyguard)")]
    public float SpottedBodyguard { get => Requirements[0, MissionRankItem.Spotted]; set => Requirements[0, MissionRankItem.Spotted] = value; }

    /// <summary>Gets or sets the most times spotted Secret Agent allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (Secret Agent)")]
    public float SpottedSecretAgent { get => Requirements[1, MissionRankItem.Spotted]; set => Requirements[1, MissionRankItem.Spotted] = value; }

    /// <summary>Gets or sets the most times spotted IGI Operative allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Times Spotted Limit (IGI Operative)")]
    public float SpottedOperative { get => Requirements[2, MissionRankItem.Spotted]; set => Requirements[2, MissionRankItem.Spotted] = value; }

    /// <summary>Gets or sets the most loads Bodyguard allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (Bodyguard)")]
    public float LoadsBodyguard { get => Requirements[0, MissionRankItem.Loads]; set => Requirements[0, MissionRankItem.Loads] = value; }

    /// <summary>Gets or sets the most loads Secret Agent allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (Secret Agent)")]
    public float LoadsSecretAgent { get => Requirements[1, MissionRankItem.Loads]; set => Requirements[1, MissionRankItem.Loads] = value; }

    /// <summary>Gets or sets the most loads IGI Operative allows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Number of Loads Allowed (IGI Operative)")]
    public float LoadsOperative { get => Requirements[2, MissionRankItem.Loads]; set => Requirements[2, MissionRankItem.Loads] = value; }

    /// <summary>Gets or sets the least health, in per cent, Bodyguard requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (Bodyguard)")]
    public float HealthBodyguard { get => Requirements[0, MissionRankItem.Health]; set => Requirements[0, MissionRankItem.Health] = value; }

    /// <summary>Gets or sets the least health, in per cent, Secret Agent requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (Secret Agent)")]
    public float HealthSecretAgent { get => Requirements[1, MissionRankItem.Health]; set => Requirements[1, MissionRankItem.Health] = value; }

    /// <summary>Gets or sets the least health, in per cent, IGI Operative requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Health Required (%) (IGI Operative)")]
    public float HealthOperative { get => Requirements[2, MissionRankItem.Health]; set => Requirements[2, MissionRankItem.Health] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, Bodyguard requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (Bodyguard)")]
    public float AccuracyBodyguard { get => Requirements[0, MissionRankItem.Accuracy]; set => Requirements[0, MissionRankItem.Accuracy] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, Secret Agent requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (Secret Agent)")]
    public float AccuracySecretAgent { get => Requirements[1, MissionRankItem.Accuracy]; set => Requirements[1, MissionRankItem.Accuracy] = value; }

    /// <summary>Gets or sets the least accuracy, in per cent, IGI Operative requires.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy Required (%) (IGI Operative)")]
    public float AccuracyOperative { get => Requirements[2, MissionRankItem.Accuracy]; set => Requirements[2, MissionRankItem.Accuracy] = value; }
}

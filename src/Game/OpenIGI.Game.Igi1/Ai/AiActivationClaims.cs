using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The level's activation claims — the global list at <c>dword_5BDA28</c> that keeps two guards from
/// running to the same alarm panel.
/// </summary>
/// <remarks>
/// <para>
/// <c>AIAction_Activate</c>'s start (<c>0x48E620</c>) tests the target task against this list
/// (<c>sub_4532D0</c>) and <b>skips the whole action when it is already claimed</b>; otherwise it adds
/// the task (<c>sub_453300(task, 1)</c>) — and, when the target is the AI's own alarm trigger, the
/// alarm <i>control</i> too — keeping the claim handles at <c>HumanAI+15024/+15028</c>. The action's
/// end function (<c>0x48E870</c>) and the AI's own death cleanup (<c>0x44F410</c> at
/// <c>0x44FAD2</c>/<c>0x44FAEB</c>) release them, so a guard shot on the way frees the panel for the
/// next one.
/// </para>
/// <para>
/// Only the AI module reads the list — membership means <b>"an activation is under way"</b>, not "the
/// task has been activated".
/// </para>
/// </remarks>
public sealed class AiActivationClaims
{
    private readonly HashSet<int> _claimed = [];

    /// <summary>Whether a task is claimed — <c>sub_4532D0</c>.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns><see langword="true"/> when some AI is already activating it.</returns>
    public bool Contains(int taskId) => _claimed.Contains(taskId);

    /// <summary>Claims a task — <c>sub_453300(task, 1)</c>.</summary>
    /// <param name="taskId">The task.</param>
    public void Claim(int taskId) => _claimed.Add(taskId);

    /// <summary>Releases a claim — <c>sub_453390</c>.</summary>
    /// <param name="taskId">The task.</param>
    public void Release(int taskId) => _claimed.Remove(taskId);
}

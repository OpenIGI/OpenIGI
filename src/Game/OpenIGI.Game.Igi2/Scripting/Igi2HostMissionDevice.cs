using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>
/// I.G.I. 2's <c>MISSION:</c> device over another game's mission: the mission's own files, and I.G.I. 2's generic scripts for
/// the soldiers and squads it has none for.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2's minds read their scripts and graphs through <c>MISSION:</c>. The graphs are the mission's own: Project I.G.I. keeps
/// them at <c>graphs/graph&lt;id&gt;.dat</c> in the same named-field format, so I.G.I. 2's AI walks the graphs its levels were
/// authored with. A soldier's or a squad's script, <c>AI/&lt;id&gt;.qvm</c> or <c>AI/Squad_&lt;id&gt;.qvm</c>, is the generic one
/// (<see cref="Igi2GenericScripts"/>): the host's own scripts beside it are compiled against the host's natives.
/// </para>
/// </remarks>
/// <param name="missionDirectory">The host mission's directory.</param>
/// <param name="scripts">I.G.I. 2's generic scripts.</param>
public sealed class Igi2HostMissionDevice(string missionDirectory, Igi2GenericScripts scripts) : AiScriptDevice(missionDirectory)
{
    private readonly Igi2GenericScripts _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));

    /// <inheritdoc/>
    /// <remarks>A soldier's is the generic soldier's script; a squad's, <c>AI/Squad_&lt;id&gt;.qvm</c>, the generic squad's.</remarks>
    protected override byte[]? Fallback(ReadOnlySpan<char> stem)
    {
        if (stem.StartsWith("squad_", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(stem[6..], out _) ? _scripts.Squad : null;
        }

        return int.TryParse(stem, out _) ? _scripts.Soldier : null;
    }
}

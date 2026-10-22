using System.Security.Cryptography;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>
/// The scripts most of I.G.I. 2's soldiers and squads run: a mind left to its type's default handler, and a squad left to its
/// default, found in the installation itself.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2 gives every soldier and every squad a compiled script of its own, <c>MISSION:AI/&lt;id&gt;.qvm</c> and
/// <c>MISSION:AI/Squad_&lt;id&gt;.qvm</c>. Most are one script, byte for byte, which leaves every event to the AI's default handler
/// (<c>AIFunction_DefaultHandler</c>, <c>AISquad_DefaultHandler</c>): 546 of the campaign's 1194 soldier scripts and 269 of its
/// 471 squad scripts. A soldier another game's level places has no script of I.G.I. 2's, so it runs that one, as an ordinary
/// I.G.I. 2 soldier does.
/// </para>
/// <para>Read from the installation, so nothing that hosts I.G.I. 2's soldiers carries the game's files.</para>
/// </remarks>
/// <param name="Soldier">The soldier script.</param>
/// <param name="Squad">The squad script.</param>
public sealed record Igi2GenericScripts(byte[] Soldier, byte[] Squad)
{
    /// <summary>Finds the scripts in an installation: the commonest of each kind across its missions.</summary>
    /// <param name="dataRoot">Where I.G.I. 2 is installed.</param>
    /// <returns>The scripts.</returns>
    /// <exception cref="InvalidOperationException">The installation has no mission scripts.</exception>
    public static Igi2GenericScripts Find(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        Dictionary<string, (int Count, string Path)> soldiers = new(StringComparer.Ordinal);
        Dictionary<string, (int Count, string Path)> squads = new(StringComparer.Ordinal);
        string missions = Path.Combine(dataRoot, "MISSIONS");
        if (Directory.Exists(missions))
        {
            foreach (string file in Directory.EnumerateFiles(missions, "*.qvm", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "AI", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                Dictionary<string, (int Count, string Path)> kind =
                    Path.GetFileName(file).StartsWith("Squad_", StringComparison.OrdinalIgnoreCase) ? squads : soldiers;
                kind[hash] = kind.TryGetValue(hash, out (int Count, string Path) seen) ? (seen.Count + 1, seen.Path) : (1, file);
            }
        }

        if (soldiers.Count == 0 || squads.Count == 0)
        {
            throw new InvalidOperationException($"I.G.I. 2's installation at {dataRoot} has no mission AI scripts.");
        }

        return new Igi2GenericScripts(File.ReadAllBytes(Commonest(soldiers)), File.ReadAllBytes(Commonest(squads)));
    }

    // The most-shared script; a tie goes to the ordinally first hash, so the choice does not depend on the walk's order.
    private static string Commonest(Dictionary<string, (int Count, string Path)> kind) =>
        kind.OrderByDescending(static e => e.Value.Count).ThenBy(static e => e.Key, StringComparer.Ordinal).First().Value.Path;
}

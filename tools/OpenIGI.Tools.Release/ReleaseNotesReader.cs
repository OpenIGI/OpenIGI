using System.Text.Json;
using System.Text.RegularExpressions;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Reads the player-facing notes out of git-cliff's <c>--context</c> output.</summary>
/// <remarks>
/// <c>cliff.toml</c> assigns each kept commit a group such as <c>&lt;!-- 1 --&gt;Fixes</c>: the number orders
/// the groups and the rest is the heading. Commits git-cliff skipped (chores, tests, merges) are not in the
/// context at all, so everything read here is something a player can be told about.
/// </remarks>
public static partial class ReleaseNotesReader
{
    /// <summary>Builds the note sections from a git-cliff context document.</summary>
    /// <param name="contextJson">The output of <c>git cliff --context</c> for the release's commit range.</param>
    /// <returns>The sections in display order, each with its changes oldest first.</returns>
    /// <exception cref="JsonException">The document is not a git-cliff context.</exception>
    public static IReadOnlyList<ReleaseNoteSection> Read(string contextJson)
    {
        using JsonDocument document = JsonDocument.Parse(contextJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A git-cliff context is an array of releases.");
        }

        Dictionary<string, (int Order, List<ReleaseNote> Notes)> groups = new(StringComparer.Ordinal);
        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("commits", out JsonElement commits))
            {
                continue;
            }

            foreach (JsonElement commit in commits.EnumerateArray())
            {
                string? group = commit.TryGetProperty("group", out JsonElement g) ? g.GetString() : null;
                string? message = commit.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                string? scope = commit.TryGetProperty("scope", out JsonElement s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;

                (int order, string title) = SplitGroup(group);
                if (!groups.TryGetValue(title, out (int Order, List<ReleaseNote> Notes) entry))
                {
                    entry = (order, []);
                    groups[title] = entry;
                }

                entry.Notes.Add(new ReleaseNote(string.IsNullOrWhiteSpace(scope) ? null : scope, Sentence(message)));
            }
        }

        return [.. groups
            .OrderBy(static pair => pair.Value.Order)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ReleaseNoteSection(pair.Key, pair.Value.Notes))];
    }

    // "<!-- 2 -->Performance" -> (2, "Performance"); a group without the comment sorts after every numbered one.
    private static (int Order, string Title) SplitGroup(string group)
    {
        Match match = OrderedGroup().Match(group);
        return match.Success
            ? (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups[2].Value.Trim())
            : (int.MaxValue, group.Trim());
    }

    // The subject line only, first letter raised: a commit body is for reviewers, not for patch notes.
    private static string Sentence(string message)
    {
        string line = message.Split('\n', 2)[0].Trim();
        return line.Length == 0 ? line : char.ToUpperInvariant(line[0]) + line[1..];
    }

    [GeneratedRegex(@"^\s*<!--\s*(\d+)\s*-->\s*(.+)$", RegexOptions.Singleline)]
    private static partial Regex OrderedGroup();
}

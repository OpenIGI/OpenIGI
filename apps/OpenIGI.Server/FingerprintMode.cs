using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Composition;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Host;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Server;

/// <summary>
/// The checker's official game files (<c>-fingerprints</c>): every mission's content fingerprint in every installation of each
/// game this machine checks in, written to standard output as one line of JSON. The checker sends it to the master, which
/// takes a run only from files one of these editions holds, and tells a player whose files are none of them before a run of
/// theirs is sent.
/// </summary>
internal static class FingerprintMode
{
    /// <summary>Gets whether the arguments ask for the fingerprints.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>Whether they do.</returns>
    public static bool Asked(string[] args) => Array.Exists(args, static a => a.Equals("-fingerprints", StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes the fingerprints: <c>{ "version": ..., "titles": { "igi1": { "1": [ ... ], ... }, ... } }</c>.</summary>
    /// <returns>The exit code: nought, or two where no game is installed here at all.</returns>
    public static int Run()
    {
        using LogRouter logger = new();
#pragma warning disable RS0030 // Its JSON is its standard output; its log goes to standard error.
        logger.AddSink(new ConsoleSink(Console.Error, Console.Error, minimumLevel: LogLevel.Error));
        using Stream stdout = Console.OpenStandardOutput();
#pragma warning restore RS0030

        Dictionary<string, SortedDictionary<int, SortedSet<string>>> titles = [];
        foreach (GameTitle title in GameTitle.All)
        {
            if (!TitleCatalog.TryResolve(title.Id, out IGameTitleRuntime? runtime))
            {
                continue;
            }

            foreach (string gamePath in VerifyMode.Installs(title, null))
            {
                using RecordingGraphicsDevice device = new() { IsRecording = false };
                using GameHost host = VerifyMode.NewHost(runtime, gamePath, logger, device);
                host.Start();
                SortedDictionary<int, SortedSet<string>> missions = titles.TryGetValue(title.Id, out var known) ? known : titles[title.Id] = [];
                foreach (int mission in host.CampaignMissionIds)
                {
                    string fingerprint = host.ContentFingerprintOf(mission);
                    (missions.TryGetValue(mission, out SortedSet<string>? set) ? set : missions[mission] = new(StringComparer.Ordinal)).Add(fingerprint);
                }

                host.Stop();
            }
        }

        using (Utf8JsonWriter json = new(stdout))
        {
            json.WriteStartObject();
            json.WriteString("version", GameHost.BuildVersion);
            json.WriteStartObject("titles");
            foreach ((string id, SortedDictionary<int, SortedSet<string>> missions) in titles)
            {
                json.WriteStartObject(id);
                foreach ((int mission, SortedSet<string> fingerprints) in missions)
                {
                    json.WriteStartArray(mission.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    foreach (string fingerprint in fingerprints)
                    {
                        json.WriteStringValue(fingerprint);
                    }

                    json.WriteEndArray();
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        stdout.WriteByte((byte)'\n');
        return titles.Count > 0 ? 0 : VerifyMode.Unchecked;
    }
}

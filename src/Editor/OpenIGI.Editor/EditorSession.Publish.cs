using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Panels;
using OpenIGI.Host.Editor;
using OpenIGI.Releases.Maps;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor;

/// <summary>
/// Publishing the open mission as a community map: its folder packed (each file the game's own, unchanged, named rather than
/// carried), checked as the server will check it, signed by this installation's key and sent; up for everyone at once.
/// </summary>
/// <remarks>
/// A mission made in the editor is one: its folder is the project's own, with its own <c>mission.qvm</c>. A level of the game
/// edited in place is not -- its files are the game's -- and is not offered.
/// </remarks>
internal sealed partial class EditorSession
{
    private Task<string>? _publishing;

    private void RegisterPublishActions(ActionTable actions) =>
        actions.Add(new EditorAction("file.publishmap", "Publish Map…", OpenPublish)
        {
            Category = "File",
            CanExecute = () => MapFolder() is not null && _publishing is null,
            DynamicTitle = () => _publishing is not null ? "Publishing…" : MapFolder() is null && _workspace.Mission is not null ? "Publish Map… (make it a new mission first)" : "Publish Map…",
        });

    // The open mission's folder when it is one the project made (it has its own mission.qvm there), or null.
    private string? MapFolder() =>
        _workspace.Mission is { } mission
        && File.Exists(Path.Combine(_workspace.Project.FilesDirectory, mission.MissionDirectory.Trim('/'), Core.Missions.NewMission.ScriptName))
            ? mission.MissionDirectory.Trim('/')
            : null;

    private void OpenPublish()
    {
        if (MapFolder() is not { } folder || _workspace.Mission is not { } mission || (_workspace.History.IsDirty && !Save()))
        {
            return;
        }

        string title = _workspace.MissionName.Length > MapRules.MaxTitle ? _workspace.MissionName[..MapRules.MaxTitle] : _workspace.MissionName;
        TextField titleField = new(_theme) { Text = title, Placeholder = "What players see it called", Validate = static t => t.Length <= MapRules.MaxTitle };
        TextField idField = new(_theme) { Text = Slug(title), Placeholder = "lowercase-with-hyphens", Validate = static t => t.Length == 0 || Releases.ContentManifest.IsValidId(t) };
        TextField authorField = new(_theme) { Text = EditorCVars.MapAuthor.Value, Placeholder = "Your name as players see it", Validate = static t => t.Length <= MapRules.MaxAuthor };
        TextField summaryField = new(_theme) { Placeholder = "What it is, in a sentence or two", Validate = static t => t.Length <= MapRules.MaxSummary };
        WizardForm details = new(_theme, [("Name", titleField), ("Id", idField), ("Author", authorField), ("About", summaryField)]);
        WizardReview review = new(_theme);
        (MapManifest Manifest, Dictionary<string, byte[]> Bytes, IReadOnlyList<string> Problems)? packed = null;

        MapManifest About() => new(
            MapManifest.CurrentSchema,
            idField.Text.Trim(),
            titleField.Text.Trim(),
            summaryField.Text.Trim(),
            _host.Title.Id,
            authorField.Text.Trim(),
            mission.Id,
            folder,
            []);

        Dialog? dialog = null;
        Wizard wizard = new(
            _theme,
            [
                new WizardPage("Details", "Publish as a community map", "Up for everyone at once, in the game and the launcher; you can publish new versions of it from this installation.", details)
                {
                    Problem = () => titleField.Text.Trim().Length == 0 ? "Give the map a name."
                        : !Releases.ContentManifest.IsValidId(idField.Text.Trim()) ? "The id is lowercase letters, digits and hyphens: it is how the map is found."
                        : authorField.Text.Trim().Length == 0 ? "Say who made it."
                        : null,
                },
                new WizardPage("Review", "Publish this map?", "Checked as the server will check it. The game's own files are named, not sent: players' own games supply them.", review)
                {
                    Entered = () =>
                    {
                        packed = PackMap(About());
                        review.Lines = PublishLines(packed.Value.Manifest, packed.Value.Problems);
                    },
                    Problem = () => packed is { Problems: [string first, ..] } ? first : null,
                },
            ],
            "Publish",
            () =>
            {
                if (packed is not { Problems.Count: 0 } ready)
                {
                    return false;
                }

                EditorCVars.MapAuthor.Value = ready.Manifest.Author;
                StartPublishing(ready.Manifest, ready.Bytes);
                return true;
            });
        wizard.Finished += () => dialog?.Close();
        dialog = Dialog.Open(_root, "Publish Map", wizard, 640f, 460f);
    }

    // The map's folder packed: every file of it, those the game has byte for byte named by the game's path; and what the rules
    // say of it, the game's own files known.
    private (MapManifest Manifest, Dictionary<string, byte[]> Bytes, IReadOnlyList<string> Problems) PackMap(MapManifest about)
    {
        string root = Path.Combine(_workspace.Project.FilesDirectory, about.MissionDirectory);
        Dictionary<string, byte[]> bytes = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            bytes[$"{about.MissionDirectory}/{Path.GetRelativePath(root, file).Replace('\\', '/')}"] = File.ReadAllBytes(file);
        }

        // Its name and description written into its script as they read: the language archives that hold the mission's own
        // keys stay behind, and players' games would show the keys.
        string script = $"{about.MissionDirectory}/{Core.Missions.NewMission.ScriptName}";
        if (bytes.TryGetValue(script, out byte[]? own) && Core.Missions.NewMission.Named(own, about.Title, about.Summary.Length > 0 ? about.Summary : about.Title) is { } named)
        {
            bytes[script] = named;
        }

        // The game's own files with the same names: the only ones a copied level's unchanged files can be. Each read and
        // hashed once.
        ILookup<string, string> gameByName = _host.GameFiles("missions").ToLookup(static p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?> hashOf = new(StringComparer.OrdinalIgnoreCase);
        string? GameHash(string path) => hashOf.TryGetValue(path, out string? known) ? known
            : hashOf[path] = _host.TryReadGameFile(path, out ReadOnlyMemory<byte> data) ? MapPackage.Hash(data.Span) : null;

        Dictionary<string, string> origins = new(StringComparer.Ordinal);
        foreach ((string path, byte[] content) in bytes)
        {
            string hash = MapPackage.Hash(content);
            if (gameByName[Path.GetFileName(path)].FirstOrDefault(game => GameHash(game) == hash) is { } origin)
            {
                origins[hash] = origin;
            }
        }

        MapManifest manifest = MapPacker.Manifest(about, bytes.Select(static b => (b.Key, (ReadOnlyMemory<byte>)b.Value)), hash => origins.GetValueOrDefault(hash));
        HashSet<string> gameHashes = [.. origins.Keys];
        return (manifest, bytes, MapRules.Check(manifest, gameHashes.Contains));
    }

    private static List<(string, string, bool)> PublishLines(MapManifest manifest, IReadOnlyList<string> problems)
    {
        int named = manifest.Files.Count(static f => f.Origin is not null);
        List<(string, string, bool)> lines =
        [
            ("Map", $"{manifest.Title}  ({manifest.Id})", false),
            ("By", manifest.Author, false),
            ("Mission", string.Create(CultureInfo.InvariantCulture, $"{manifest.MissionId}, {manifest.MissionDirectory}"), false),
            ("Files", string.Create(CultureInfo.InvariantCulture, $"{manifest.Files.Count}: {manifest.Files.Count - named} its own, {named} the game's (named, not sent)"), false),
            ("Sends", string.Create(CultureInfo.InvariantCulture, $"{manifest.EmbeddedSize / (1024.0 * 1024.0):0.0} MB"), false),
            ("To", MapService.Master.Host, false),
        ];
        lines.AddRange(problems.Take(6).Select(static p => ("Problem", p, true)));
        return lines;
    }

    // Sent off the frame; what came of it said when it comes.
    private void StartPublishing(MapManifest manifest, Dictionary<string, byte[]> bytes)
    {
        using MemoryStream stream = new();
        MapPackage.Write(manifest, path => bytes[path], stream);
        byte[] package = stream.ToArray();
        _publishing = Task.Run(async () =>
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
            using MapIdentity identity = Host.GameHost.MapIdentity();
            try
            {
                MapListing listing = await new MapService(http, MapService.Master).PublishAsync(package, identity).ConfigureAwait(false);
                return string.Create(CultureInfo.InvariantCulture, $"Published {listing.Title} (version {listing.Version}): up for everyone now.");
            }
            catch (MapRefusedException refused)
            {
                return $"Not published: {refused.Message}{(refused.Problems.Count > 0 ? " " + refused.Problems[0] : string.Empty)}";
            }
            catch (HttpRequestException error)
            {
                return $"Not published: the map server could not be reached ({error.Message}).";
            }
        });
        Notify(string.Create(CultureInfo.InvariantCulture, $"Publishing {manifest.Title} ({package.Length / (1024.0 * 1024.0):0.0} MB)…"));
        _pendingSteps.Enqueue(() =>
        {
            if (_publishing is not { IsCompleted: true } done)
            {
                return false;
            }

            _publishing = null;
            string said = done.IsCompletedSuccessfully ? done.Result : $"Not published: {done.Exception?.GetBaseException().Message}";
            Notify(said, isError: said.StartsWith("Not", StringComparison.Ordinal));
            _host.Logger.Information("Editor", said);
            return true;
        });
    }

    // A map's id made from its name: lowercase letters and digits, hyphens between words.
    private static string Slug(string title)
    {
        string slug = string.Join('-', new string([.. title.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;
using OpenIGI.Frontend.Saves;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Host;

/// <summary>Changing which game the session is running, without the process restarting.</summary>
/// <remarks>
/// <para>
/// <b>Everything a title owns is rebuilt; everything the session owns is kept.</b> The window, the
/// graphics device, the audio device, the console, the settings, the render scheduler and the wall
/// clock belong to the process. The file system's <c>LOCAL</c> device, the resource registry, the task
/// loop, the mission table, the content modules and every renderer belong to the game, and a switch
/// replaces all of them together.
/// </para>
/// <para>
/// The audio <em>device</em> is the session's and what plays through it is the title's, which is a line
/// worth stating because the first version of this did not draw it: the sound bank, the sound
/// definitions and the score all read through the <c>LOCAL</c> device, so a switch that left them
/// standing kept the outgoing game's menu theme playing under the incoming game's menu.
/// <see cref="ResetSoundsForTitle"/> is where that is put right.
/// </para>
/// <para>
/// The mechanism is not new. Promoting a menu backdrop into the played mission already swaps
/// <c>_loop</c>, <c>_resources</c> and <c>_resourceFileSystem</c> for a set built elsewhere, and the
/// mission preview loader already builds a complete title session off the render thread. This is that
/// same handover with the title changed as well.
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>The category title diagnostics are logged under.</summary>
    private const string TitleLogCategory = "Title";

    /// <summary>Gets the title the session is running now.</summary>
    public GameTitle ActiveTitle => _titleRuntime.Title;

    /// <summary>Asks for a different game, to be taken up at the next frame boundary.</summary>
    /// <param name="titleId">The title's id, as <see cref="GameTitle.Id"/> gives it.</param>
    /// <returns><see langword="false"/> when nothing will happen, and the reason is logged.</returns>
    /// <remarks>
    /// Deferred rather than immediate, for the same reason a mission load is: the switch disposes the
    /// loop, the registry and every presenter, and doing that from inside the shell's own update would
    /// pull them out from under the pass that is drawing it.
    /// </remarks>
    public bool RequestTitle(string titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return false;
        }

        foreach (GameTitle candidate in _titles.Titles)
        {
            if (!string.Equals(candidate.Id, titleId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate == ActiveTitle)
            {
                // Picking the game already running is a way back to the landing page, not a reload:
                // tearing a session down and rebuilding it identically would cost a second and change
                // nothing the player can see.
                return false;
            }

            if (!_titles.TryResolve(candidate, out TitleBinding? binding) || !binding.IsInstalled)
            {
                _logger.Warning(
                    TitleLogCategory,
                    $"{candidate.DisplayName} is not installed where this build looks for it, so it cannot be run.");
                return false;
            }

            _pendingTitle = candidate;
            return true;
        }

        _logger.Warning(TitleLogCategory, $"'{titleId}' is not a game this build runs.");
        return false;
    }

    /// <summary>Asks for the next installed game after the one running, wrapping round.</summary>
    /// <remarks>
    /// What <c>-titlecycle</c> drives, and what a selector would do if it only had Next. Skips titles
    /// that are not installed rather than requesting one and being refused, so a machine with one game
    /// simply stays on it instead of logging a warning every cycle.
    /// </remarks>
    private void RequestNextInstalledTitle()
    {
        IReadOnlyList<GameTitle> titles = _titles.Titles;
        int start = 0;
        for (int i = 0; i < titles.Count; i++)
        {
            if (titles[i] == ActiveTitle)
            {
                start = i;
                break;
            }
        }

        for (int step = 1; step <= titles.Count; step++)
        {
            GameTitle candidate = titles[(start + step) % titles.Count];
            if (candidate != ActiveTitle
                && _titles.TryResolve(candidate, out TitleBinding? binding)
                && binding.IsInstalled)
            {
                RequestTitle(candidate.Id);
                return;
            }
        }
    }

    /// <summary>Takes up a requested title. Call between frames, with nothing half-drawn.</summary>
    /// <returns><see langword="true"/> when the session changed games and the shell must be rebuilt.</returns>
    private bool SwitchTitleIfDue()
    {
        if (_pendingTitle is not { } target)
        {
            return false;
        }

        _pendingTitle = null;

        if (!_titles.TryResolve(target, out TitleBinding? binding))
        {
            _logger.Warning(TitleLogCategory, $"'{target.Id}' resolved to nothing between the request and the switch.");
            return false;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        _logger.Information(TitleLogCategory, $"Switching to {target.DisplayName}.");

        // The level first, because it holds the largest things and two of them are worker threads still
        // tracing it. Everything below replaces what the level was built out of, so releasing it after
        // would release it against a session it never belonged to.
        ReleasePlayedMission();
        ReleaseTitleScopedPresentation();

        _titleRuntime = binding.Runtime;
        _presentation = binding.Presentation;
        _worldBuilder = binding.WorldBuilder;
        _missionCatalogue = binding.Missions;
        _gamePath = binding.DataRoot;

        // The shared animation sets are read from _gamePath, so they belong to the title and not to the
        // session. Left standing across a switch they hand the incoming game the outgoing one's
        // skeletons -- and a set that is merely present is enough to send the mission down a path it
        // would otherwise have skipped, which is how a switch to a title with no animations at all
        // ended in an unhandled exception rather than a warning.
        ForgetSharedAnimations();

        // The same file system, re-pointed. VirtualFileSystem keys devices by name, so registering a
        // second LOCAL replaces the first -- which matters because the console, the audio system and the
        // settings all hold this instance and would not see a replacement.
        _fileSystem.RegisterDevice(new LocalFileDevice(_gamePath));
        FileSystemConfig
            .LoadOrDefault(Path.Combine(_gamePath, FileSystemConfig.DefaultFileName), _titleRuntime.Layout)
            .ApplyTo(_fileSystem);

        LoadTitleResources();
        LoadTitleSession();

        // After the session, because the score is one of the things it registers natives against, and
        // after the LOCAL device above, because the menu archive this re-mounts is read through it.
        ResetSoundsForTitle();

        RebuildPreviewContext();

        _logger.Information(
            TitleLogCategory,
            $"{target.DisplayName} is up in {clock.Elapsed.TotalMilliseconds:F0} ms: "
                + $"{_missions.Count} mission(s).");
        return true;
    }

    /// <summary>Builds the running title's resource registry and the archives the menus resolve through.</summary>
    /// <remarks>
    /// The first of the two phases a title comes up in. It is separate because of when each is possible
    /// rather than for tidiness: the menus need their string table before the console, the settings and
    /// the render scheduler exist, and the task loop below cannot be built until they do. A switch runs
    /// both back to back; start-up runs them where each becomes possible.
    /// </remarks>
    private void LoadTitleResources()
    {
        // The session's archives are the TITLE's: menusystem.res holds the menu's own labels and
        // missions.res the mission names, and both are different files in a different installation.
        //
        // Kept as bytes as well as registered, because a mission previewed for the menu builds into its
        // own registry on a worker and that registry becomes the game's if the preview is promoted.
        _sessionArchives.Clear();
        _resourceFileSystem = _fileSystem;
        _resources = new ResourceRegistry(_resourceFileSystem, _logger);

        // The overrides folder, beside the game's own directories. Anything dropped in there replaces the
        // asset that lives at the same path in the installation -- an archived texture by the directory
        // its archive sits in, a loose file by its own path -- so a replacement pack is just the game
        // folder with the files somebody changed and nothing else.
        //
        // Rebuilt per title rather than once at boot, which is what moving it here buys: the folder is
        // the INSTALLATION's, so a session that switches games has to look in the other one's.
        // OPENIGI_OVERRIDES names another folder instead, for trying a pack -- a converted one, a work in
        // progress -- without putting it in the installation.
        string? overridesRoot = Environment.GetEnvironmentVariable("OPENIGI_OVERRIDES") is { Length: > 0 } elsewhere
            ? elsewhere
            : _resourceFileSystem.TryGetDevice(LocalFileDevice.DeviceName, out IFileDevice? local)
                && local is LocalFileDevice localDevice
                    ? Path.Combine(localDevice.RootDirectory, ResourceOverrides.DirectoryName)
                    : null;

        _overrides = new ResourceOverrides(overridesRoot, _logger);

        _resources.Overrides = _overrides;

        // Scanned here, because the archives mounted below consult it. Reported by the caller, once the
        // console has started and the player's log verbosity is in force.
        _overrideScan = _overrides.Rescan();

        foreach (string archive in new[]
        {
            MenuStringTable.GetArchiveName(),
            MenuStringTable.GetMissionArchiveName(),
        }.Concat(_titleRuntime.Layout.StringArchives))
        {
            if (_fileSystem.Exists(archive))
            {
                byte[] bytes = _fileSystem.ReadAllBytes(archive);
                _sessionArchives[archive] = bytes;
                _resources.Register(archive, bytes);
            }
        }

        MenuStringTable strings = new(_resources);
        _menuStrings = strings;

        // Null on the very first call, which happens before the config host exists; the boot path sets
        // it there and every switch after finds one standing.
        if (_configHost is not null)
        {
            _configHost.Strings = strings;
        }
    }

    /// <summary>
    /// Builds the running title's task loop, mission table and content session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once from start-up and again on every switch, which is what keeps the two honest: there is
    /// no separate boot path for a switch to drift away from. What it deliberately does NOT touch is
    /// anything the process owns — the console has already wrapped the log, the settings are loaded, the
    /// audio device is open, and none of that is a game's to replace.
    /// </para>
    /// <para>
    /// The wall clock is kept too, and that is load-bearing rather than incidental: a new
    /// <see cref="GameLoop"/> starts its own <c>GameClock</c> at tick zero, and every elapsed reading in
    /// the frame loop would jump backwards at the moment of the swap. The schedule is carried across the
    /// same way a promoted backdrop carries it.
    /// </para>
    /// </remarks>
    private void LoadTitleSession()
    {
        // The title's structural task types, not the engine's fixed set: what makes a type structural is
        // that the title's own editor emits no declaration for it, and the two games disagree by one
        // name. A loop carrying the wrong set refuses every level of the other game.
        GameLoop outgoing = _loop;
        _loop = new GameLoop(
            time: _timeSource,
            host: _menuHost,
            renderer: _scheduler,
            logger: _logger,
            structuralTypes: _titleRuntime.Layout.StructuralTaskTypes);

        if (outgoing is not null)
        {
            _loop.Clock.AdoptScheduleFrom(outgoing.Clock);
        }

        // A build that offers only the opening of the campaign cuts the catalogue itself, so every route
        // into a mission -- the lists, a save slot, the command line, a finished mission's successor --
        // meets the same limit without knowing there is one.
        IMissionCatalogue<MissionDescriptor> titleMissions = _missionCatalogue(_logger);
        CampaignLimitedMissionCatalogue? limitedMissions = _options.CampaignMissionLimit is { } limit
            ? new CampaignLimitedMissionCatalogue(titleMissions, limit)
            : null;
        _missions = limitedMissions ?? titleMissions;
        _missions.Register(_loop.Natives);
        _missionState.Register(_loop.Natives);

        if (_music is not null)
        {
            new GameMusicNatives(_music).Register(_loop.Natives);
        }

        _title = TitleSession.Start(
            _titleRuntime.Modules, _loop, _fileSystem, _resources, _logger, loadTables: false);

        // Asked for, not required: these four are tables one game publishes and the other does not.
        // Cleared first, because TryGet leaves the target alone when a title has none and the previous
        // game's would otherwise still be standing.
        _magic = null;
        _vehiclePhysics = null;
        _animationTriggers = null;
        _aiConfig = null;
        _title.Services.TryGet(out _magic);
        _title.Services.TryGet(out _vehiclePhysics);
        _title.Services.TryGet(out _animationTriggers);
        _title.Services.TryGet(out _aiConfig);

        try
        {
            _missions.Scan(_fileSystem, _resources, _loop.Natives);
            _title.LoadTables(_loop, _fileSystem, _resources);
            _configHost.Missions = _missions;

            if (limitedMissions is not null)
            {
                _logger.Information(
                    "Mission",
                    $"This build offers the first {limitedMissions.Limit} mission(s) of the campaign; {limitedMissions.Count} are available.");

                // The profiles were read before the scan, and one from the full game can be further along
                // than this build goes.
                foreach (PlayerProfile profile in _configuration.Profiles)
                {
                    profile.ActiveMission = limitedMissions.ClampToOffered(profile.ActiveMission);
                }
            }
        }
        catch (EngineFatalException ex)
        {
            _logger.Warning("Mission", $"The mission scan failed, so no missions are selectable: {ex.Message}");
        }

        RebuildPreviewContext();
    }

    /// <summary>Rebuilds what a menu backdrop is built from on a worker.</summary>
    /// <remarks>
    /// Half of it is the title's — its content modules, its structural task types, its format profile
    /// and the service table its modules published — so a switch that left the old one standing would
    /// have the menu previewing the incoming game's levels through the outgoing game's content.
    /// <para>
    /// Its own step because of when it is possible: the detail maps and the lamp manifest it also
    /// carries are built later in start-up than the session is, and none of it is mutated afterwards,
    /// which is what makes it safe to hand to a worker.
    /// </para>
    /// </remarks>
    private void RebuildPreviewContext()
    {
        // What a backdrop needs to be built on a worker, rebuilt because half of it is the title's: its
        // modules, its structural types, its format profile and its service table.
        // The title the backdrop belongs to, so its fixture list is not confused with the other
        // game's: the two number their missions independently and the ranges overlap.
        _previewContext = new MissionPreviewContext(
            _timeSource,
            _menuHost,
            _scheduler,
            _missionState,
            _detailMaps,
            _sessionArchives,
            _overrides,
            _lampManifest,
            _sdfCache,
            _title.Services,
            _titleRuntime.Modules,
            _titleRuntime.Layout.StructuralTaskTypes,
            _titleRuntime.Formats,
            _worldBuilder,
            _device.Capabilities.SupportsCompute,
            _titleRuntime.Title.Id,
            _device.Capabilities.SupportsBlockCompression);
    }

    /// <summary>The campaign as the shell lists it, for the title running now.</summary>
    /// <remarks>
    /// <para>
    /// The <em>progression</em>, not the browser order. Both are the catalogue's to define and they
    /// differ: the progression is the campaign exactly, while the browser order is everything the
    /// install holds with the campaign first. The screen this fills is headed CAMPAIGN, so it wants the
    /// former — and the distinction only started to matter when the sequel could be reached, because
    /// its browser order ends with six deathmatch maps and the screen offered them as missions 20 to 25.
    /// </para>
    /// <para>
    /// Not the scan's order either, which is a directory walk and therefore alphabetical — it listed
    /// level 10 between level 1 and level 2. What the progression is, is the title's: one game chains
    /// each mission to the next and the other numbers them by location and level.
    /// </para>
    /// </remarks>
    private List<MissionInfo> ShellMissions()
    {
        List<MissionInfo> missions = [];
        int number = 1;

        foreach (MissionDescriptor descriptor in _missions.InProgressionOrder())
        {
            missions.Add(new MissionInfo(
                descriptor.Id,
                number++,
                _menuStrings?.Get(descriptor.Name) ?? descriptor.Name,
                _menuStrings?.Get(descriptor.Description) ?? descriptor.Description,
                descriptor.BriefingSprite));
        }

        _shellMissions = missions;
        return missions;
    }

    /// <summary>Builds the shell's context for the title running now.</summary>
    /// <param name="host">The seam the shell acts through; it belongs to the session, not to a game.</param>
    /// <param name="theme">The fonts and colours, likewise.</param>
    /// <param name="saves">The player's save slots, likewise.</param>
    /// <returns>A context listing this build's games with the running one current.</returns>
    private ShellContext BuildShellContext(IShellHost host, UiTheme theme, SaveSlotStore saves) =>
        new(
            theme,
            host,
            new ScreenStack(),
            TitleCatalogue(),
            ActiveTitle.Id,
            ShellMissions(),
            CVarRegistry.Default,
            GraphicsPresetRegistry.Default,
            _configuration,
            saves);

    /// <summary>Drops the presenters a title owns, which a switch replaces rather than reuses.</summary>
    /// <remarks>
    /// <see cref="ReleasePlayedMission"/> already drops the world, ground and sky, because those go with
    /// a level. These are the ones the menu keeps up behind it — a backdrop is a level too — and a
    /// switch that left them standing would draw the outgoing game's forest over the incoming one's.
    /// </remarks>
    private void ReleaseTitleScopedPresentation()
    {
        _forest?.Dispose();
        _forest = null;
        _water?.Dispose();
        _water = null;
        _shadowPass = null;
        _showcaseCamera = null;

        // NOT _menuCameras. It reads as title-scoped -- a backdrop is a level, and the rest of this
        // method drops what a level owns -- but it is one JSON file covering both games, and every entry
        // names the game it belongs to. Emptied here it was never reloaded: only the boot path loads it,
        // so after the FIRST switch no title had its authored cameras, including the one switched back
        // to. Both games then fell to derived places, and on the first game's large outdoor maps a
        // derived pick is a different and worse shot than the authored one -- which is exactly the
        // "backdrops in the wrong place after switching" this looked like.
    }

    /// <summary>The games the shell lists, each bound to its own installation.</summary>
    /// <remarks>
    /// Rebuilt whenever the shell is, because which entry is the current one moves with the running
    /// title — and because a title's profile is a disk check, so a drive unplugged mid-session shows as
    /// what it is rather than as what it was at boot.
    /// </remarks>
    private IReadOnlyList<GameProfile> TitleCatalogue() =>
        GameProfileResolver.BuildCatalogue(
            ActiveTitle,
            _gamePath,
            null,
            null,
            title => _titles.TryResolve(title, out TitleBinding? other) ? other.DataRoot : null);
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Frontend;
using OpenIGI.Game;
using OpenIGI.Game.World;

namespace OpenIGI.Host;

/// <summary>The language the game's text is read in, and changing it while the game runs.</summary>
/// <remarks>
/// <para>
/// Every language is a folder of archives whose keys carry the language's name (<see cref="LanguageCVars"/>), so a language is
/// changed by unregistering one folder's archives and registering another's, and pointing the string table at the new name.
/// Nothing is rebuilt: the menus, the campaign list and a mission's computer all look their keys up as they draw.
/// </para>
/// <para>
/// Two registries can be holding the text: the title's, which the menus' string table reads, and the played mission's, which a
/// menu backdrop brought with it when it was promoted. A backdrop built before the change was built with the old language's
/// archives, so a promotion mounts the current one into it too (<see cref="MountLanguage(ResourceRegistry)"/>).
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    // The registry the menus' string table reads, the title's own.
    private ResourceRegistry? _textResources;

    // Set when the language setting changes; taken up between frames.
    private bool _languageChanged;

    /// <summary>Takes up a change of language at the next frame boundary.</summary>
    private void WatchLanguage() => WhenChanged(LanguageCVars.Language, () => _languageChanged = true);

    /// <summary>Reads the chosen language's archives and mounts them in the title's registry, for the menus' string table.</summary>
    /// <remarks>
    /// The bytes are kept as the session's archives, which every backdrop registers, so a mission promoted from the menu reads
    /// the same language.
    /// </remarks>
    private void MountLanguage()
    {
        // An installation without the chosen language reads its English rather than a screen of placeholders.
        string language = LanguageCVars.Directory;
        if (!_fileSystem.Exists(MenuStringTable.GetArchiveName(language)))
        {
            _logger.Warning(TitleLogCategory, $"No '{language}' text in this installation; using {MenuStringTable.DefaultLanguageDirectory}.");
            language = MenuStringTable.DefaultLanguageDirectory;
        }

        _sessionArchives.Clear();
        foreach (string archive in LanguageArchives(language))
        {
            if (_fileSystem.Exists(archive))
            {
                _sessionArchives[archive] = _fileSystem.ReadAllBytes(archive);
            }
        }

        MountLanguage(_textResources!);
        _menuStrings!.LanguageDirectory = language;
    }

    /// <summary>Puts the session's language archives into a registry in place of any other language's.</summary>
    /// <param name="registry">The registry.</param>
    private void MountLanguage(ResourceRegistry registry)
    {
        foreach (string language in MenuStringTable.LanguageNames)
        {
            foreach (string archive in LanguageArchives(language))
            {
                registry.Free(archive);
            }
        }

        foreach ((string archive, byte[] bytes) in _sessionArchives)
        {
            registry.Register(archive, bytes);
        }
    }

    /// <summary>Changes the language between frames, after the setting changed.</summary>
    /// <param name="state">The frame loop's state, for the shell's campaign list.</param>
    private void ApplyLanguageIfDue(FrameLoopState state)
    {
        if (!_languageChanged)
        {
            return;
        }

        _languageChanged = false;
        MountLanguage();

        // A mission in play reads its text from the registry its backdrop brought.
        if (!ReferenceEquals(_resources, _textResources))
        {
            MountLanguage(_resources);
        }

        // Backdrops built from here on carry the new archives; one built before is put right as it is promoted.
        RebuildPreviewContext();
        if (state.Shell?.Context is { } shell)
        {
            shell.Missions = ShellMissions();
        }

        _logger.Information(TitleLogCategory, $"Text is now read in '{_menuStrings?.LanguageDirectory}'.");
    }

    // The archives a language's text is in, for the title running now.
    private IEnumerable<string> LanguageArchives(string language) =>
        [
            MenuStringTable.GetArchiveName(language),
            MenuStringTable.GetMissionArchiveName(language),
            .. _titleRuntime.Layout.StringArchives(language),
        ];
}

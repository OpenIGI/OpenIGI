using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// What every shell test builds on: a shell over two missions, a default profile and a fake host, and the
/// frames and keys a test drives it with. The shell tests are one collection, as when they were one class:
/// some of them change the cheat variables every campaign row reads.
/// </summary>
public abstract class ShellTestHarness
{
    /// <summary>The collection every shell test class joins.</summary>
    public const string CollectionName = "shell";

    private protected static (Shell Shell, FakeHost Host) BuildShell(
        string savesDir,
        int activeMission = 2,
        IReadOnlyList<TitleInputBinding>? controls = null,
        IReadOnlyList<MissionInfo>? extraMissions = null)
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        UiFont font = OpenIGI.UI.Text.UiFont.ForMeasurement(
            OpenIGI.UI.Text.FontAtlasBuilder.Build(File.ReadAllBytes(fontPath), 48f));
        var theme = new UiTheme(font);

        var host = new FakeHost { Controls = controls ?? [] };
        var missions = new List<MissionInfo>
        {
            new(1, 1, "Mission One", "The first briefing.", "mission1.spr"),
            new(2, 2, "Mission Two", "The second briefing.", "mission2.spr"),
        };
        missions.AddRange(extraMissions ?? []);

        GameConfig config = GameConfig.CreateDefault();
        if (config.Active is { } active)
        {
            active.ActiveMission = activeMission;
            TitleInputBindings.RestoreMissing(active, host.Controls);
            host.Profile = active;
        }

        var context = new ShellContext(
            theme,
            host,
            new ScreenStack(),
            GameProfileResolver.BuildCatalogue(GameTitle.Igi1, "C:\\Games\\IGI", null, null),
            GameTitle.Igi1.Id,
            missions,
            new CVarRegistry(),
            GraphicsPresetRegistry.Default,
            config,
            new SaveSlotStore(savesDir));

        return (new Shell(context), host);
    }

    private protected static void Pump(Shell shell, float seconds = 0.6f)
    {
        var context = new LayoutContext(1920f, 1080f);
        var input = new InputState();
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            input.BeginFrame();
            shell.Update(context, input, 1f / 60f);
        }
    }

    private protected static T? Find<T>(Screen screen, Func<T, bool> predicate)
        where T : UiElement =>
        screen.Root?.Descendants().OfType<T>().FirstOrDefault(predicate);

    private protected static void Type(Shell shell, string text)
    {
        var input = new InputState();
        input.BeginFrame();
        foreach (char character in text)
        {
            input.Keyboard.EnqueueCharacter(character);
        }

        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
    }

    // Escape, the way the shell reads it -- one frame with the key down against a frame without.
    private protected static void Back(Shell shell)
    {
        var input = new InputState();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Escape, true);
        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
    }
}

/// <summary>The app as the shell sees it, recording what it was asked to do.</summary>
internal sealed class FakeHost : IShellHost
{
    public IReadOnlyList<TitleInputBinding> Controls { get; set; } = [];

    public PlayerProfile? Profile { get; set; }

    public string? InputBindingLabel(string action) => TitleInputBindings.Label(action, Controls);

    public void ResetInputBindings(PlayerProfile profile) => TitleInputBindings.Reset(profile, Controls);

    public int? Previewed { get; private set; }

    public int StartedMission { get; private set; } = -1;

    public int StartedDifficulty { get; private set; } = -1;

    public bool Quit_Called { get; private set; }

    public int SaveSettingsCalls { get; private set; }

    public int SaveProfilesCalls { get; private set; }

    public int ResumeCalls { get; private set; }

    public int RestartCalls { get; private set; }

    public int AbandonCalls { get; private set; }

    /// <summary>The title the shell last asked for, or empty.</summary>
    public string SwitchedTo { get; private set; } = string.Empty;

    /// <summary>What SwitchTitle answers; false stands for "already running, or not installed".</summary>
    public bool SwitchAccepted { get; set; } = true;

    public MissionConditions Conditions { get; private set; } = MissionConditions.Default;

    public int ConditionCalls { get; private set; }

    public void StartMission(int missionId, int difficulty)
    {
        StartedMission = missionId;
        StartedDifficulty = difficulty;
    }

    public void SetMissionConditions(MissionConditions conditions)
    {
        Conditions = conditions;
        ConditionCalls++;
    }

    public bool SwitchTitle(string titleId)
    {
        SwitchedTo = titleId;
        return SwitchAccepted;
    }

    public void ResumeMission() => ResumeCalls++;

    public void RestartMission() => RestartCalls++;

    public void AbandonMission() => AbandonCalls++;

    public void Quit() => Quit_Called = true;

    public int PreviewCalls { get; private set; }

    public void PreviewMission(int? missionId)
    {
        Previewed = missionId;
        PreviewCalls++;
    }

    /// <summary>Forgets every preview asked for so far, so a test can assert about the ones after it.</summary>
    public void ForgetPreview()
    {
        Previewed = null;
        PreviewCalls = 0;
    }

    public void SaveSettings() => SaveSettingsCalls++;

    public void SaveProfiles() => SaveProfilesCalls++;

    public Bgra32[] CaptureThumbnail(out int width, out int height)
    {
        width = 0;
        height = 0;
        return [];
    }
}

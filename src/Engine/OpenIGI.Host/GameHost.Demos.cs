using OpenIGI.Core.Configuration;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Host;

/// <content>Demos: a joined session saved from the console, into the demos folder beside the settings.</content>
public sealed partial class GameHost
{
    // Where a demo is saved: named as asked, or by the time it was saved.
    private static string DemoPath(string? name)
    {
        string file = string.IsNullOrWhiteSpace(name)
            ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)
            : string.Concat(name.Trim().Split(Path.GetInvalidFileNameChars()));
        if (!file.EndsWith(Game.Multiplayer.DemoFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            file += Game.Multiplayer.DemoFile.Extension;
        }

        return Path.Combine(SettingsPaths.GetUserDirectory(), "demos", file);
    }

    private void RegisterDemoCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "demo_save",
            "Saves the multiplayer session joined or hosted, from its start until now, as a demo in the demos folder; play it with -playdemo <file>.",
            context =>
            {
                if (TickObserver is not IDemoSource session)
                {
                    context.Error("Only a multiplayer session is recorded: join or host one first.");
                    return;
                }

                string path = DemoPath(context.Count > 0 ? context[0] : null);
                if (session.SaveDemo(path) is { } problem)
                {
                    context.Error($"Not saved: {problem}.");
                    return;
                }

                context.Print($"Saved {path}.");
            },
            "[name]");
        console.Commands.Add(
            "demo_pause",
            "Pauses the demo being played, or plays it on.",
            context =>
            {
                if (Playing() is { } playback)
                {
                    playback.Paused = !playback.Paused;
                    context.Print(playback.Paused ? "Paused." : "Playing.");
                }
            });
        console.Commands.Add(
            "demo_speed",
            "Sets how fast the demo being played runs: 1 as it was recorded, 0.25 to 8.",
            context =>
            {
                if (Playing() is not { } playback)
                {
                    return;
                }

                if (context.Count > 0 && double.TryParse(context[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double speed))
                {
                    playback.Speed = speed;
                }

                context.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Speed {playback.Speed:0.##}x, {playback.Seconds:0} of {playback.Length:0} s."));
            },
            "[speed]");

        // The demo being played, or an error to the console when none is.
        DemoPlayback? Playing() => TickObserver is SessionClient { Playback: { } playback } ? playback : null;
    }
}

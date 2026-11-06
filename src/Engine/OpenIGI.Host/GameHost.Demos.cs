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

    private void RegisterDemoCommands(ConsoleSystem console) =>
        console.Commands.Add(
            "demo_save",
            "Saves the multiplayer session joined, from its start until now, as a demo in the demos folder; play it with -playdemo <file>.",
            context =>
            {
                if (TickObserver is not SessionClient client)
                {
                    context.Error("Only a session joined is recorded: join a server or a room first.");
                    return;
                }

                string path = DemoPath(context.Count > 0 ? context[0] : null);
                if (client.SaveDemo(path) is { } problem)
                {
                    context.Error($"Not saved: {problem}.");
                    return;
                }

                context.Print($"Saved {path}.");
            },
            "[name]");
}

using OpenIGI.Core.Configuration;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The console commands that drive the developer panels: showing them, pinning values, filtering the
/// log, and finding things in the world.
/// </summary>
/// <remarks>
/// Registered before <c>settings.cfg</c> is read, and therefore before the panels have been built —
/// so every one of these must survive being run with no panels in existence. A saved configuration
/// naming a panel has to work, and the alternative is a start-up error for a line the console itself
/// wrote.
/// </remarks>
internal sealed class DevConsoleCommands
{
    private readonly DevConsolePanels _panels;
    private readonly PromptEditor _editor;

    /// <summary>Initialises a new instance of the <see cref="DevConsoleCommands"/> class.</summary>
    /// <param name="panels">The panels the commands act on.</param>
    /// <param name="editor">The typing shared by every panel, for the clipboard.</param>
    public DevConsoleCommands(DevConsolePanels panels, PromptEditor editor)
    {
        _panels = panels;
        _editor = editor;
    }

    /// <summary>Registers every command with a console.</summary>
    /// <param name="console">The console to register with.</param>
    public void Register(ConsoleSystem console)
    {
        ConsoleModel model = _panels.Model;
        ConsoleWatchList watch = _panels.WatchList;

        console.Commands.Add(
            "con_panel",
            "Shows or hides a developer panel.",
            context =>
            {
                string name = context[0];

                if (!_panels.Windows.TryGetValue(name, out UiWindow? window))
                {
                    // Before the display exists the panels do not either. A named one is remembered
                    // and applied once they are built; anything else lists what there is.
                    if (name.Length > 0 && _panels.Theme is null && DevConsolePanels.IsPanelName(name))
                    {
                        _panels.PendingOpen.Add(name);
                        return;
                    }

                    context.Print($"Panels: {string.Join(", ", DevConsolePanels.KnownPanels)}");
                    return;
                }

                string mode = context[1];
                bool wanted = mode switch
                {
                    "on" or "1" => true,
                    "off" or "0" => false,
                    _ => !window.IsOpen,
                };

                // The palette is stateful in a way the other panels are not — it has a query and a
                // result list — so showing it has to go through the same door the chord uses, or it
                // comes up reading "no matches" because nothing ever ran the search.
                if (wanted && string.Equals(name, DevConsolePanels.PalettePanel, StringComparison.OrdinalIgnoreCase))
                {
                    _panels.OpenPalette();
                    context.Print($"{name} is shown.");
                    return;
                }

                window.IsOpen = wanted;

                // The two pinnable panels take their visibility from a variable whenever the console
                // is shut, so showing one through this command has to set that variable too. Without
                // it, `con_panel watch on` would work until the console was closed and then lose the
                // panel, which reads as the command not having stuck.
                if (string.Equals(name, DevConsolePanels.WatchPanel, StringComparison.OrdinalIgnoreCase))
                {
                    DevConsoleCVars.WatchVisible.Value = wanted;
                }

                if (wanted)
                {
                    _panels.Layer?.Raise(window);
                    model.IsOpen = true;
                }

                context.Print($"{name} is {(wanted ? "shown" : "hidden")}.");
            },
            "<console|cvars|watch|world|palette> [on|off]");

        console.Commands.Add(
            "con_reset_layout",
            "Puts every developer panel back where it started.",
            _ =>
            {
                DevConsoleCVars.Layout.Value = string.Empty;
                _panels.LayoutRestored = false;
            });

        console.Commands.Add(
            "watch",
            "Pins a variable to the watch panel.",
            context =>
            {
                string name = context[0];
                if (name.Length == 0)
                {
                    context.Print(watch.Names.Count == 0 ? "Nothing is pinned." : string.Join(", ", watch.Names));
                    return;
                }

                if (watch.Add(name))
                {
                    _panels.ShowPinned(DevConsolePanels.WatchPanel, DevConsoleCVars.WatchVisible);
                    context.Print($"Watching {name}.");
                }
            },
            "<variable>");

        console.Commands.Add(
            "unwatch",
            "Unpins a variable from the watch panel.",
            context =>
            {
                if (context[0].Length == 0)
                {
                    watch.Clear();
                    context.Print("Unpinned everything.");
                    return;
                }

                context.Print(watch.Remove(context[0]) ? $"Unpinned {context[0]}." : $"{context[0]} was not pinned.");
            },
            "[variable]");

        console.Commands.Add(
            "palette",
            "Opens the command palette.",
            _ => _panels.OpenPalette());

        console.Commands.Add(
            "find_log",
            "Filters the scrollback to lines containing some text.",
            context =>
            {
                model.Filter.Search = context.Rest(0);
                context.Print(model.Filter.Search.Length == 0
                    ? "Log filter cleared."
                    : $"Showing {model.VisibleLineIndices.Count} line(s) matching '{model.Filter.Search}'.");
            },
            "[text]");

        console.Commands.Add(
            "ent_list",
            "Lists what is in the world.",
            context =>
            {
                if (_panels.World is not { } world)
                {
                    context.Error("No world is loaded.");
                    return;
                }

                List<InspectableEntity> found = [];
                world.Describe(context.Rest(0), found);

                foreach (InspectableEntity entity in found)
                {
                    context.Print(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{entity.Id,5}  {entity.Kind,-8} {entity.Name,-28} {entity.State}"));
                }

                context.Print($"{found.Count} entity(ies).");
            },
            "[filter]");

        console.Commands.Add(
            "ent_info",
            "Reports what is known about one entity.",
            context =>
            {
                if (_panels.World is not { } world)
                {
                    context.Error("No world is loaded.");
                    return;
                }

                if (!int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_info <id>");
                    return;
                }

                List<string> lines = [];
                world.Detail(id, lines);

                if (lines.Count == 0)
                {
                    context.Error($"No entity {id}.");
                    return;
                }

                foreach (string line in lines)
                {
                    context.Print(line);
                }
            },
            "<id>");

        console.Commands.Add(
            "ent_select",
            "Marks an entity in the world and shows it in the World panel.",
            context =>
            {
                if (!int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_select <id>");
                    return;
                }

                // Same story as `con_panel`: from `settings.cfg` or a `+` argument this runs before
                // there is a display, so the selection is remembered rather than lost.
                if (_panels.Inspector is not { } inspector)
                {
                    _panels.PendingSelection = id;
                    _panels.PendingOpen.Add(DevConsolePanels.InspectorPanel);
                    return;
                }

                if (_panels.InspectorWindow is { } inspectorWindow)
                {
                    inspectorWindow.IsOpen = true;
                    model.IsOpen = true;
                }

                inspector.SelectWhenListed(id);
                context.Print($"Selected {id}.");
            },
            "<id>");

        console.Commands.Add(
            "ent_goto",
            "Moves the free camera to look at an entity.",
            context =>
            {
                if (_panels.World is not { } world || !int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_goto <id>");
                    return;
                }

                context.Print(world.TryMoveCameraTo(id) ? $"Camera moved to {id}." : $"Could not reach {id}.");
            },
            "<id>",
            CommandOptions.Cheat);

        console.Commands.Add(
            "ent_teleport",
            "Moves the player to an entity.",
            context =>
            {
                if (_panels.World is not { } world || !int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_teleport <id>");
                    return;
                }

                context.Print(world.TryTeleportPlayerTo(id) ? $"Teleported to {id}." : $"Could not reach {id}.");
            },
            "<id>",
            CommandOptions.Cheat);

        console.Commands.Add(
            "con_copy",
            "Copies the visible scrollback to the clipboard.",
            context =>
            {
                if (_editor.WriteClipboard is not { } writeClipboard)
                {
                    context.Error("This platform has no clipboard.");
                    return;
                }

                var text = new System.Text.StringBuilder();
                foreach (int index in model.VisibleLineIndices)
                {
                    text.AppendLine(model.Lines[index].Text);
                }

                writeClipboard(text.ToString());
                context.Print($"Copied {model.VisibleLineIndices.Count} line(s).");
            });
    }
}

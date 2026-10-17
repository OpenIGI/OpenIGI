using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;

namespace OpenIGI.Host.DevTools;

/// <summary>The console's handle on the content layers: listing them and their conflicts, switching one, rescanning.</summary>
/// <param name="overrides">The current title's content layers; another title's after the game changes.</param>
/// <param name="resources">The current title's resource registry.</param>
/// <param name="reapply">Brings what is loaded up to date with the layers, and says what it reloaded.</param>
/// <param name="logger">Where reports go besides the scrollback.</param>
internal sealed class ContentLayerCommands(
    Func<ResourceOverrides> overrides,
    Func<ResourceRegistry> resources,
    Func<string> reapply,
    IOpenIgiLogger logger)
{
    private ResourceOverrides Overrides => overrides();

    private ResourceRegistry Resources => resources();

    /// <summary>Reports what a scan of the content layers found.</summary>
    /// <param name="scan">The scan result.</param>
    public void Report(ResourceOverrides.ScanResult scan)
    {
        IReadOnlyList<ResourceOverrides.LayerInfo> layers = Overrides.Layers;

        if (scan.Total == 0 && !scan.Moved)
        {
            logger.Debug("Overrides", $"No overrides in {layers.Count} content layer(s).");
            return;
        }

        logger.Information("Overrides", $"{scan} across {layers.Count} content layer(s).");

        foreach (ResourceOverrides.LayerInfo layer in layers)
        {
            logger.Information("Overrides", "  " + Describe(layer));
        }
    }

    private static string Describe(ResourceOverrides.LayerInfo layer) =>
        $"{(layer.Enabled ? "ON " : "OFF")}  {layer.Name} [{layer.Id}]: {layer.Files} file(s), wins {layer.Wins}"
            + (layer.Root is null ? string.Empty : $"  ({layer.Root})");

    /// <summary>Registers the commands that manage the content layers.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// <para>
    /// Content that only took effect on a cold launch would make replacing an asset a compile-run-look
    /// loop with a whole game start in the middle of it. Rescanning is cheap -- a directory walk and a byte
    /// compare against what is registered -- and switching a layer is cheaper still, so the loop becomes:
    /// save the file, alt-tab, type the command.
    /// </para>
    /// <para>
    /// <c>overrides</c> is the older command and keeps its meaning; <c>content</c> is the one that knows
    /// there is more than one layer.
    /// </para>
    /// </remarks>
    public void Register(ConsoleSystem console)
    {
        console.Commands.Add(
            "overrides",
            "Rescans the content layers and reloads whatever changed. 'list' reports what is there.",
            context =>
            {
                if (context.Count > 0 && context[0] is "list")
                {
                    ListOverrides(context);
                    return;
                }

                Rescan(context);
            },
            "[list]");

        console.Commands.Add(
            "content",
            "Lists the content layers (top wins), their conflicts, and switches a layer on or off live.",
            context =>
            {
                string verb = context.Count > 0 ? context[0].ToLowerInvariant() : string.Empty;

                switch (verb)
                {
                    case "":
                    case "list":
                        ListLayers(context);
                        break;

                    case "conflicts":
                        ListConflicts(context, context.Count > 1 ? context[1] : null);
                        break;

                    case "rescan":
                        Rescan(context);
                        break;

                    case "on":
                    case "off":
                    case "toggle":
                        SwitchLayer(context, verb, context.Count > 1 ? context[1] : string.Empty);
                        break;

                    default:
                        context.Warn($"Unknown content command '{context[0]}'.");
                        break;
                }
            },
            "[list | conflicts [filter] | on <#|id> | off <#|id> | toggle <#|id> | rescan]");
    }

    private void ListLayers(ConsoleCommandContext context)
    {
        IReadOnlyList<ResourceOverrides.LayerInfo> layers = Overrides.Layers;
        if (layers.Count == 0)
        {
            context.Print("No content layers.");
            return;
        }

        context.Print(
            $"{layers.Count} content layer(s), top wins; {Overrides.Count} path(s) overridden"
                + (Overrides.Bypassed ? " -- all bypassed (F8 / r_overrides 0)." : "."));

        for (int index = 0; index < layers.Count; index++)
        {
            string line = $"  {index + 1,2}. {Describe(layers[index])}";
            context.Print(line);
            logger.Information("Overrides", line);
        }
    }

    private void ListConflicts(ConsoleCommandContext context, string? filter)
    {
        IReadOnlyList<ResourceOverrides.LayerInfo> layers = Overrides.Layers;
        IReadOnlyList<ResourceOverrides.Conflict> conflicts = Overrides.Conflicts();

        if (!string.IsNullOrEmpty(filter))
        {
            conflicts = [.. conflicts.Where(conflict => conflict.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))];
        }

        context.Print($"{conflicts.Count} path(s) carried by more than one enabled layer.");

        foreach (ResourceOverrides.Conflict conflict in conflicts.Take(40))
        {
            string covered = string.Join(", ", conflict.Covered.Select(layer => layers[layer].Name));
            string line = $"  {conflict.Path}  <- {layers[conflict.Winner].Name} (covers {covered})";
            context.Print(line);
            logger.Information("Overrides", line);
        }

        if (conflicts.Count > 40)
        {
            context.Print($"  ... and {conflicts.Count - 40} more.");
        }
    }

    private void SwitchLayer(ConsoleCommandContext context, string verb, string which)
    {
        int index = Overrides.Find(which);
        if (index < 0)
        {
            context.Warn($"No content layer '{which}'. 'content' lists them.");
            return;
        }

        ResourceOverrides.LayerInfo layer = Overrides.Layers[index];
        bool enabled = verb switch
        {
            "on" => true,
            "off" => false,
            _ => !layer.Enabled,
        };

        Overrides.SetEnabled(layer.Id, enabled, out ResourceOverrides.ScanResult result);
        string reload = reapply();

        string report = $"{layer.Name} {(enabled ? "on" : "off")}: {result}; {reload}";
        if (Overrides.Bypassed)
        {
            report += " Every layer is bypassed until F8 / r_overrides 1.";
        }

        context.Print(report);
        logger.Information("Overrides", report);
    }

    private void Rescan(ConsoleCommandContext context)
    {
        ResourceOverrides.ScanResult scan = Overrides.Rescan();
        string reload = reapply();

        string report = $"{scan}; {reload}";
        context.Print(report);
        logger.Information("Overrides", report);
    }

    private void ListOverrides(ConsoleCommandContext context)
    {
        // To the log as well as the scrollback: a headless -runat run has no scrollback, and this is
        // exactly the command somebody reaches for when a pack is not taking effect.
        logger.Information(
            "Overrides",
            $"{Overrides.Count} override(s) across {Overrides.Layers.Count} layer(s); "
                + $"{Resources.OverriddenCount} standing in for a shipped asset.");

        if (!Overrides.IsActive)
        {
            context.Print(Overrides.Bypassed ? "Overrides are bypassed (F8)." : "No overrides in any enabled layer.");
            return;
        }

        IReadOnlyList<ResourceOverrides.LayerInfo> layers = Overrides.Layers;
        string[] paths = [.. Overrides.Paths.Order(StringComparer.OrdinalIgnoreCase)];

        for (int index = 0; index < Math.Min(paths.Length, 40); index++)
        {
            int layer = Overrides.LayerOf(paths[index]);
            string line = layer < 0 ? $"  {paths[index]}" : $"  {paths[index]}  <- {layers[layer].Name}";
            context.Print(line);

            if (index < 10)
            {
                logger.Information("Overrides", line);
            }
        }

        if (paths.Length > 40)
        {
            context.Print($"  ... and {paths.Length - 40} more.");
        }
    }
}

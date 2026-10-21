using OpenIGI.Core.Configuration;

namespace OpenIGI.Core.Configuration.Presets;

/// <summary>
/// Registers the <c>gfx_preset</c> console command.
/// </summary>
public static class GraphicsPresetCommands
{
    /// <summary>The name the command is invoked by.</summary>
    public const string CommandName = "gfx_preset";

    /// <summary>Registers <c>gfx_preset</c> on a console.</summary>
    /// <param name="console">The console to register on.</param>
    /// <param name="presets">The registry the command drives, or <see langword="null"/> for <see cref="GraphicsPresetRegistry.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    public static void Register(ConsoleSystem console, GraphicsPresetRegistry? presets = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        GraphicsPresetRegistry registry = presets ?? GraphicsPresetRegistry.Default;

        console.Commands.Add(
            CommandName,
            "Reports or applies a graphics preset.",
            context => Execute(context, registry),
            "[vanilla | enhanced | high | ultra]");
    }

    private static void Execute(ConsoleCommandContext context, GraphicsPresetRegistry registry)
    {
        if (context.Count == 0)
        {
            context.Print($"{CommandName} = {Name(registry.Current)}");
            context.Print(registry.LockedPreset is { } held
                ? $"  managing {registry.Managed.Count} setting(s); this build is locked to {Name(held)}"
                : $"  managing {registry.Managed.Count} setting(s); choices: vanilla | enhanced | high | ultra");
            return;
        }

        if (!Enum.TryParse(context[0], ignoreCase: true, out GraphicsPreset preset) ||
            preset == GraphicsPreset.Custom)
        {
            context.Error($"'{context[0]}' is not a preset. Choose vanilla, enhanced, high or ultra.");
            return;
        }

        if (registry.LockedPreset is { } locked && locked != preset)
        {
            context.Error($"This build is locked to the {Name(locked)} preset.");
            return;
        }

        int applied = registry.Apply(preset);
        context.Print($"Applied '{Name(preset)}' to {applied} setting(s).");
    }

    private static string Name(GraphicsPreset preset) => preset.ToString().ToLowerInvariant();
}

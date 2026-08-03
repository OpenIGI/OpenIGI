using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
    // The mission's authored grade, or the one r_lut_table names instead. Loaded per level rather than
    // per frame -- it is a volume texture upload, while how much of it to apply is a number.
    // A mission with no table of its own is not a fault: it renders ungraded, which is what every
    // mission did before this existed.
    private void ApplyColourLookup(int mission)
    {
        string named = RenderCVars.ColourLookupTable.Value.Trim();
        string name = named.Length > 0
            ? named
            : FormattableString.Invariant($"mission{mission}");

        // Anchored to the assets directory and stripped of any path, so r_lut_table cannot be pointed
        // at an arbitrary file on the machine from a settings file or a +statement.
        string file = Path.Combine(
            AppContext.BaseDirectory, "Assets", "luts", Path.GetFileName(name) + ".cube");

        ColourLookupTable? table = ColourLookupTable.Load(file, _logger);

        if (table is null && named.Length > 0)
        {
            _logger.Warning("Grade", $"No colour table called '{named}' in Assets/luts; the frame is ungraded.");
        }

        _device.SetColourLookup(table);
    }
}

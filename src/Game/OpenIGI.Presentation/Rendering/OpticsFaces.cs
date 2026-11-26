using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The faces a remastered optic's interface is set in: the menus' own body and display faces and their theme.</summary>
/// <param name="Body">The body face.</param>
/// <param name="Display">The distance-field display face, where one loaded.</param>
/// <param name="Theme">The theme the faces make.</param>
public sealed record OpticsFaces(UiFont Body, UiFont? Display, UiTheme Theme)
{
    /// <summary>Loads the faces, or reports why they cannot be.</summary>
    /// <param name="device">The device the faces are made on.</param>
    /// <param name="logger">Where a face that cannot load is reported.</param>
    /// <param name="optic">What the faces are for, as the report names it: "the remastered binoculars".</param>
    /// <returns>The faces, or null where they cannot load.</returns>
    public static OpticsFaces? Load(IGraphicsDevice device, IOpenIgiLogger logger, string optic)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        if (!UiFontLibrary.TypographyAvailable)
        {
            logger.Warning("Hud", $"No interface font at {UiFontLibrary.FontsDirectory}; {optic} cannot draw.");
            return null;
        }

        try
        {
            IgiTypography typography = UiFontLibrary.Typography(device);
            return new OpticsFaces(typography.Body, typography.Display, typography.CreateTheme());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            logger.Warning("Hud", $"Could not load {optic}' faces: {ex.Message}");
            return null;
        }
    }
}

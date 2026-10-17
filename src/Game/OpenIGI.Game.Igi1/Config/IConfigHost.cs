using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Igi1.Config;

/// <summary>
/// What the <c>Config_*</c> functions need from outside the configuration itself.
/// </summary>
/// <remarks>
/// The list fillers are the reason this exists: the render devices and display modes come from the
/// graphics layer and the missions from the installed data, neither of which belongs in
/// <see cref="OpenIGI.Game"/>. A host that knows none of them returns nothing, and the corresponding
/// list box draws empty — which is what should happen, rather than a made-up list.
/// </remarks>
public interface IConfigHost
{
    /// <summary>Gets the display mode a fresh profile starts on.</summary>
    int DefaultDisplayMode { get; }

    /// <summary>Lists the render devices available.</summary>
    /// <returns>One item per device, whose id is what <c>Config_GraphicOptionsSetDevice</c> receives.</returns>
    IEnumerable<MenuListItem> GetRenderDevices();

    /// <summary>Lists the display modes a device offers.</summary>
    /// <param name="device">The device id, which the graphics screen passes from its device list box.</param>
    /// <returns>One item per mode.</returns>
    IEnumerable<MenuListItem> GetDisplayModes(int device);

    /// <summary>Maps a stored device GUID onto its position in <see cref="GetRenderDevices"/>.</summary>
    /// <param name="guid">The GUID from the profile.</param>
    /// <returns>The id, or zero when the device is gone.</returns>
    /// <remarks>
    /// A profile keeps the GUID rather than the index precisely because the list can change between
    /// runs — the original re-resolves it on load, falling back to the first device.
    /// </remarks>
    int GetDeviceIndex(string guid);

    /// <summary>Maps a device id back onto the GUID to store.</summary>
    /// <param name="device">The id.</param>
    /// <returns>The GUID.</returns>
    string GetDeviceGuid(int device);

    /// <summary>Applies a resolution the player chose.</summary>
    /// <param name="mode">The index into <see cref="GetDisplayModes"/>.</param>
    void SetResolution(int mode);

    /// <summary>Applies a gamma setting immediately, so the slider previews.</summary>
    /// <param name="gamma">The gamma, 0 to 1.</param>
    void SetGamma(float gamma);

    /// <summary>Lists the missions the player may start.</summary>
    /// <param name="reached">The furthest mission the active profile has reached.</param>
    /// <returns>One item per mission.</returns>
    IEnumerable<MenuListItem> GetMissions(int reached);

    /// <summary>Fills a picture box with the selected mission's images.</summary>
    /// <param name="target">The control running its collect script.</param>
    void FillMissionPictures(MenuWindowTask? target);

    /// <summary>Fills a text box with the selected mission's briefing.</summary>
    /// <param name="target">The control running its collect script.</param>
    void FillMissionText(MenuWindowTask? target);
}

/// <summary>An <see cref="IConfigHost"/> that knows nothing, for tests and for a headless boot.</summary>
/// <remarks>
/// Every list comes back empty rather than invented. A configuration screen driven by this shows its
/// controls with nothing in them, which is the honest rendering of "there is no graphics device here".
/// </remarks>
public sealed class NullConfigHost : IConfigHost
{
    /// <summary>Gets the shared instance.</summary>
    public static NullConfigHost Instance { get; } = new();

    /// <inheritdoc/>
    public int DefaultDisplayMode => 0;

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetRenderDevices() => [];

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetDisplayModes(int device) => [];

    /// <inheritdoc/>
    public int GetDeviceIndex(string guid) => 0;

    /// <inheritdoc/>
    public string GetDeviceGuid(int device) => PlayerProfile.DefaultDeviceGuid;

    /// <inheritdoc/>
    public void SetResolution(int mode)
    {
    }

    /// <inheritdoc/>
    public void SetGamma(float gamma)
    {
    }

    /// <inheritdoc/>
    public IEnumerable<MenuListItem> GetMissions(int reached) => [];

    /// <inheritdoc/>
    public void FillMissionPictures(MenuWindowTask? target)
    {
    }

    /// <inheritdoc/>
    public void FillMissionText(MenuWindowTask? target)
    {
    }
}

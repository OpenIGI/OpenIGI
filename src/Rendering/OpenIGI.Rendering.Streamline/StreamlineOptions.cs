namespace OpenIGI.Rendering.Streamline;

/// <summary>Where the NVIDIA Streamline runtime is, and how to start it.</summary>
/// <remarks>
/// <para>
/// Streamline is the NVIDIA layer DLSS, ray reconstruction, frame generation and Reflex are reached
/// through. It ships as signed proprietary DLLs this GPL repository does not carry:
/// players get them as the optional <c>nvidia-streamline</c> runtime pack from the launcher, and a
/// development checkout gets them from <c>tools/fetch-streamline.ps1</c>. Which folder is used is the
/// application's decision; the backend only ever loads from the one it is handed here.
/// </para>
/// <para>
/// Every failure past this point is survivable. A missing folder, an unsigned file, a machine with no
/// NVIDIA adapter or an old driver each leave the backend running on its own reconstruction, and say why.
/// </para>
/// </remarks>
/// <param name="Directory">The absolute path of the folder holding <c>sl.interposer.dll</c> and its plugins.</param>
/// <param name="Development">
/// Load the development binaries from the folder's <c>development</c> subfolder instead: they draw
/// NVIDIA's debug overlays and read the plugins' JSON configuration files. Never what a player runs.
/// </param>
/// <param name="LogDirectory">
/// Where Streamline may write its log and the NGX runtime its data. Separate from
/// <paramref name="Directory"/> because an installed pack's folder is not necessarily writable.
/// </param>
/// <param name="FrameGeneration">
/// Whether frame generation is wanted from the start. Its plugin is otherwise unloaded before the first
/// swapchain is built: while it is loaded the host renders off-screen and pays a copy every frame, even
/// with frame generation off, so it is loaded only on request (and the swapchain rebuilt around that).
/// </param>
public sealed record StreamlineOptions(string Directory, bool Development, string LogDirectory, bool FrameGeneration = false)
{
    /// <summary>The folder Streamline is loaded from, after the development switch is applied.</summary>
    public string EffectiveDirectory =>
        Development ? Path.Combine(Directory, "development") : Directory;
}

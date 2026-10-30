using OpenIGI.Platform.Web;

namespace OpenIGI.Platform.Web.Tests;

/// <summary>A page that queues what a test says happened and records what the window asked of it.</summary>
internal sealed class FakeBrowserPage : IBrowserPage
{
    private readonly List<string> _queued = [];

    public (int Width, int Height) CanvasSize { get; set; } = (1280, 720);

    public bool HasFocus { get; set; } = true;

    public bool IsFullscreen { get; set; }

    public string Title { get; private set; } = string.Empty;

    public (bool Visible, bool Locked) Pointer { get; private set; }

    public List<bool> FullscreenRequests { get; } = [];

    public string Clipboard { get; private set; } = string.Empty;

    public void Queue(params string[] events) => _queued.AddRange(events);

    public string[] TakeEvents()
    {
        string[] taken = [.. _queued];
        _queued.Clear();
        return taken;
    }

    public void SetTitle(string title) => Title = title;

    public void SetPointer(bool visible, bool locked) => Pointer = (visible, locked);

    public void RequestFullscreen(bool fullscreen) => FullscreenRequests.Add(fullscreen);

    public void WriteClipboard(string text) => Clipboard = text;

    public List<bool> GyroRequests { get; } = [];

    public void SetGyro(bool wanted) => GyroRequests.Add(wanted);
}

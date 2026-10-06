using OpenIGI.Launcher;
using OpenIGI.Platform.Desktop;

// The launcher's own icon, amber, on its window and in the notification area: told apart from the game's.
WindowIcon.Kind = WindowIconKind.Launcher;

using var app = new LauncherApp();
app.Run();

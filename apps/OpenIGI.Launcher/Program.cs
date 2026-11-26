using OpenIGI.Launcher;
using OpenIGI.Platform.Desktop;

// The launcher's own icon, amber, on its window and in the notification area: told apart from the game's.
WindowIcon.Kind = WindowIconKind.Launcher;

// One launcher a user: a second, opened by a link on the website while this one runs, hands the link over and closes.
using OpenIGI.Launcher.Services.SingleInstance? instance = OpenIGI.Launcher.Services.SingleInstance.Claim(args);
if (instance is null)
{
    return;
}

using var app = new LauncherApp(args, instance);
app.Run();

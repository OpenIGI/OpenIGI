using System.Reflection;
using System.Runtime.InteropServices;
using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class UpdateNoticeTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_healthy_launcher_asks_once_a_minute_with_a_little_jitter()
    {
        PollSchedule early = new(() => 0.0);
        PollSchedule late = new(() => 1.0);
        early.Finished(Now, succeeded: true);
        late.Finished(Now, succeeded: true);

        Assert.Equal(Now + PollSchedule.Interval, early.Next);
        Assert.Equal(Now + PollSchedule.Interval + TimeSpan.FromSeconds(10), late.Next);
    }

    [Fact]
    public void An_unreachable_service_is_asked_less_and_less_often_up_to_a_quarter_hour()
    {
        PollSchedule schedule = new(() => 0.0);
        TimeSpan[] waits = new TimeSpan[7];
        for (int i = 0; i < waits.Length; i++)
        {
            schedule.Finished(Now, succeeded: false);
            waits[i] = schedule.Next - Now;
        }

        Assert.Equal(
            [1, 2, 4, 8, 15, 15, 15],
            waits.Select(static w => (int)w.TotalMinutes));

        schedule.Finished(Now, succeeded: true);
        Assert.Equal(0, schedule.Failures);
        Assert.Equal(Now + PollSchedule.Interval, schedule.Next);
    }

    [Fact]
    public void Switching_to_the_window_asks_at_once_but_not_more_than_every_few_seconds()
    {
        PollSchedule schedule = new(() => 0.0);
        schedule.Started(Now);
        schedule.Finished(Now, succeeded: true);

        // Straight after a check: not before the spacing.
        schedule.Soon(Now + TimeSpan.FromSeconds(2));
        Assert.Equal(Now + PollSchedule.MinimumSpacing, schedule.Next);

        // Long after: at once.
        PollSchedule later = new(() => 0.0);
        later.Started(Now);
        later.Finished(Now, succeeded: true);
        later.Soon(Now + TimeSpan.FromSeconds(30));
        Assert.Equal(Now + TimeSpan.FromSeconds(30), later.Next);

        // Never later than the next check already due.
        PollSchedule backingOff = new(() => 0.0);
        backingOff.Started(Now);
        backingOff.Finished(Now, succeeded: true);
        DateTime due = backingOff.Next;
        backingOff.Soon(Now + TimeSpan.FromSeconds(59.9));
        Assert.True(backingOff.Next <= due);
    }

    [Fact]
    public void Each_release_is_announced_once_and_only_when_notifications_are_on()
    {
        LauncherSettings settings = new();
        Assert.True(UpdateNotices.ShouldAnnounce(settings, "game", "0.2.0"));
        Assert.False(UpdateNotices.ShouldAnnounce(settings, "game", "0.2.0"));
        Assert.True(UpdateNotices.ShouldAnnounce(settings, "launcher", "0.2.0"));
        Assert.True(UpdateNotices.ShouldAnnounce(settings, "game", "0.2.1"));

        settings.Notifications = false;
        Assert.False(UpdateNotices.ShouldAnnounce(settings, "game", "0.3.0"));
        Assert.Equal("0.2.1", settings.Announced["game"]);
    }

    [Fact]
    public void What_was_announced_survives_a_restart()
    {
        string dir = Directory.CreateTempSubdirectory("openigi-announced-").FullName;
        try
        {
            string path = Path.Combine(dir, "settings.json");
            LauncherSettings settings = new();
            Assert.True(UpdateNotices.ShouldAnnounce(settings, "game", "0.2.0"));
            settings.Save(path);

            Assert.False(UpdateNotices.ShouldAnnounce(LauncherSettings.Load(path), "game", "0.2.0"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_windows_notification_structure_has_the_size_the_shell_expects()
    {
        // NOTIFYICONDATAW since Vista: 976 bytes on 64-bit, 956 on 32-bit. A wrong size and the shell
        // silently ignores the call.
        Type data = typeof(OpenIGI.Platform.Desktop.TrayIcon).GetNestedType("NotifyIconData", BindingFlags.NonPublic)!;
        Assert.Equal(Environment.Is64BitProcess ? 976 : 956, Marshal.SizeOf(data));
    }
}

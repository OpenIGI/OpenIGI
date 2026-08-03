using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the new save-slot format's round-trip: manifest, screenshot, overwrite and delete.</summary>
public sealed class SaveSlotStoreTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "openigi-saves-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_saved_slot_round_trips_through_the_manifest_and_reloads()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            var pixels = new Bgra32[4 * 4];
            Array.Fill(pixels, Bgra32.White);
            var when = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

            SaveSlot saved = store.Save(0, "Joe", 3, "The Getaway", when, pixels, 4, 4);
            Assert.Equal(0, saved.Slot);
            Assert.Equal("The Getaway", saved.MissionName);
            Assert.NotNull(saved.ScreenshotFile);
            Assert.True(File.Exists(store.ScreenshotPath(saved)!));

            // A fresh store reads the same slot back from disk.
            var reloaded = new SaveSlotStore(dir);
            SaveSlot? read = reloaded.Find(0);
            Assert.NotNull(read);
            Assert.Equal("Joe", read!.ProfileName);
            Assert.Equal(3, read.MissionId);
            Assert.Equal(when, read.SavedAt);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Saving_the_same_slot_replaces_it()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            store.Save(1, "A", 1, "First", DateTimeOffset.UnixEpoch, ReadOnlySpan<Bgra32>.Empty, 0, 0);
            store.Save(1, "B", 5, "Fifth", DateTimeOffset.UnixEpoch, ReadOnlySpan<Bgra32>.Empty, 0, 0);

            Assert.Single(store.Slots);
            Assert.Equal("B", store.Find(1)!.ProfileName);
            Assert.Equal(5, store.Find(1)!.MissionId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Deleting_a_slot_removes_it_and_its_screenshot()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            var pixels = new Bgra32[1];
            SaveSlot saved = store.Save(2, "Joe", 1, "One", DateTimeOffset.UnixEpoch, pixels, 1, 1);
            string screenshot = store.ScreenshotPath(saved)!;
            Assert.True(File.Exists(screenshot));

            Assert.True(store.Delete(2));
            Assert.Null(store.Find(2));
            Assert.False(File.Exists(screenshot));
            Assert.False(store.Delete(2)); // already gone
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_missing_manifest_is_an_empty_store()
    {
        string dir = TempDir();
        var store = new SaveSlotStore(dir); // directory does not exist yet
        Assert.Empty(store.Slots);
    }
}

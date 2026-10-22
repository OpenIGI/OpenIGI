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
    public void A_slot_keeps_the_mods_it_was_saved_with_and_says_which_are_off()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            OpenIGI.Core.Content.ModReference nightfall = new("nightfall", "1.2");
            store.Save(0, "Joe", 901, "Nightfall One", DateTimeOffset.UnixEpoch, [], 0, 0, [nightfall]);

            SaveSlot read = new SaveSlotStore(dir).Find(0)!;

            Assert.Equal([nightfall], read.Mods);
            Assert.Equal([nightfall], OpenIGI.Core.Content.ModReference.Missing(read.Mods, []));
            Assert.Equal([nightfall], OpenIGI.Core.Content.ModReference.Missing(read.Mods, [new("nightfall", "1.3")]));
            Assert.Empty(OpenIGI.Core.Content.ModReference.Missing(read.Mods, [new("NIGHTFALL", "1.2")]));
            Assert.Empty(OpenIGI.Core.Content.ModReference.Missing(null, []));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

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
    public void Different_profiles_can_use_the_same_slot_without_overwriting_each_other()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            store.Save(1, "A", 1, "First", DateTimeOffset.UnixEpoch, ReadOnlySpan<Bgra32>.Empty, 0, 0);
            store.Save(1, "B", 5, "Fifth", DateTimeOffset.UnixEpoch, ReadOnlySpan<Bgra32>.Empty, 0, 0);

            Assert.Equal(2, store.Slots.Count);
            Assert.Equal(1, store.Find("A", 1)!.MissionId);
            Assert.Equal(5, store.Find("B", 1)!.MissionId);
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

    [Fact]
    public void Deleting_a_profile_removes_its_saves_and_screenshots_but_keeps_other_profiles()
    {
        string dir = TempDir();
        try
        {
            var store = new SaveSlotStore(dir);
            var pixels = new Bgra32[1];
            SaveSlot joe = store.Save(0, "Joe", 1, "One", DateTimeOffset.UnixEpoch, pixels, 1, 1);
            SaveSlot alex = store.Save(0, "Alex", 2, "Two", DateTimeOffset.UnixEpoch, pixels, 1, 1);
            string joeScreenshot = store.ScreenshotPath(joe)!;
            string alexScreenshot = store.ScreenshotPath(alex)!;

            Assert.Equal(1, store.DeleteByProfile("jOe"));

            Assert.Null(store.Find("Joe", 0));
            Assert.NotNull(store.Find("Alex", 0));
            Assert.False(File.Exists(joeScreenshot));
            Assert.True(File.Exists(alexScreenshot));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

using OpenIGI.Editor.Core.Projects;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Copies of unsaved work: the last few kept, the newest offered only when newer than the save, cleared by saving.</summary>
public sealed class MissionAutosaveTests
{
    /// <summary>Six copies keep five, the newest first; one newer than the save is unsaved work, none is after a later save.</summary>
    [Fact]
    public void Copies_are_kept_and_offered_after_the_save()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-autosave-" + Guid.NewGuid().ToString("N"));
        try
        {
            MissionAutosave autosave = new(folder, "igi1");
            const string Mission = "missions/location0/level1/objects.qvm";
            DateTime start = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 6; i++)
            {
                autosave.Write(Mission, [(byte)i], start.AddMinutes(3 * i));
            }

            IReadOnlyList<string> copies = autosave.Copies(Mission);
            Assert.Equal(MissionAutosave.Kept, copies.Count);
            Assert.Equal([5], File.ReadAllBytes(copies[0]));

            (string file, DateTime taken) = autosave.Unsaved(Mission, start.AddMinutes(4))!.Value;
            Assert.Equal(copies[0], file);
            Assert.Equal(start.AddMinutes(15), taken);
            Assert.Null(autosave.Unsaved(Mission, start.AddMinutes(20)));
            Assert.Empty(autosave.Copies("missions/location0/level2/objects.qvm"));

            autosave.Clear(Mission);
            Assert.Empty(autosave.Copies(Mission));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

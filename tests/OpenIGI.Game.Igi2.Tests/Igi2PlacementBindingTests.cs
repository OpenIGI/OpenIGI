using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The task types that name their model, position or facing something other than the usual, and so were
/// walked straight past.
/// </summary>
/// <remarks>
/// <para>
/// <c>Igi2WorldScene</c> collects a placement by reading three parameters by name, which works because
/// most of the sequel's placeable types agree on <c>Model</c>, <c>Position</c> and <c>Orientation</c>. A
/// dozen do not, and every one of them was dropped: across the 24 shipped levels that was 802 doors, 155
/// switches, 124 security cameras, 60 gun holders and 8 lifts, plus the subsystems
/// <see cref="Igi2WorldScene.Bindings"/> lists as still outstanding.
/// </para>
/// <para>
/// These check the bindings against the shipped data rather than against the aggregate counts the other
/// suites pin, so that a binding that starts reading the wrong parameter fails as itself.
/// </para>
/// </remarks>
public sealed class Igi2PlacementBindingTests
{
    /// <summary>Every bound type places all of its tasks, across the whole corpus.</summary>
    /// <remarks>
    /// A task of these types always carries the parameters its binding names, so anything short of the
    /// whole population means a binding is reading a name the data does not use.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_bound_type_places_all_of_its_tasks()
    {
        Dictionary<string, int> declared = new(StringComparer.Ordinal);
        Dictionary<string, int> placed = new(StringComparer.Ordinal);

        foreach (Igi2Level level in Igi2Level.All())
        {
            HashSet<int> ids = [];
            foreach (Igi2Placement placement in level.Scene.Placements)
            {
                ids.Add(placement.LevelId);
            }

            foreach (GameTask task in Walk(level.Root))
            {
                if (task is not DeclaredTask task2 || !Igi2WorldScene.Bindings.ContainsKey(task2.Type.Name))
                {
                    continue;
                }

                declared[task2.Type.Name] = declared.GetValueOrDefault(task2.Type.Name) + 1;
                if (ids.Contains(task2.Id))
                {
                    placed[task2.Type.Name] = placed.GetValueOrDefault(task2.Type.Name) + 1;
                }
            }
        }

        Assert.Equal(802, declared["Door"]);
        Assert.Equal(155, declared["Switch"]);
        Assert.Equal(124, declared["SCamera"]);
        Assert.Equal(41, declared["AIStationaryGunHolder"]);
        Assert.Equal(19, declared["StationaryGun"]);
        Assert.Equal(8, declared["Lift"]);

        foreach ((string type, int count) in declared)
        {
            Assert.Equal(count, placed.GetValueOrDefault(type));
        }
    }

    /// <summary>A door stands closed, at the position and facing its own parameters name.</summary>
    /// <remarks>
    /// <c>Door_UpdateInternalDataHandler</c> (<c>igi2 0x537E10</c>) copies the task's position into
    /// <c>tOriginalPosStart</c> and sets <c>isClosed</c>; <c>Door_SetPosition</c> (<c>0x5362B0</c>) then
    /// offsets by <c>stop × slider</c> and turns by <c>maxAngle × slider</c>, both zero while it is shut.
    /// So a closed door is exactly its <c>Position start</c> under its <c>Orientation</c> — which is a
    /// different parameter from the <c>Position</c> the walk used to require, and the whole reason 802 of
    /// them were missing.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_door_stands_at_its_start_position()
    {
        Igi2Level level = LevelOne();
        DeclaredTask door = Walk(level.Root)
            .OfType<DeclaredTask>()
            .First(t => t.Type.Name == "Door");

        Assert.True(door.TryGetValue("Position start", out object? rawStart));
        Vector3d start = Assert.IsType<Vector3d>(rawStart);
        Assert.True(door.TryGetValue("Orientation", out object? rawAngles));
        Vector3f angles = Assert.IsType<Vector3f>(rawAngles);

        Igi2Placement placement = level.Scene.Placements.Single(p => p.LevelId == door.Id);
        Assert.Equal(start, placement.Position);
        Assert.Equal(angles, placement.Orientation);

        // And it is NOT at "Position", which a door does not carry at all.
        Assert.False(door.TryGetValue(Igi2WorldScene.PositionParameter, out _));
    }

    /// <summary>A switch wears the model its starting state names, and that model's own group letter.</summary>
    /// <remarks>
    /// <c>Switch_SetModel</c> (<c>igi2 0x4DA360</c>) indexes the five models as on, on-pressed, off,
    /// off-pressed and destroyed, so an untouched switch shows the first or the third.
    /// <c>Switch_EnumerateLightmapVirModelsHandler</c> (<c>0x4DB120</c>) gives each slot the group letter
    /// of its own index plus one, which is what
    /// <see cref="Igi2Placement.ModelVariant"/> carries and what picks the matching bake.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_switch_wears_the_model_its_starting_state_names()
    {
        string[] slots = ["On model", "On pressed model", "Off model", "Off pressed model", "Destroyed model"];
        int switches = 0;
        int offSlot = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (DeclaredTask task in Walk(level.Root).OfType<DeclaredTask>().Where(t => t.Type.Name == "Switch"))
            {
                Assert.True(task.TryGetValue("Initial on", out object? rawInitial));
                bool initial = Assert.IsType<bool>(rawInitial);
                Assert.True(task.TryGetValue(initial ? "On model" : "Off model", out object? rawModel));
                string expected = Assert.IsType<string>(rawModel);

                Igi2Placement placement = level.Scene.Placements.Single(p => p.LevelId == task.Id);
                Assert.Equal(expected, placement.Model);
                switches++;

                // The variant is the FIRST slot naming that model, counted from one. First, because the
                // enumerator de-duplicates: a slot repeating an earlier slot's model takes no letter of
                // its own, so a switch whose off model is also its on model wears the on model's letter.
                int first = 1 + Array.FindIndex(
                    slots,
                    slot => task.TryGetValue(slot, out object? raw)
                        && raw is string name
                        && string.Equals(name, expected, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(first, placement.ModelVariant);

                if (placement.ModelVariant == 3)
                {
                    offSlot++;
                }
            }
        }

        Assert.Equal(155, switches);
        Assert.True(offSlot > 0, "No switch in the corpus reaches the off slot, so its letter is untested.");
    }

    /// <summary>A camera's bracket turns about the up axis alone, by the one angle it carries.</summary>
    /// <remarks>
    /// <c>SCamera_UpdateInternalDataHandler</c> (<c>igi2 0x48FB80</c>) builds the holder's rotation from
    /// a single angle's sine and cosine. The camera body is a child task placed on a MAGIC VERTEX of the
    /// holder (<c>0x48F370</c>), so only the bracket is bound here.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_camera_holder_turns_by_its_lone_gamma()
    {
        Igi2Level level = LevelOne();
        DeclaredTask camera = Walk(level.Root)
            .OfType<DeclaredTask>()
            .First(t => t.Type.Name == "SCamera");

        Assert.True(camera.TryGetValue("Holder Model", out object? rawModel));
        Assert.True(camera.TryGetValue("Holder Position", out object? rawPosition));
        Assert.True(camera.TryGetValue("Holder Gamma", out object? rawGamma));

        Igi2Placement placement = level.Scene.Placements.Single(p => p.LevelId == camera.Id);
        Assert.Equal(Assert.IsType<string>(rawModel), placement.Model);
        Assert.Equal(Assert.IsType<Vector3d>(rawPosition), placement.Position);
        Assert.Equal(new Vector3f(0f, 0f, Assert.IsType<float>(rawGamma)), placement.Orientation);
    }

    private static Igi2Level LevelOne() =>
        Igi2Level.All().First(level =>
            level.Directory.Replace(Path.DirectorySeparatorChar, '/')
                .EndsWith("location1/level1", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}

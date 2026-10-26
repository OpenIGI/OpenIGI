using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// The game's own per-model level-of-detail distances: <c>LOCAL:lod.qsc</c> run as the original's container runs it
/// (<c>0x4EDC00</c>), each <c>ModelLODSettings</c> it makes made ascending and taken in world units (<c>0x4EDB60</c>).
/// </summary>
internal static class Igi1ModelLodTable
{
    /// <summary>The compiled script.</summary>
    public const string Script = "LOCAL:lod.qvm";

    /// <summary>Reads the distances.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <returns>Each model's distances to its second to fifth levels and its cutoff, world units, by its first level's name.</returns>
    public static IReadOnlyDictionary<string, float[]> Read(ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Dictionary<string, float[]> distances = new(StringComparer.OrdinalIgnoreCase);
        if (!resources.TryLoad(Script, out ReadOnlyMemory<byte> bytes))
        {
            return distances;
        }

        NativeRegistry natives = new();
        TaskTree tree = new(new TaskTypeRegistry());
        TaskScriptCommands commands = new(tree, new ScriptVarRegistry(natives));
        commands.Register(natives);
        commands.BeginLoad();
        QvmInterpreter interpreter = new(QvmProgram.Read(bytes), natives);
        if (interpreter.Run() != QvmError.None)
        {
            return distances;
        }

        foreach (GameTask task in tree.Root.Descendants())
        {
            if (task is not DeclaredTask { Type.Name: "ModelLODSettings" } setting
                || !setting.TryGetValue("Model", out object? model) || model is not string name)
            {
                continue;
            }

            float[] own = new float[5];
            string[] names = ["Distance to LOD 1", "Distance to LOD 2", "Distance to LOD 3", "Distance to LOD 4", "Distance to cutoff"];
            for (int i = 0; i < own.Length; i++)
            {
                float metres = setting.TryGetValue(names[i], out object? value) && value is float real ? real : 0f;
                if (i > 0 && metres < own[i - 1] / 4096f)
                {
                    metres = own[i - 1] / 4096f;
                }

                own[i] = metres * 4096f;
            }

            distances[name.Trim()] = own;
        }

        return distances;
    }
}

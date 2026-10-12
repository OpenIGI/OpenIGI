using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>
/// The faces humans are given, <c>Human_MakeRandomFaceModel</c> (human.c): a model named for a kind of face rather than one is made one
/// of three, in turn, by a counter every human made steps.
/// </summary>
public sealed class Igi2HumanFaces
{
    /// <summary>The models that stand for a kind of face, and the name each variant is written as.</summary>
    private static readonly (string[] Generic, string Pattern)[] Kinds =
    [
        (["ch_rsg_1", "rsg_1"], "rsg{0}_1"),
        (["resg_1", "ch_resg_1"], "resg{0}_1"),
        (["libgu_1"], "libgu{0}_1"),
        (["les_1"], "les{0}_1"),
        (["chn_1"], "chn{0}_1"),
        (["csf_1"], "csf{0}_1"),
    ];

    private int _counter;

    /// <summary>
    /// Gives every human of a level its face in load order, the player, the soldiers and the figures stepping the one counter as each is
    /// made (<c>Human_UpdateModel</c>, <c>EditBoneObj_UpdateInternalDataHandler</c>).
    /// </summary>
    /// <param name="root">The level's root.</param>
    /// <returns>The model each human wears, by its task.</returns>
    public static IReadOnlyDictionary<GameTask, string> Assign(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Igi2HumanFaces faces = new();
        Dictionary<GameTask, string> worn = new(ReferenceEqualityComparer.Instance);
        foreach (GameTask task in Igi2TaskTree.Descendants(root))
        {
            string? model = task switch
            {
                Player.Igi2HumanPlayerTask player => player.LevelModel,
                Ai.Igi2HumanSoldierTask soldier => soldier.Model,
                World.Igi2EditBoneObjTask figure => figure.Model,
                _ => null,
            };
            if (model is not null)
            {
                worn[task] = faces.Make(model);
            }
        }

        return worn;
    }

    /// <summary>Gets the model a human is made with, stepping the counter whatever the model.</summary>
    /// <param name="model">The model the level names.</param>
    /// <returns>The model it wears.</returns>
    public string Make(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int variant = (_counter % 3) + 1;
        _counter++;
        foreach ((string[] generic, string pattern) in Kinds)
        {
            if (Array.IndexOf(generic, model) >= 0)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, variant);
            }
        }

        return model;
    }
}

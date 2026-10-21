using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>Telling the level's listeners where the human is, <c>human.c Human_Notify</c>.</content>
public abstract partial class Igi2Human
{
    /// <summary>
    /// Gets or sets what the human tells its place to each tick, <c>GameFunctions_ptNotifyQTaskList</c>: the level's fences and the
    /// mines laid in it.
    /// </summary>
    public IReadOnlyList<IIgi2Notified> Notified { get; set; } = [];

    /// <summary>
    /// Tells each listener, in the list's order, where the human is, <c>Human_Notify</c>: its feet raised a metre, with the radius of
    /// its root bone's reach and its model.
    /// </summary>
    protected void Notify()
    {
        Vector3f r = RootTranslation;
        float radius = (Fpu.Sqrt((((Fpu)r.X * r.X) + ((Fpu)r.Y * r.Y)) + ((Fpu)r.Z * r.Z)) + ModelRadius).F;
        Vector3d at = new(Position.X, Position.Y, ((Fpu)Position.Z + 4096.0).Value);
        for (int i = 0; i < Notified.Count; i++)
        {
            Notified[i].Notify(this, at, radius);
        }
    }
}

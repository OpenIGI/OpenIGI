using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// What the shadow passes draw as characters: the level's soldiers, and whatever a title or a mod draws for its tasks that
/// casts a shadow.
/// </summary>
/// <param name="soldiers">The soldiers, or <see langword="null"/> for none.</param>
/// <param name="tasks">The task presenters that cast a shadow.</param>
/// <param name="blend">How far the frame is between ticks, which the task presenters draw at.</param>
internal sealed class SoldierBodies(ISoldierBodies? soldiers, IReadOnlyList<ITaskPresenter> tasks, float blend) : ICharacterPresenter
{
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        soldiers?.Draw(camera, projection);
        foreach (ITaskPresenter task in tasks)
        {
            task.Draw(camera, projection, blend);
        }
    }

    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection) => soldiers?.DrawPlayerBody(camera, projection);
}

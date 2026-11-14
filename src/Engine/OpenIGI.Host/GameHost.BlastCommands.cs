using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Registers the commands that set the remastered bomb blast off where the camera looks, and put it back, for tuning it.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// Presentation alone: <c>bomb_blast</c> shows the blast -- its light, shake, shockwave, fireball, ground and what it
    /// shatters -- and harms nobody, so it can be set off in any level, in or out of a match, as many times as tuning takes.
    /// The rocket's own fire the bomb is drawn over in a match is the game's and is not shown. Its pieces land on the level's
    /// own collision, as a match's do. <c>bomb_blast_reset</c>
    /// puts back what it broke, as a new round does.
    /// </remarks>
    private void RegisterBlastCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "bomb_blast",
            "Sets the remastered bomb blast off on the ground ahead of the camera: bomb_blast [metres ahead, 25] [radius in metres, 40].",
            context =>
            {
                float ahead = float.TryParse(context[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float a) ? a : 25f;
                float radius = float.TryParse(context[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 40f;
                Camera3D eye = _inspectorEye;
                Vector3f forward = eye.Orientation.Row3;
                Vector3f level = new Vector3f(forward.X, forward.Y, 0f).Normalized();

                // On the ground there: the terrain's own height when the level's raster is built, else an eye's height under the camera.
                double x = eye.Position.X + (level.X * ahead);
                double y = eye.Position.Y + (level.Y * ahead);
                double z = eye.Position.Z - 1.7;
                if (_terrainRaster is { } terrain && terrain.SurfaceOver(x, y) is float ground && ground > ShelterHeightField.OpenSky)
                {
                    z = ground;
                }

                Vector3d metres = new(x, y, z);
                bool shown = _effects.StartBomb(
                    metres * WorldUnits.PerMetre,
                    radius * (float)WorldUnits.PerMetre,
                    eye.Position,
                    _scene,
                    _world as IShatterableWorld,
                    debrisWorld: new OpenIGI.Game.Physics.RagdollWorld(_mission.Collision.Cast));
                string said = shown
                    ? string.Create(CultureInfo.InvariantCulture, $"Bomb blast at {metres.X:0.#},{metres.Y:0.#},{metres.Z:0.#}, radius {radius:0.#} m.")
                    : "The remastered bomb blast is off (r_blast_quality 0).";

                // To the log too, for a headless -runat capture, which has no scrollback to read.
                context.Print(said);
                _logger.Information("Blast", said);
            });

        console.Commands.Add(
            "bomb_blast_skip",
            "Moves the bomb blast and everything it set going on some seconds at once: bomb_blast_skip [seconds, 5].",
            context =>
            {
                float seconds = float.TryParse(context[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 5f;
                int ticks = Math.Clamp((int)MathF.Round(seconds * 30f), 0, 30 * 120);
                _effects.FastForward(ticks);
                string said = string.Create(CultureInfo.InvariantCulture, $"Bomb blast moved on {ticks / 30f:0.#} s.");
                context.Print(said);
                _logger.Information("Blast", said);
            });

        console.Commands.Add(
            "bomb_blast_reset",
            "Puts back everything the bomb blast shattered and clears its fire and smoke, as a new round does.",
            context =>
            {
                _effects.Clear();
                context.Print("Bomb blast cleared.");
            });
    }
}

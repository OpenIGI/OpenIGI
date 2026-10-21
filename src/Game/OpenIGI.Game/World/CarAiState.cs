using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>One CarAI task (<c>0x425DE0</c>): the car it drives, its patrol route and the fields it keeps between ticks.</summary>
internal sealed class CarAiState(
    DeclaredTask task,
    DeclaredTask car,
    CutsceneTransformState transform,
    GraphFile graph,
    AiPatrolRoute? route)
{
    public DeclaredTask Task { get; } = task;
    public DeclaredTask Car { get; } = car;
    public CutsceneTransformState Transform { get; } = transform;
    public GraphFile Graph { get; } = graph;
    public AiPatrolRoute? Route { get; } = route;
    public bool Stopped { get; set; }

    // Retail CarAI's own fields, named by offset from 0x425DE0's object.
    public GraphNode? TargetNode { get; set; }            // +104
    public float CommandSpeedKmh { get; set; }            // +108
    public float Halt { get; set; }                       // +112
    public float RouteSpeedKmh { get; set; }              // +128
    public float DesiredSpeedKmh { get; set; }            // +100
    public float Throttle { get; set; }                   // +88, channel 1
    public float Steering { get; set; }                   // +92, channel 0
    public float Brake { get; set; }                      // +96, channel 3
    public int WaitTicks { get; set; }                    // +124
    public int Packets { get; set; }                      // +80
    public bool ChaseHeld { get; set; }                   // +116, the held target (0x426000)
    public int ChaseRetention { get; set; }               // +120, counted down from 600
}

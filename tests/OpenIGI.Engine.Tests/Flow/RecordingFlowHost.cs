using OpenIGI.Engine.Flow;

namespace OpenIGI.Engine.Tests.Flow;

/// <summary>Records which flow transitions fired, in order.</summary>
internal sealed class RecordingFlowHost : IFlowHost
{
    public List<string> Calls { get; } = [];

    public Action? OnBeginGame { get; set; }

    public Action? OnLoadLevel { get; set; }

    public void ShowIntroScreens() => Calls.Add("intro");

    public void ShowMainMenu() => Calls.Add("mainmenu");

    public void ShowOutroScreens() => Calls.Add("outro");

    public void ShowAttractScreens() => Calls.Add("attract");

    public void BeginGame(FlowTask flow)
    {
        Calls.Add("begingame");
        OnBeginGame?.Invoke();
    }

    public void LoadLevel()
    {
        Calls.Add("loadlevel");
        OnLoadLevel?.Invoke();
    }

    public void EndGame() => Calls.Add("endgame");
}

/// <summary>Records the calls the arbiter makes into the renderer.</summary>
internal sealed class RecordingRenderScheduler : IRenderScheduler
{
    public List<string> Calls { get; } = [];

    public int TickRate { get; private set; }

    public bool? LastFirstFrame { get; private set; }

    public void Flush() => Calls.Add("flush");

    public void Reset() => Calls.Add("reset");

    public void SetFirstFrame(bool isFirstFrame)
    {
        LastFirstFrame = isFirstFrame;
        Calls.Add($"firstframe:{isFirstFrame}");
    }

    public void SetTickRate(int tickRate)
    {
        TickRate = tickRate;
        Calls.Add($"tickrate:{tickRate}");
    }
}

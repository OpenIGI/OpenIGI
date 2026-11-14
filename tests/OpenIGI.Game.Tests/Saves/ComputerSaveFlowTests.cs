using OpenIGI.Game.Computer;
using Xunit;

namespace OpenIGI.Game.Tests.Saves;

/// <summary>
/// The save screen every computer runs (<see cref="ComputerSaveFlow"/>, I.G.I. 2's <c>ComputerSaveMenu</c>): the upload to a free or
/// an overwritten slot, the download, and the battery that refuses an upload when it is flat.
/// </summary>
public sealed class ComputerSaveFlowTests
{
    [Fact]
    public void An_upload_to_a_full_set_of_slots_asks_before_it_overwrites()
    {
        Saves saves = new();
        Array.Fill(saves.Used, true);
        ComputerSaveFlow flow = new() { Saves = saves };
        flow.Open();
        Assert.True(flow.Run());
        Assert.Equal(ComputerSaveState.Main, flow.State);

        Assert.True(flow.Upload());
        Assert.True(flow.Run());
        Assert.Equal(ComputerSaveState.Upload, flow.State);
        Assert.False(flow.Run());

        Assert.Equal(ComputerSaveChoice.Overwrite, flow.Choose(3));
        flow.Run();
        Assert.Equal(ComputerSaveState.Overwrite, flow.State);
        flow.Keep();
        flow.Run();
        Assert.Equal(ComputerSaveState.Upload, flow.State);

        flow.Choose(3);
        flow.Run();
        flow.Overwrite();
        flow.Run();
        Assert.Equal(ComputerSaveState.Uploading, flow.State);
        for (int i = 0; i < 102 && flow.State == ComputerSaveState.Uploading; i++)
        {
            flow.Run();
        }

        Assert.Equal(ComputerSaveState.Complete, flow.State);
        Assert.Equal(1f, flow.UploadProgress);
        for (int i = 0; i < ComputerSaveFlow.CompleteTicks + 10; i++)
        {
            flow.Run();
        }

        Assert.Equal(["freeze", "freeze", "begin 3", "finish 3", "quit"], saves.Calls);
    }

    [Fact]
    public void A_download_loads_a_used_slot_and_refuses_a_free_one()
    {
        Saves saves = new();
        saves.Used[1] = true;
        ComputerSaveFlow flow = new() { Saves = saves };
        flow.Open();
        flow.Run();
        flow.Download();
        flow.Run();
        Assert.Equal(ComputerSaveState.Download, flow.State);
        Assert.Equal([true, false, true, true, true], flow.SlotLabels.Select(static s => s.IsFree));

        Assert.Equal(ComputerSaveChoice.Empty, flow.Choose(0));
        Assert.Equal(ComputerSaveChoice.Download, flow.Choose(1));
        Assert.Equal(["freeze", "freeze", "load 1", "quit"], saves.Calls);
    }

    [Fact]
    public void A_flat_battery_uploads_nothing()
    {
        ComputerSaveFlow flow = new() { Saves = new Saves { BatteryLevel = 0f } };
        flow.Open();
        flow.Run();
        Assert.False(flow.Upload());
        Assert.False(flow.Run());
        Assert.Equal(ComputerSaveState.Main, flow.State);
    }

    private sealed class Saves : IComputerSaves
    {
        public List<string> Calls { get; } = [];

        public bool[] Used { get; } = new bool[ComputerSaveFlow.Slots];

        public float BatteryLevel { get; init; } = 1f;

        public bool IsSaveAllowed => true;

        public int QuickLoadSlot => 0;

        public (bool IsUsed, string Info, string When) Slot(int slot) => (Used[slot], "Mission time: 0:01:00", "(today)");

        public void BeginSave(int slot) => Calls.Add($"begin {slot}");

        public void FinishSave(int slot) => Calls.Add($"finish {slot}");

        public void Load(int slot) => Calls.Add($"load {slot}");

        public void FreezeFire() => Calls.Add("freeze");

        public void QuitComputer() => Calls.Add("quit");
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// Co-op lifts, on mission 3's: whoever calls a car, everyone in it rides it on both games -- standing on its floor, jumping
/// in it, drawn on it by the others -- however far apart the games are.
/// </summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopLiftTests
{
    [RequiresGameDataFact]
    public void A_joiner_riding_a_lift_the_host_calls_rides_it_on_both_games_without_being_put_back() => RideALift(0);

    [RequiresGameDataFact]
    public void A_joiner_a_long_way_off_riding_a_lift_the_host_calls_stays_in_the_car() => RideALift(0.35);

    [RequiresGameDataFact]
    public void A_joiner_calling_the_lift_it_rides_goes_with_the_host_without_being_put_back() => RideALift(0, joinerPresses: true);

    [RequiresGameDataFact]
    public void A_joiner_a_long_way_off_calling_the_lift_it_rides_stays_in_the_car() => RideALift(0.35, joinerPresses: true);

    [RequiresGameDataFact]
    public void A_host_riding_with_a_joiner_far_off_is_drawn_on_the_joiners_car_floor() => RideALift(0.35, joinerPresses: true, hostRides: true);

    [RequiresGameDataFact]
    public void A_host_calling_the_lift_it_rides_with_a_joiner_far_off_is_drawn_on_the_joiners_car_floor() => RideALift(0.35, hostRides: true);

    [RequiresGameDataFact]
    public void A_joiner_far_off_jumping_in_a_moving_lift_lands_in_it_on_both_games() => RideALift(0.35, jumps: true);

    // The joiner stands in a car over a line with the given round trip; the host or the joiner calls it, the host riding too
    // or not, the joiner jumping halfway or not.
    private static void RideALift(double ping, bool joinerPresses = false, bool hostRides = false, bool jumps = false)
    {
        using Igi1CoopPair pair = Joined(mission: 3);
        pair.Ping(ping);
        PlacedElevator lift = Assert.IsType<PlacedElevator>(pair.Host!.World.Elevators?.Elevators.FirstOrDefault(static l => l.State.FloorCount > 1));
        int liftIndex = pair.Host.World.Elevators.Elevators.ToList().IndexOf(lift);
        PlacedElevator joinerLift = pair.Joiner!.World.Elevators!.Elevators[liftIndex];
        Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
        Igi1ClientWorld joinerWorld = Assert.IsType<Igi1ClientWorld>(pair.Client.World);
        ClientSession session = pair.Client.Session!;
        Game.Igi1.Player.PlayerCamera hostPlayer = pair.Host.World.Player!;

        // The joiner stands in the car, on both games; the host beside it, or well away.
        Vector3d aboard = lift.State.Position + new Vector3d(0, 0, 64);
        partner.Player.Body.Position = aboard;
        pair.Joiner.World.Player!.Body.Position = aboard;
        if (hostRides)
        {
            hostPlayer.Body.Position = aboard + new Vector3d(1024, 0, 0);
        }

        // Put there, the joiner's commands in flight come back from the server first: a few round trips, and some.
        pair.RunUntil(() => false, 30 + (int)(ping * 240));
        double startZ = partner.Player.Body.Position.Z;
        int to = lift.State.CurrentFloorIndex == 0 ? 1 : 0;
        int startLiftsTaken = joinerWorld.LiftsTaken;

        // Where the joiner settled on its car's floor, which stands some way under the car's origin.
        double settled = pair.Joiner.World.Player.Body.Position.Z - joinerLift.State.Position.Z;

        // The car's own button. The host's press calls it on the host at once; the joiner's is pressed on its own game,
        // where it calls nothing -- the lifts are the server's -- and reaches the server's car with the joiner's commands,
        // about half a round trip later.
        WorldInteraction button = Assert.Single(
            pair.Host.World.Interactions!.Items,
            item => item.Task.Type.Name == "Switch" && item.Task.IsSelfOrDescendantOf(lift.Task));
        long correctionsBefore = session.Reconciler.Corrections;
        if (joinerPresses)
        {
            WorldInteraction joinerButton = Assert.Single(
                pair.Joiner.World.Interactions!.Items,
                item => item.Task.Type.Name == "Switch" && item.Task.IsSelfOrDescendantOf(joinerLift.Task));
            joinerButton.Press();
            pair.RunUntil(() => false, 1 + (int)Math.Ceiling(ping * 15));
            Assert.False(joinerLift.State.IsMoving && !lift.State.IsMoving, "the joiner's car went before the server's");
        }

        button.Press();

        List<double> hostTrip = [];
        List<(double Z, int Lead)> joinerTrip = [];
        double lowestInCar = double.MaxValue;
        double highestInCar = double.MinValue;
        double lowestHostShown = double.MaxValue;
        double highestHostShown = double.MinValue;
        bool jumped = false;
        Assert.True(
            pair.RunUntil(
                () =>
                {
                    int lead = session.SentCommands.Count > 0 && session.LastRunCommandTick != long.MinValue
                        ? (int)(session.SentCommands[^1].Tick - session.LastRunCommandTick)
                        : 0;
                    hostTrip.Add(lift.State.Position.Z);
                    joinerTrip.Add((joinerLift.State.Position.Z, lead));
                    double inCar = pair.Joiner.World.Player.Body.Position.Z - joinerLift.State.Position.Z;
                    lowestInCar = Math.Min(lowestInCar, inCar);
                    highestInCar = Math.Max(highestInCar, inCar);

                    // The host as the joiner draws it, against the joiner's own car.
                    if (hostRides && joinerWorld.Shown.Count > 0 && joinerWorld.OtherOf(joinerWorld.Shown.First()) is { } shownHost)
                    {
                        double shownInCar = shownHost.Player.Body.Position.Z - joinerLift.State.Position.Z;
                        lowestHostShown = Math.Min(lowestHostShown, shownInCar);
                        highestHostShown = Math.Max(highestHostShown, shownInCar);
                    }

                    // Partway up, the joiner jumps.
                    double travelled = Math.Abs(joinerLift.State.Position.Z - lift.State.Floors[1 - to].Position.Z);
                    double span = Math.Abs(lift.State.Floors[to].Position.Z - lift.State.Floors[1 - to].Position.Z);
                    bool jumpNow = jumps && !jumped && lift.State.IsMoving && joinerLift.State.IsMoving && travelled > span * 0.4;
                    jumped |= jumpNow;
                    Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.Space, jumpNow);

                    return lift.State.CurrentFloorIndex == to && !lift.State.IsMoving;
                },
                3000) >= 0,
            "the host's car never arrived");
        Assert.True(!jumps || jumped, "the joiner never jumped");

        // The rest of the joiner's commands from the ride reach the server.
        pair.RunUntil(() => false, 15 + (int)(ping * 30));

        // On the joiner's own game its player stood in its car the whole way: never left below the floor or above it.
        Assert.True(
            highestInCar - lowestInCar < (jumps ? 4096 : 256),
            $"the joiner's player stood from {lowestInCar:0} to {highestInCar:0} units over its car's floor on the ride");
        Assert.True(lowestInCar > settled - 128, $"the joiner's player fell {settled - lowestInCar:0} units under its car's floor");
        long rideCorrections = session.Reconciler.Corrections - correctionsBefore;

        // The server carried the partner with the car.
        double rode = Math.Abs(partner.Player.Body.Position.Z - startZ);
        double floors = Math.Abs(lift.State.Floors[to].Position.Z - lift.State.Floors[1 - to].Position.Z);
        Assert.True(rode > floors * 0.8, $"the partner rode {rode} of the {floors} units between the floors on the host");

        // The joiner's car went with the host's: each frame it stood where the host's stood then or up to the joiner's lead
        // (and a few ticks) later -- its own commands are that far ahead -- and it stopped there too.
        double worstApart = 0;
        for (int i = 0; i < joinerTrip.Count; i++)
        {
            (double z, int lead) = joinerTrip[i];
            double nearest = double.MaxValue;
            // Or as far behind as the host's press takes to reach the joiner: until it learns of the call, its car stands.
            for (int k = Math.Max(0, i - (int)Math.Ceiling(ping * 30) - 2); k <= Math.Min(hostTrip.Count - 1, i + lead + 4); k++)
            {
                nearest = Math.Min(nearest, Math.Abs(hostTrip[k] - z));
            }

            worstApart = Math.Max(worstApart, nearest);
        }

        Assert.True(worstApart < 1024, $"the joiner's car was {worstApart} units from anywhere the host's stood over its lead");
        Assert.True(Math.Abs(joinerLift.State.Position.Z - lift.State.Position.Z) < 64, "the joiner's car stopped elsewhere than the host's");
        Assert.InRange(joinerWorld.LiftsTaken - startLiftsTaken, 1, 2);

        // Its player rode it there without being put back. (Afterwards the guards at the top may shoot it, whose shove the
        // joiner's game takes late: only the ride is counted.)
        Assert.True(rideCorrections <= (jumps ? 1 : 0), $"the joiner's player was put back {rideCorrections} times on the ride");
        double height = Math.Abs(pair.Joiner.World.Player.Body.Position.Z - partner.Player.Body.Position.Z);
        Assert.True(height < 64, $"the joiner's player stands {height} units above or below where the host has it");

        // The host, riding too, was drawn standing on the joiner's car the whole way: not under its floor, not in its roof.
        if (hostRides)
        {
            Assert.True(
                highestHostShown - lowestHostShown < 256 && lowestHostShown > settled - 128,
                $"the joiner drew the host from {lowestHostShown:0} to {highestHostShown:0} units over its car's floor");
        }
    }

    private static Igi1CoopPair Joined(int mission)
    {
        Igi1CoopPair pair = new(mission: mission);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}

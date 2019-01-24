namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class FleetAdmiral
    {
        public TuningSettings TuningSettings;
        public MyPlayer MyPlayer;
        public Logger Logger;
        public OpponentPlayer[] Opponents;
        public int TotalTurns;
        public MapBooster MapBooster;
        public Action<MyShip, ShipRole> SetShipRole;
        public MacroEngine MacroEngine;
        public BitMapLayer ForbiddenCellsMap;

        public int TurnNumber;

        public int MaxDistance;
        public int ActiveDuration;
        public int RemainingTurns;
        public List<MyShip> Interceptors;

        public void Initialize()
        {
            MaxDistance = (MapBooster.MapWidth + MapBooster.MapHeight) / 2;
            ActiveDuration = (int)Math.Round(MaxDistance * 0.75d);
            Interceptors = new List<MyShip>(MyPlayer.MyShips.Count);
        }

        public void CommandInterceptors()
        {
            RemainingTurns = TotalTurns - TurnNumber;
            if (RemainingTurns > ActiveDuration)
            {
                return;
            }

            var simulationResult = MacroEngine.DecisionSimulationResult;
            if (simulationResult.MyPlayerStanding == 1)
            {
                var secondResult = simulationResult.GetPlayerResultByStanding(2);
                if (secondResult.Halite == 0)
                {
                    return;
                }

                double haliteRatio = simulationResult.MyPlayerResult.Halite / secondResult.Halite;
                if (haliteRatio > 1.25)
                {
                    Logger.LogInfo("FleetAdmiral: I'm standing too well to launch interceptors (" + simulationResult.MyPlayerResult + ").");
                    return;
                }
            }

            Logger.LogInfo("FleetAdmiral: Launching attack (" + simulationResult.MyPlayerResult + " vs " + simulationResult.WinnerPlayerResult + ")");

            var opponentsSorted = simulationResult.PlayerResultMap.Values
                .Where(result => result.Player != MyPlayer)
                .OrderByDescending(result => result.Halite)
                .Select(result => result.Player)
                .Cast<OpponentPlayer>()
                .ToArray();

            var targetsSorted = opponentsSorted
                .SelectMany(opponent => opponent.OpponentShips
                    .OrderByDescending(ship => ship.Halite))
                .Where(ship => ship.Halite > 250)
                .ToList();

            Logger.LogInfo("FleetAdmiral: Targets: " + string.Join(Environment.NewLine, targetsSorted));
            Interceptors.Clear();
            foreach (var ship in MyPlayer.MyShips)
            {
                if (ship.Role != ShipRole.Interceptor)
                {
                    if (!ShouldBeInterceptor(ship))
                    {
                        continue;
                    }

                    SetShipRole(ship, ShipRole.Interceptor);
                }

                Interceptors.Add(ship);
            }

            foreach (var target in targetsSorted)
            {
                int minDistance = int.MaxValue;
                MyShip closestShip = null;
                foreach (var ship in Interceptors)
                {
                    if (ship.InterceptorTarget != null)
                    {
                        continue;
                    }

                    int distance = MapBooster.Distance(ship.OriginPosition, target.Position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestShip = ship;
                    }
                }

                if (closestShip == null)
                {
                    break;
                }

                closestShip.InterceptorTarget = target;
                closestShip.Destination = target.ExpectedNextPosition ?? target.Position;
                closestShip.DistanceFromDestination = minDistance;
            }

            Logger.LogInfo("FleetAdmiral: Interceptors: " + string.Join(Environment.NewLine, Interceptors));

            var queue = new DoublePriorityQueue<Position>();
            var travelDistanceMap = new DataMapLayer<int>(MapBooster.MapWidth, MapBooster.MapHeight);
            for (int i = 0; i < Interceptors.Count; i++)
            {
                var ship = Interceptors[i];
                var target = ship.InterceptorTarget;
                if (target == null)
                {
                    Logger.LogInfo("FleetAdmiral: No target for " + ship + ", turning into harvester.");
                    SetShipRole(ship, ShipRole.Harvester);
                    Interceptors.RemoveAt(i);
                    i--;
                    continue;
                }

                Debug.Assert(ship.Destination.HasValue);
                ship.InterceptorNextPosition = null;
                foreach (var position in MapBooster.GetNeighbours(ship.OriginPosition))
                {
                    int distance = MapBooster.Distance(position, target.Position);
                    if (distance >= ship.DistanceFromDestination
                        || ForbiddenCellsMap[position])
                    {
                        continue;
                    }

                    Logger.LogInfo("FleetAdmiral: " + ship + " targeting " + target + " easily found next position in " + position + ".");
                    ship.InterceptorNextPosition = position;
                    break;
                }

                if (!ship.InterceptorNextPosition.HasValue)
                {
                    Logger.LogInfo("FleetAdmiral: " + ship + " targeting " + target + " must look hard for a path.");
                    bool pathFound = false;
                    int cellsVisited = 0;
                    int maxCellsToVisit = ship.DistanceFromDestination * 10;
                    queue.Clear();
                    travelDistanceMap.Fill(int.MaxValue);

                    travelDistanceMap[target.Position] = 0;
                    queue.Enqueue(0, target.Position);

                    while (queue.Count > 0)
                    {
                        cellsVisited++;
                        if (cellsVisited > maxCellsToVisit)
                        {
                            break;
                        }

                        double flightDistance = queue.PeekPriority();
                        var position = queue.Dequeue();
                        int travelDistance = travelDistanceMap[position];
                        int neighbourTravelDistance = travelDistance + 1;
                        foreach (var neighbour in MapBooster.GetNeighbours(position))
                        {
                            if (neighbour == ship.OriginPosition)
                            {
                                pathFound = true;
                                ship.InterceptorNextPosition = position;
                                break;
                            }

                            if (travelDistanceMap[neighbour] <= neighbourTravelDistance 
                                || ForbiddenCellsMap[neighbour])
                            {
                                continue;
                            }

                            travelDistanceMap[neighbour] = neighbourTravelDistance;
                            int neighbourFlightDistance = MapBooster.Distance(neighbour, ship.OriginPosition);
                            queue.Enqueue(neighbourFlightDistance, neighbour);
                        }

                        if (pathFound)
                        {
                            Logger.LogInfo("FleetAdmiral: " + ship + " targeting " + target + " found path.");
                            break;
                        }
                    }

                    if (!ship.InterceptorNextPosition.HasValue)
                    {
                        Logger.LogInfo("FleetAdmiral: " + ship + " targeting " + target + " did not find a path.");
                    }
                }
            }
        }

        private bool ShouldBeInterceptor(MyShip ship)
        {
            if (ship.DistanceFromDropoff > RemainingTurns + 1)
            {
                Logger.LogInfo("FleetAdmiral: Ship " + ship + " should be an interceptor because it is far off (" + ship.DistanceFromDropoff + " vs " + RemainingTurns + ")");
                return true;
            }

            if (ship.Role.IsHigherPriorityThan(ShipRole.Interceptor))
            {
                return false;
            }

            if (ship.Halite < 100)
            {
                return true;
            }

            return false;
        }
    }
}

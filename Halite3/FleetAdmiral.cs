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
        public Action<DataMapLayer<int>, string> PaintMap;

        public int TurnNumber;

        public int MaxDistance;
        public int ActiveDuration;
        public int RemainingTurns;
        public List<MyShip> Interceptors;

        public void Initialize()
        {
            MaxDistance = (MapBooster.MapWidth + MapBooster.MapHeight) / 2;
            ActiveDuration = (int)Math.Round(MaxDistance * 0.85d);
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
                    Logger.LogDebug("FleetAdmiral: I'm standing too well to launch interceptors (" + simulationResult.MyPlayerResult + ").");
                    return;
                }
            }

            Logger.LogDebug("FleetAdmiral: Launching attack (" + simulationResult.MyPlayerResult + " vs " + simulationResult.WinnerPlayerResult + ")");

            var opponentsSorted = simulationResult.PlayerResultMap.Values
                .Where(result => result.Player != MyPlayer)
                .OrderByDescending(result => result.Halite)
                .Select(result => result.Player)
                .Cast<OpponentPlayer>()
                .ToArray();

            var targetsSorted = opponentsSorted
                .SelectMany(opponent => opponent.OpponentShips
                    .OrderByDescending(ship => ship.Halite))
                .Where(ship => ship.Halite > 250 && ship.Owner.DistanceFromDropoffMap[ship.Position] > 2)
                .ToList();

            Logger.LogDebug("FleetAdmiral: Targets: " + string.Join(Environment.NewLine, targetsSorted));
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

            var dummyDisc = new Position[MapBooster.Calculator.GetDiscArea(2)];
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
                var targetLocation = target.ExpectedNextPosition ?? target.Position;
                int bestDropoffDistance = int.MaxValue;
                Position bestPosition = default(Position);
                MapBooster.Calculator.GetDiscCells(targetLocation, 2, dummyDisc);
                foreach (var discPosition in dummyDisc)
                {
                    if (MapBooster.Calculator.MaxSingleDimensionDistance(discPosition, targetLocation) == 1)
                    {
                        int dropoffDistance = target.Owner.DistanceFromDropoffMap[discPosition];
                        if (dropoffDistance < bestDropoffDistance)
                        {
                            bestDropoffDistance = dropoffDistance;
                            bestPosition = discPosition;
                        }
                    }
                }

                if (bestDropoffDistance != 0)
                {
                    closestShip.Destination = bestPosition;
                }
                else
                {
                    closestShip.Destination = targetLocation;
                }

                closestShip.DistanceFromDestination = MapBooster.Distance(closestShip.Destination.Value, closestShip.Position);
            }

            Logger.LogDebug("FleetAdmiral: Interceptors: " + string.Join(Environment.NewLine, Interceptors));

            var queue = new DoublePriorityQueue<Position>();
            var travelDistanceMap = new DataMapLayer<int>(MapBooster.MapWidth, MapBooster.MapHeight);
            for (int i = 0; i < Interceptors.Count; i++)
            {
                var ship = Interceptors[i];
                var target = ship.InterceptorTarget;
                if (target == null)
                {
                    Logger.LogDebug("FleetAdmiral: No target for " + ship + ", turning into harvester.");
                    SetShipRole(ship, ShipRole.Harvester);
                    Interceptors.RemoveAt(i);
                    i--;
                    continue;
                }

                Debug.Assert(ship.Destination.HasValue);
                var destination = ship.Destination.Value;
                ship.InterceptorNextPosition = null;
                int bestAntiStuff = int.MaxValue;
                int bestDistance = int.MaxValue;
                var bestPosition = default(Position);
                foreach (var position in MapBooster.GetNeighbours(ship.OriginPosition))
                {
                    int distance = MapBooster.Distance(position, destination);
                    if (MyPlayer.MyShipMap[position] != null)
                    {
                        continue;
                    }

                    if (distance > bestDistance)
                    {
                        continue;
                    }

                    int antiStuff = MapBooster.Calculator.MaxSingleDimensionDistance(position, destination);
                    if (distance < bestDistance || antiStuff < bestAntiStuff)
                    {
                        bestDistance = distance;
                        bestPosition = position;
                        bestAntiStuff = antiStuff;
                    }
                }

                if (bestDistance != int.MaxValue)
                {
                    ship.InterceptorNextPosition = bestPosition;
                    Logger.LogDebug("FleetAdmiral: " + ship + " targeting " + target + " easily found next position in " + bestPosition + ".");
                }

                if (!ship.InterceptorNextPosition.HasValue)
                {
                    Logger.LogDebug("FleetAdmiral: " + ship + " targeting " + target + " must look hard for a path.");
                    bool pathFound = false;
                    int cellsVisited = 0;
                    int maxCellsToVisit = ship.DistanceFromDestination * 10;
                    queue.Clear();
                    travelDistanceMap.Fill(int.MaxValue);

                    travelDistanceMap[destination] = 0;
                    queue.Enqueue(0, destination);
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

                            if (travelDistanceMap[neighbour] <= neighbourTravelDistance)
                            {
                                continue;
                            }

                            if (ForbiddenCellsMap[neighbour] && flightDistance > 1)
                            {
                                continue;
                            }

                            travelDistanceMap[neighbour] = neighbourTravelDistance;
                            int neighbourFlightDistance = MapBooster.Distance(neighbour, ship.OriginPosition);
                            queue.Enqueue(neighbourFlightDistance, neighbour);
                        }

                        if (pathFound)
                        {
                            Logger.LogDebug("FleetAdmiral: " + ship + " targeting " + target + " found path.");
                            break;
                        }
                    }

                    //PaintMap(travelDistanceMap, "travelDistanceMap" + TurnNumber.ToString().PadLeft(3, '0') + "_" + ship.Id);
                    if (!ship.InterceptorNextPosition.HasValue)
                    {
                        Logger.LogDebug("FleetAdmiral: " + ship + " targeting " + target + " did not find a path.");
                    }
                }
            }
        }

        private bool ShouldBeInterceptor(MyShip ship)
        {
            if (ship.DistanceFromDropoff > RemainingTurns + 1)
            {
                Logger.LogDebug("FleetAdmiral: Ship " + ship + " should be an interceptor because it is far off (" + ship.DistanceFromDropoff + " vs " + RemainingTurns + ")");
                return true;
            }

            if (ship.Role.IsHigherPriorityThan(ShipRole.Interceptor))
            {
                return false;
            }

            if (ship.Halite < 80)
            {
                return true;
            }

            return false;
        }
    }
}

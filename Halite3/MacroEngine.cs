﻿namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class MacroEngine
    {
        public TuningSettings TuningSettings;
        public MyPlayer MyPlayer;
        public GameSimulator Simulator;
        public ExpansionMap ExpansionMap;
        public Logger Logger;
        public MapBooster MapBooster;
        public int TotalTurns;
        public Action<BitMapLayer, string> PaintMap;

        public ExpansionMap.DropoffAreaInfo BestDropoffArea;
        public int TurnNumber;
        public GameSimulator.SimulationResult DecisionSimulationResult;

        public Decision MakeDecision(int turnNumber)
        {
            TurnNumber = turnNumber;

            var normalResult = RunSimulation("");
            var oneShipResult = RunSimulation("s");
            if (oneShipResult.IsBetterThan(normalResult))
            {
                BestDropoffArea = FindBestDropoffLocation();
                if (BestDropoffArea != null)
                {
                    var multipleShipResult = RunSimulation("ssss");
                    var dropoffResult = RunSimulation("d");

                    if (dropoffResult.IsBetterThan(oneShipResult)
                        && dropoffResult.IsBetterThan(multipleShipResult))
                    {
                        DecisionSimulationResult = dropoffResult;
                        return new Decision()
                        {
                            BuildDropoff = true,
                            DropoffAreaInfo = BestDropoffArea,
                            BuildShip = (MyPlayer.Halite >= GameConstants.ShipCost + GameConstants.DropoffCost)
                        };
                    }
                }
            }
            else
            {
                DecisionSimulationResult = normalResult;
                return new Decision();
            }

            DecisionSimulationResult = oneShipResult;
            return new Decision()
            {
                BuildShip = true
            };
        }

        private GameSimulator.SimulationResult RunSimulation(string events)
        {
            var eventList = new List<GameSimulator.PlayerEvent>(events.Length);
            var scheduler = new EventScheduler(this);
            foreach (char eventCharacter in events)
            {
                switch (eventCharacter)
                {
                    case 's':
                        int shipTurnNumber = scheduler.GetShipTurnNumber();
                        var shipEvent = Simulator.GetMyPlayerBuildShipEvent(shipTurnNumber);
                        eventList.Add(shipEvent);
                        break;
                    case 'd':
                        int builderDispatchTurn = scheduler.TurnNumber;
                        bool builderFound = FindClosestShip(BestDropoffArea.CenterPosition, out var builder, out var builderDistance);
                        Debug.Assert(builderFound);
                        int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance, true);
                        int baseStartupDelay = (int)(TuningSettings.MacroEngineDropoffStartupDelayMaxDistanceMultiplier * Simulator.MaxDistance);
                        int startupDelay = (dropoffTurnNumber - builderDispatchTurn) + baseStartupDelay;
                        var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(builderDispatchTurn, startupDelay, BestDropoffArea.CenterPosition);
                        eventList.Add(dropoffEventPair.Item1);
                        eventList.Add(dropoffEventPair.Item2);
                        break;
                    default:
                        continue;
                }
            }

            var result = Simulator.RunSimulation(TurnNumber, eventList.ToArray());
            Logger.LogDebug("RunSimulation(" + events + "): " + result);
            return result;
        }

        private ExpansionMap.DropoffAreaInfo FindBestDropoffLocation()
        {
            GameSimulator.SimulationResult bestResult = null;
            ExpansionMap.DropoffAreaInfo bestArea = null;
            foreach (var dropoffAreaInfoCandidate in ExpansionMap.BestDropoffAreaCandidates)
            {
                if (!FindClosestShip(dropoffAreaInfoCandidate.CenterPosition, out var builder, out var builderDistance))
                {
                    break;
                }

                Debug.Assert(builder != null);
                var scheduler = new EventScheduler(this);
                int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance);
                var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(TurnNumber, dropoffTurnNumber - TurnNumber, dropoffAreaInfoCandidate.CenterPosition);
                var currentResult = Simulator.RunSimulation(TurnNumber, dropoffEventPair.Item1, dropoffEventPair.Item2);
                if (bestResult == null || currentResult.IsBetterThan(bestResult))
                {
                    bestResult = currentResult;
                    bestArea = dropoffAreaInfoCandidate;
                }
            }

            return bestArea;
        }

        private bool FindClosestShip(Position dropoffPosition, out MyShip ship, out int distance)
        {
            if (MyPlayer.MyShips.Count == 0)
            {
                ship = null;
                distance = int.MaxValue;
                return false;
            }

            MyShip closestShip = null;
            int closestShipDistance = int.MaxValue;
            foreach (var shipCandidate in MyPlayer.MyShips)
            {
                int candidateDistance = MapBooster.Distance(shipCandidate.Position, dropoffPosition);
                if (candidateDistance < closestShipDistance)
                {
                    closestShipDistance = candidateDistance;
                    closestShip = shipCandidate;
                }
            }

            ship = closestShip;
            distance = closestShipDistance;
            return true;
        }

        public class Decision
        {
            public bool BuildShip;
            public bool BuildDropoff;
            public ExpansionMap.DropoffAreaInfo DropoffAreaInfo;
        }

        private class EventScheduler
        {
            private readonly MacroEngine macroEngine;

            public int MyHalite;
            public int TurnNumber;

            public EventScheduler(MacroEngine macroEngine)
            {
                this.macroEngine = macroEngine;
                MyHalite = this.macroEngine.MyPlayer.Halite;
                int haliteSoonGained = this.macroEngine.MyPlayer.MyShips
                    .Where(ship => ship.Role == ShipRole.Inbound 
                        && ship.Destination.HasValue 
                        && ship.DistanceFromDestination <= 2)
                    .Sum(ship => ship.Halite);
                MyHalite += haliteSoonGained;
                TurnNumber = this.macroEngine.TurnNumber;
            }

            public int GetShipTurnNumber()
            {
                int delay = GetCostDelay(GameConstants.ShipCost);
                TurnNumber += delay;
                return TurnNumber;
            }

            public int GetDropoffTurnNumber(int distance, bool onlyForwardToCostDelay = false)
            {
                int costDelay = GetCostDelay(GameConstants.DropoffCost);
                int distanceDelay = distance;
                int delay = Math.Max(costDelay, distanceDelay);
                if (onlyForwardToCostDelay)
                {
                    int result = TurnNumber + delay;
                    TurnNumber += costDelay;
                    return result;
                }
                else
                {
                    TurnNumber += delay;
                    if (distanceDelay > costDelay)
                    {
                        int difference = distanceDelay - costDelay;
                        MyHalite += (int)(difference * macroEngine.MyPlayer.AverageIncomePerTurn);
                    }

                    return TurnNumber;
                }
            }

            private int GetCostDelay(int cost)
            {
                double profitPerTurn = macroEngine.MyPlayer.AverageIncomePerTurn;
                int haliteMissing = Math.Max(cost - MyHalite, 0);
                
                int delay = (haliteMissing == 0)
                    ? 0
                    : (profitPerTurn == 0) 
                        ? 1000
                        : (int)Math.Ceiling(haliteMissing / profitPerTurn);

                MyHalite += (int)(delay * profitPerTurn) - cost;
                return delay;
            }
        }
    }
}

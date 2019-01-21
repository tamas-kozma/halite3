namespace Halite3
{
    using System;
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

        public int TurnNumber;
        public GameSimulator.SimulationResult DecisionSimulationResult;

        public Decision MakeDecision(int turnNumber)
        {
            TurnNumber = turnNumber;

            var decision = new Decision();

            var normalSimulationResult = Simulator.RunSimulation(TurnNumber);
            Logger.LogInfo("normal: " + normalSimulationResult);

            if (!GetShipOnlyResult(out var shipOnlyResult))
            {
                DecisionSimulationResult = normalSimulationResult;
                return decision;
            }

            Logger.LogInfo("ship only: " + shipOnlyResult);
            if (normalSimulationResult.IsBetterThan(shipOnlyResult))
            {
                DecisionSimulationResult = normalSimulationResult;
                return decision;
            }

            PaintMap(Simulator.VisitedCells, "shipOnlyResultVisted" + TurnNumber.ToString().PadLeft(3, '0'));
            if (!FindBestDropoffSecondResult(out var dropoffAreaInfo, out var shipThenDropoffResult))
            {
                DecisionSimulationResult = shipOnlyResult;
                decision.BuildShip = true;
                return decision;
            }

            PaintMap(Simulator.VisitedCells, "shipThenDropoffResultVisted" + TurnNumber.ToString().PadLeft(3, '0'));

            var dropoffThenShipResult = GetDropoffFirstResult(dropoffAreaInfo);
            Logger.LogInfo("dropoff candidate =" + dropoffAreaInfo.CenterPosition);
            Logger.LogInfo("shipThenDropoffResult: " + shipThenDropoffResult);
            Logger.LogInfo("dropoffThenShipResult: " + dropoffThenShipResult);
            if ((shipOnlyResult.IsBetterThan(shipThenDropoffResult) && shipOnlyResult.IsBetterThan(dropoffThenShipResult))
                || shipThenDropoffResult.IsBetterThan(dropoffThenShipResult))
            {
                DecisionSimulationResult = shipOnlyResult;
                decision.BuildShip = true;
                return decision;
            }

            decision.BuildDropoff = true;
            decision.DropoffAreaInfo = dropoffAreaInfo;
            decision.BuildShip = (MyPlayer.Halite >= GameConstants.ShipCost + GameConstants.DropoffCost);
            DecisionSimulationResult = dropoffThenShipResult;
            return decision;
        }

        private bool GetShipOnlyResult(out GameSimulator.SimulationResult result)
        {
            var scheduler = new EventScheduler(this);
            int shipTurnNumber = scheduler.GetShipTurnNumber();
            if (shipTurnNumber >= TotalTurns)
            {
                result = null;
                return false;
            }

            var shipEvent = Simulator.GetMyPlayerBuildShipEvent(shipTurnNumber);
            result = Simulator.RunSimulation(TurnNumber, shipEvent);
            return true;
        }

        private GameSimulator.SimulationResult GetDropoffFirstResult(ExpansionMap.DropoffAreaInfo dropoffAreaInfo)
        {
            bool builderFound = FindClosestShip(dropoffAreaInfo.CenterPosition, out var builder, out var builderDistance);
            Debug.Assert(builderFound);

            var scheduler = new EventScheduler(this);
            int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance, true);
            int firstShipTurnNumber = scheduler.GetShipTurnNumber();
            int secondShipTurnNumber = scheduler.GetShipTurnNumber();
            var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(TurnNumber, dropoffTurnNumber - TurnNumber, dropoffAreaInfo.CenterPosition);
            var firstShipEvent = Simulator.GetMyPlayerBuildShipEvent(firstShipTurnNumber);
            var secondShipEvent = Simulator.GetMyPlayerBuildShipEvent(secondShipTurnNumber);
            return Simulator.RunSimulation(TurnNumber, dropoffEventPair.Item1, dropoffEventPair.Item2, firstShipEvent, secondShipEvent);
        }

        private bool FindBestDropoffSecondResult(out ExpansionMap.DropoffAreaInfo dropoffAreaInfo, out GameSimulator.SimulationResult result)
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
                int firstShipTurnNumber = scheduler.GetShipTurnNumber();
                int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance, true);
                int secondShipTurnNumber = scheduler.GetShipTurnNumber();
                var firstShipEvent = Simulator.GetMyPlayerBuildShipEvent(firstShipTurnNumber);
                var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(TurnNumber, dropoffTurnNumber - TurnNumber, dropoffAreaInfoCandidate.CenterPosition);
                var secondShipEvent = Simulator.GetMyPlayerBuildShipEvent(secondShipTurnNumber);
                var currentResult = Simulator.RunSimulation(TurnNumber, firstShipEvent, dropoffEventPair.Item1, dropoffEventPair.Item2, secondShipEvent);
                if (bestResult == null || currentResult.IsBetterThan(bestResult))
                {
                    bestResult = currentResult;
                    bestArea = dropoffAreaInfoCandidate;
                }
            }

            result = bestResult;
            dropoffAreaInfo = bestArea;
            return (result != null);
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
            private int myHalite;
            private int turnNumber;

            public EventScheduler(MacroEngine macroEngine)
            {
                this.macroEngine = macroEngine;
                myHalite = this.macroEngine.MyPlayer.Halite;
                int haliteSoonGained = this.macroEngine.MyPlayer.MyShips
                    .Where(ship => ship.Role == ShipRole.Inbound 
                        && ship.Destination.HasValue 
                        && ship.DistanceFromDestination <= 2)
                    .Sum(ship => ship.Halite);
                myHalite += haliteSoonGained;
                turnNumber = this.macroEngine.TurnNumber;
            }

            public int GetShipTurnNumber()
            {
                int delay = GetCostDelay(GameConstants.ShipCost);
                turnNumber += delay;
                return turnNumber;
            }

            public int GetDropoffTurnNumber(int distance, bool onlyForwardToCostDelay = false)
            {
                int costDelay = GetCostDelay(GameConstants.DropoffCost);
                int distanceDelay = distance;
                int delay = Math.Max(costDelay, distanceDelay);
                if (onlyForwardToCostDelay)
                {
                    int result = turnNumber + delay;
                    turnNumber += costDelay;
                    return result;
                }
                else
                {
                    turnNumber += delay;
                    if (distanceDelay > costDelay)
                    {
                        int difference = distanceDelay - costDelay;
                        myHalite += (int)(difference * macroEngine.MyPlayer.AverageIncomePerTurn);
                    }

                    return turnNumber;
                }
            }

            private int GetCostDelay(int cost)
            {
                double profitPerTurn = macroEngine.MyPlayer.AverageIncomePerTurn;
                int haliteMissing = Math.Max(cost - myHalite, 0);
                
                int delay = (haliteMissing == 0)
                    ? 0
                    : (profitPerTurn == 0) 
                        ? 1000
                        : (int)Math.Ceiling(haliteMissing / profitPerTurn);

                myHalite += (int)(delay * profitPerTurn) - cost;
                return delay;
            }
        }
    }
}

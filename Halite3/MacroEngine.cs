namespace Halite3
{
    using System;
    using System.Diagnostics;

    public sealed class MacroEngine
    {
        public TuningSettings TuningSettings;
        public MyPlayer MyPlayer;
        public GameSimulator Simulator;
        public ExpansionMap ExpansionMap;
        public Logger Logger;
        public MapBooster MapBooster;
        public int TotalTurns;

        public int TurnNumber;
        public GameSimulator.SimulationResult DecisionSimulationResult;

        public Decision MakeDecision(int turnNumber)
        {
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

            if (!FindBestDropoffSecondResult(out var dropoffPosition, out var shipThenDropoffResult))
            {
                DecisionSimulationResult = shipOnlyResult;
                decision.BuildShip = true;
                return decision;
            }

            var dropoffThenShipResult = GetDropoffFirstResult(dropoffPosition);
            if ((shipOnlyResult.IsBetterThan(shipThenDropoffResult) && shipOnlyResult.IsBetterThan(dropoffThenShipResult))
                || shipThenDropoffResult.IsBetterThan(dropoffThenShipResult))
            {
                DecisionSimulationResult = shipOnlyResult;
                decision.BuildShip = true;
                return decision;
            }

            decision.BuildDropoff = true;
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

        private GameSimulator.SimulationResult GetDropoffFirstResult(Position dropoffPosition)
        {
            bool builderFound = FindClosestShip(dropoffPosition, out var builder, out var builderDistance);
            Debug.Assert(builderFound);

            var scheduler = new EventScheduler(this);
            int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance, true);
            int shipTurnNumber = scheduler.GetShipTurnNumber();
            var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(TurnNumber, dropoffTurnNumber - TurnNumber, dropoffPosition);
            var shipEvent = Simulator.GetMyPlayerBuildShipEvent(shipTurnNumber);
            return Simulator.RunSimulation(TurnNumber, dropoffEventPair.Item1, dropoffEventPair.Item2, shipEvent);
        }

        private bool FindBestDropoffSecondResult(out Position dropoffPosition, out GameSimulator.SimulationResult result)
        {
            GameSimulator.SimulationResult bestResult = null;
            Position bestPosition = default(Position);
            foreach (var dropoffAreaCenterPosition in ExpansionMap.BestDropoffAreaCandidateCenters)
            {
                if (!FindClosestShip(dropoffAreaCenterPosition, out var builder, out var builderDistance))
                {
                    break;
                }

                Debug.Assert(builder != null);
                var scheduler = new EventScheduler(this);
                int shipTurnNumber = scheduler.GetShipTurnNumber();
                int dropoffTurnNumber = scheduler.GetDropoffTurnNumber(builderDistance);
                var shipEvent = Simulator.GetMyPlayerBuildShipEvent(shipTurnNumber);
                var dropoffEventPair = Simulator.GetMyPlayerBuildDropoffEvent(TurnNumber, dropoffTurnNumber - TurnNumber, dropoffAreaCenterPosition);
                var currentResult = Simulator.RunSimulation(TurnNumber, shipEvent, dropoffEventPair.Item1, dropoffEventPair.Item2);
                if (bestResult == null || currentResult.IsBetterThan(bestResult))
                {
                    bestResult = currentResult;
                    bestPosition = dropoffAreaCenterPosition;
                }
            }

            result = bestResult;
            dropoffPosition = bestPosition;
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
            public Position DropoffLocation;
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
                        myHalite += (int)(difference * macroEngine.MyPlayer.AverageProfitPerTurn);
                    }

                    return turnNumber;
                }
            }

            private int GetCostDelay(int cost)
            {
                double profitPerTurn = macroEngine.MyPlayer.AverageProfitPerTurn;
                int haliteMissing = Math.Max(cost - myHalite, 0);
                
                int delay = (haliteMissing > 0 && profitPerTurn == 0) 
                    ? 1000
                    : (int)Math.Ceiling(haliteMissing / profitPerTurn);

                myHalite += (int)(delay * profitPerTurn) - cost;
                return delay;
            }
        }
    }
}

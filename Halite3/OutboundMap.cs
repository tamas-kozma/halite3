﻿namespace Halite3
{
    using System;

    public sealed class OutboundMap
    {
        public TuningSettings TuningSettings { get; set; }
        public AdjustedHaliteMap AdjustedHaliteMap { get; set; }
        public MyPlayer MyPlayer { get; set; }
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }
        public bool IsEarlyGameMap { get; set; }
        public ReturnMap ReturnMap { get; set; }

        public static double[] EstimatedHarvestTimes;
        public DataMapLayer<double> DiscAverageLayer { get; private set; }
        public DataMapLayer<double> HarvestTimeMap { get; private set; }
        public DataMapLayer<double> OutboundPaths { get; private set; }

        public void Calculate()
        {
            CalculateEstimatedHarvestTimes();
            CalculateHarvestTimeMap();
            CalculateOutboundPaths();
        }

        private void CalculateOutboundPaths()
        {
            int mapWidth = HarvestTimeMap.Width;
            int mapHeight = HarvestTimeMap.Height;
            var outboundPaths = new DataMapLayer<double>(mapWidth, mapHeight);
            OutboundPaths = outboundPaths;
            outboundPaths.Fill(double.MaxValue);
            var forbiddenCellsMap = ForbiddenCellsMap;

            var harvestTimeMap = HarvestTimeMap;
            int estimatedMaxQueueSize = (int)(harvestTimeMap.CellCount * Math.Log(harvestTimeMap.CellCount));
            var queue = new DoublePriorityQueue<Position>(estimatedMaxQueueSize);
            var returnDistanceMap = ReturnMap.CellData;
            foreach (var position in harvestTimeMap.AllPositions)
            {
                if (forbiddenCellsMap[position])
                {
                    continue;
                }

                double harvestTime = harvestTimeMap[position];
                double returnTime = returnDistanceMap[position].Distance;
                queue.Enqueue(harvestTime + returnTime, position);
            }

            var mapBooster = MapBooster;
            var distanceFromDropoffMap = MyPlayer.DistanceFromDropoffMap;
            int cellCount = outboundPaths.CellCount;
            int cellsAssigned = 0;
            double stepPenaltyMultiplier = (IsEarlyGameMap) 
                ? TuningSettings.OutboundMapEarlyGamePathStepPenaltyMultiplier
                : TuningSettings.OutboundMapPathStepPenaltyMultiplier;

            // Plus one because I check it only on the source cell.
            int outboundMapDropoffAvoidanceRadius = TuningSettings.OutboundMapDropoffAvoidanceRadius + 1;
            double outboundDistanceOnOneTank = GameConstants.ExtractRatio / GameConstants.MoveCostRatio;
            double outboundPathFuelPenaltyMultiplier = (1d + outboundDistanceOnOneTank) / outboundDistanceOnOneTank;
            double outboundStepTime = outboundPathFuelPenaltyMultiplier;
            while (queue.Count > 0)
            {
                double newTime = queue.PeekPriority();
                var position = queue.Dequeue();
                double oldTime = outboundPaths[position];
                if (newTime >= oldTime)
                {
                    continue;
                }

                int distanceFromDropoff = distanceFromDropoffMap[position];
                bool isCloseToDropoff = (distanceFromDropoff < outboundMapDropoffAvoidanceRadius);

                outboundPaths[position] = newTime;
                cellsAssigned++;

                // With uniform edge costs, all cells are visited exactly once.
                if (cellsAssigned == cellCount)
                {
                    // TODO
                    //break;
                }

                double nextTime = newTime + outboundStepTime;
                var neighbourArray = mapBooster.GetNeighbours(position.Row, position.Column);
                foreach (var neighbour in neighbourArray)
                {
                    double neighbourTime = outboundPaths[neighbour];
                    if (nextTime >= neighbourTime || forbiddenCellsMap[neighbour])
                    {
                        continue;
                    }

                    if (isCloseToDropoff)
                    {
                        int neighbourDistanceFromDropoff = distanceFromDropoffMap[neighbour];
                        if (neighbourDistanceFromDropoff > distanceFromDropoff)
                        {
                            continue;
                        }
                    }

                    queue.Enqueue(nextTime, neighbour);
                }
            }

            Logger.LogInfo("CellsAssigned" + cellsAssigned);
        }

        private void CalculateHarvestTimeMap()
        {
            var mapBooster = MapBooster;
            var adjustedHaliteValues = AdjustedHaliteMap.Values;
            int mapWidth = adjustedHaliteValues.Width;
            int mapHeight = adjustedHaliteValues.Height;
            var discAverageLayer = new DataMapLayer<double>(mapWidth, mapHeight);
            DiscAverageLayer = discAverageLayer;
            int discArea = adjustedHaliteValues.GetDiscArea(TuningSettings.OutboundMapHarvestAreaSmoothingRadius);
            for (int row = 0; row < mapHeight; row++)
            {
                for (int column = 0; column < mapWidth; column++)
                {
                    var discPositions = mapBooster.GetOutboundMapHarvestAreaSmoothingDisc(row, column);

                    double discSum = 0;
                    foreach (var discPosition in discPositions)
                    {
                        discSum += adjustedHaliteValues[discPosition];
                    }

                    discAverageLayer[row, column] = discSum / discArea;
                }
            }

            double centerWeight = TuningSettings.OutboundMapHarvestAreaCenterWeight;
            double centerWeightPlusOne = centerWeight + 1d;
            var harvestAreaMap = new DataMapLayer<double>(mapWidth, mapHeight);
            HarvestTimeMap = harvestAreaMap;
            foreach (var position in HarvestTimeMap.AllPositions)
            {
                double valueAtCell = adjustedHaliteValues[position];
                double averageAtCell = DiscAverageLayer[position];
                double weightedAverage = (valueAtCell >= averageAtCell)
                    ? (valueAtCell * centerWeight + averageAtCell) / centerWeightPlusOne
                    : 0;

                int intHalite = (int)weightedAverage;
                harvestAreaMap[position] = EstimatedHarvestTimes[intHalite];
            }

            foreach (var dropoff in MyPlayer.Dropoffs)
            {
                harvestAreaMap[dropoff.Position] = double.MaxValue;
            }
        }

        private void CalculateEstimatedHarvestTimes()
        {
            if (EstimatedHarvestTimes != null)
            {
                return;
            }

            int maxHalite = (int)Math.Ceiling(TuningSettings.AdjustedHaliteMapMaxHalite);
            EstimatedHarvestTimes = new double[maxHalite + 1];
            EstimatedHarvestTimes[0] = double.MaxValue;
            double moveThresholdRatio = TuningSettings.HarvesterMoveThresholdHaliteRatio;
            double turnsHarvestingAtOneCell = Math.Log(moveThresholdRatio, 1d - GameConstants.ExtractRatio);
            for (int halite = 1; halite <= maxHalite; halite++)
            {
                double haliteLeftAtCell = halite * moveThresholdRatio;
                double haliteGatheredFromOneCell = halite - haliteLeftAtCell;
                double haliteGatheredFromOneCellMinusMoveCost = haliteGatheredFromOneCell - (haliteLeftAtCell * GameConstants.MoveCostRatio);
                double harvestTime = (GameConstants.ShipCapacity / haliteGatheredFromOneCellMinusMoveCost) * (turnsHarvestingAtOneCell + 1d);
                EstimatedHarvestTimes[halite] = harvestTime;
            }
        }
    }
}

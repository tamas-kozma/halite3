namespace Halite3
{
    using System;
    using System.Diagnostics;

    public sealed class OutboundMap
    {
        private static readonly int InitialFillStepSize = 25;

        public TuningSettings TuningSettings { get; set; }
        public AdjustedHaliteMap AdjustedHaliteMap { get; set; }
        public MyPlayer MyPlayer { get; set; }
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }
        public bool IsEarlyGameMap { get; set; }
        public ReturnMap ReturnMap { get; set; }

        public static double[][] EstimatedHarvestTimes;
        public DataMapLayer<double> DiscAverageLayer { get; private set; }
        public DataMapLayer<double> HarvestTimeMap { get; private set; }
        public DataMapLayer<double> OutboundPaths { get; private set; }

        public void Calculate()
        {
            CalculateEstimatedHarvestTimes();
            CalculateHarvestTimeMap();
            CalculateOutboundPaths();
        }

        public double GetEstimatedJobTimeInNeighbourhood(Position center, int initialFill)
        {
            var disc = new Position[HarvestTimeMap.GetDiscArea(1)];
            HarvestTimeMap.GetDiscCells(center, 1, disc);
            double bestTime = -1d;
            foreach (var position in disc)
            {
                if (!ForbiddenCellsMap[position])
                {
                    continue;
                }

                var returnMapCellInfo = ReturnMap.CellData[position];
                double haliteLostOnReturn = GameConstants.MoveCostRatio * returnMapCellInfo.SumHalite * TuningSettings.AdjustedHaliteMapLostHaliteMultiplier;
                double returnedHalite = GameConstants.ShipCapacity - haliteLostOnReturn;
                if (returnedHalite <= 0)
                {
                    continue;
                }

                double lostHaliteMultiplier = GameConstants.ShipCapacity / returnedHalite;
                Debug.Assert(lostHaliteMultiplier >= 1d);

                int halite = (int)AdjustedHaliteMap.Values[position];
                int fillCategory = initialFill / InitialFillStepSize;
                if (fillCategory > EstimatedHarvestTimes.Length)
                {
                    return 1d;
                }

                double baseHarvestTime = EstimatedHarvestTimes[fillCategory][halite];
                double harvestTime = baseHarvestTime * lostHaliteMultiplier;
                double returnTime = returnMapCellInfo.Distance * lostHaliteMultiplier;
                double jobTime = harvestTime + returnTime;
                if (jobTime < bestTime)
                {
                    bestTime = jobTime;
                }
            }

            return bestTime;
        }

        private double GetBaseOutboundStepTime()
        {
            double outboundDistanceOnOneTank = GameConstants.ExtractRatio / GameConstants.MoveCostRatio;
            double outboundPathFuelPenaltyMultiplier = (1d + outboundDistanceOnOneTank) / outboundDistanceOnOneTank;
            return outboundPathFuelPenaltyMultiplier;
        }

        private void CalculateOutboundPaths()
        {
            int mapWidth = HarvestTimeMap.Width;
            int mapHeight = HarvestTimeMap.Height;
            var outboundPaths = new DataMapLayer<double>(mapWidth, mapHeight);
            OutboundPaths = outboundPaths;
            outboundPaths.Fill(double.MaxValue);
            var forbiddenCellsMap = ForbiddenCellsMap;

            double baseOutboundStepTime = GetBaseOutboundStepTime();
            var harvestTimeMap = HarvestTimeMap;
            int estimatedMaxQueueSize = (int)(harvestTimeMap.CellCount * Math.Log(harvestTimeMap.CellCount));
            var queue = new DoublePriorityQueue<PositionWithStepTime>(estimatedMaxQueueSize);
            var returnDistanceMap = ReturnMap.CellData;
            int forbiddenCellCount = 0;
            foreach (var position in harvestTimeMap.AllPositions)
            {
                double baseHarvestTime = harvestTimeMap[position];
                if (forbiddenCellsMap[position])
                {
                    forbiddenCellCount++;
                    continue;
                }

                if (baseHarvestTime == double.MaxValue)
                {
                    continue;
                }

                var returnMapCellInfo = returnDistanceMap[position];
                double haliteLostOnReturn = GameConstants.MoveCostRatio * returnMapCellInfo.SumHalite * TuningSettings.AdjustedHaliteMapLostHaliteMultiplier;
                double returnedHalite = GameConstants.ShipCapacity - haliteLostOnReturn;
                if (returnedHalite <= 0)
                {
                    continue;
                }

                double lostHaliteMultiplier = GameConstants.ShipCapacity / returnedHalite;
                Debug.Assert(lostHaliteMultiplier >= 1d);
                double harvestTime = baseHarvestTime * lostHaliteMultiplier;
                double returnTime = returnMapCellInfo.Distance * lostHaliteMultiplier;
                double outboundStepTime = baseOutboundStepTime * lostHaliteMultiplier;
                queue.Enqueue(harvestTime + returnTime, new PositionWithStepTime(position, outboundStepTime));
            }

            var mapBooster = MapBooster;
            var distanceFromDropoffMap = MyPlayer.DistanceFromDropoffMap;
            int cellCount = outboundPaths.CellCount;
            int maxAssignedCellCount = cellCount - forbiddenCellCount;
            int cellsAssigned = 0;
            double stepPenaltyMultiplier = (IsEarlyGameMap) 
                ? TuningSettings.OutboundMapEarlyGamePathStepPenaltyMultiplier
                : TuningSettings.OutboundMapPathStepPenaltyMultiplier;

            // Plus one because I check it only on the source cell.
            int outboundMapDropoffAvoidanceRadius = TuningSettings.OutboundMapDropoffAvoidanceRadius + 1;
            while (queue.Count > 0)
            {
                double newTime = queue.PeekPriority();
                var positionWithStepTime = queue.Dequeue();
                var position = positionWithStepTime.Position;
                double oldTime = outboundPaths[position];
                if (newTime >= oldTime)
                {
                    continue;
                }

                int distanceFromDropoff = distanceFromDropoffMap[position];
                bool isCloseToDropoff = (distanceFromDropoff < outboundMapDropoffAvoidanceRadius);

                outboundPaths[position] = newTime;
                cellsAssigned++;

                if (cellsAssigned == maxAssignedCellCount)
                {
                    break;
                }

                double nextTime = newTime + positionWithStepTime.OutboundStepTime;
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

                    queue.Enqueue(nextTime, new PositionWithStepTime(neighbour, positionWithStepTime.OutboundStepTime));
                }
            }
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
            var emptyEstimatedHarvestTimes = EstimatedHarvestTimes[0];
            foreach (var position in HarvestTimeMap.AllPositions)
            {
                double valueAtCell = adjustedHaliteValues[position];
                double averageAtCell = discAverageLayer[position];
                double weightedAverage = (valueAtCell >= averageAtCell)
                    ? (valueAtCell * centerWeight + averageAtCell) / centerWeightPlusOne
                    : 0;

                int intHalite = (int)weightedAverage;
                harvestAreaMap[position] = emptyEstimatedHarvestTimes[intHalite];
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
            int stepCount = GameConstants.ShipCapacity / InitialFillStepSize;
            double moveThresholdRatio = TuningSettings.HarvesterMoveThresholdHaliteRatio;
            double turnsHarvestingAtOneCell = Math.Log(moveThresholdRatio, 1d - GameConstants.ExtractRatio);
            EstimatedHarvestTimes = new double[stepCount][];
            for (int i = 0; i < stepCount; i++)
            {
                EstimatedHarvestTimes[i] = new double[maxHalite + 1];
                int initialFill = i * InitialFillStepSize;
                int remainingCapacity = GameConstants.ShipCapacity - initialFill;
                EstimatedHarvestTimes[i][0] = double.MaxValue;
                for (int halite = 1; halite <= maxHalite; halite++)
                {
                    double harvestTime;
                    if (Math.Ceiling(halite * GameConstants.ExtractRatio) >= remainingCapacity)
                    {
                        harvestTime = 1d;
                    }
                    else
                    {
                        double haliteLeftAtCell = halite * moveThresholdRatio;
                        double haliteGatheredFromOneCell = halite - haliteLeftAtCell;
                        double haliteGatheredFromOneCellMinusMoveCost = haliteGatheredFromOneCell - (haliteLeftAtCell * GameConstants.MoveCostRatio);
                        harvestTime = (remainingCapacity / haliteGatheredFromOneCellMinusMoveCost) * (turnsHarvestingAtOneCell + 1d);
                    }

                    EstimatedHarvestTimes[i][halite] = harvestTime;
                }
            }

            //Logger.LogDebug(string.Join(",", EstimatedHarvestTimes));
        }

        private struct PositionWithStepTime
        {
            public readonly Position Position;
            public readonly double OutboundStepTime;

            public PositionWithStepTime(Position position, double outboundStepTime)
            {
                Position = position;
                OutboundStepTime = outboundStepTime;
            }
        }
    }
}

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
        public OpponentPlayer[] Opponents;
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }
        public bool IsEarlyGameMap { get; set; }
        public ReturnMap ReturnMap { get; set; }
        public DataMapLayer<int> AllOpponentDropoffDistanceMap;
        public readonly MapSetKind MapSetKind;

        public static double[][] EstimatedHarvestTimes;
        public double BaseOutboundStepTime;
        public double AverageShipSurround;

        public DataMapLayer<double> DiscAverageLayer { get; private set; }
        public DataMapLayer<double> HarvestTimeMap { get; private set; }
        public DataMapLayer<double> OutboundPaths { get; private set; }
        public DataMapLayer<double> JobTimeMap;

        public OutboundMap(MapSetKind kind)
        {
            MapSetKind = kind;
        }

        public void Calculate()
        {
            CalculateEstimatedHarvestTimes();
            CalculateHarvestTimeMap();
            CalculateOutboundPaths();
        }

        public double GetEstimatedJobTimeInNeighbourhood(Position center, int initialFill, bool isEarlyGameShip)
        {
            var disc = new Position[HarvestTimeMap.GetDiscArea(1)];
            HarvestTimeMap.GetDiscCells(center, 1, disc);
            double bestTime = double.MaxValue;
            foreach (var position in disc)
            {
                if (ForbiddenCellsMap[position])
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
                int adjustedInitialFill = initialFill;
                if (isEarlyGameShip)
                {
                    int targetHalite = Math.Min(TuningSettings.EarlyGameShipMinReturnedHalite + 50, GameConstants.ShipCapacity);
                    adjustedInitialFill += (GameConstants.ShipCapacity - targetHalite);
                    adjustedInitialFill = Math.Min(adjustedInitialFill, GameConstants.ShipCapacity);
                }

                int fillCategory = adjustedInitialFill / InitialFillStepSize;
                if (fillCategory >= EstimatedHarvestTimes.Length)
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

        private void CalculateOutboundPaths()
        {
            int mapWidth = HarvestTimeMap.Width;
            int mapHeight = HarvestTimeMap.Height;
            var outboundPaths = new DataMapLayer<double>(mapWidth, mapHeight);
            OutboundPaths = outboundPaths;
            outboundPaths.Fill(double.MaxValue);
            var forbiddenCellsMap = ForbiddenCellsMap;

            double outboundDistanceOnOneTank = GameConstants.ExtractRatio / GameConstants.MoveCostRatio;
            double outboundPathFuelPenaltyMultiplier = (1d + outboundDistanceOnOneTank) / outboundDistanceOnOneTank;

            BaseOutboundStepTime = outboundPathFuelPenaltyMultiplier * (1d + AverageShipSurround);

            JobTimeMap = new DataMapLayer<double>(mapWidth, mapHeight);
            JobTimeMap.Fill(double.MaxValue);
            var harvestTimeMap = HarvestTimeMap;
            int estimatedMaxQueueSize = (int)(harvestTimeMap.CellCount * Math.Log(harvestTimeMap.CellCount));
            var queue = new DoublePriorityQueue<PositionInfo>(estimatedMaxQueueSize);
            var returnDistanceMap = ReturnMap.CellData;
            int skippedForbiddenCellCount = 0;
            foreach (var position in harvestTimeMap.AllPositions)
            {
                double baseHarvestTime = harvestTimeMap[position];
                if (forbiddenCellsMap[position])
                {
                    skippedForbiddenCellCount++;
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
                double outboundStepTime = BaseOutboundStepTime * lostHaliteMultiplier;
                double jobTime = harvestTime + returnTime;
                JobTimeMap[position] = jobTime;
                queue.Enqueue(jobTime, new PositionInfo(position, outboundStepTime, 0));
            }

            var mapBooster = MapBooster;
            var distanceFromMyDropoffMap = MyPlayer.DistanceFromDropoffMap;
            int cellCount = outboundPaths.CellCount;
            int maxAssignedCellCount = cellCount - skippedForbiddenCellCount;
            int cellsAssigned = 0;

            int outboundMapDropoffAvoidanceRadius = TuningSettings.OutboundMapDropoffAvoidanceRadius;
            int maxSpreadDistance = TuningSettings.OutboundMapMaxSpreadDistance;
            while (queue.Count > 0)
            {
                double newTime = queue.PeekPriority();
                var positionInfo = queue.Dequeue();
                var position = positionInfo.Position;
                double oldTime = outboundPaths[position];
                if (newTime >= oldTime)
                {
                    continue;
                }

                int distanceFromMyDropoff = distanceFromMyDropoffMap[position];
                bool isCloseToMyDropoff = (distanceFromMyDropoff <= outboundMapDropoffAvoidanceRadius);

                outboundPaths[position] = newTime;
                cellsAssigned++;

                if (cellsAssigned == maxAssignedCellCount)
                {
                    break;
                }

                int nextDistance = positionInfo.Distance + 1;
                double nextTime = newTime + positionInfo.OutboundStepTime;
                Debug.Assert(nextTime > 0 && positionInfo.OutboundStepTime > 0, "nextTime=" + nextTime + ", positionWithStepTime.OutboundStepTime=" + positionInfo.OutboundStepTime);
                var neighbourArray = mapBooster.GetNeighbours(position.Row, position.Column);
                foreach (var neighbour in neighbourArray)
                {
                    double neighbourTime = outboundPaths[neighbour];
                    if (nextTime >= neighbourTime)
                    {
                        continue;
                    }

                    int neighbourDistanceFromOpponentDropoff = AllOpponentDropoffDistanceMap[neighbour];
                    if (forbiddenCellsMap[neighbour])
                    {
                        continue;
                    }

                    if (isCloseToMyDropoff)
                    {
                        int distanceFromOpponentDropoff = AllOpponentDropoffDistanceMap[neighbour];
                        if (distanceFromOpponentDropoff > TuningSettings.MapOpponentDropoffNoGoZoneRadius)
                        {
                            int neighbourDistanceFromMyDropoff = distanceFromMyDropoffMap[neighbour];
                            if (neighbourDistanceFromMyDropoff > distanceFromMyDropoff)
                            {
                                continue;
                            }
                        }
                    }

                    queue.Enqueue(nextTime, new PositionInfo(neighbour, positionInfo.OutboundStepTime, nextDistance));
                }
            }

            foreach (var dropoff in MyPlayer.Dropoffs)
            {
                outboundPaths[dropoff.Position] = double.MaxValue;
            }

            foreach (var player in Opponents)
            {
                foreach (var dropoff in player.Dropoffs)
                {
                    outboundPaths[dropoff.Position] = double.MaxValue;
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
            double[] estimatedHarvestTimesToUse;
            if (IsEarlyGameMap)
            {
                int targetHalite = Math.Min(TuningSettings.EarlyGameShipMinReturnedHalite + 50, GameConstants.ShipCapacity);
                int initialFill = (GameConstants.ShipCapacity - targetHalite);
                initialFill = Math.Min(initialFill, GameConstants.ShipCapacity);
                int fillCategory = Math.Min(initialFill / InitialFillStepSize, EstimatedHarvestTimes.Length - 1);
                estimatedHarvestTimesToUse = EstimatedHarvestTimes[fillCategory];
            }
            else
            {
                estimatedHarvestTimesToUse = EstimatedHarvestTimes[0];
            }
            foreach (var position in HarvestTimeMap.AllPositions)
            {
                double valueAtCell = adjustedHaliteValues[position];
                double averageAtCell = discAverageLayer[position];
                double weightedAverage = (valueAtCell >= averageAtCell)
                    ? (valueAtCell * centerWeight + averageAtCell) / centerWeightPlusOne
                    : valueAtCell;

                int intHalite = (int)weightedAverage;
                harvestAreaMap[position] = estimatedHarvestTimesToUse[intHalite];
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

        private struct PositionInfo
        {
            public readonly Position Position;
            public readonly double OutboundStepTime;
            public readonly int Distance;

            public PositionInfo(Position position, double outboundStepTime, int distance)
            {
                Position = position;
                OutboundStepTime = outboundStepTime;
                Distance = distance;
            }
        }
    }
}

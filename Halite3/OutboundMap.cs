namespace Halite3
{
    using System;
    using System.Diagnostics;
    using System.Linq;

    public sealed class OutboundMap
    {
        public TuningSettings TuningSettings { get; set; }
        public AdjustedHaliteMap AdjustedHaliteMap { get; set; }
        public MyPlayer MyPlayer { get; set; }
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }
        public bool IsEarlyGameMap { get; set; }

        public DataMapLayer<double> DiscAverageLayer { get; private set; }
        public DataMapLayer<double> HarvestAreaMap { get; private set; }
        public DataMapLayer<double> OutboundPaths { get; private set; }

        public void Calculate()
        {
            CalculateHarvestAreaMap();
            CalculateOutboundPaths();
        }

        private void CalculateOutboundPaths()
        {
            int mapWidth = HarvestAreaMap.Width;
            int mapHeight = HarvestAreaMap.Height;
            var outboundPaths = new DataMapLayer<double>(mapWidth, mapHeight);
            OutboundPaths = outboundPaths;
            var forbiddenCellsMap = ForbiddenCellsMap;

            int estimatedMaxQueueSize = (int)(HarvestAreaMap.CellCount * Math.Log(HarvestAreaMap.CellCount));
            var queue = new DoublePriorityQueue<Position>(estimatedMaxQueueSize);
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double value = HarvestAreaMap[position];
                if (value > 1d && !forbiddenCellsMap[position])
                {
                    queue.Enqueue(-1 * value, position);
                }
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
            while (queue.Count > 0)
            {
                double newValue = -1 * queue.PeekPriority();
                var position = queue.Dequeue();
                double oldValue = outboundPaths[position];
                if (newValue <= oldValue)
                {
                    continue;
                }

                int distanceFromDropoff = distanceFromDropoffMap[position];
                bool isCloseToDropoff = (distanceFromDropoff < outboundMapDropoffAvoidanceRadius);

                outboundPaths[position] = newValue;
                cellsAssigned++;

                // With uniform edge costs, all cells are visited exactly once.
                if (cellsAssigned == cellCount)
                {
                    break;
                }

                double nextValue = newValue * stepPenaltyMultiplier;
                if (nextValue < double.Epsilon)
                {
                    continue;
                }

                var nextPriority = -1 * nextValue;
                var neighbourArray = mapBooster.GetNeighbours(position.Row, position.Column);
                foreach (var neighbour in neighbourArray)
                {
                    double neighbourValue = outboundPaths[neighbour];
                    if (nextValue <= neighbourValue || forbiddenCellsMap[neighbour])
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

                    queue.Enqueue(nextPriority, neighbour);
                }
            }
        }

        private void CalculateHarvestAreaMap()
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
            double centerWeightPlusOne = centerWeight + 1;
            var harvestAreaMap = new DataMapLayer<double>(mapWidth, mapHeight);
            HarvestAreaMap = harvestAreaMap;
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double valueAtCell = adjustedHaliteValues[position];
                double averageAtCell = DiscAverageLayer[position];
                harvestAreaMap[position] = (valueAtCell >= averageAtCell)
                    ? (valueAtCell * centerWeight + averageAtCell) / centerWeightPlusOne
                    : 0;
            }

            foreach (var position in MyPlayer.DropoffPositions)
            {
                harvestAreaMap[position] = 0;
            }
        }
    }
}

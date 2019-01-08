namespace Halite3.hlt
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
            OutboundPaths = new DataMapLayer<double>(mapWidth, mapHeight);

            int estimatedMaxQueueSize = (int)(HarvestAreaMap.CellCount * Math.Log(HarvestAreaMap.CellCount));
            var queue = new PriorityQueue<double, Position>(estimatedMaxQueueSize, DoubleInverseComparer.Default);
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double value = HarvestAreaMap[position];
                if (value > 1d)
                {
                    queue.Enqueue(value, position);
                }
            }

            var neighbourArray = new Position[4];
            double stepPenaltyMultiplier = TuningSettings.OutboundMapPathStepPenaltyMultiplier;
            while (queue.Count > 0)
            {
                double newValue = queue.PeekPriority();
                var position = queue.Dequeue();
                double oldValue = OutboundPaths[position];
                if (newValue <= oldValue)
                {
                    continue;
                }

                OutboundPaths[position] = newValue;

                double nextValue = newValue * stepPenaltyMultiplier;
                if (nextValue < 1d)
                {
                    continue;
                }

                OutboundPaths.GetNeighbours(position, neighbourArray);
                foreach (var neighbour in neighbourArray)
                {
                    queue.Enqueue(nextValue, neighbour);
                }
            }
        }

        private void CalculateHarvestAreaMap()
        {
            var adjustedHaliteValues = AdjustedHaliteMap.Values;
            int mapWidth = adjustedHaliteValues.Width;
            int mapHeight = adjustedHaliteValues.Height;
            DiscAverageLayer = new DataMapLayer<double>(mapWidth, mapHeight);

            int windowRadius = 2;
            Debug.Assert(adjustedHaliteValues.Width > windowRadius * 2 + 1 && adjustedHaliteValues.Height > windowRadius * 2 + 1);

            int discArea = adjustedHaliteValues.GetDiscArea(windowRadius);
            var discPositions = new Position[discArea];
            for (int row = 0; row < mapHeight; row++)
            {
                for (int column = 0; column < mapWidth; column++)
                {
                    var position = new Position(row, column);
                    adjustedHaliteValues.GetDiscCells(position, windowRadius, discPositions);
                    double discSum = discPositions.Sum(discPosition => adjustedHaliteValues[discPosition]);
                    DiscAverageLayer[position] = discSum / discArea;
                }
            }

            double centerWeight = TuningSettings.OutboundMapHarvestAreaCenterWeight;
            HarvestAreaMap = new DataMapLayer<double>(mapWidth, mapHeight);
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double valueAtCell = adjustedHaliteValues[position];
                double averageAtCell = DiscAverageLayer[position];
                HarvestAreaMap[position] = (valueAtCell * centerWeight + averageAtCell) / (centerWeight + 1);
            }

            foreach (var position in MyPlayer.DropoffPositions)
            {
                HarvestAreaMap[position] = 0;
            }
        }
    }
}
